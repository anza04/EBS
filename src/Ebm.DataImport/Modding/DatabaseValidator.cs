using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Services;
using Ebm.Services.Data;
using Ebm.Services.Modding;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.DataImport.Modding;

/// <summary>
/// Whole-database checks for rules that single records cannot break on their own (docs/MODDING.md §4). References
/// are already enforced by the import and by the database; this looks at how records fit together.
/// </summary>
internal sealed class DatabaseValidator(IServiceScopeFactory scopes, GameClock clock) : IDatabaseValidator
{
    /// <summary>Fewest players a club in a competition should have under contract (five play, the rest rotate).</summary>
    public const int MinimumSquad = 8;

    public async Task<ValidationReport> ValidateAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var countries = (await List<Country, CountryId>(services, cancellationToken)).ToDictionary(c => c.Id);
        var arenas = (await List<Arena, ArenaId>(services, cancellationToken)).ToDictionary(a => a.Id);
        var clubs = (await List<Club, ClubId>(services, cancellationToken)).ToDictionary(c => c.Id);
        var divisions = (await List<Division, DivisionId>(services, cancellationToken)).ToDictionary(d => d.Id);
        var leagues = (await List<League, LeagueId>(services, cancellationToken)).ToDictionary(l => l.Id);
        var competitions = (await List<Competition, CompetitionId>(services, cancellationToken)).ToDictionary(c => c.Id);
        var seasons = (await List<Season, SeasonId>(services, cancellationToken)).ToDictionary(s => s.Id);
        var participants = await List<CompetitionParticipant, CompetitionParticipantId>(services, cancellationToken);
        var players = (await List<Player, PlayerId>(services, cancellationToken)).ToDictionary(p => p.Id);
        var contracts = (await List<Contract, ContractId>(services, cancellationToken))
            .Where(c => c.PlayerId is not null && c.Status == ContractStatus.Active)
            .ToList();
        var issues = new List<ValidationIssue>();
        var today = clock.Today;

        // Competitions: rules must parse; league championships belong to a division of a league in their country.
        foreach (var competition in competitions.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            try
            {
                CompetitionRules.Parse(competition.RulesJson);
            }
            catch (Exception ex) when (ex is DomainException or System.Text.Json.JsonException)
            {
                issues.Add(new(IssueSeverity.Error, "Competitions", competition.Name, $"The rules cannot be read: {ex.Message}"));
            }

            if (competition.Type == CompetitionType.League && competition.DivisionId is null)
            {
                issues.Add(new(IssueSeverity.Warning, "Competitions", competition.Name,
                    "This league is not linked to a division, so it has no promotion or relegation. Set its divisionId."));
            }
            else if (competition.DivisionId is { } division && divisions.TryGetValue(division, out var d)
                     && leagues.TryGetValue(d.LeagueId, out var league) && league.CountryId is { } leagueCountry
                     && competition.CountryId is { } country && leagueCountry != country)
            {
                issues.Add(new(IssueSeverity.Warning, "Competitions", competition.Name,
                    $"Its country ({countries[country].Code}) differs from the country of league {league.Name} ({countries[leagueCountry].Code})."));
            }
        }

        // Participants: domestic competitions take clubs of their country; each club needs a squad.
        foreach (var participant in participants)
        {
            var competition = competitions[participant.CompetitionId];
            var club = clubs[participant.ClubId];
            if (competition.CountryId is { } country && club.CountryId != country)
            {
                issues.Add(new(IssueSeverity.Warning, "Competitions", $"{competition.Name} {seasons[participant.SeasonId].Name}",
                    $"{club.Name} ({countries[club.CountryId].Code}) takes part in a {countries[country].Code} competition."));
            }
        }

        var playing = participants.Where(p => seasons[p.SeasonId].EndDate >= today).Select(p => p.ClubId).ToHashSet();
        var squads = contracts.Where(c => c.EffectiveEndDate >= today && c.StartDate <= today)
            .GroupBy(c => c.ClubId)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var clubId in playing.OrderBy(c => clubs[c].Name, StringComparer.Ordinal))
        {
            var size = squads.GetValueOrDefault(clubId);
            if (size < MinimumSquad)
            {
                issues.Add(new(size < 5 ? IssueSeverity.Error : IssueSeverity.Warning, "Clubs", clubs[clubId].Name,
                    $"Only {size} player(s) under contract today; a club in a competition needs at least {MinimumSquad} (5 to play a match)."));
            }
        }

        // Clubs: the home arena should be in the club's country.
        foreach (var club in clubs.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            if (club.HomeArenaId is { } arenaId && arenas.TryGetValue(arenaId, out var arena) && arena.CountryId != club.CountryId)
            {
                issues.Add(new(IssueSeverity.Warning, "Clubs", club.Name, $"The home arena {arena.Name} is in another country ({countries[arena.CountryId].Code})."));
            }
        }

        // Contracts: a player has one club at a time, and retired players have none.
        foreach (var group in contracts.GroupBy(c => c.PlayerId!.Value))
        {
            var player = players[group.Key];
            if (player.RetiredOn is { } retired && group.Any(c => c.EffectiveEndDate > retired))
            {
                issues.Add(new(IssueSeverity.Error, "Contracts", player.FullName, $"Retired on {retired:yyyy-MM-dd} but still under contract."));
            }

            var ordered = group.OrderBy(c => c.StartDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartDate <= ordered[i - 1].EffectiveEndDate)
                {
                    issues.Add(new(IssueSeverity.Error, "Contracts", player.FullName,
                        $"Two contracts overlap: {clubs[ordered[i - 1].ClubId].Name} until {ordered[i - 1].EffectiveEndDate:yyyy-MM-dd} and " +
                        $"{clubs[ordered[i].ClubId].Name} from {ordered[i].StartDate:yyyy-MM-dd}. Remove or shorten one of them."));
                }
            }
        }

        return new ValidationReport(issues.OrderBy(i => i.Severity).ThenBy(i => i.Area, StringComparer.Ordinal).ToList());
    }

    private static async Task<IReadOnlyList<TEntity>> List<TEntity, TId>(IServiceProvider services, CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        await services.GetRequiredService<IRepository<TEntity, TId>>().ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
}
