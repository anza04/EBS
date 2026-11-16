using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>
/// A knockout tie. <see cref="First"/> hosts the single game (or the first leg), <see cref="Second"/> the second leg.
/// Two legs are decided on aggregate; a level aggregate (possible only after a forfeit or an edited result, since
/// the second leg goes to overtime) goes to the winner of the second leg.
/// </summary>
public sealed record CupTie(int Round, string RoundName, int Legs, ClubId First, ClubId Second, IReadOnlyList<SeasonGame> Games)
{
    public int FirstPoints => Games.Where(g => g.IsPlayed).Sum(g => g.Home == First ? g.HomeScore!.Value : g.AwayScore!.Value);

    public int SecondPoints => Games.Where(g => g.IsPlayed).Sum(g => g.Home == Second ? g.HomeScore!.Value : g.AwayScore!.Value);

    public bool IsDecided => Games.Count == Legs && Games.All(g => g.IsPlayed);

    public ClubId? Winner => !IsDecided ? null
        : FirstPoints > SecondPoints ? First
        : SecondPoints > FirstPoints ? Second
        : Games[^1].Winner;

    public ClubId? Loser => Winner is { } winner ? (winner == First ? Second : First) : null;
}

/// <summary>
/// One season of a cup, derived from its rules, the seeds stored by the draw and the games: optional group stage,
/// then knockout rounds (byes for the top seeds in the first round when the bracket is not full). Pure and
/// deterministic: the same seeds and draw seed always give the same groups and ties. Documented in docs/CUP_FORMATS.md.
/// </summary>
public sealed class CupSeason
{
    private readonly CompetitionRules _rules;
    private readonly CupRules _cup;
    private readonly List<SeasonGame> _games;
    private readonly int _drawSeed;
    private IReadOnlyList<IReadOnlyList<ClubId>>? _groups;
    private IReadOnlyList<GroupTable>? _tables;
    private Knockout? _knockout;

    /// <param name="seeds">The drawn clubs, best seed first; empty before the draw.</param>
    /// <param name="drawSeed">Seed of the random draws (<see cref="DrawSeed"/>).</param>
    public CupSeason(CompetitionRules rules, IReadOnlyList<ClubId> seeds, IEnumerable<SeasonGame> games, int drawSeed)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(games);
        _rules = rules;
        _cup = rules.Cup ?? throw new DomainValidationException("The competition has no cup format (rules.cup).");
        Seeds = [.. seeds];
        if (Seeds.Distinct().Count() != Seeds.Count)
        {
            throw new DomainValidationException("A club cannot hold two cup seeds.");
        }

