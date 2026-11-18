using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Chemistry;

/// <summary>How a player feels about his playing time (docs/CHEMISTRY.md §2).</summary>
public enum RoleSatisfaction
{
    /// <summary>Not judged: too few club games, injured, or covered by a contract promise.</summary>
    NotJudged,
    Unhappy,
    Content,
    Happy,
}

/// <summary>A first-team player as the hierarchy sees him.</summary>
public sealed record HierarchyInput(Player Player, int Overall, int Age, ContractPromises? Promises = null);

/// <summary>
/// A player's place in the squad hierarchy: <paramref name="Rank"/> 1 = best overall, the role he expects from it,
/// the minutes per game he expects (the role's minutes, or a contract promise when larger) and his standing as a leader.
/// </summary>
public sealed record HierarchyPlace(Player Player, int Rank, SquadRole Role, int ExpectedMinutes, int LeaderScore, bool IsCaptain, bool IsLeader);

/// <summary>
/// A team's chemistry for a match (docs/CHEMISTRY.md §5): <paramref name="Score"/> −1 … +1 (0 = neutral) from the
/// components, each −1 … +1, and the unhappy players among the core.
/// </summary>
public sealed record TeamChemistry(double Score, double Morale, double Relationships, double Leadership, double Tactical, int Conflicts)
{
    public static readonly TeamChemistry Neutral = new(0, 0, 0, 0, 0, 0);

    /// <summary>The score on a 0–100 scale (50 = neutral).</summary>
    public int Rating => (int)Math.Round(50 + (50 * Score), MidpointRounding.AwayFromZero);
}

/// <summary>
/// Squad hierarchy, role satisfaction, morale, relationships and team chemistry (docs/CHEMISTRY.md). Pure and
/// deterministic; every constant is documented there.
/// </summary>
public static class ChemistryModel
{
    /// <summary>A player at or below this morale is unhappy; unhappy and short of minutes, he is in conflict.</summary>
    public const int UnhappyMorale = 30;

    /// <summary>Leader score (70% leadership, 30% experience) from which a player is a dressing-room leader.</summary>
    public const int LeaderThreshold = 70;

    /// <summary>Starters plus this many rotation players make the core whose chemistry counts.</summary>
    public const int CoreRotation = 3;

    /// <summary>Minutes both players must play in a game for it to count as shared court time.</summary>
    public const int SharedMinutes = 10;

    /// <summary>Weights of the chemistry components (sum 1) and the penalty per unhappy core player.</summary>
    public const double MoraleWeight = 0.35;
    public const double RelationshipWeight = 0.25;
    public const double LeadershipWeight = 0.15;
    public const double TacticalWeight = 0.25;
    public const double ConflictPenalty = 0.1;

    /// <summary>Match effects at chemistry ±1 and of a player's morale at 1/100 (docs/CHEMISTRY.md §6).</summary>
    public const double MakeEdge = 0.02;
    public const double TurnoverEdge = 0.008;
    public const double MoraleForm = 0.01;

    /// <summary>Overall points an AI coach gives up to give an unhappy player minutes or to release him first.</summary>
    public const int BalanceBonus = 4;

