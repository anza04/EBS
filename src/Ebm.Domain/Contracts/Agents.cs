using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Contracts;

public readonly record struct AgentId(Guid Value) : IEntityId<AgentId>
{
    public static AgentId New() => new(Guid.CreateVersion7());

    public static AgentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// A player agent (docs/CONTRACTS.md §4.1). <see cref="Toughness"/> (1–100, 50 = average) shortens the agent's patience
/// and raises the share of the asking value below which offers are rejected outright.
/// </summary>
public sealed class Agent : Entity<AgentId>
{
    public const int NameMaxLength = 60;

    public Agent(AgentId id, string name, int toughness)
        : base(id)
    {
        Name = Guard.Text(name, "Agent name", NameMaxLength);
        Toughness = Guard.Range(toughness, Player.MinRating, Player.MaxRating, "Toughness");
    }

    private Agent()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public int Toughness { get; private set; }
}

public readonly record struct AgentRelationshipId(Guid Value) : IEntityId<AgentRelationshipId>
{
    public static AgentRelationshipId New() => new(Guid.CreateVersion7());

    public static AgentRelationshipId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>How an agent feels about a club, −100 (hostile) to +100 (trusted); 0 when they have no history.</summary>
public sealed class AgentRelationship : Entity<AgentRelationshipId>
{
    public const int Min = -100;
    public const int Max = 100;

    public AgentRelationship(AgentRelationshipId id, AgentId agentId, ClubId clubId)
        : base(id)
    {
        AgentId = Guard.Required(agentId, nameof(agentId));
        ClubId = Guard.Required(clubId, nameof(clubId));
    }

    private AgentRelationship()
    {
    }

    public AgentId AgentId { get; private set; }

    public ClubId ClubId { get; private set; }

    public int Score { get; private set; }

    public void Adjust(int delta) => Score = Math.Clamp(Score + delta, Min, Max);
}

/// <summary>
/// The agents of the game world and who represents whom. The pool is created once per database (<see cref="Create"/>);
/// a player's agent is a stable function of his id, so no player data changes (docs/CONTRACTS.md §4.1).
/// </summary>
public static class AgentPool
{
    /// <summary>Players below this reputation negotiate for themselves.</summary>
    public const int MinClientReputation = 15;

    /// <summary>Relationship changes (docs/CONTRACTS.md §4.1).</summary>
    public const int DealSigned = 5;
    public const int TalksBrokenOff = -10;
    public const int PromiseKept = 2;
    public const int PromiseBroken = -15;

    private static readonly string[] FirstNames =
        ["Marco", "Luis", "Andreas", "Dimitris", "Jordi", "Pierre", "Tomas", "Igor", "Stefan", "Paul", "Aleksandar", "Emre"];

    private static readonly string[] LastNames =
        ["Rossi", "Fernandez", "Weber", "Papadakis", "Puig", "Durand", "Novak", "Petrov", "Jansen", "Kovac", "Moreau", "Lindqvist"];

    /// <summary>The fixed pool of 24 agents, toughness spread from 30 to 99.</summary>
    public static IReadOnlyList<Agent> Create() =>
    [
        .. Enumerable.Range(0, 24).Select(i => new Agent(
            AgentId.New(),
            $"{FirstNames[i % FirstNames.Length]} {LastNames[((i * 5) + (i / 12) + 3) % LastNames.Length]}",
            30 + (i * 37 % 70))),
    ];

    /// <summary>The agent of <paramref name="playerId"/> among <paramref name="agents"/> (any order), or <c>null</c> if he has none.</summary>
    public static Agent? For(PlayerId playerId, int reputation, IReadOnlyCollection<Agent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (reputation < MinClientReputation || agents.Count == 0)
        {
            return null;
        }

        var ordered = agents.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Id.Value).ToList();
        var bytes = playerId.Value.ToByteArray();
        var hash = bytes.Aggregate(17u, (h, b) => unchecked((h * 31) + b));
        return ordered[(int)(hash % (uint)ordered.Count)];
    }
}
