using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Development;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Scouting;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Development;

/// <summary>Implements <see cref="IDevelopmentService"/> over the repositories and the pure <see cref="PlayerDevelopment"/> formulas.</summary>
internal sealed class DevelopmentService(
    IServiceScopeFactory scopes,
    PlayerDevelopment engine,
    RetirementRules retirement,
    PlayerRatingCalculator calculator,
    GameClock clock) : IDevelopmentService
{
    /// <summary>Playing time is measured over this many days.</summary>
    public const int PlayingTimeDays = 28;

    /// <summary>A club without league division develops its players like a second division.</summary>
    public const int DefaultDivisionLevel = 2;

    private static readonly StaffRole[] CoachRoles = [StaffRole.HeadCoach, StaffRole.AssistantCoach, StaffRole.FitnessCoach];

    private DateOnly Today => clock.Today;

    public async Task<ClubTraining?> GetClubTrainingAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = Today;
        if (await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var (members, _) = await ClubMembership.MembersOfAsync(s, clubId, today, cancellationToken).ConfigureAwait(false);
        var world = await World.LoadAsync(s, today, members, cancellationToken).ConfigureAwait(false);
        var stored = world.Plans.GetValueOrDefault(clubId);
        var plan = stored ?? new TrainingPlan(TrainingPlanId.New(), clubId);
        var rows = world.Players
            .Select(p =>
            {
                var overall = calculator.Evaluate(p).Overall;
                var input = world.Input(p, overall, plan);
                return new TrainingPlayer(p, input.Age, overall, input.Potential, plan.FocusOf(p.Id), Math.Round(engine.ExpectedYearlyChange(input), 1));
            })
            .OrderByDescending(r => r.Overall).ThenBy(r => r.Player.LastName)
            .ToList();
        var coaches = (await StaffHiring.StaffAsync(s, today, clubId, CoachRoles, cancellationToken).ConfigureAwait(false)).Select(x => x.Member).OrderBy(m => m.Role).ThenByDescending(m => m.Ability).ToList();
        return new ClubTraining(club, plan, stored is not null, coaches, world.CoachAbility(clubId), world.FitnessCoachAbility(clubId), rows);
    }

    public async Task<TrainingPlan> SavePlanAsync(ClubId clubId, TrainingPlanDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var club = await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database.");
        var (members, _) = await ClubMembership.MembersOfAsync(s, clubId, Today, cancellationToken).ConfigureAwait(false);
        if (draft.Individuals.FirstOrDefault(i => !members.Contains(i.PlayerId)) is { } stranger)
        {
            throw new DomainValidationException($"Only players of the {club.Name} squad can get an individual programme ({stranger.PlayerId}).");
        }

        var plans = Repo<TrainingPlan, TrainingPlanId>(s);
        var plan = (await plans.ListAsync(p => p.ClubId == clubId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (plan is null)
        {
            plan = new TrainingPlan(TrainingPlanId.New(), clubId);
            plans.Add(plan);
        }

        plan.Update(draft.TeamFocus, draft.Intensity, draft.Individuals);
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return plan;
    }

    public async Task<int> HireMissingCoachesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var assistants = await StaffHiring.HireMissingAsync(s, Today, StaffRole.AssistantCoach, c => IDevelopmentService.AssistantCoachesFor(c.Reputation), CoachSalary, 0x1000, cancellationToken).ConfigureAwait(false);
        var fitness = await StaffHiring.HireMissingAsync(s, Today, StaffRole.FitnessCoach, _ => 1, CoachSalary, 0x2000, cancellationToken).ConfigureAwait(false);
        return assistants + fitness;
    }

    /// <summary>Annual salary of a generated coach: € 25,000 + € 700 per ability point.</summary>
    public static long CoachSalary(int ability) => 25_000 + (700L * ability);

    public async Task<DevelopmentDay> ProcessDayAsync(IReadOnlyCollection<MatchId> playedToday, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playedToday);
        var today = Today;
        var fitnessChanged = await RecoverAsync(today, playedToday, cancellationToken).ConfigureAwait(false);
        if (today.DayOfWeek != DayOfWeek.Monday)
        {
            return DevelopmentDay.None with { FitnessChanged = fitnessChanged };
        }

        var hired = await HireMissingCoachesAsync(cancellationToken).ConfigureAwait(false);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var world = await World.LoadAsync(s, today, null, cancellationToken).ConfigureAwait(false);
        int developed = 0, improved = 0, declined = 0;
        foreach (var player in world.Players)
        {
            var before = calculator.Evaluate(player).Overall;
            var input = world.Input(player, before, null);
            var random = new Random(ScoutingRules.StableSeed(player.Id.Value, Guid.Empty, today.DayNumber));
            var step = engine.Week(input, random, PlayerDevelopment.SeasonLuck(player.Id, world.SeasonYear));
            var attributes = step.LeadershipGain > 0
                ? step.Attributes.With(PlayerAttribute.Leadership, Math.Min(RatingSet<PlayerAttribute>.MaxRating, step.Attributes.Leadership + step.LeadershipGain))
                : step.Attributes;
            var changed = PlayerCatalog.Attributes.Any(a => attributes[a] != player.Attributes[a]);
            if (!changed && step.ExperienceGain == 0)
            {
                continue;
            }

            player.Develop(changed ? attributes : player.Attributes, step.ExperienceGain, player.Potential);
            developed++;
            var after = calculator.Evaluate(player).Overall;
            improved += after > before ? 1 : 0;
            declined += after < before ? 1 : 0;
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DevelopmentDay(fitnessChanged, developed, improved, declined, hired);
    }

    public async Task<SeasonDevelopment> ProcessSeasonEndAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = Today;
        var players = await Repo<Player, PlayerId>(s).ListAsync(p => p.RetiredOn == null, cancellationToken).ConfigureAwait(false);
        var members = await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false);
        var contracts = (await Repo<Contract, ContractId>(s).ListAsync(c => c.PlayerId != null, cancellationToken).ConfigureAwait(false))
            .GroupBy(c => c.PlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var revised = 0;
        var retired = new List<RetiredPlayer>();
        foreach (var player in players.OrderBy(p => p.Id.Value))
        {
            var age = player.AgeOn(today);
            var overall = calculator.Evaluate(player).Overall;
            var random = new Random(ScoutingRules.StableSeed(player.Id.Value, Guid.Empty, (today.Year * 31) + 7));
            var potential = PlayerDevelopment.RevisePotential(age, overall, Math.Max(overall, player.Potential), random);
            if (potential != player.Potential)
            {
                player.Develop(player.Attributes, 0, potential);
                revised++;
            }

            var own = contracts.GetValueOrDefault(player.Id) ?? [];
            if (age < retirement.MinAge || own.Any(c => c.Status == ContractStatus.OnLoan && c.IsRunningOn(today)))
            {
                continue; // Players on loan finish the loan first.
            }

            ClubId? club = members.TryGetValue(player.Id, out var current) ? current : null;
            var binding = own.Where(c => c.Status == ContractStatus.Active && (c.IsRunningOn(today) || c.IsUpcomingOn(today)) && c.EffectiveEndDate > today).ToList();
            var chance = retirement.Chance(age, overall, club is not null, binding.Count > 0, player.InjuryStatus == InjuryStatus.Injured);
            if (random.NextDouble() >= chance)
            {
                continue;
            }

            var lastDay = today;
            player.Retire(lastDay.AddDays(1));
            foreach (var contract in binding)
            {
                contract.Terminate(lastDay.AddDays(1), 0);
            }

            if (club is { } id)
            {
                await ClubMembership.LeaveAsync(s, player.Id, id, lastDay, cancellationToken).ConfigureAwait(false);
            }

            retired.Add(new RetiredPlayer(player.Id, player.FullName, age, overall, club is { } c ? clubs.GetValueOrDefault(c) : null));
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SeasonDevelopment(revised, retired);
    }

    /// <summary>Daily fitness: fatigue from today's minutes, then recovery by the club's training (§3).</summary>
    private async Task<int> RecoverAsync(DateOnly today, IReadOnlyCollection<MatchId> playedToday, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        List<MatchId> matchIds = [.. playedToday];
        var minutes = matchIds.Count == 0
            ? []
            : (await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListAsync(x => matchIds.Contains(x.MatchId), cancellationToken).ConfigureAwait(false))
                .GroupBy(x => x.PlayerId).ToDictionary(g => g.Key, g => g.Sum(x => x.BoxScore.Minutes));
        List<PlayerId> played = [.. minutes.Keys];
        var players = await Repo<Player, PlayerId>(s).ListAsync(p => p.RetiredOn == null && (p.Fitness < Player.MaxFitness || played.Contains(p.Id)), cancellationToken).ConfigureAwait(false);
        if (players.Count == 0)
        {
            return 0;
        }

        var members = await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false);
        var plans = (await Repo<TrainingPlan, TrainingPlanId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.ClubId);
        var staff = Staff.From(await StaffHiring.StaffAsync(s, today, null, CoachRoles, cancellationToken).ConfigureAwait(false));
        var changed = 0;
        foreach (var player in players)
        {
            var training = members.TryGetValue(player.Id, out var club) ? staff.Conditions(club, player.Id, plans.GetValueOrDefault(club)) : TrainingConditions.Alone;
            var fitness = engine.DailyFitness(player.Fitness, minutes.GetValueOrDefault(player.Id), training, player.InjuryStatus);
            if (fitness != player.Fitness)
            {
                player.UpdateFitness(fitness);
                changed++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>Coaching ability per club.</summary>
    private sealed class Staff
    {
        private readonly Dictionary<ClubId, int> _coaches;
        private readonly Dictionary<ClubId, int> _fitness;

        private Staff(Dictionary<ClubId, int> coaches, Dictionary<ClubId, int> fitness)
        {
            _coaches = coaches;
            _fitness = fitness;
        }

        public static Staff From(IReadOnlyList<(StaffMember Member, Contract Contract)> staff) => new(
            staff.Where(x => x.Member.Role is StaffRole.HeadCoach or StaffRole.AssistantCoach).GroupBy(x => x.Contract.ClubId)
                .ToDictionary(g => g.Key, g => (int)Math.Round(g.Average(x => x.Member.Ability), MidpointRounding.AwayFromZero)),
            staff.Where(x => x.Member.Role == StaffRole.FitnessCoach).GroupBy(x => x.Contract.ClubId)
                .ToDictionary(g => g.Key, g => g.Max(x => x.Member.Ability)));

        public int CoachAbility(ClubId club) => _coaches.GetValueOrDefault(club, TrainingConditions.MissingCoachAbility);

        public int FitnessCoachAbility(ClubId club) => _fitness.GetValueOrDefault(club, TrainingConditions.MissingCoachAbility);

        /// <summary>The training a club's player gets: the stored plan, else balanced and normal.</summary>
        public TrainingConditions Conditions(ClubId club, PlayerId player, TrainingPlan? plan) => new(
            plan?.TeamFocus ?? TrainingFocus.Balanced,
            plan?.FocusOf(player),
            plan?.Intensity ?? TrainingIntensity.Normal,
            CoachAbility(club),
            FitnessCoachAbility(club));
    }

    /// <summary>What a week of development needs about every player and club, loaded once.</summary>
    private sealed class World
    {
        private Staff _staff = null!;

        public required DateOnly Today { get; init; }

        public required int SeasonYear { get; init; }

        public required IReadOnlyList<Player> Players { get; init; }

        public required Dictionary<PlayerId, ClubId> Members { get; init; }

        public required Dictionary<ClubId, TrainingPlan> Plans { get; init; }

        public required Dictionary<ClubId, int> DivisionLevels { get; init; }

        public required HashSet<ClubId> International { get; init; }

        /// <summary>Minutes per player over the last <see cref="PlayingTimeDays"/> days, and over the last seven.</summary>
        public required Dictionary<PlayerId, int> Minutes { get; init; }

        public required Dictionary<PlayerId, int> WeekMinutes { get; init; }

        /// <summary>Games each club played (not forfeited) over the last <see cref="PlayingTimeDays"/> days.</summary>
        public required Dictionary<ClubId, int> ClubGames { get; init; }

        public int CoachAbility(ClubId club) => _staff.CoachAbility(club);

        public int FitnessCoachAbility(ClubId club) => _staff.FitnessCoachAbility(club);

        /// <summary>The development input of a player; <paramref name="plan"/> overrides his club's stored plan (training screen).</summary>
        public DevelopmentInput Input(Player player, int overall, TrainingPlan? plan)
        {
            ClubId? club = Members.TryGetValue(player.Id, out var current) ? current : null;
            var training = club is { } id ? _staff.Conditions(id, player.Id, plan ?? Plans.GetValueOrDefault(id)) : TrainingConditions.Alone;
            double? share = club is { } c && ClubGames.GetValueOrDefault(c) is > 0 and var games
                ? Minutes.GetValueOrDefault(player.Id) / (double)(games * PlayerDevelopment.GameMinutes)
                : null;
            return new DevelopmentInput(
                player.AgeOn(Today),
                overall,
                Math.Max(overall, player.Potential),
                player.Attributes,
                player.Fitness,
                player.InjuryStatus,
                share,
                WeekMinutes.GetValueOrDefault(player.Id),
                training,
                club is { } d ? DivisionLevels.GetValueOrDefault(d, DefaultDivisionLevel) : null,
                club is { } i && International.Contains(i));
        }

        /// <summary>Loads the world for every active player, or only for <paramref name="only"/>.</summary>
        public static async Task<World> LoadAsync(IServiceProvider s, DateOnly today, IReadOnlyCollection<PlayerId>? only, CancellationToken cancellationToken)
        {
            List<PlayerId> ids = only is null ? [] : [.. only];
            var players = await Repo<Player, PlayerId>(s)
                .ListAsync(only is null ? p => p.RetiredOn == null : p => p.RetiredOn == null && ids.Contains(p.Id), cancellationToken).ConfigureAwait(false);
            var seasons = await Repo<Season, SeasonId>(s).ListAsync(x => x.StartDate <= today, cancellationToken).ConfigureAwait(false);
            var season = seasons.Where(x => x.EndDate >= today).OrderBy(x => x.StartDate).FirstOrDefault() ?? seasons.MaxBy(x => x.StartDate);

            Dictionary<ClubId, int> levels = [];
            HashSet<ClubId> international = [];
            if (season is not null)
            {
                var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false);
                var competitions = (await Repo<Competition, CompetitionId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
                var divisions = (await Repo<Division, DivisionId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Level);
                foreach (var entry in entries)
                {
                    if (!competitions.TryGetValue(entry.CompetitionId, out var competition))
                    {
                        continue;
                    }

                    if (competition.DivisionId is { } division && divisions.TryGetValue(division, out var level))
                    {
                        levels[entry.ClubId] = Math.Min(levels.GetValueOrDefault(entry.ClubId, level), level);
                    }
                    else if (competition.Type == CompetitionType.Cup && competition.CountryId is null)
                    {
                        international.Add(entry.ClubId);
                    }
                }
            }

            var from = today.AddDays(1 - PlayingTimeDays);
            var weekFrom = today.AddDays(-6);
            var fixtures = (await Repo<Fixture, FixtureId>(s).ListAsync(f => f.Date >= from && f.Date <= today, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
            List<FixtureId> fixtureIds = [.. fixtures.Keys];
            var matches = (await Repo<Match, MatchId>(s).ListAsync(m => fixtureIds.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.Id, m => fixtures[m.FixtureId]);
            List<MatchId> matchIds = [.. matches.Keys];
            var lines = matchIds.Count == 0
                ? []
                : await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListAsync(x => matchIds.Contains(x.MatchId), cancellationToken).ConfigureAwait(false);
            var clubGames = matches.Values.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

            var world = new World
            {
                Today = today,
                SeasonYear = season?.StartDate.Year ?? today.Year,
                Players = players,
                Members = await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false),
                Plans = (await Repo<TrainingPlan, TrainingPlanId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.ClubId),
                DivisionLevels = levels,
                International = international,
                Minutes = lines.GroupBy(x => x.PlayerId).ToDictionary(g => g.Key, g => g.Sum(x => x.BoxScore.Minutes)),
                WeekMinutes = lines.Where(x => matches[x.MatchId].Date >= weekFrom).GroupBy(x => x.PlayerId).ToDictionary(g => g.Key, g => g.Sum(x => x.BoxScore.Minutes)),
                ClubGames = clubGames,
            };
            world._staff = Staff.From(await StaffHiring.StaffAsync(s, today, null, CoachRoles, cancellationToken).ConfigureAwait(false));
            return world;
        }
    }
}
