using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Ebm.Services.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Scouting;

/// <summary>Implements <see cref="IScoutingService"/> over the repositories and the <see cref="ScoutingEstimator"/>.</summary>
internal sealed class ScoutingService(
    IServiceScopeFactory scopes,
    ScoutingEstimator estimator,
    TransferNegotiator fees,
    ContractNegotiator salaries,
    GameClock clock) : IScoutingService
{
    /// <summary>Length of a scout's contract, in years.</summary>
    public const int ScoutContractYears = 3;

    /// <summary>A player reported on this recently is watched again only when nobody new is left to watch.</summary>
    public const int RecentReportDays = 90;

    private DateOnly Today => clock.Today;

    public async Task<IReadOnlyList<Scout>> GetScoutsAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var scouts = await ScoutsAsync(s, Today, clubId, cancellationToken).ConfigureAwait(false);
        var active = await LinesAsync(s, (await Repo<ScoutingAssignment, ScoutingAssignmentId>(s).ListAsync(a => a.ClubId == clubId && a.Status == AssignmentStatus.Active, cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
        return [.. scouts.Select(x => new Scout(x.Member, x.Contract, active.FirstOrDefault(a => a.Assignment.ScoutId == x.Member.Id))).OrderByDescending(x => x.Member.Ability).ThenBy(x => x.Name)];
    }

    public async Task<AssignmentLine> AssignAsync(ClubId clubId, AssignmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = Today;
        var club = await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database.");
        var scout = (await ScoutsAsync(s, today, clubId, cancellationToken).ConfigureAwait(false)).Select(x => x.Member).FirstOrDefault(m => m.Id == request.ScoutId)
            ?? throw new DomainValidationException($"The scout does not work for {club.Name}.");
        var assignments = Repo<ScoutingAssignment, ScoutingAssignmentId>(s);
        if ((await assignments.ListAsync(a => a.ScoutId == scout.Id && a.Status == AssignmentStatus.Active, cancellationToken).ConfigureAwait(false)).FirstOrDefault() is { } busy)
        {
            throw new DomainValidationException($"{scout.FullName} is busy until {busy.DueDate:yyyy-MM-dd}; cancel that assignment first.");
        }

        if (request.PlayerId is { } playerId)
        {
            var player = await Repo<Player, PlayerId>(s).FindAsync(playerId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The player no longer exists in the database.");
            if ((await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false)).GetValueOrDefault(playerId) == clubId)
            {
                throw new DomainValidationException($"{player.FullName} already plays for {club.Name}: there is nothing to scout.");
            }
        }

        if (request.CountryId is { } countryId && await Repo<Country, CountryId>(s).FindAsync(countryId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new DomainException("The country no longer exists in the database.");
        }

        if (request.CompetitionId is { } competitionId && await Repo<Competition, CompetitionId>(s).FindAsync(competitionId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new DomainException("The competition no longer exists in the database.");
        }

        var assignment = request.Focus == ScoutingFocus.Player
            ? ScoutingAssignment.ForPlayer(ScoutingAssignmentId.New(), clubId, scout.Id, today, request.PlayerId ?? throw new DomainValidationException("A player assignment needs a player."))
            : ScoutingAssignment.ForGroup(ScoutingAssignmentId.New(), clubId, scout.Id, request.Focus, today, request.CountryId, request.CompetitionId, request.Position, request.MaxAge);
        assignments.Add(assignment);
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await LinesAsync(s, [assignment], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task CancelAsync(ScoutingAssignmentId assignmentId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var assignment = await Repo<ScoutingAssignment, ScoutingAssignmentId>(s).FindAsync(assignmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The assignment no longer exists in the database.");
        assignment.Cancel(Today);
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AssignmentLine>> GetAssignmentsAsync(ClubId clubId, bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var list = await Repo<ScoutingAssignment, ScoutingAssignmentId>(s)
            .ListAsync(a => a.ClubId == clubId && (!activeOnly || a.Status == AssignmentStatus.Active), cancellationToken).ConfigureAwait(false);
        return [.. (await LinesAsync(s, list, cancellationToken).ConfigureAwait(false)).OrderByDescending(a => a.Assignment.StartDate).ThenByDescending(a => a.Assignment.Id.Value)];
    }

    public async Task<IReadOnlyList<ReportLine>> SearchReportsAsync(ClubId clubId, ReportSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var newest = (await Repo<ScoutReport, ScoutReportId>(s).ListAsync(r => r.ClubId == clubId, cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.PlayerId)
            .Select(g => g.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id.Value).First())
            .ToList();
        var name = search.Name?.Trim();
        return
        [
            .. (await ReportLinesAsync(s, newest, cancellationToken).ConfigureAwait(false))
                .Where(l => string.IsNullOrEmpty(name) || l.Player.FullName.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                .Where(l => search.Position is not { } position || l.Player.Position == position || l.Player.SecondaryPositions.Contains(position))
                .Where(l => search.MaxAge is not { } maxAge || l.Age <= maxAge)
                .Where(l => search.MinOverall is not { } minOverall || l.Report.EstimatedOverall >= minOverall)
                .Where(l => search.MinPotential is not { } minPotential || l.Report.EstimatedPotential >= minPotential)
                .Where(l => search.MaxSalary is not { } maxSalary || l.Report.EstimatedSalary <= maxSalary)
                .Where(l => search.NationalityId is not { } nationality || l.Player.NationalityId == nationality)
                .OrderByDescending(l => l.Report.EstimatedOverall)
                .ThenByDescending(l => l.Report.EstimatedPotential)
                .ThenBy(l => l.Player.LastName)
                .Take(Math.Max(1, search.MaxResults)),
        ];
    }

    public async Task<IReadOnlyList<ReportLine>> GetPlayerReportsAsync(ClubId clubId, PlayerId playerId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var reports = await Repo<ScoutReport, ScoutReportId>(s).ListAsync(r => r.ClubId == clubId && r.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        return [.. (await ReportLinesAsync(s, reports, cancellationToken).ConfigureAwait(false)).OrderByDescending(l => l.Report.Date).ThenByDescending(l => l.Report.Id.Value)];
    }

    public async Task<int> HireMissingScoutsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = Today;
        var clubs = await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false);
        var employed = (await ScoutsAsync(s, today, null, cancellationToken).ConfigureAwait(false)).GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.Count());
        var missing = clubs.Select(c => (Club: c, Count: ScoutingRules.ScoutsFor(c.Reputation) - employed.GetValueOrDefault(c.Id))).Where(x => x.Count > 0).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        // Names recombine the players' names of the club's country (data, not code), else of every player.
        var players = await Repo<Player, PlayerId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false);
        var byCountry = players.GroupBy(p => p.NationalityId).ToDictionary(g => g.Key, g => g.ToList());
        var staff = Repo<StaffMember, StaffMemberId>(s);
        var contracts = Repo<Contract, ContractId>(s);
        var hired = 0;
        foreach (var (club, count) in missing)
        {
            var pool = byCountry.GetValueOrDefault(club.CountryId) is { Count: > 0 } local ? local : players;
            if (pool.Count == 0)
            {
                continue;
            }

            var random = new Random(ScoutingRules.StableSeed(club.Id.Value, Guid.Empty, today.DayNumber));
            for (var i = 0; i < count; i++)
            {
                var ability = Math.Clamp(ScoutingRules.TypicalAbility(club.Reputation) + random.Next(-10, 11), StaffMember.MinAbility, StaffMember.MaxAbility);
                var scout = new StaffMember(
                    StaffMemberId.New(),
                    pool[random.Next(pool.Count)].FirstName,
                    pool[random.Next(pool.Count)].LastName,
                    today.AddYears(-random.Next(35, 61)).AddDays(-random.Next(0, 365)),
                    club.CountryId,
                    StaffRole.Scout,
                    ability);
                staff.Add(scout);
                contracts.Add(Contract.ForStaff(ContractId.New(), club.Id, scout.Id, today, today.AddYears(ScoutContractYears).AddDays(-1), ScoutSalary(ability)));
                hired++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return hired;
    }

    public async Task<ScoutingDay> ProcessDayAsync(CancellationToken cancellationToken = default)
    {
        var hired = await HireMissingScoutsAsync(cancellationToken).ConfigureAwait(false);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = Today;
        var due = (await Repo<ScoutingAssignment, ScoutingAssignmentId>(s).ListAsync(a => a.Status == AssignmentStatus.Active && a.DueDate <= today, cancellationToken).ConfigureAwait(false))
            .OrderBy(a => a.DueDate).ThenBy(a => a.Id.Value)
            .ToList();
        if (due.Count == 0)
        {
            return new ScoutingDay(hired, 0, 0, 0);
        }

        var world = await World.LoadAsync(s, today, cancellationToken).ConfigureAwait(false);
        var scouts = (await ScoutsAsync(s, today, null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Member.Id, x => x.Member);
        var reports = Repo<ScoutReport, ScoutReportId>(s);
        var clubIds = due.Select(a => a.ClubId).Distinct().ToList();
        var known = (await reports.ListAsync(r => clubIds.Contains(r.ClubId), cancellationToken).ConfigureAwait(false))
            .GroupBy(r => (r.ClubId, r.PlayerId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).First());
        int completed = 0, cancelled = 0, written = 0;
        foreach (var assignment in due)
        {
            if (!scouts.TryGetValue(assignment.ScoutId, out var scout) || !world.Clubs.TryGetValue(assignment.ClubId, out var club))
            {
                assignment.Cancel(today); // the scout left the club before finishing
                cancelled++;
                continue;
            }

            var attractiveness = world.Attractiveness(club);
            foreach (var player in Targets(world, assignment, club, known))
            {
                var previous = known.GetValueOrDefault((club.Id, player.Id));
                var accuracy = ScoutingRules.Combine(
                    ScoutingRules.Accuracy(scout.Ability, ScoutingRules.StudyDays(assignment.Focus, assignment.Days), world.Visibility(player, club)),
                    previous?.Accuracy);
                var report = Write(world, club, player, scout.Id, assignment.Id, accuracy, attractiveness);
                reports.Add(report);
                known[(club.Id, player.Id)] = report;
                written++;
            }

            assignment.Complete(today);
            completed++;
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ScoutingDay(hired, completed, cancelled, written);
    }

    /// <summary>Annual salary of a scout: € 20,000 plus € 600 per ability point.</summary>
    public static long ScoutSalary(int ability) => 20_000 + (600L * ability);

    /// <summary>The players an assignment reports on: its target, or up to <see cref="ScoutingRules.ReportsPerAssignment"/> of the most visible players of its group not recently reported on.</summary>
    private static IEnumerable<Player> Targets(World world, ScoutingAssignment assignment, Club club, Dictionary<(ClubId, PlayerId), ScoutReport> known)
    {
        if (assignment.Focus == ScoutingFocus.Player)
        {
            return world.PlayerById.TryGetValue(assignment.PlayerId!.Value, out var target) ? [target] : [];
        }

        var entered = assignment.CompetitionId is { } competitionId ? world.ClubsIn(competitionId) : null;
        return world.Players
            .Where(p => world.ClubOf(p) is not { } current || current.Id != club.Id)
            .Where(p => assignment.Focus switch
            {
                ScoutingFocus.Region => InCountry(world, p, assignment.CountryId!.Value),
                ScoutingFocus.League => world.ClubOf(p) is { } current && entered!.Contains(current.Id),
                ScoutingFocus.Youth => assignment.CountryId is not { } country || InCountry(world, p, country),
                _ => true,
            })
            .Where(p => assignment.Position is not { } position || p.Position == position || p.SecondaryPositions.Contains(position))
            .Where(p => assignment.MaxAge is not { } maxAge || p.AgeOn(world.Today) <= maxAge)
            .OrderBy(p => known.TryGetValue((club.Id, p.Id), out var last) && last.Date.AddDays(RecentReportDays) > world.Today)
            .ThenByDescending(p => world.Visibility(p, club))
            .ThenByDescending(p => p.Reputation)
            .ThenBy(p => p.Id.Value)
            .Take(ScoutingRules.ReportsPerAssignment)
            .ToList();
    }

    /// <summary>Plays in the country, or (free agent) holds its nationality.</summary>
    private static bool InCountry(World world, Player player, CountryId country) =>
        world.ClubOf(player) is { } current ? current.CountryId == country : player.HasNationality(c => c == country);

    private ScoutReport Write(World world, Club club, Player player, StaffMemberId? scoutId, ScoutingAssignmentId? assignmentId, int accuracy, int attractiveness)
    {
        var estimate = estimator.Estimate(player, accuracy, ScoutingRules.StableSeed(club.Id.Value, player.Id.Value, world.Today.DayNumber));
        var age = player.AgeOn(world.Today);
        var contract = world.ContractOf(player.Id);
        var yearsLeft = contract is null ? 0 : Math.Max(0, (contract.EffectiveEndDate.DayNumber - world.Today.DayNumber + 1) / 365.25);
        var value = fees.MarketValue(new ValuationContext(estimate.Overall, estimate.Potential, player.Reputation, age, yearsLeft));
        var salary = salaries.AskingSalary(new NegotiationContext(estimate.Overall, estimate.Potential, player.Reputation, age, attractiveness, contract?.AnnualSalary));
        return new ScoutReport(ScoutReportId.New(), club.Id, player.Id, scoutId, assignmentId, world.Today, estimate, value, salary);
    }

    private async Task<IReadOnlyList<AssignmentLine>> LinesAsync(IServiceProvider s, IReadOnlyList<ScoutingAssignment> assignments, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
        {
            return [];
        }

        var scoutIds = assignments.Select(a => a.ScoutId).Distinct().ToList();
        var scouts = (await Repo<StaffMember, StaffMemberId>(s).ListAsync(m => scoutIds.Contains(m.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id, m => m.FullName);
        var playerIds = assignments.Where(a => a.PlayerId != null).Select(a => a.PlayerId!.Value).Distinct().ToList();
        var players = (await Repo<Player, PlayerId>(s).ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);
        var countries = (await Repo<Country, CountryId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var competitions = (await Repo<Competition, CompetitionId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var clubIds = assignments.Select(a => a.ClubId).Distinct().ToList();
        var counts = (await Repo<ScoutReport, ScoutReportId>(s).ListAsync(r => clubIds.Contains(r.ClubId) && r.AssignmentId != null, cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.AssignmentId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return
        [
            .. assignments.Select(a =>
            {
                var target = a.Focus switch
                {
                    ScoutingFocus.Player => $"Player: {players.GetValueOrDefault(a.PlayerId!.Value, "?")}",
                    ScoutingFocus.Region => $"Region: {countries.GetValueOrDefault(a.CountryId!.Value, "?")}",
                    ScoutingFocus.League => $"League: {competitions.GetValueOrDefault(a.CompetitionId!.Value, "?")}",
                    ScoutingFocus.Position => $"Position: {PlayerCatalog.DisplayName(a.Position!.Value)}",
                    _ => a.CountryId is { } country ? $"Youth: {countries.GetValueOrDefault(country, "?")}" : "Youth",
                };
                var filters = string.Join(", ", new[]
                {
                    a.Focus != ScoutingFocus.Position && a.Position is { } position ? PlayerCatalog.DisplayName(position) : null,
                    a.MaxAge is { } maxAge ? $"age ≤ {maxAge}" : null,
                }.OfType<string>());
                return new AssignmentLine(a, scouts.GetValueOrDefault(a.ScoutId, "?"), filters.Length > 0 ? $"{target} ({filters})" : target, counts.GetValueOrDefault(a.Id));
            }),
        ];
    }

    private static async Task<IReadOnlyList<ReportLine>> ReportLinesAsync(IServiceProvider s, IReadOnlyList<ScoutReport> reports, CancellationToken cancellationToken)
    {
        if (reports.Count == 0)
        {
            return [];
        }

        var today = s.GetRequiredService<GameClock>().Today;
        var playerIds = reports.Select(r => r.PlayerId).Distinct().ToList();
        var players = (await Repo<Player, PlayerId>(s).ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id);
        var scoutIds = reports.Where(r => r.ScoutId != null).Select(r => r.ScoutId!.Value).Distinct().ToList();
        var scouts = (await Repo<StaffMember, StaffMemberId>(s).ListAsync(m => scoutIds.Contains(m.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id, m => m.FullName);
        var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var countries = (await Repo<Country, CountryId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var members = await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false);
        return
        [
            .. reports.Where(r => players.ContainsKey(r.PlayerId)).Select(r =>
            {
                var player = players[r.PlayerId];
                return new ReportLine(
                    r,
                    player,
                    player.AgeOn(today),
                    members.TryGetValue(player.Id, out var clubId) ? clubs.GetValueOrDefault(clubId) : null,
                    countries.GetValueOrDefault(player.NationalityId, string.Empty),
                    r.ScoutId is { } scoutId ? scouts.GetValueOrDefault(scoutId, "?") : "Background knowledge");
            }),
        ];
    }

    /// <summary>Scouts with a contract running today, at one club or (<c>null</c>) every club.</summary>
    internal static async Task<IReadOnlyList<(StaffMember Member, Contract Contract)>> ScoutsAsync(IServiceProvider s, DateOnly today, ClubId? clubId, CancellationToken cancellationToken)
    {
        var running = Contract.RunningOn(today);
        var contracts = (await Repo<Contract, ContractId>(s).ListAsync(running, cancellationToken).ConfigureAwait(false))
            .Where(c => c.StaffMemberId != null && (clubId is null || c.ClubId == clubId))
            .ToList();
        var ids = contracts.Select(c => c.StaffMemberId!.Value).Distinct().ToList();
        var scouts = (await Repo<StaffMember, StaffMemberId>(s).ListAsync(m => m.Role == StaffRole.Scout && ids.Contains(m.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id);
        return [.. contracts.Where(c => scouts.ContainsKey(c.StaffMemberId!.Value)).Select(c => (scouts[c.StaffMemberId!.Value], c))];
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>What report writing needs about every player and club, loaded once.</summary>
    private sealed class World
    {
        private readonly Dictionary<PlayerId, ClubId> _members;
        private readonly Dictionary<PlayerId, Contract> _contracts;
        private readonly Dictionary<ClubId, int> _tiers;
        private readonly Dictionary<CompetitionId, HashSet<ClubId>> _entries;

        private World(DateOnly today, IReadOnlyList<Player> players, Dictionary<ClubId, Club> clubs, Dictionary<PlayerId, ClubId> members, Dictionary<PlayerId, Contract> contracts, Dictionary<ClubId, int> tiers, Dictionary<CompetitionId, HashSet<ClubId>> entries)
        {
            Today = today;
            Players = players;
            PlayerById = players.ToDictionary(p => p.Id);
            Clubs = clubs;
            _members = members;
            _contracts = contracts;
            _tiers = tiers;
            _entries = entries;
        }

        public DateOnly Today { get; }

        public IReadOnlyList<Player> Players { get; }

        public Dictionary<PlayerId, Player> PlayerById { get; }

        public Dictionary<ClubId, Club> Clubs { get; }

        public static async Task<World> LoadAsync(IServiceProvider s, DateOnly today, CancellationToken cancellationToken)
        {
            var players = await Repo<Player, PlayerId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false);
            var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
            var members = await ClubMembership.CurrentClubsAsync(s, today, cancellationToken).ConfigureAwait(false);

            // The contract that binds the player: the parent contract while on loan, else the latest running one.
            var contracts = (await Repo<Contract, ContractId>(s).ListAsync(Contract.RunningOn(today), cancellationToken).ConfigureAwait(false))
                .Where(c => c.PlayerId != null)
                .GroupBy(c => c.PlayerId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Status == ContractStatus.OnLoan).ThenByDescending(c => c.StartDate).First());
            var tiers = await ClubSeason.InternationalTiersAsync(s, today, cancellationToken).ConfigureAwait(false);
            var season = await ClubSeason.CurrentAsync(s, today, cancellationToken).ConfigureAwait(false);
            var entries = season is null
                ? []
                : (await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false))
                    .GroupBy(p => p.CompetitionId)
                    .ToDictionary(g => g.Key, g => g.Select(p => p.ClubId).ToHashSet());
            return new World(today, players, clubs, members, contracts, tiers, entries);
        }

        public Club? ClubOf(Player player) => _members.TryGetValue(player.Id, out var id) ? Clubs.GetValueOrDefault(id) : null;

        public Contract? ContractOf(PlayerId playerId) => _contracts.GetValueOrDefault(playerId);

        public IReadOnlySet<ClubId> ClubsIn(CompetitionId competitionId) => _entries.GetValueOrDefault(competitionId) ?? [];

        public int Attractiveness(Club club) => ClubAttractiveness.Of(club.Reputation, _tiers.TryGetValue(club.Id, out var tier) ? tier : null);

        public int Visibility(Player player, Club scoutingClub)
        {
            var current = ClubOf(player);
            var sameCountry = current is not null ? current.CountryId == scoutingClub.CountryId : player.HasNationality(c => c == scoutingClub.CountryId);
            return ScoutingRules.Visibility(player.Reputation, sameCountry, current is not null && _tiers.ContainsKey(current.Id), player.AgeOn(Today));
        }
    }
}
