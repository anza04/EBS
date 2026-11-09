using Ebm.Domain.Clubs;
using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>One game of a generated schedule.</summary>
public sealed record ScheduledGame(int Round, ClubId Home, ClubId Away);

/// <summary>
/// Round-robin schedule by the circle method. Each leg is a full round robin; even legs mirror the first one
/// with home and away swapped. Each club plays every other club <c>legs</c> times, at most once per round.
/// </summary>
/// <remarks>
/// Home/away balance within a leg: equal for an odd number of clubs, off by at most one for an even number,
/// and never more than two home or away games in a row.
/// </remarks>
public static class RoundRobinScheduler
{
    /// <summary>Rounds in one leg: with an odd number of clubs, one club rests in every round.</summary>
    public static int RoundsPerLeg(int clubs) => clubs % 2 == 0 ? clubs - 1 : clubs;

    /// <summary>Games each club plays in the regular season.</summary>
    public static int GamesPerClub(int clubs, int legs) => (clubs - 1) * legs;

    public static IReadOnlyList<ScheduledGame> Generate(IReadOnlyList<ClubId> clubs, int legs)
    {
        ArgumentNullException.ThrowIfNull(clubs);
        if (clubs.Count < 2)
        {
            throw new DomainValidationException("A schedule needs at least two clubs.");
        }

        if (clubs.Distinct().Count() != clubs.Count)
        {
            throw new DomainValidationException("A club cannot appear twice in a schedule.");
        }

        Guard.Range(legs, 1, 10, "Legs");

        // With an odd number of clubs the empty slot is the fixed one, so the rest is perfectly balanced.
        var slots = clubs.Select(c => (ClubId?)c).ToList();
        if (slots.Count % 2 == 1)
        {
            slots.Insert(0, null);
        }

        var size = slots.Count;
        var fixedSlot = slots[0];
        var rotating = slots.Skip(1).ToList();
        var firstLeg = new List<ScheduledGame>();
        for (var round = 0; round < size - 1; round++)
        {
            ClubId?[] row = [fixedSlot, .. rotating];
            for (var i = 0; i < size / 2; i++)
            {
                if (row[i] is not { } top || row[size - 1 - i] is not { } bottom)
                {
                    continue;
                }

                var topHome = i == 0 ? round % 2 == 0 : i % 2 == 1;
                firstLeg.Add(topHome ? new ScheduledGame(round + 1, top, bottom) : new ScheduledGame(round + 1, bottom, top));
            }

            rotating.Insert(0, rotating[^1]);
            rotating.RemoveAt(rotating.Count - 1);
        }

        var perLeg = size - 1;
        var games = new List<ScheduledGame>(firstLeg.Count * legs);
        for (var leg = 0; leg < legs; leg++)
        {
            var offset = leg * perLeg;
            games.AddRange(firstLeg.Select(g => leg % 2 == 0
                ? g with { Round = offset + g.Round }
                : new ScheduledGame(offset + g.Round, g.Away, g.Home)));
        }

        return games;
    }
}
