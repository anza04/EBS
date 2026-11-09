using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;

namespace Ebm.Domain.Transfers;

/// <summary>A first-team player as the recruiter sees him.</summary>
public sealed record SquadMember(
    PlayerId PlayerId,
    string Name,
    Position Position,
    PositionSet SecondaryPositions,
    int Overall,
    int Age,
    long Salary,
    RosterRole Role,
    bool IsForeign)
{
    public bool CanPlay(Position position) => Position == position || SecondaryPositions.Contains(position);
}

/// <summary>A player on the market as the recruiter sees him, priced for the recruiting club.</summary>
/// <param name="AskingSalary">Annual salary the player would ask the recruiting club.</param>
/// <param name="Fee">Transfer fee his club asks; 0 for a free agent.</param>
/// <param name="FromClubId">His current club; <c>null</c> for a free agent.</param>
/// <param name="IsForeign">Whether he counts as foreign at the recruiting club.</param>
public sealed record Candidate(
    PlayerId PlayerId,
    string Name,
    Position Position,
    PositionSet SecondaryPositions,
    int Overall,
    int Age,
    long AskingSalary,
    long Fee,
    ClubId? FromClubId,
    bool IsForeign)
{
    public bool IsFreeAgent => FromClubId is null;

    public bool CanPlay(Position position) => Position == position || SecondaryPositions.Contains(position);
}

/// <summary>Everything the recruiter knows about the club it works for.</summary>
/// <param name="FirstTeam">The registered players (every role but youth).</param>
/// <param name="Rules">The squad rules to satisfy: the club rules tightened by the competitions it plays in.</param>
public sealed record ClubSituation(ClubId ClubId, long Balance, long WageBudget, long WageBill, IReadOnlyList<SquadMember> FirstTeam, SquadRules Rules);

/// <summary>Why the club wants a player; <paramref name="Position"/> when the need is positional.</summary>
public sealed record RosterNeed(string Reason, Position? Position = null);

/// <param name="Years">Contract length the club offers.</param>
public sealed record PlannedSigning(Candidate Candidate, long Salary, int Years, string Reason);

public sealed record PlannedSale(SquadMember Player, string Reason);

/// <param name="Needs">Needs found, including those no candidate could fill.</param>
/// <param name="Unmet">Needs no affordable candidate could fill.</param>
public sealed record RecruitmentPlan(
    IReadOnlyList<RosterNeed> Needs,
    IReadOnlyList<PlannedSigning> Signings,
    IReadOnlyList<PlannedSale> Sales,
    IReadOnlyList<RosterNeed> Unmet)
{
    public static RecruitmentPlan Empty { get; } = new([], [], [], []);
}

/// <summary>
/// The simple AI recruitment rules of Step 08 (docs/TRANSFERS.md §6): find roster gaps, sell surplus players, and
/// sign the best affordable candidates within the balance, the wage budget and the registration limits. Pure and
/// deterministic: the same situation and candidates always give the same plan.
/// </summary>
public sealed class ClubRecruiter
{
    public static ClubRecruiter Default { get; } = new();

    /// <summary>Fewest first-team players who can play each position (primary or secondary).</summary>
    public int MinPositionCover { get; init; } = 2;

    /// <summary>A free agent is preferred to a player with a fee unless the latter is better by more than this many overall points.</summary>
    public int FreeAgentPreference { get; init; } = 5;

    /// <summary>Share of the balance a club keeps back from transfer fees.</summary>
    public double CashReserve { get; init; } = 0.10;

    /// <summary>Players the club wants in its first team when it is below the registration maximum: the matchday squad plus one.</summary>
    public int TargetPlayers(SquadRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Math.Min(rules.MaxPlayers, rules.MatchdayPlayers + 1);
    }

    /// <summary>Which players the club should sell to meet the squad size, the foreign-player limit and the wage budget.</summary>
    public IReadOnlyList<PlannedSale> Surplus(ClubSituation club)
    {
        ArgumentNullException.ThrowIfNull(club);
        var sales = new List<PlannedSale>();
        var team = club.FirstTeam.ToList();
        var wageBill = club.WageBill;

        // Too many players: the weakest go first.
        foreach (var weakest in team.OrderBy(m => m.Overall).ThenByDescending(m => m.Salary).Take(Math.Max(0, team.Count - club.Rules.MaxPlayers)).ToList())
        {
            sales.Add(new PlannedSale(weakest, $"squad over the limit of {club.Rules.MaxPlayers}"));
            team.Remove(weakest);
            wageBill -= weakest.Salary;
        }

        // Too many foreign players: the weakest foreigners go.
        if (club.Rules.MaxForeignPlayers is { } maxForeign)
        {
            foreach (var foreigner in team.Where(m => m.IsForeign).OrderBy(m => m.Overall).Take(Math.Max(0, team.Count(m => m.IsForeign) - maxForeign)).ToList())
            {
                sales.Add(new PlannedSale(foreigner, $"more than {maxForeign} foreign players"));
                team.Remove(foreigner);
                wageBill -= foreigner.Salary;
            }
        }

        // Over the wage budget: the most expensive non-starters go until the bill fits (never below the minimum squad).
        foreach (var expensive in team.Where(m => m.Role != RosterRole.Starter).OrderByDescending(m => m.Salary).ToList())
        {
            if (wageBill <= club.WageBudget || team.Count <= club.Rules.MinPlayers)
            {
                break;
            }

            sales.Add(new PlannedSale(expensive, "wage bill over the budget"));
            team.Remove(expensive);
            wageBill -= expensive.Salary;
        }

        return sales;
    }