        _games = [.. games];
        _drawSeed = drawSeed;
    }

    public IReadOnlyList<ClubId> Seeds { get; }

    public bool IsDrawn => Seeds.Count > 0;

    public SeasonPhase Phase =>
        !IsDrawn ? SeasonPhase.Registration
        : !GroupStageOver ? SeasonPhase.GroupStage
        : Winner is null ? SeasonPhase.Knockout
        : SeasonPhase.Completed;

    /// <summary>The drawn groups (best pot first in each); empty without a group stage or before the draw.</summary>
    public IReadOnlyList<IReadOnlyList<ClubId>> Groups() => _groups ??= DrawGroups();

    /// <summary>The ranked group tables; empty without a group stage or before the draw.</summary>
    public IReadOnlyList<GroupTable> GroupTables()
    {
        if (_tables is not null)
        {
            return _tables;
        }

        var results = Stage(FixtureStage.CupGroup).Where(g => g.IsPlayed).Select(g => g.Result).ToList();
        return _tables =
        [
            .. Groups().Select((members, i) =>
            {
                var set = members.ToHashSet();
                return new GroupTable(GroupName(i), StandingsCalculator.Calculate(members, results.Where(r => set.Contains(r.Home)), _rules));
            }),
        ];
    }

    /// <summary>Clubs going straight to the second round.</summary>
    public IReadOnlyList<ClubId> Byes => Bracket()?.Byes ?? [];

    /// <summary>Every tie whose clubs are known, round by round.</summary>
    public IReadOnlyList<CupTie> Ties() => Bracket()?.Ties ?? [];

    public ClubId? Winner => Bracket() is { Ties.Count: > 0 } k && k.Ties[^1].Round == _cup.Rounds.Count ? k.Ties[^1].Winner : null;

    public ClubId? RunnerUp => Winner is null ? null : Bracket()!.Ties[^1].Loser;

    /// <summary>Games to schedule now: the whole group stage, the next knockout round, or second legs.</summary>
    public IReadOnlyList<SeasonGameRequest> NextGames()
    {
        var phase = Phase;
        if (phase == SeasonPhase.GroupStage)
        {
            var legs = _cup.GroupStage!.Legs;
            return Stage(FixtureStage.CupGroup).Any()
                ? []
                : [.. Groups().SelectMany(g => RoundRobinScheduler.Generate(g, legs)).Select(g => new SeasonGameRequest(FixtureStage.CupGroup, g.Round, 1, g.Home, g.Away))];
        }

        if (phase != SeasonPhase.Knockout)
        {
            return [];
        }

        var round = Bracket()!.Ties.Max(t => t.Round);
        var ties = Bracket()!.Ties.Where(t => t.Round == round).ToList();
        if (ties.All(t => t.Games.Count == 0))
        {
            return [.. ties.Select(t => new SeasonGameRequest(FixtureStage.Cup, round, 1, t.First, t.Second))];
        }

        return [.. ties.Where(t => t.Legs == 2 && t.Games.Count == 1 && t.Games[0].IsPlayed)
            .Select(t => new SeasonGameRequest(FixtureStage.Cup, round, 2, t.Second, t.First))];
    }

    /// <summary>The cup winner (and runner-up) of a completed season; cups promote and relegate nobody.</summary>
    public SeasonOutcome Outcome() =>
        Phase == SeasonPhase.Completed
            ? new SeasonOutcome(Winner!.Value, [], [], RunnerUp)
            : throw new DomainValidationException("The cup is not over yet.");

    /// <summary>A stable draw seed for one competition season (the same IDs always give the same draws).</summary>
    public static int DrawSeed(Guid competition, Guid season)
    {
        var (a, b) = (competition.ToByteArray(), season.ToByteArray());
        var hash = 17;
        for (var i = 0; i < a.Length; i++)
        {
            hash = unchecked((hash * 31) + (a[i] ^ (b[i] << 1)));
        }

        return hash;
    }

    public static string GroupName(int index) => $"Group {(char)('A' + index)}";

    private bool GroupStageOver
    {
        get
        {
            if (_cup.GroupStage is null)
            {
                return true;
            }

            var games = Stage(FixtureStage.CupGroup).ToList();
            return games.Count > 0 && games.All(g => g.IsPlayed);
        }
    }

    private IEnumerable<SeasonGame> Stage(FixtureStage stage) => _games.Where(g => g.Stage == stage);

    /// <summary>Pots of as many seeds as groups; each group gets one club of each pot (serpentine for a bracket cup, drawn otherwise).</summary>
    private List<IReadOnlyList<ClubId>> DrawGroups()
    {
        if (_cup.GroupStage is not { } stage || !IsDrawn)
        {
            return [];
        }

        var groups = Enumerable.Range(0, stage.Groups).Select(_ => new List<ClubId>()).ToList();
        var random = new Random(_drawSeed);
        for (var pot = 0; pot * stage.Groups < Seeds.Count; pot++)
        {
            var clubs = Seeds.Skip(pot * stage.Groups).Take(stage.Groups).ToList();
            if (_cup.Draw == CupDraw.Bracket)
            {
                if (pot % 2 == 1)
                {
                    clubs.Reverse();
                }
            }
            else
            {
                Shuffle(clubs, random);
            }

            for (var i = 0; i < clubs.Count; i++)
            {
                groups[i].Add(clubs[i]);
            }
        }

        var groupOf = groups.SelectMany((g, i) => g.Select(c => (c, i))).ToDictionary(x => x.c, x => x.i);
        if (Stage(FixtureStage.CupGroup).Any(g => !groupOf.TryGetValue(g.Home, out var h) || !groupOf.TryGetValue(g.Away, out var a) || h != a))
        {
            throw new DomainValidationException("A cup group game does not match the drawn groups.");
        }

        return [.. groups];
    }

    /// <summary>Clubs reaching the knockout rounds, best seed first: the seeds, or the group qualifiers (group winners first).</summary>
    private List<ClubId> KnockoutEntrants()
    {
        if (_cup.GroupStage is not { } stage)
        {
            return [.. Seeds];
        }

        // By group finish, then group order: in a bracket each group winner meets another group's runner-up (A1 v B2, B1 v A2).
        return
        [
            .. GroupTables().SelectMany((t, group) => t.Rows.Take(stage.Advance).Select(r => (r.Position, Group: group, r.ClubId)))
                .OrderBy(x => x.Position)
                .ThenBy(x => x.Group)
                .Select(x => x.ClubId),
        ];
    }

    private Knockout? Bracket()
    {
        if (_knockout is not null || !IsDrawn || !GroupStageOver)
        {
            return _knockout;
        }

        var entrants = KnockoutEntrants();
        var size = _cup.BracketSize;
        if (entrants.Count > size || entrants.Count <= size / 2)
        {
            throw new DomainValidationException($"The cup's {_cup.Rounds.Count} knockout rounds cannot take {entrants.Count} clubs.");
        }

        var seedOf = entrants.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var games = Stage(FixtureStage.Cup).ToList();
        var ties = new List<CupTie>();
        List<ClubId> byes = [];

        // Bracket order: slots of seeds (null = bye), adjacent slots meet.
        List<ClubId?> slots = [.. PlayoffBracket.BracketOrder(size).Select(s => s <= entrants.Count ? entrants[s - 1] : (ClubId?)null)];
        var next = entrants;
        for (var round = 1; round <= _cup.Rounds.Count; round++)
        {
            var spec = _cup.Rounds[round - 1];
            var roundGames = games.Where(g => g.Round == round).ToList();
            List<(ClubId A, ClubId B)> pairs;
            var advancing = new List<ClubId>();
            if (_cup.Draw == CupDraw.Bracket)
            {
                pairs = [];
                for (var i = 0; i < slots.Count; i += 2)
                {
                    if (slots[i] is { } a && slots[i + 1] is { } b)
                    {
                        pairs.Add((a, b));
                    }
                    else if ((slots[i] ?? slots[i + 1]) is { } alone)
                    {
                        byes.Add(alone);
                    }
                }
            }
            else
            {
                var playing = next;
                if (round == 1)
                {
                    byes = [.. entrants.Take(size - entrants.Count)];
                    advancing.AddRange(byes);
                    playing = [.. entrants.Skip(byes.Count)];
                }

                pairs = roundGames.Count > 0 ? StoredPairs(roundGames, playing) : DrawPairs(playing, round, seedOf);
            }

            var roundTies = pairs.Select(p => Tie(round, spec, p, roundGames, seedOf)).ToList();
            if (roundTies.Sum(t => t.Games.Count) != roundGames.Count)
            {
                throw new DomainValidationException($"A game of cup round {round} does not belong to its ties.");
            }

            ties.AddRange(roundTies);
            if (roundTies.Any(t => !t.IsDecided))
            {
                break;
            }

            if (_cup.Draw == CupDraw.Bracket)
            {
                slots = [.. Enumerable.Range(0, slots.Count / 2).Select(i => slots[2 * i] is { } a && slots[(2 * i) + 1] is { } b
                    ? roundTies.Single(t => t.First == a || t.Second == a).Winner
                    : slots[2 * i] ?? slots[(2 * i) + 1])];
            }
            else
            {
                advancing.AddRange(roundTies.Select(t => t.Winner!.Value));
                next = [.. advancing.OrderBy(c => seedOf[c])];
            }
        }

        if (games.Count > ties.Sum(t => t.Games.Count))
        {
            throw new DomainValidationException("A cup game belongs to a round that has not been reached.");
        }

        return _knockout = new Knockout(ties, byes);
    }

    /// <summary>The tie of a pair with its games, oriented by the first leg if played, else by the hosting rule.</summary>
    private CupTie Tie(int round, CupRound spec, (ClubId A, ClubId B) pair, List<SeasonGame> roundGames, Dictionary<ClubId, int> seedOf)
    {
        var games = roundGames.Where(g => (g.Home == pair.A && g.Away == pair.B) || (g.Home == pair.B && g.Away == pair.A)).OrderBy(g => g.GameNumber).ToList();
        var (better, worse) = seedOf[pair.A] < seedOf[pair.B] ? (pair.A, pair.B) : (pair.B, pair.A);
        var (first, second) = games.Count > 0 ? (games[0].Home, games[0].Away)
            : spec.Legs == 2 || _cup.Host == CupHost.LowerSeed ? (worse, better)
            : (better, worse);
        for (var leg = 0; leg < games.Count; leg++)
        {
            var host = leg == 0 ? first : second;
            if (games[leg].GameNumber != leg + 1 || games[leg].Home != host || leg >= spec.Legs)
            {
                throw new DomainValidationException($"The games of the cup tie {first} v {second} (round {round}) are inconsistent.");
            }
        }

        return new CupTie(round, spec.Name, spec.Legs, first, second, games);
    }

    /// <summary>The ties of a round already drawn and scheduled: the pairs of its first legs, which must cover the clubs.</summary>
    private static List<(ClubId A, ClubId B)> StoredPairs(List<SeasonGame> roundGames, List<ClubId> playing)
    {
        var pairs = roundGames.Where(g => g.GameNumber == 1).Select(g => (g.Home, g.Away)).ToList();
        var clubs = pairs.SelectMany(p => new[] { p.Home, p.Away }).ToList();
        if (clubs.Count != playing.Count || !clubs.ToHashSet().SetEquals(playing))
        {
            throw new DomainValidationException("The scheduled cup ties do not match the clubs left in the cup.");
        }

        return pairs;
    }

    /// <summary>A seeded draw (better half against the other half) or an open draw, from a seed fixed per round.</summary>
    private List<(ClubId A, ClubId B)> DrawPairs(List<ClubId> playing, int round, Dictionary<ClubId, int> seedOf)
    {
        var random = new Random(unchecked((_drawSeed * 397) ^ round));
        var ordered = playing.OrderBy(c => seedOf[c]).ToList();
        if (_cup.Draw == CupDraw.Open)
        {
            Shuffle(ordered, random);
            return [.. Enumerable.Range(0, ordered.Count / 2).Select(i => (ordered[2 * i], ordered[(2 * i) + 1]))];
        }

        var half = ordered.Count / 2;
        var lower = ordered.Skip(half).ToList();
        Shuffle(lower, random);
        return [.. Enumerable.Range(0, half).Select(i => (ordered[i], lower[i]))];
    }

    /// <summary>Fisher-Yates with the given generator (kept here so draws never change with the runtime).</summary>
    private static void Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private sealed record Knockout(IReadOnlyList<CupTie> Ties, IReadOnlyList<ClubId> Byes);
}
