using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Clubs;

/// <summary>Implements <see cref="IClubService"/> over the repositories and the <see cref="RosterValidator"/>.</summary>
internal sealed class ClubService : IClubService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PlayerRatingCalculator _calculator;
    private readonly GameClock _time;

    public ClubService(IServiceScopeFactory scopes, PlayerRatingCalculator calculator, GameClock time)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(time);
        _scopes = scopes;
        _calculator = calculator;
        _time = time;
    }

    private DateOnly Today => _time.Today;

    public async Task<ClubProfile?> GetProfileAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        if (await data.LoadRosterAsync(clubId, _calculator, cancellationToken).ConfigureAwait(false) is not { } roster)
        {
            return null;
        }

        var club = roster.Club;
        var arena = club.HomeArenaId is { } arenaId ? await data.Repo<Arena, ArenaId>().FindAsync(arenaId, cancellationToken).ConfigureAwait(false) : null;
        var country = await data.Repo<Country, CountryId>().FindAsync(club.CountryId, cancellationToken).ConfigureAwait(false);
        var wageBill = (await data.RunningContractsAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.ClubId == clubId && c.Status != ContractStatus.OnLoan) // the loan club pays a loaned player (docs/TRANSFERS.md §4)
            .Sum(c => c.AnnualSalary);
        var (season, competitions) = await data.CurrentCompetitionsAsync(clubId, cancellationToken).ConfigureAwait(false);
        return new ClubProfile(roster, country?.Name ?? string.Empty, club.City ?? arena?.City, arena, season, competitions, wageBill);
    }

    public async Task<ClubRoster?> GetRosterAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        return await new Data(scope.ServiceProvider, Today).LoadRosterAsync(clubId, _calculator, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClubRoster> SaveRosterAsync(ClubId clubId, IReadOnlyList<RosterAssignment> assignments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        using (var scope = _scopes.CreateScope())
        {
            var data = new Data(scope.ServiceProvider, Today);
            _ = await data.Repo<Club, ClubId>().FindAsync(clubId, cancellationToken).ConfigureAwait(false)
                ?? throw new DomainException("The club no longer exists in the database.");
            var members = (await data.MembersAsync(cancellationToken).ConfigureAwait(false))
                .Where(m => m.Value == clubId)
                .Select(m => m.Key)
                .ToHashSet();
            var listed = assignments.Select(a => a.PlayerId).ToList();
            if (listed.Distinct().Count() != listed.Count)
            {
                throw new DomainValidationException("A player is listed twice in the squad.");
            }

            if (!members.SetEquals(listed))
            {
                throw new DomainException("The squad has changed since it was loaded. Reload it and try again.");
            }

            var spots = data.Repo<RosterSpot, RosterSpotId>();
            var existing = (await spots.ListAsync(s => listed.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false))
                .ToDictionary(s => s.PlayerId);
            var rotationOrder = 0;
            foreach (var assignment in assignments)
            {
                int? order = assignment.Role == RosterRole.Rotation ? ++rotationOrder : null;
                if (existing.TryGetValue(assignment.PlayerId, out var spot))
                {
                    // A spot at another club is left over from a move made through the editor or an import.
                    spot.MoveTo(clubId);
                    spot.Assign(assignment.Role, assignment.LineupPosition, order);
                }
                else
                {
                    spots.Add(new RosterSpot(RosterSpotId.New(), assignment.PlayerId, clubId, assignment.Role, assignment.LineupPosition, order));
                }
            }

            // Spots of players who are no longer at the club are current state, not history: drop them.
            foreach (var stale in await spots.ListAsync(s => s.ClubId == clubId && !listed.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false))
            {
                spots.Remove(stale);
            }

            await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The club no longer exists in the database.");
    }

    public async Task AssignPlayerAsync(PlayerId playerId, ClubId clubId, RosterRole role = RosterRole.Bench, CancellationToken cancellationToken = default)
    {
        if (role is not (RosterRole.Bench or RosterRole.Inactive or RosterRole.Youth))
        {
            throw new DomainValidationException("A new player joins the bench, the inactive list or the youth squad; pick his starting or rotation role in the squad.");
        }

        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var player = await data.Repo<Player, PlayerId>().FindAsync(playerId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The player no longer exists in the database.");
        var club = await data.Repo<Club, ClubId>().FindAsync(clubId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The club no longer exists in the database.");
        if ((await data.MembersAsync(cancellationToken).ConfigureAwait(false)).GetValueOrDefault(playerId) == clubId)
        {
            throw new DomainValidationException($"{player.FullName} already plays for {club.Name}.");
        }

        await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, clubId, data.Today, role, cancellationToken).ConfigureAwait(false);
        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The reads shared by the use cases, within one scope.</summary>
    private sealed class Data(IServiceProvider services, DateOnly today)
    {
        public DateOnly Today { get; } = today;

        public IRepository<TEntity, TId> Repo<TEntity, TId>()
            where TEntity : Entity<TId>
            where TId : struct, IEntityId<TId> =>
            services.GetRequiredService<IRepository<TEntity, TId>>();

        public Task SaveAsync(CancellationToken cancellationToken) => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        /// <summary>Player contracts running today.</summary>
        public async Task<IReadOnlyList<Contract>> RunningContractsAsync(CancellationToken cancellationToken) =>
            [.. (await Repo<Contract, ContractId>().ListAsync(Contract.RunningOn(Today), cancellationToken).ConfigureAwait(false)).Where(c => c.PlayerId is not null)];

        /// <summary>Current club of every player who has one: the open spell, else the latest running contract.</summary>
        public Task<Dictionary<PlayerId, ClubId>> MembersAsync(CancellationToken cancellationToken) =>
            ClubMembership.CurrentClubsAsync(services, Today, cancellationToken);

        /// <summary>The current season (earliest not yet ended) and the club's competitions in it.</summary>
        public async Task<(Season? Season, IReadOnlyList<Competition> Competitions)> CurrentCompetitionsAsync(ClubId clubId, CancellationToken cancellationToken)
        {
            var season = (await Repo<Season, SeasonId>().ListAsync(s => s.EndDate >= Today, cancellationToken).ConfigureAwait(false))
                .OrderBy(s => s.StartDate)
                .FirstOrDefault();
            if (season is null)
            {
                return (null, []);
            }

            var entered = (await Repo<CompetitionParticipant, CompetitionParticipantId>()
                    .ListAsync(p => p.ClubId == clubId && p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false))
                .Select(p => p.CompetitionId)
                .ToList();
            var competitions = await Repo<Competition, CompetitionId>().ListAsync(c => entered.Contains(c.Id), cancellationToken).ConfigureAwait(false);
            return (season, [.. competitions.OrderBy(c => c.Type).ThenBy(c => c.Name)]);
        }

        public async Task<ClubRoster?> LoadRosterAsync(ClubId clubId, PlayerRatingCalculator calculator, CancellationToken cancellationToken)
        {
            if (await Repo<Club, ClubId>().FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
            {
                return null;
            }

            var ids = (await MembersAsync(cancellationToken).ConfigureAwait(false)).Where(m => m.Value == clubId).Select(m => m.Key).ToList();
            var players = await Repo<Player, PlayerId>().ListAsync(p => ids.Contains(p.Id), cancellationToken).ConfigureAwait(false);
            var spots = (await Repo<RosterSpot, RosterSpotId>().ListAsync(s => s.ClubId == clubId, cancellationToken).ConfigureAwait(false))
                .ToDictionary(s => s.PlayerId);
            var contracted = (await RunningContractsAsync(cancellationToken).ConfigureAwait(false))
                .Where(c => c.ClubId == clubId)
                .Select(c => c.PlayerId!.Value)
                .ToHashSet();
            var countries = (await Repo<Country, CountryId>().ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);

            var roster = players
                .Select(p =>
                {
                    var spot = spots.GetValueOrDefault(p.Id);
                    return new RosterPlayer(
                        p,
                        countries.GetValueOrDefault(p.NationalityId) ?? string.Empty,
                        p.AgeOn(Today),
                        p.NationalityId != club.CountryId,
                        spot?.Role ?? RosterRole.Bench,
                        spot?.LineupPosition,
                        spot?.RotationOrder,
                        RosterRoles.AvailabilityOf(p),
                        contracted.Contains(p.Id),
                        calculator.Evaluate(p));
                })
                .OrderBy(p => p.Role)
                .ThenBy(p => p.LineupPosition)
                .ThenBy(p => p.RotationOrder)
                .ThenByDescending(p => p.Evaluation.Overall)
                .ThenBy(p => p.Player.LastName)
                .ToList();

            var (_, competitions) = await CurrentCompetitionsAsync(clubId, cancellationToken).ConfigureAwait(false);
            var (validation, registrations) = ClubRoster.Check([.. roster.Select(p => p.ToMember())], competitions);
            return new ClubRoster(club, Today, roster, validation, registrations);
        }
    }
}
