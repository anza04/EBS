using System.Globalization;
using System.Text.Json;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Services.Data;
using Ebm.Services.Modding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ebm.DataImport.Modding;

/// <summary>
/// Writes the world as an <c>ebm-dataset</c> (version 1) with the field names of the import format, so the file imports
/// back unchanged. Only importable state is written: retired players, ended contracts, staff, fixtures, matches and
/// history records are not part of the format (docs/MODDING.md §3).
/// </summary>
internal sealed class DatabaseExportService(IServiceScopeFactory scopes, ILogger<DatabaseExportService> logger) : IDatabaseExportService
{
    public async Task<ExportSummary> ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var countries = await List<Country, CountryId>(services, cancellationToken).ConfigureAwait(false);
        var arenas = await List<Arena, ArenaId>(services, cancellationToken).ConfigureAwait(false);
        var clubs = await List<Club, ClubId>(services, cancellationToken).ConfigureAwait(false);
        var leagues = await List<League, LeagueId>(services, cancellationToken).ConfigureAwait(false);
        var divisions = await List<Division, DivisionId>(services, cancellationToken).ConfigureAwait(false);
        var competitions = await List<Competition, CompetitionId>(services, cancellationToken).ConfigureAwait(false);
        var seasons = await List<Season, SeasonId>(services, cancellationToken).ConfigureAwait(false);
        var participants = await List<CompetitionParticipant, CompetitionParticipantId>(services, cancellationToken).ConfigureAwait(false);
        var players = (await List<Player, PlayerId>(services, cancellationToken).ConfigureAwait(false)).Where(p => p.RetiredOn is null).ToList();
        var active = players.Select(p => p.Id).ToHashSet();
        var contracts = (await List<Contract, ContractId>(services, cancellationToken).ConfigureAwait(false))
            .Where(c => c.PlayerId is { } player && active.Contains(player) && c.Status == ContractStatus.Active)
            .ToList();
        var spells = (await List<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>(services, cancellationToken).ConfigureAwait(false))
            .Where(s => s.EndDate is not null && active.Contains(s.PlayerId))
            .ToList();
        var codes = countries.ToDictionary(c => c.Id, c => c.Code);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
        await using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("format", "ebm-dataset");
            json.WriteNumber("version", 1);
            Section(json, "countries", countries.OrderBy(c => c.Code, StringComparer.Ordinal), c =>
            {
                Id(json, c.Id.Value);
                json.WriteString("code", c.Code);
                json.WriteString("name", c.Name);
                json.WriteBoolean("euEea", c.IsEuEea);
            });
            Section(json, "arenas", arenas.OrderBy(a => a.Name, StringComparer.Ordinal).ThenBy(a => a.Id.Value), a =>
            {
                Id(json, a.Id.Value);
                json.WriteString("name", a.Name);
                json.WriteString("city", a.City);
                json.WriteString("country", codes[a.CountryId]);
                json.WriteNumber("capacity", a.Capacity);
            });
            Section(json, "clubs", clubs.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Id.Value), c =>
            {
                Id(json, c.Id.Value);
                json.WriteString("name", c.Name);
                json.WriteString("shortName", c.ShortName);
                json.WriteString("country", codes[c.CountryId]);
                Optional(json, "homeArenaId", c.HomeArenaId?.Value.ToString());
                if (c.FoundedYear is { } founded)
                {
                    json.WriteNumber("foundedYear", founded);
                }

                Optional(json, "city", c.City);
                json.WriteNumber("reputation", c.Reputation);
                json.WriteNumber("balance", c.Balance);
                json.WriteNumber("wageBudget", c.WageBudget);
                json.WriteString("boardExpectation", c.BoardExpectation.ToString());
                json.WriteString("dataStatus", c.DataStatus.ToString());
            });
            Section(json, "leagues", leagues.OrderBy(l => l.Name, StringComparer.Ordinal).ThenBy(l => l.Id.Value), l =>
            {
                Id(json, l.Id.Value);
                json.WriteString("name", l.Name);
                json.WriteString("shortName", l.ShortName);
                Optional(json, "country", l.CountryId is { } country ? codes[country] : null);
            });
            Section(json, "divisions", divisions.OrderBy(d => d.LeagueId.Value).ThenBy(d => d.Level), d =>
            {
                Id(json, d.Id.Value);
                json.WriteString("leagueId", d.LeagueId.Value.ToString());
                json.WriteString("name", d.Name);
                json.WriteNumber("level", d.Level);
            });
            Section(json, "competitions", competitions.OrderBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Id.Value), c =>
            {
                Id(json, c.Id.Value);
                json.WriteString("name", c.Name);
                json.WriteString("shortName", c.ShortName);
                json.WriteString("type", c.Type.ToString());
                Optional(json, "country", c.CountryId is { } country ? codes[country] : null);
                Optional(json, "divisionId", c.DivisionId?.Value.ToString());
                json.WritePropertyName("rules");
                using var rules = JsonDocument.Parse(c.RulesJson);
                rules.WriteTo(json);
            });
            Section(json, "seasons", seasons.OrderBy(s => s.StartDate).ThenBy(s => s.Id.Value), s =>
            {
                Id(json, s.Id.Value);
                json.WriteString("name", s.Name);
                Date(json, "startDate", s.StartDate);
                Date(json, "endDate", s.EndDate);
            });
            Section(json, "participants", participants.OrderBy(p => p.SeasonId.Value).ThenBy(p => p.CompetitionId.Value).ThenBy(p => p.Id.Value), p =>
            {
                Id(json, p.Id.Value);
                json.WriteString("competitionId", p.CompetitionId.Value.ToString());
                json.WriteString("seasonId", p.SeasonId.Value.ToString());
                json.WriteString("clubId", p.ClubId.Value.ToString());
            });
            Section(json, "players", players.OrderBy(p => p.LastName, StringComparer.Ordinal).ThenBy(p => p.FirstName, StringComparer.Ordinal).ThenBy(p => p.Id.Value), p =>
            {
                Id(json, p.Id.Value);
                json.WriteString("firstName", p.FirstName);
                json.WriteString("lastName", p.LastName);
                Date(json, "dateOfBirth", p.DateOfBirth);
                json.WriteString("nationality", codes[p.NationalityId]);
                Optional(json, "secondNationality", p.SecondNationalityId is { } second ? codes[second] : null);
                json.WriteNumber("heightCm", p.HeightCm);
                json.WriteNumber("weightKg", p.WeightKg);
                json.WriteNumber("wingspanCm", p.WingspanCm);
                json.WriteString("position", p.Position.ToString());
                Optional(json, "secondaryPositions", p.SecondaryPositions.IsEmpty ? null : string.Join('|', p.SecondaryPositions.Positions));
                json.WriteNumber("experience", p.Experience);
                json.WriteNumber("reputation", p.Reputation);
                json.WriteNumber("potential", p.Potential);
                json.WriteNumber("fitness", p.Fitness);
                json.WriteNumber("morale", p.Morale);
                json.WriteString("injuryStatus", p.InjuryStatus.ToString());
                if (p.InjuryReturnDate is { } back)
                {
                    Date(json, "injuryReturnDate", back);
                }

                json.WriteString("dataStatus", p.ProfileStatus.ToString());
                json.WriteString("attributesStatus", p.AttributesStatus.ToString());
                Optional(json, "source", p.Source);
                foreach (var attribute in PlayerCatalog.Attributes)
                {
                    json.WriteNumber(PlayerCatalog.FieldName(attribute), p.Attributes[attribute]);
                }

                foreach (var tendency in PlayerCatalog.Tendencies)
                {
                    json.WriteNumber(PlayerCatalog.FieldName(tendency), p.Tendencies[tendency]);
                }
            });
            Section(json, "contracts", contracts.OrderBy(c => c.StartDate).ThenBy(c => c.Id.Value), c =>
            {
                Id(json, c.Id.Value);
                json.WriteString("clubId", c.ClubId.Value.ToString());
                json.WriteString("playerId", c.PlayerId!.Value.Value.ToString());
                Date(json, "startDate", c.StartDate);
                Date(json, "endDate", c.EndDate);
                json.WriteNumber("annualSalary", c.AnnualSalary);
                json.WriteString("paymentFrequency", c.PaymentFrequency.ToString());
                json.WriteNumber("signingBonus", c.SigningBonus);
                Optional(json, "performanceBonuses", c.PerformanceBonuses.ToString() is { Length: > 0 } bonuses ? bonuses : null);
                if (c.ReleaseClause is { } clause)
                {
                    json.WriteNumber("releaseClause", clause);
                }

                if (c.ClubOptionUntil is { } clubOption)
                {
                    Date(json, "clubOptionUntil", clubOption);
                }

                if (c.PlayerOptionUntil is { } playerOption)
                {
                    Date(json, "playerOptionUntil", playerOption);
                }

                json.WriteString("dataStatus", c.TermsStatus.ToString());
                Optional(json, "source", c.Source);
            });
            Section(json, "playerHistory", spells.OrderBy(s => s.StartDate).ThenBy(s => s.Id.Value), s =>
            {
                Id(json, s.Id.Value);
                json.WriteString("playerId", s.PlayerId.Value.ToString());
                json.WriteString("clubId", s.ClubId.Value.ToString());
                Date(json, "startDate", s.StartDate);
                Date(json, "endDate", s.EndDate!.Value);
            });
            json.WriteEndObject();
        }

        File.Move(temporary, path, overwrite: true);
        var summary = new ExportSummary(Path.GetFullPath(path),
        [
            ("countries", countries.Count), ("arenas", arenas.Count), ("clubs", clubs.Count), ("leagues", leagues.Count),
            ("divisions", divisions.Count), ("competitions", competitions.Count), ("seasons", seasons.Count),
            ("participants", participants.Count), ("players", players.Count), ("contracts", contracts.Count), ("playerHistory", spells.Count),
        ]);
        logger.LogInformation("Database exported to {File}: {Summary}", summary.File, summary.Summary);
        return summary;
    }

    private static async Task<IReadOnlyList<TEntity>> List<TEntity, TId>(IServiceProvider services, CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        await services.GetRequiredService<IRepository<TEntity, TId>>().ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);

    private static void Section<T>(Utf8JsonWriter json, string name, IEnumerable<T> items, Action<T> write)
    {
        json.WriteStartArray(name);
        foreach (var item in items)
        {
            json.WriteStartObject();
            write(item);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void Id(Utf8JsonWriter json, Guid id) => json.WriteString("id", id.ToString());

    private static void Date(Utf8JsonWriter json, string name, DateOnly date) =>
        json.WriteString(name, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static void Optional(Utf8JsonWriter json, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            json.WriteString(name, value);
        }
    }
}
