using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.News;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Transfers;
using Ebm.Services.Competitions;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.News;

/// <summary>Implements <see cref="INewsService"/> over the repositories, <see cref="MediaRules"/> and <see cref="NewsWriter"/>.</summary>
internal sealed class NewsService(IServiceScopeFactory scopes, ICompetitionService competitions) : INewsService
{
    /// <summary>Transfers and injuries are looked up this many days back, so nothing dated just before a run is missed.</summary>
    private const int LookBackDays = 7;

    private static readonly TransferKind[] HeadlineMoves = [TransferKind.Transfer, TransferKind.ReleaseClause, TransferKind.OptionToBuy];

    public async Task<NewsDay> ProcessDayAsync(DateOnly day, SeasonId seasonId, ClubId? userClub, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var w = await Writer.LoadAsync(s, cancellationToken).ConfigureAwait(false);
        var from = day.AddDays(-LookBackDays);

        // Matches of the day: upsets for every club, reviews, press conferences, interviews and streaks for the user's club.
        var fixtures = (await Repo<Fixture, FixtureId>(s).ListAsync(f => f.Date == day, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
        var ids = fixtures.Keys.ToList();
        var played = ids.Count == 0 ? [] : await Repo<Match, MatchId>(s).ListAsync(m => ids.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false);
        foreach (var match in played.OrderBy(m => m.Id.Value))
        {
            var fixture = fixtures[match.FixtureId];
            await w.UpsetAsync(match, fixture, day, cancellationToken).ConfigureAwait(false);
            if (userClub is { } user && (fixture.HomeClubId == user || fixture.AwayClubId == user))
            {
                await w.UserMatchAsync(s, user, match, fixture, day, cancellationToken).ConfigureAwait(false);
            }
        }

        // Completed transfers: every move of the user's club, fee-paying moves elsewhere.
        var moves = await Repo<Transfer, TransferId>(s).ListAsync(t => t.Date > from && t.Date <= day, cancellationToken).ConfigureAwait(false);
        foreach (var t in moves.Where(t => t.ToClubId == userClub || t.FromClubId == userClub || HeadlineMoves.Contains(t.Kind)).OrderBy(t => t.Date).ThenBy(t => t.Id.Value))
        {
            await w.TransferAsync(t, cancellationToken).ConfigureAwait(false);
        }

        // New injuries: every one at the user's club, serious ones elsewhere.
        var injuries = await Repo<Injury, InjuryId>(s).ListAsync(i => i.OccurredOn > from && i.OccurredOn <= day && i.ClubId != null, cancellationToken).ConfigureAwait(false);
        foreach (var injury in injuries.Where(i => i.ClubId == userClub || i.Severity >= MediaRules.HeadlineSeverity).OrderBy(i => i.OccurredOn).ThenBy(i => i.Id.Value))
        {
            await w.InjuryAsync(injury, day, cancellationToken).ConfigureAwait(false);
        }

        if (userClub is { } club)
        {
            if (day.DayOfWeek == DayOfWeek.Monday)
            {
                await w.ContractRumorsAsync(s, club, day, cancellationToken).ConfigureAwait(false);
                await w.FanMoodAsync(s, competitions, club, seasonId, day, cancellationToken).ConfigureAwait(false);
            }

            // Tomorrow's matches of the user's club, written after today's injuries.
            var tomorrow = day.AddDays(1);
            var next = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.Date == tomorrow && (f.HomeClubId == club || f.AwayClubId == club), cancellationToken).ConfigureAwait(false);
            foreach (var fixture in next)
            {
                await w.PreviewAsync(s, competitions, club, fixture, day, cancellationToken).ConfigureAwait(false);
            }
        }

        return await w.SaveAsync(s, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NewsDay> ProcessSeasonEndAsync(SeasonId seasonId, IReadOnlyDictionary<ClubId, bool> objectivesMet, DateOnly date, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectivesMet);
        var causes = new Dictionary<ClubId, List<(ReputationCause Cause, string Text)>>();
        void Add(ClubId club, ReputationCause cause, string text)
        {
            if (!causes.TryGetValue(club, out var list))
            {
                causes[club] = list = [];
            }

            list.Add((cause, text));
        }

        List<Competition> entered;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            var ids = (await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false))
                .Select(p => p.CompetitionId).Distinct().ToList();
            entered = [.. (await Repo<Competition, CompetitionId>(s).ListAsync(c => ids.Contains(c.Id), cancellationToken).ConfigureAwait(false)).OrderBy(c => c.Type).ThenBy(c => c.Name)];
        }

        foreach (var competition in entered)
        {
            if (await competitions.GetPhaseAsync(competition.Id, seasonId, cancellationToken).ConfigureAwait(false) != CompetitionPhase.Completed)
            {
                continue;
            }

            var outcome = await competitions.GetOutcomeAsync(competition.Id, seasonId, cancellationToken).ConfigureAwait(false);
            if (competition.Type != CompetitionType.League)
            {
                Add(outcome.Champion, ReputationCause.CupWin, $"Winners of the {competition.Name}.");
                continue;
            }

            Add(outcome.Champion, ReputationCause.LeagueTitle, $"Champions of the {competition.Name}.");
            foreach (var club in outcome.Promoted)
            {
                Add(club, ReputationCause.Promotion, $"Promoted from the {competition.Name}.");
            }

            foreach (var club in outcome.Relegated)
            {
                Add(club, ReputationCause.Relegation, $"Relegated from the {competition.Name}.");
            }
        }

        foreach (var (club, met) in objectivesMet)
        {
            Add(club, met ? ReputationCause.ObjectiveMet : ReputationCause.ObjectiveMissed, met ? "The board's objective was met." : "The board's objective was missed.");
        }

        using var write = scopes.CreateScope();
        var w = await Writer.LoadAsync(write.ServiceProvider, cancellationToken).ConfigureAwait(false);
        foreach (var (club, list) in causes.OrderBy(c => c.Key.Value))
        {
            await w.SeasonVerdictAsync(club, seasonId, list, date, cancellationToken).ConfigureAwait(false);
        }

        return await w.SaveAsync(write.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NewsDay> ProcessSeasonStartAsync(SeasonId seasonId, ClubId userClub, DateOnly date, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var w = await Writer.LoadAsync(s, cancellationToken).ConfigureAwait(false);
        if (await FansAsync(s, competitions, userClub, seasonId, cancellationToken).ConfigureAwait(false) is { } fans)
        {
            var story = NewsWriter.Expectations(w.Name(userClub), fans.Expectation, fans.Competition, fans.ReputationRank, fans.Clubs);
            await w.AddAsync($"fans:{seasonId}:{userClub}:start", date, NewsCategory.FanExpectations, story, new() { ClubId = userClub, SeasonId = seasonId }, cancellationToken).ConfigureAwait(false);
        }

        return await w.SaveAsync(s, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NewsItem>> GetNewsAsync(NewsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var scope = scopes.CreateScope();
        var (club, category, from, to) = (query.ClubId, query.Category, query.From ?? DateOnly.MinValue, query.To ?? DateOnly.MaxValue);
        var items = await Repo<NewsItem, NewsItemId>(scope.ServiceProvider).ListUntrackedAsync(
            n => (club == null || n.ClubId == club || n.OtherClubId == club) && (category == null || n.Category == category) && n.Date >= from && n.Date <= to,
            cancellationToken).ConfigureAwait(false);
        return [.. items.OrderByDescending(n => n.Date).ThenBy(n => n.Category).ThenBy(n => n.Key, StringComparer.Ordinal).Take(Math.Max(1, query.Max))];
    }

    public async Task<IReadOnlyList<ReputationChange>> GetReputationHistoryAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var changes = await Repo<ReputationChange, ReputationChangeId>(scope.ServiceProvider).ListUntrackedAsync(c => c.ClubId == clubId, cancellationToken).ConfigureAwait(false);
        return Chain(changes);
    }

    public async Task<FanView?> GetFansAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var career = (await Repo<Career, CareerId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return career is null ? null : await FansAsync(s, competitions, clubId, career.SeasonId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A club's changes in the order they were made (each starts where the last one ended).</summary>
    internal static IReadOnlyList<ReputationChange> Chain(IEnumerable<ReputationChange> changes)
    {
        var chain = new List<ReputationChange>();
        foreach (var day in changes.GroupBy(c => c.Date).OrderBy(g => g.Key))
        {
            // Several changes on one day (a season's verdict): each starts where the previous one ended.
            var left = day.OrderBy(c => c.Id.Value).ToList();
            while (left.Count > 0)
            {
                var next = left.FirstOrDefault(c => chain.Count > 0 && c.Before == chain[^1].After)
                    ?? left.FirstOrDefault(c => !left.Any(o => o != c && o.After == c.Before))
                    ?? left[0];
                chain.Add(next);
                left.Remove(next);
            }
        }

        return chain;
    }

    /// <summary>The fans' view of <paramref name="club"/> in its league of <paramref name="seasonId"/>.</summary>
    internal static async Task<FanView?> FansAsync(IServiceProvider s, ICompetitionService competitions, ClubId club, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId && p.ClubId == club, cancellationToken).ConfigureAwait(false);
        var ids = entries.Select(e => e.CompetitionId).ToList();
        var league = (await Repo<Competition, CompetitionId>(s).ListAsync(c => ids.Contains(c.Id) && c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false))
            .OrderBy(c => c.Name).FirstOrDefault();
        if (league is null)
        {
            return null;
        }

        var members = (await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId && p.CompetitionId == league.Id, cancellationToken).ConfigureAwait(false))
            .Select(p => p.ClubId).ToList();
        var clubs = await Repo<Club, ClubId>(s).ListUntrackedAsync(c => members.Contains(c.Id), cancellationToken).ConfigureAwait(false);
        var ranked = clubs.OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).ToList();
        var rank = ranked.FindIndex(c => c.Id == club) + 1;
        if (rank == 0)
        {
            return null;
        }

        var expectation = MediaRules.Expect(rank, ranked.Count);
        var table = await competitions.GetStandingsAsync(league.Id, seasonId, cancellationToken).ConfigureAwait(false);
        var row = table.FirstOrDefault(r => r.ClubId == club);
        var results = await ResultsAsync(s, club, seasonId, league.Id, 5, cancellationToken).ConfigureAwait(false);
        var form = Form(results);
        FanMood? mood = row is { Played: >= MediaRules.FanJudgementGames }
            ? MediaRules.Mood(expectation, row.Position, table.Count, results.Count(r => r), results.Count)
            : null;
        return new FanView(league.Name, ranked[rank - 1].Reputation, rank, ranked.Count, expectation, row is { Played: > 0 } ? row.Position : null, form, mood);
    }

    /// <summary>The club's last <paramref name="count"/> results of the season (won = true), newest first; one competition or all.</summary>
    internal static async Task<IReadOnlyList<bool>> ResultsAsync(IServiceProvider s, ClubId club, SeasonId seasonId, CompetitionId? competition, int count, CancellationToken cancellationToken)
    {
        var fixtures = (await Repo<Fixture, FixtureId>(s).ListAsync(
            f => f.SeasonId == seasonId && (f.HomeClubId == club || f.AwayClubId == club) && (competition == null || f.CompetitionId == competition),
            cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
        var ids = fixtures.Keys.ToList();
        var matches = await Repo<Match, MatchId>(s).ListUntrackedAsync(m => ids.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false);
        return [.. matches.Select(m => (Fixture: fixtures[m.FixtureId], Match: m))
            .OrderByDescending(x => x.Fixture.Date).ThenByDescending(x => x.Fixture.Round).ThenByDescending(x => x.Fixture.GameNumber)
            .Take(count)
            .Select(x => x.Match.HomeWon == (x.Fixture.HomeClubId == club))];
    }

    /// <summary>Results oldest to newest as W/L, e.g. <c>WWLWL</c>.</summary>
    internal static string Form(IReadOnlyList<bool> newestFirst) => string.Concat(newestFirst.Reverse().Select(w => w ? 'W' : 'L'));

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>Links of a story, copied onto the <see cref="NewsItem"/>.</summary>
    private readonly record struct Links(
        ClubId? ClubId = null,
        ClubId? OtherClubId = null,
        PlayerId? PlayerId = null,
        SeasonId? SeasonId = null,
        FixtureId? FixtureId = null,
        MatchId? MatchId = null,
        TransferId? TransferId = null,
        InjuryId? InjuryId = null,
        ReputationChangeId? ReputationChangeId = null);

    /// <summary>One unit of work of news writing: the stories and reputation changes of one run, never one event twice.</summary>
    private sealed class Writer
    {
        private readonly Dictionary<ClubId, Club> _clubs;
        private readonly IRepository<NewsItem, NewsItemId> _news;
        private readonly IRepository<ReputationChange, ReputationChangeId> _changes;
        private readonly Dictionary<CompetitionId, Competition> _competitions;
        private readonly IRepository<Player, PlayerId> _players;
        private readonly HashSet<string> _written = new(StringComparer.Ordinal);
        private readonly Dictionary<PlayerId, string> _names = [];
        private int _stories;
        private int _reputation;

        private Writer(IServiceProvider s, Dictionary<ClubId, Club> clubs, Dictionary<CompetitionId, Competition> competitions)
        {
            _clubs = clubs;
            _competitions = competitions;
            _news = Repo<NewsItem, NewsItemId>(s);
            _changes = Repo<ReputationChange, ReputationChangeId>(s);
            _players = Repo<Player, PlayerId>(s);
        }

        public static async Task<Writer> LoadAsync(IServiceProvider s, CancellationToken cancellationToken) => new(
            s,
            (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id),
            (await Repo<Competition, CompetitionId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id));

        public string Name(ClubId club) => _clubs.TryGetValue(club, out var c) ? c.Name : "?";

        public async Task<NewsDay> SaveAsync(IServiceProvider s, CancellationToken cancellationToken)
        {
            await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(_stories, _reputation);
        }

        /// <summary>Adds a story unless its event was already reported. Returns whether it was added.</summary>
        public async Task<bool> AddAsync(string key, DateOnly date, NewsCategory category, Story story, Links links, CancellationToken cancellationToken)
        {
            if (!await IsNewAsync(key, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            _written.Add(key);
            _news.Add(new NewsItem(NewsItemId.New(), key, date, category, Trim(story.Headline, NewsItem.HeadlineMaxLength), Trim(story.Body, NewsItem.BodyMaxLength), story.Sentiment)
            {
                ClubId = links.ClubId,
                OtherClubId = links.OtherClubId,
                PlayerId = links.PlayerId,
                SeasonId = links.SeasonId,
                FixtureId = links.FixtureId,
                MatchId = links.MatchId,
                TransferId = links.TransferId,
                InjuryId = links.InjuryId,
                ReputationChangeId = links.ReputationChangeId,
            });
            _stories++;
            return true;
        }

        public async Task UpsetAsync(Match match, Fixture fixture, DateOnly day, CancellationToken cancellationToken)
        {
            var (winner, loser) = match.HomeWon ? (fixture.HomeClubId, fixture.AwayClubId) : (fixture.AwayClubId, fixture.HomeClubId);
            if (!_clubs.TryGetValue(winner, out var w) || !_clubs.TryGetValue(loser, out var l) || !MediaRules.IsUpset(w.Reputation, l.Reputation))
            {
                return;
            }

            var key = $"upset:{match.Id}";
            if (!await IsNewAsync(key, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var won = await ChangeAsync(w, ReputationCause.UpsetWin, fixture.SeasonId, match.Id, $"reputation:{match.Id}:{winner}", day, cancellationToken).ConfigureAwait(false);
            var lost = await ChangeAsync(l, ReputationCause.UpsetDefeat, fixture.SeasonId, match.Id, $"reputation:{match.Id}:{loser}", day, cancellationToken).ConfigureAwait(false);
            var (ws, ls) = match.HomeWon ? (match.HomeScore, match.AwayScore) : (match.AwayScore, match.HomeScore);
            var story = NewsWriter.Upset(w.Name, l.Name, ws, ls, Competition(fixture));
            var reputation = string.Join(" ", new[] { won, lost }.OfType<ReputationChange>().Select(c => $"{Name(c.ClubId)} reputation {c.Before} → {c.After}."));
            await AddAsync(key, day, NewsCategory.MediaReaction, story with { Body = $"{story.Body} {reputation}".TrimEnd() }, new() { ClubId = winner, OtherClubId = loser, SeasonId = fixture.SeasonId, FixtureId = fixture.Id, MatchId = match.Id, ReputationChangeId = won?.Id ?? lost?.Id }, cancellationToken).ConfigureAwait(false);
        }

        public async Task UserMatchAsync(IServiceProvider s, ClubId user, Match match, Fixture fixture, DateOnly day, CancellationToken cancellationToken)
        {
            var home = fixture.HomeClubId == user;
            var opponent = home ? fixture.AwayClubId : fixture.HomeClubId;
            var lines = await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(p => p.MatchId == match.Id, cancellationToken).ConfigureAwait(false);
            var star = await StarAsync(lines, user, cancellationToken).ConfigureAwait(false);
            var otherStar = await StarAsync(lines, opponent, cancellationToken).ConfigureAwait(false);
            var results = await ResultsAsync(s, user, fixture.SeasonId, null, 40, cancellationToken).ConfigureAwait(false);
            var streak = MediaRules.Streak(results);
            var story = new MatchStory(
                Name(user), Name(opponent), home, home ? match.HomeScore : match.AwayScore, home ? match.AwayScore : match.HomeScore, match.Overtimes, Competition(fixture),
                star?.Line, otherStar?.Line, streak);
            var links = new Links(user, opponent, null, fixture.SeasonId, fixture.Id, match.Id);
            await AddAsync($"review:{match.Id}", day, NewsCategory.MatchReview, NewsWriter.Review($"review:{match.Id}", story), links, cancellationToken).ConfigureAwait(false);
            var manager = (await Repo<Career, CareerId>(s).ListAsync(c => c.ClubId == user, cancellationToken).ConfigureAwait(false)).FirstOrDefault()?.ManagerName ?? $"The {Name(user)} coach";
            await AddAsync($"press:{match.Id}", day, NewsCategory.PressConference, NewsWriter.PressConference($"press:{match.Id}", manager, story), links, cancellationToken).ConfigureAwait(false);
            if (star is { } top)
            {
                await AddAsync($"interview:{match.Id}", day, NewsCategory.Interview, NewsWriter.Interview($"interview:{match.Id}", top.Line, story), links with { PlayerId = top.Player }, cancellationToken).ConfigureAwait(false);
            }

            if (Math.Abs(streak) >= MediaRules.StreakLength && Math.Abs(streak) % MediaRules.StreakLength == 0)
            {
                await AddAsync($"streak:{match.Id}:{user}", day, NewsCategory.MediaReaction, NewsWriter.Streak(Name(user), streak), links, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task TransferAsync(Transfer t, CancellationToken cancellationToken)
        {
            var key = $"transfer:{t.Id}";
            if (!await IsNewAsync(key, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var player = t.PlayerId;
            var rumoured = (await _news.ListUntrackedAsync(n => n.PlayerId == player && n.Category == NewsCategory.Rumor, cancellationToken).ConfigureAwait(false)).Count > 0;
            var story = NewsWriter.Transfer(t.Kind, await PlayerAsync(player, cancellationToken).ConfigureAwait(false), t.FromClubId is { } f ? Name(f) : null, Name(t.ToClubId), t.Fee, t.LoanEndDate, rumoured);
            await AddAsync(key, t.Date, NewsCategory.Transfer, story, new() { ClubId = t.ToClubId, OtherClubId = t.FromClubId, PlayerId = player, TransferId = t.Id }, cancellationToken).ConfigureAwait(false);
        }

        public async Task InjuryAsync(Injury injury, DateOnly day, CancellationToken cancellationToken)
        {
            var club = injury.ClubId!.Value;
            var story = NewsWriter.Injury(await PlayerAsync(injury.PlayerId, cancellationToken).ConfigureAwait(false), Name(club), injury.Definition.Name, injury.Severity, injury.Source, injury.EstimatedReturn, injury.OccurredOn);
            await AddAsync($"injury:{injury.Id}", injury.OccurredOn, NewsCategory.Injury, story, new() { ClubId = club, PlayerId = injury.PlayerId, MatchId = injury.MatchId, InjuryId = injury.Id }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Players of the club whose contract runs out within <see cref="MediaRules.ContractRumorDays"/> days with nothing agreed after it.</summary>
        public async Task ContractRumorsAsync(IServiceProvider s, ClubId club, DateOnly day, CancellationToken cancellationToken)
        {
            var contracts = Repo<Contract, ContractId>(s);
            var limit = day.AddDays(MediaRules.ContractRumorDays);
            var running = (await contracts.ListUntrackedAsync(Contract.RunningOn(day), cancellationToken).ConfigureAwait(false))
                .Where(c => c.ClubId == club && c.PlayerId is not null && c.Status == ContractStatus.Active && c.EffectiveEndDate <= limit)
                .ToList();
            var players = running.Select(c => c.PlayerId!.Value).ToList();
            var later = (await contracts.ListUntrackedAsync(c => c.PlayerId != null && players.Contains(c.PlayerId.Value) && c.StartDate > day, cancellationToken).ConfigureAwait(false))
                .Select(c => c.PlayerId!.Value).ToHashSet();
            foreach (var contract in running.Where(c => !later.Contains(c.PlayerId!.Value)).OrderBy(c => c.EffectiveEndDate).ThenBy(c => c.Id.Value))
            {
                var key = $"rumor:contract:{contract.Id}";
                var story = NewsWriter.ContractRumor(key, await PlayerAsync(contract.PlayerId!.Value, cancellationToken).ConfigureAwait(false), Name(club), contract.EffectiveEndDate);
                await AddAsync(key, day, NewsCategory.Rumor, story, new() { ClubId = club, PlayerId = contract.PlayerId }, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>A fan story when the mood differs from the last one reported this season.</summary>
        public async Task FanMoodAsync(IServiceProvider s, ICompetitionService competitions, ClubId club, SeasonId seasonId, DateOnly day, CancellationToken cancellationToken)
        {
            if (await FansAsync(s, competitions, club, seasonId, cancellationToken).ConfigureAwait(false) is not { Mood: { } mood, Position: { } position } fans)
            {
                return;
            }

            var last = (await _news.ListUntrackedAsync(n => n.ClubId == club && n.SeasonId == seasonId && n.Category == NewsCategory.FanExpectations && n.Sentiment != 0, cancellationToken).ConfigureAwait(false))
                .OrderByDescending(n => n.Date).FirstOrDefault();
            if (last?.Sentiment == (int)mood)
            {
                return;
            }

            var story = NewsWriter.Mood(Name(club), mood, fans.Expectation, position, fans.Clubs, fans.Form);
            await AddAsync($"fans:{seasonId}:{club}:{day:yyyy-MM-dd}", day, NewsCategory.FanExpectations, story, new() { ClubId = club, SeasonId = seasonId }, cancellationToken).ConfigureAwait(false);
        }

        public async Task PreviewAsync(IServiceProvider s, ICompetitionService competitions, ClubId club, Fixture fixture, DateOnly day, CancellationToken cancellationToken)
        {
            var key = $"preview:{fixture.Id}";
            var fixtureId = fixture.Id;
            if (!await IsNewAsync(key, cancellationToken).ConfigureAwait(false)
                || (await Repo<Match, MatchId>(s).ListUntrackedAsync(m => m.FixtureId == fixtureId, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var opponent = fixture.HomeClubId == club ? fixture.AwayClubId : fixture.HomeClubId;
            var league = _competitions.TryGetValue(fixture.CompetitionId, out var c) && c.Type == CompetitionType.League && fixture.Stage == FixtureStage.RegularSeason;
            var table = league ? await competitions.GetStandingsAsync(fixture.CompetitionId, fixture.SeasonId, cancellationToken).ConfigureAwait(false) : [];
            async Task<PreviewSide> SideAsync(ClubId id)
            {
                var injured = await Repo<Injury, InjuryId>(s).ListUntrackedAsync(i => i.ClubId == id && i.RecoveredOn == null && i.OccurredOn <= day, cancellationToken).ConfigureAwait(false);
                var names = new List<string>();
                foreach (var injury in injured.Where(i => i.Severity > InjurySeverity.Minor).OrderBy(i => i.OccurredOn))
                {
                    names.Add(await PlayerAsync(injury.PlayerId, cancellationToken).ConfigureAwait(false));
                }

                var row = table.FirstOrDefault(r => r.ClubId == id);
                return new PreviewSide(Name(id), row is { Played: > 0 } ? row.Position : null, Form(await ResultsAsync(s, id, fixture.SeasonId, null, 5, cancellationToken).ConfigureAwait(false)), names);
            }

            var story = NewsWriter.Preview(key, await SideAsync(club).ConfigureAwait(false), await SideAsync(opponent).ConfigureAwait(false), fixture.HomeClubId == club, Competition(fixture), fixture.Date);
            await AddAsync(key, day, NewsCategory.MatchPreview, story, new() { ClubId = club, OtherClubId = opponent, SeasonId = fixture.SeasonId, FixtureId = fixture.Id }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Applies the season's causes to one club in order, then writes one story.</summary>
        public async Task SeasonVerdictAsync(ClubId clubId, SeasonId seasonId, IReadOnlyList<(ReputationCause Cause, string Text)> causes, DateOnly date, CancellationToken cancellationToken)
        {
            if (!_clubs.TryGetValue(clubId, out var club))
            {
                return;
            }

            var before = club.Reputation;
            var made = new List<ReputationChange>();
            foreach (var (cause, _) in causes)
            {
                if (await ChangeAsync(club, cause, seasonId, null, $"reputation:{seasonId}:{clubId}:{cause}", date, cancellationToken).ConfigureAwait(false) is { } change)
                {
                    made.Add(change);
                }
            }

            if (made.Count == 0 || club.Reputation == before)
            {
                return;
            }

            var main = causes.MaxBy(c => Math.Abs(MediaRules.Points(c.Cause))).Cause;
            var story = NewsWriter.Reputation(club.Name, main, before, club.Reputation, string.Join(" ", causes.Select(c => c.Text)));
            await AddAsync($"reputation:{seasonId}:{clubId}", date, NewsCategory.Reputation, story, new() { ClubId = clubId, SeasonId = seasonId, ReputationChangeId = made[0].Id }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Moves a club's reputation for one event, once; <c>null</c> when already applied or when nothing would change (at the limits).</summary>
        private async Task<ReputationChange?> ChangeAsync(Club club, ReputationCause cause, SeasonId seasonId, MatchId? matchId, string key, DateOnly date, CancellationToken cancellationToken)
        {
            if (!_written.Add(key) || (await _changes.ListUntrackedAsync(c => c.Key == key, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return null;
            }

            var before = club.Reputation;
            club.ChangeReputation(MediaRules.Points(cause));
            if (club.Reputation == before)
            {
                return null;
            }

            var change = new ReputationChange(ReputationChangeId.New(), key, club.Id, date, cause, before, club.Reputation, seasonId, matchId);
            _changes.Add(change);
            _reputation++;
            return change;
        }

        private async Task<bool> IsNewAsync(string key, CancellationToken cancellationToken) =>
            !_written.Contains(key) && (await _news.ListUntrackedAsync(n => n.Key == key, cancellationToken).ConfigureAwait(false)).Count == 0;

        private async Task<(PlayerId Player, StarLine Line)?> StarAsync(IReadOnlyList<PlayerMatchStatistics> lines, ClubId club, CancellationToken cancellationToken)
        {
            var best = lines.Where(l => l.ClubId == club)
                .OrderByDescending(l => l.BoxScore.Points + l.BoxScore.Rebounds + l.BoxScore.Assists).ThenByDescending(l => l.BoxScore.Points).ThenBy(l => l.PlayerId.Value)
                .FirstOrDefault();
            return best is null ? null : (best.PlayerId, new StarLine(await PlayerAsync(best.PlayerId, cancellationToken).ConfigureAwait(false), best.BoxScore.Points, best.BoxScore.Rebounds, best.BoxScore.Assists));
        }

        private async Task<string> PlayerAsync(PlayerId id, CancellationToken cancellationToken)
        {
            if (!_names.TryGetValue(id, out var name))
            {
                _names[id] = name = (await _players.FindAsync(id, cancellationToken).ConfigureAwait(false))?.FullName ?? "A player";
            }

            return name;
        }

        private string Competition(Fixture fixture) => _competitions.TryGetValue(fixture.CompetitionId, out var c) ? c.Name : "?";

        private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