    public static int LeaderScore(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return (int)Math.Round((0.7 * player.Attributes.Leadership) + (0.3 * player.Experience), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Ranks the first team by overall (ties by id): the role each player expects is <see cref="PromiseRules.Expected"/>
    /// of his rank; the captain is the best leader score; leaders reach <see cref="LeaderThreshold"/>.
    /// </summary>
    public static IReadOnlyList<HierarchyPlace> Hierarchy(IReadOnlyList<HierarchyInput> squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        var ranked = squad.OrderByDescending(p => p.Overall).ThenBy(p => p.Player.Id.Value).ToList();
        var captain = ranked.OrderByDescending(p => LeaderScore(p.Player)).ThenBy(p => p.Player.Id.Value).FirstOrDefault()?.Player.Id;
        return
        [
            .. ranked.Select((p, i) =>
            {
                var role = PromiseRules.Expected(i + 1, p.Age);
                var leader = LeaderScore(p.Player);
                var isCaptain = p.Player.Id == captain;
                return new HierarchyPlace(
                    p.Player,
                    i + 1,
                    role,
                    Math.Max(PromiseRules.Minutes(role), p.Promises?.TargetMinutes ?? 0),
                    leader,
                    isCaptain,
                    leader >= LeaderThreshold);
            }),
        ];
    }

    /// <summary>Happy at the expected minutes or more, content from 60% of them, unhappy below; not judged without minutes.</summary>
    public static RoleSatisfaction Satisfaction(int expectedMinutes, double? averageMinutes) =>
        averageMinutes is not { } minutes ? RoleSatisfaction.NotJudged
        : minutes >= expectedMinutes ? RoleSatisfaction.Happy
        : minutes >= PromiseRules.BrokenShare * expectedMinutes ? RoleSatisfaction.Content
        : RoleSatisfaction.Unhappy;

    /// <summary>
    /// Weekly morale change: results (wins − losses, ±2), playing time (happy +1, unhappy −3 star/starter, −2 rotation,
    /// −1 prospect, 0 reserve), drift (−1 above 60; +1 below 40 when the club has a leader). The result stays in 1–100.
    /// </summary>
    public static int MoraleChange(int morale, SquadRole role, RoleSatisfaction satisfaction, int wins, int losses, bool hasLeader)
    {
        var change = Math.Clamp(wins - losses, -2, 2);
        change += satisfaction switch
        {
            RoleSatisfaction.Happy => 1,
            RoleSatisfaction.Unhappy => role switch
            {
                SquadRole.Star or SquadRole.Starter => -3,
                SquadRole.Rotation => -2,
                SquadRole.Prospect => -1,
                _ => 0,
            },
            _ => 0,
        };
        change += morale > 60 ? -1 : morale < 40 && hasLeader ? 1 : 0;
        return Math.Clamp(morale + change, Player.MinRating, Player.MaxRating) - morale;
    }

    public static bool InConflict(int morale, RoleSatisfaction satisfaction) => morale <= UnhappyMorale && satisfaction == RoleSatisfaction.Unhappy;

    /// <summary>Weekly change of a pair of teammates: +2 for shared court time (+1 more from 3 games, +1 for a shared nationality), −6 in a conflict.</summary>
    public static int RelationshipChange(int sharedGames, bool sameNationality, bool conflict)
    {
        var change = sharedGames > 0 ? 2 + (sharedGames >= 3 ? 1 : 0) + (sameNationality ? 1 : 0) : 0;
        return conflict ? change - 6 : change;
    }

    /// <summary>Fit points lost by the assigned role against the best role typical of his position (0 when it is one of them or better).</summary>
    public static int TacticalGap(Player player, TacticalRole assigned)
    {
        ArgumentNullException.ThrowIfNull(player);
        var best = TacticalRoles.TypicalFor(player.Position).Max(r => TacticalRoles.Fit(player.Attributes, r));
        return Math.Max(0, best - TacticalRoles.Fit(player.Attributes, assigned));
    }

    /// <summary>
    /// Chemistry of a core (starters and first rotation players): morale (mean − 50) / 40, relationships (mean pair
    /// score / 50), leadership (best leader score − 60) / 30, tactical fit (−mean gap / 10, never positive), each
    /// clamped to −1 … +1; weighted, minus <see cref="ConflictPenalty"/> per unhappy player, clamped to −1 … +1.
    /// </summary>
    public static TeamChemistry Team(IReadOnlyList<(Player Player, TacticalRole Role)> core, Func<PlayerId, PlayerId, int> relationship)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(relationship);
        if (core.Count == 0)
        {
            return TeamChemistry.Neutral;
        }

        var morale = Unit((core.Average(p => p.Player.Morale) - 50) / 40.0);
        var pairs = core.SelectMany((p, i) => core.Skip(i + 1).Select(q => relationship(p.Player.Id, q.Player.Id))).ToList();
        var relationships = pairs.Count == 0 ? 0 : Unit(pairs.Average() / 50.0);
        var leadership = Unit((core.Max(p => LeaderScore(p.Player)) - 60) / 30.0);
        var tactical = Math.Max(-1, -core.Average(p => TacticalGap(p.Player, p.Role)) / 10.0);
        var conflicts = core.Count(p => p.Player.Morale <= UnhappyMorale);
        var score = (MoraleWeight * morale) + (RelationshipWeight * relationships) + (LeadershipWeight * leadership) + (TacticalWeight * tactical) - (ConflictPenalty * conflicts);
        return new TeamChemistry(Unit(score), morale, relationships, leadership, tactical, conflicts);
    }

    /// <summary>Make-probability change of a player's day from his morale: ±<see cref="MoraleForm"/> at 100 / 1.</summary>
    public static double FormEdge(int morale) => MoraleForm * (morale - 50) / 50.0;

    private static double Unit(double value) => Math.Clamp(value, -1, 1);
}
