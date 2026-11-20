using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Development;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Medical;

/// <summary>Implements <see cref="IMedicalService"/> over the repositories and the pure <see cref="InjuryRules"/>.</summary>
internal sealed class MedicalService(IServiceScopeFactory scopes, InjuryRules rules, GameClock clock) : IMedicalService
{
    private const int MatchSalt = 0x3A11;
    private const int TrainingSalt = 0x3B22;
    private const int AssessmentSalt = 0x3C33;

    private static readonly StaffRole[] PhysioRole = [StaffRole.Physio];

    /// <summary>Annual salary of a generated physio: € 20,000 + € 600 per ability point.</summary>
    public static long PhysioSalary(int ability) => StaffRules.BaseSalary(StaffRole.Physio, ability);

    public async Task<int> HireMissingPhysiosAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return await StaffHiring.HireMissingAsync(scope.ServiceProvider, clock.Today, StaffRole.Physio, 0x4000, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MedicalDay> ProcessDayAsync(IReadOnlyCollection<MatchId> playedToday, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playedToday);
        var today = clock.Today;
        var monday = today.DayOfWeek == DayOfWeek.Monday;
        var hired = monday ? await HireMissingPhysiosAsync(cancellationToken).ConfigureAwait(false) : 0;
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var world = await World.LoadAsync(s, today, rules, playedToday, null, cancellationToken).ConfigureAwait(false);
        var injuries = Repo<Injury, InjuryId>(s);
        var news = new List<InjuryNews>();
        int aggravated = 0, recovered = 0;
        foreach (var player in world.Players)
        {
            var active = world.ActiveInjury(player.Id);
            var physio = world.PhysioAbility(player.Id);
            var age = player.AgeOn(today);
            if (active is null && player.InjuryStatus != InjuryStatus.Healthy && (player.InjuryReturnDate is not { } back || back <= today))
            {
                // A condition set by the editor or an import, without a medical record: it ends on its return date.
                player.UpdateCondition(player.Fitness, player.Morale, InjuryStatus.Healthy, null);
                recovered++;
            }

            if (active is not null && active.OccurredOn < today)
            {
                active.Recover(today, InjuryRules.RecoverySpeed(physio, age));
                if (!active.IsActive)
                {
                    player.UpdateCondition(Math.Min(player.Fitness, InjuryRules.ReturnFitness(active.DaysOut(today))), player.Morale, InjuryStatus.Healthy, null);
                    recovered++;
                    continue; // Back from tomorrow: no training risk on the last day of rehabilitation.
                }

                if (monday)
                {
                    Assess(active, player, today, physio, age);
                }
            }

            if (rules.Frequency <= 0)
            {
                continue;
            }

            var (minutes, matchId) = world.Played.GetValueOrDefault(player.Id);
            InjurySource? source = null;
            var factors = world.Factors(player, age, physio, active);
            if (minutes > 0)
            {
                var random = new Random(ScoutingRules.StableSeed(player.Id.Value, matchId!.Value.Value, today.DayNumber ^ MatchSalt));
                source = random.NextDouble() < rules.MatchChance(minutes, factors) ? InjurySource.Match : null;
            }
            else if (world.Training(player.Id) is { } intensity && active?.Restriction != TrainingRestriction.NoTraining)
            {
                var random = new Random(ScoutingRules.StableSeed(player.Id.Value, Guid.Empty, today.DayNumber ^ TrainingSalt));
                source = random.NextDouble() < rules.TrainingChance(intensity, active is not null, factors) ? InjurySource.Training : null;
            }

            if (source is not { } where)
            {
                continue;
            }

            var roll = new Random(ScoutingRules.StableSeed(player.Id.Value, Guid.Empty, today.DayNumber ^ (MatchSalt + TrainingSalt)));
            Injury injury;
            if (active is not null)
            {
                active.Aggravate(InjuryRules.AggravationDays(roll));
                injury = active;
                aggravated++;
            }
            else
            {
                var recent = world.RecentParts(player.Id);
                var definition = InjuryRules.ChooseType(where, recent, roll);
                var recurrence = recent.Contains(definition.BodyPart);
                injury = new Injury(InjuryId.New(), player.Id, world.ClubOf(player.Id), definition.Type, where, today, rules.RecoveryDays(definition, recurrence, roll), recurrence, where == InjurySource.Match ? matchId : null);
                injuries.Add(injury);
            }

            Assess(injury, player, today, physio, age);
            news.Add(new InjuryNews(player.Id, player.FullName, world.ClubOf(player.Id), injury.Type, injury.Severity, where, injury.EstimatedReturn, active is not null));
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new MedicalDay(news, aggravated, recovered, hired);
    }

    public async Task<ClubMedical?> GetClubMedicalAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        if (await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var (members, _) = await ClubMembership.MembersOfAsync(s, clubId, today, cancellationToken).ConfigureAwait(false);
        var world = await World.LoadAsync(s, today, rules, [], members, cancellationToken).ConfigureAwait(false);
        var physio = (await StaffHiring.StaffAsync(s, today, clubId, PhysioRole, cancellationToken).ConfigureAwait(false)).Select(x => x.Member).MaxBy(m => m.Ability);
        var rows = world.Players
            .Select(p =>
            {
                var active = world.ActiveInjury(p.Id);
                var age = p.AgeOn(today);
                var factors = world.Factors(p, age, world.PhysioAbility(p.Id), active);
                return new MedicalPlayer(p, age, active, Math.Round(InjuryRules.RiskMultiplier(factors), 2), factors.RecentInjuries + (active is null ? 0 : 1));
            })
            .OrderByDescending(r => r.Injury is not null).ThenBy(r => r.Injury?.EstimatedReturn).ThenBy(r => r.Player.LastName)
            .ToList();
        var from = today.AddDays(-rules.RecurrenceWindowDays);
        var history = (await Repo<Injury, InjuryId>(s).ListAsync(i => i.ClubId == clubId && i.OccurredOn >= from, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(i => i.OccurredOn).ToList();
        List<PlayerId> ids = [.. history.Select(i => i.PlayerId).Distinct()];
        var names = (await Repo<Player, PlayerId>(s).ListAsync(p => ids.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);
        return new ClubMedical(club, physio, physio?.Ability ?? TrainingConditions.MissingCoachAbility, rows, [.. history.Select(i => new InjuryRecord(i, names.GetValueOrDefault(i.PlayerId, "?")))]);
    }

    public async Task<IReadOnlyList<Injury>> GetInjuriesAsync(PlayerId playerId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return [.. (await Repo<Injury, InjuryId>(scope.ServiceProvider).ListAsync(i => i.PlayerId == playerId, cancellationToken).ConfigureAwait(false)).OrderByDescending(i => i.OccurredOn)];
    }

    /// <summary>Medical assessment: a new estimated return date, and the player's condition follows the injury.</summary>
    private static void Assess(Injury injury, Player player, DateOnly today, int physio, int age)
    {
        var random = new Random(ScoutingRules.StableSeed(injury.Id.Value, player.Id.Value, today.DayNumber ^ AssessmentSalt));
        injury.Assess(today, InjuryRules.EstimateReturn(today, injury.RemainingDays, InjuryRules.RecoverySpeed(physio, age), physio, random));
        player.UpdateCondition(player.Fitness, player.Morale, injury.Status, injury.EstimatedReturn);
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>What a medical day needs about players, clubs, staff and injuries, loaded once.</summary>
    private sealed class World
    {
        public required InjuryRules Rules { get; init; }

        public required DateOnly Date { get; init; }

        public required IReadOnlyList<Player> Players { get; init; }

        public required Dictionary<PlayerId, ClubId> Members { get; init; }

        public required Dictionary<ClubId, TrainingIntensity> Intensities { get; init; }

        public required Dictionary<ClubId, int> Physios { get; init; }

        /// <summary>Injuries in the recurrence window or still active, per player.</summary>
        public required Dictionary<PlayerId, List<Injury>> Injuries { get; init; }

        public required Dictionary<PlayerId, int> WeekMinutes { get; init; }

        /// <summary>Minutes played today and the match they were played in.</summary>
        public required Dictionary<PlayerId, (int Minutes, MatchId? Match)> Played { get; init; }

        public ClubId? ClubOf(PlayerId player) => Members.TryGetValue(player, out var club) ? club : null;

        public int PhysioAbility(PlayerId player) =>
            ClubOf(player) is { } club ? Physios.GetValueOrDefault(club, TrainingConditions.MissingCoachAbility) : TrainingConditions.MissingCoachAbility;

        /// <summary>The club's training intensity (normal without a plan); <c>null</c> for a free agent (no team training).</summary>
        public TrainingIntensity? Training(PlayerId player) =>
            ClubOf(player) is { } club ? Intensities.GetValueOrDefault(club, TrainingIntensity.Normal) : null;

        public Injury? ActiveInjury(PlayerId player) => Injuries.GetValueOrDefault(player)?.FirstOrDefault(i => i.IsActive);

        public IReadOnlySet<BodyPart> RecentParts(PlayerId player) => (Injuries.GetValueOrDefault(player) ?? []).Select(i => i.BodyPart).ToHashSet();

        public InjuryRiskFactors Factors(Player player, int age, int physio, Injury? active)
        {
            var own = Injuries.GetValueOrDefault(player.Id) ?? [];
            var returnedFrom = Date.AddDays(-Rules.ReturnWindowDays);
            return new InjuryRiskFactors(
                age,
                player.Fitness,
                WeekMinutes.GetValueOrDefault(player.Id),
                player.Attributes.Strength,
                player.Attributes.Stamina,
                physio,
                own.Count(i => !i.IsActive),
                own.Any(i => i.RecoveredOn is { } back && back > returnedFrom),
                active is not null,
                InjuryRules.Proneness(player.Id));
        }

        /// <summary>Loads the world for every active player, or only for <paramref name="only"/>.</summary>
        public static async Task<World> LoadAsync(
            IServiceProvider s, DateOnly today, InjuryRules rules, IReadOnlyCollection<MatchId> playedToday, IReadOnlyCollection<PlayerId>? only, CancellationToken cancellationToken)
        {
            List<PlayerId> ids = only is null ? [] : [.. only];
            var players = await Repo<Player, PlayerId>(s)
                .ListAsync(only is null ? p => p.RetiredOn == null : p => p.RetiredOn == null && ids.Contains(p.Id), cancellationToken).ConfigureAwait(false);
            var from = today.AddDays(-rules.RecurrenceWindowDays);
            var injuries = (await Repo<Injury, InjuryId>(s).ListAsync(i => i.RecoveredOn == null || i.OccurredOn >= from, cancellationToken).ConfigureAwait(false))
                .GroupBy(i => i.PlayerId).ToDictionary(g => g.Key, g => g.ToList());

            var weekFrom = today.AddDays(-6);
            List<FixtureId> fixtures = [.. (await Repo<Fixture, FixtureId>(s).ListUntrackedAsync(f => f.Date >= weekFrom && f.Date <= today, cancellationToken).ConfigureAwait(false)).Select(f => f.Id)];
            List<MatchId> matches = [.. (await Repo<Match, MatchId>(s).ListUntrackedAsync(m => fixtures.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false)).Select(m => m.Id), .. playedToday];
            var lines = matches.Count == 0
                ? []
                : (await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(x => matches.Contains(x.MatchId), cancellationToken).ConfigureAwait(false))
                    .DistinctBy(x => x.Id).ToList();
            var played = playedToday.ToHashSet();

            var staff = await StaffHiring.StaffAsync(s, today, null, PhysioRole, cancellationToken).ConfigureAwait(false);
            return new World
            {
                Rules = rules,
                Date = today,
                Players = players,
                Members = await ClubMembership.SavedClubsAsync(s, today, cancellationToken).ConfigureAwait(false),
                Intensities = (await Repo<TrainingPlan, TrainingPlanId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.ClubId, p => p.Intensity),
                Physios = staff.GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.Max(x => x.Member.Ability)),
                Injuries = injuries,
                WeekMinutes = lines.GroupBy(x => x.PlayerId).ToDictionary(g => g.Key, g => g.Sum(x => x.BoxScore.Minutes)),
                Played = lines.Where(x => played.Contains(x.MatchId) && x.BoxScore.Minutes > 0)
                    .GroupBy(x => x.PlayerId)
                    .ToDictionary(g => g.Key, g => (g.Sum(x => x.BoxScore.Minutes), (MatchId?)g.First().MatchId)),
            };
        }
    }
}