    /// <summary>The gaps in the first team after the planned <paramref name="sales"/>: squad size and position cover.</summary>
    public IReadOnlyList<RosterNeed> Needs(ClubSituation club, IReadOnlyList<PlannedSale> sales)
    {
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(sales);
        var sold = sales.Select(s => s.Player.PlayerId).ToHashSet();
        var team = club.FirstTeam.Where(m => !sold.Contains(m.PlayerId)).ToList();
        var needs = new List<RosterNeed>();

        // Positions first: a hole in the lineup is worse than a short bench.
        foreach (var position in Enum.GetValues<Position>())
        {
            var cover = team.Count(m => m.CanPlay(position));
            for (var i = cover; i < MinPositionCover; i++)
            {
                needs.Add(new RosterNeed(cover == 0 ? $"nobody can play {PlayerCatalog.DisplayName(position)}" : $"only {cover} can play {PlayerCatalog.DisplayName(position)}", position));
            }
        }

        var count = team.Count + needs.Count;
        for (var i = count; i < club.Rules.MinPlayers; i++)
        {
            needs.Add(new RosterNeed($"fewer than the {club.Rules.MinPlayers} players required"));
        }

        for (var i = Math.Max(count, club.Rules.MinPlayers); i < TargetPlayers(club.Rules); i++)
        {
            needs.Add(new RosterNeed($"short squad ({team.Count} players)"));
        }

        return needs;
    }

    /// <summary>
    /// Plans the club's transfer window: sales, then one signing per need from <paramref name="candidates"/>, each
    /// within the money left (balance minus the cash reserve for fees, wage budget minus the wage bill for salaries)
    /// and the registration limits. Free agents are preferred unless a player with a fee is clearly better.
    /// </summary>
    public RecruitmentPlan Plan(ClubSituation club, IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(candidates);
        var sales = Surplus(club);
        var needs = Needs(club, sales);
        var sold = sales.Select(s => s.Player.PlayerId).ToHashSet();
        var team = club.FirstTeam.Where(m => !sold.Contains(m.PlayerId)).ToList();

        var cash = club.Balance - (long)(Math.Max(0, club.Balance) * CashReserve);
        var budgetLeft = club.WageBudget - team.Sum(m => m.Salary);
        var count = team.Count;
        var foreign = team.Count(m => m.IsForeign);
        var taken = new HashSet<PlayerId>();
        var signings = new List<PlannedSigning>();
        var unmet = new List<RosterNeed>();

        foreach (var need in needs)
        {
            if (count >= club.Rules.MaxPlayers)
            {
                unmet.Add(need);
                continue;
            }

            var eligible = candidates
                .Where(c => !taken.Contains(c.PlayerId) && c.FromClubId != club.ClubId)
                .Where(c => need.Position is not { } position || c.CanPlay(position))
                .Where(c => c.Fee <= cash && c.AskingSalary <= budgetLeft)
                .Where(c => !c.IsForeign || club.Rules.MaxForeignPlayers is not { } maxForeign || foreign < maxForeign)
                .ToList();
            var bestFree = eligible.Where(c => c.IsFreeAgent).OrderByDescending(c => c.Overall).ThenBy(c => c.AskingSalary).ThenBy(c => c.Age).FirstOrDefault();
            var bestPaid = eligible.Where(c => !c.IsFreeAgent).OrderByDescending(c => c.Overall).ThenBy(c => c.Fee).ThenBy(c => c.AskingSalary).FirstOrDefault();
            var pick = bestFree is not null && (bestPaid is null || bestFree.Overall >= bestPaid.Overall - FreeAgentPreference) ? bestFree : bestPaid;
            if (pick is null)
            {
                unmet.Add(need);
                continue;
            }

            taken.Add(pick.PlayerId);
            signings.Add(new PlannedSigning(pick, pick.AskingSalary, ContractYears(pick.Age), need.Reason));
            cash -= pick.Fee;
            budgetLeft -= pick.AskingSalary;
            count++;
            foreign += pick.IsForeign ? 1 : 0;
        }

        return new RecruitmentPlan(needs, signings, sales, unmet);
    }

    /// <summary>Contract length offered: three years to young players, one to veterans, two otherwise.</summary>
    public static int ContractYears(int age) => age <= 24 ? 3 : age >= 32 ? 1 : 2;
}
