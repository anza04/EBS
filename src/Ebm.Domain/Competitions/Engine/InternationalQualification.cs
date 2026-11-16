using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>
/// A competition qualifying its clubs by <see cref="CupEntry"/> slots, with the clubs already entered by hand and
/// the countries whose clubs may enter (<c>registration.countries</c>; empty = any).
/// </summary>
public sealed record EntryCompetition(CompetitionId Id, string ShortName, CupEntry Entry, int MaxTeams, IReadOnlyList<ClubId> Entered, IReadOnlyList<string> Countries);

/// <summary>A club playing a league this season: its country, the division level and its reputation.</summary>
public sealed record QualifyingClub(ClubId Club, string Country, int Level, int Reputation);

/// <summary>
/// What the slots read. <paramref name="Rankings"/>: per country and division level, its clubs best first (last
/// season's champion, then its final table; without a finished last season, by reputation).
/// <paramref name="Winners"/>: last season's winners by competition short name.
/// </summary>
public sealed record QualificationSources(
    IReadOnlyDictionary<(string Country, int Level), IReadOnlyList<ClubId>> Rankings,
    IReadOnlyDictionary<string, ClubId> Winners,
    IReadOnlyList<QualifyingClub> Clubs);

/// <summary>
/// Fills international competitions from their entry slots (docs/INTERNATIONAL_COMPETITIONS.md): competitions by
/// tier, slots in order, each club in one competition only; a club already taken is skipped and the next one
/// moves up. Pure and deterministic.
/// </summary>
public static class InternationalQualification
{
    /// <returns>The clubs to enter in each competition, in slot order (hand entries not repeated).</returns>
    public static IReadOnlyDictionary<CompetitionId, IReadOnlyList<ClubId>> Select(IReadOnlyList<EntryCompetition> competitions, QualificationSources sources)
    {
        ArgumentNullException.ThrowIfNull(competitions);
        ArgumentNullException.ThrowIfNull(sources);
        var taken = competitions.SelectMany(c => c.Entered).ToHashSet();
        var winners = new Dictionary<string, ClubId>(sources.Winners, StringComparer.OrdinalIgnoreCase);
        var countryOf = sources.Rankings.SelectMany(r => r.Value.Select(c => (c, r.Key.Country)))
            .Concat(sources.Clubs.Select(c => (c.Club, c.Country)))
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.First().Item2);
        var result = new Dictionary<CompetitionId, IReadOnlyList<ClubId>>();
        foreach (var competition in competitions.OrderBy(c => c.Entry.Tier).ThenBy(c => c.Id.Value))
        {
            var added = new List<ClubId>();
            var free = competition.MaxTeams - competition.Entered.Count;
            var allowed = competition.Countries.Select(c => c.ToUpperInvariant()).ToHashSet();
            bool Eligible(ClubId club) => allowed.Count == 0 || (countryOf.TryGetValue(club, out var code) && allowed.Contains(code.ToUpperInvariant()));
            void Take(IEnumerable<ClubId> candidates, int count)
            {
                foreach (var club in candidates)
                {
                    if (count == 0 || added.Count >= free)
                    {
                        return;
                    }

                    if (!taken.Contains(club) && Eligible(club) && taken.Add(club))
                    {
                        added.Add(club);
                        count--;
                    }
                }
            }

            foreach (var slot in competition.Entry.Slots)
            {
                switch (slot.Type)
                {
                    case CupSlotType.Holder when winners.TryGetValue(competition.ShortName, out var holder):
                        Take([holder], 1);
                        break;
                    case CupSlotType.Winner when winners.TryGetValue(slot.Competition!, out var winner):
                        Take([winner], 1);
                        break;
                    case CupSlotType.League:
                        foreach (var country in slot.Countries)
                        {
                            if (sources.Rankings.TryGetValue((country.ToUpperInvariant(), slot.Level), out var ranking))
                            {
                                Take(ranking, slot.Clubs);
                            }
                        }

                        break;
                    case CupSlotType.Reputation:
                        var countries = slot.Countries.Select(c => c.ToUpperInvariant()).ToHashSet();
                        Take(
                            sources.Clubs.Where(c => c.Level == slot.Level && (countries.Count == 0 || countries.Contains(c.Country)))
                                .OrderByDescending(c => c.Reputation).ThenBy(c => c.Club.Value).Select(c => c.Club),
                            slot.Clubs == 0 ? int.MaxValue : slot.Clubs);
                        break;
                }
            }

            result[competition.Id] = added;
        }

        return result;
    }
}
