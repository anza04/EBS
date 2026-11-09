using Ebm.DataImport.Sources;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Data;
using Ebm.Services.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.DataImport.Validation;

/// <summary>
/// Validates and applies records within one DI scope. Accepted records are added to (or updated in) the scope's
/// repositories; the caller decides whether to save.
/// </summary>
internal sealed class ImportSession
{
    private static readonly Dictionary<string, Position> PositionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PG"] = Position.PointGuard,
        ["SG"] = Position.ShootingGuard,
        ["SF"] = Position.SmallForward,
        ["PF"] = Position.PowerForward,
        ["C"] = Position.Center,
    };

    private readonly ImportMode _mode;
    private EntityStore<Country, CountryId> _countries = null!;
    private EntityStore<Arena, ArenaId> _arenas = null!;
    private EntityStore<Club, ClubId> _clubs = null!;
    private EntityStore<League, LeagueId> _leagues = null!;
    private EntityStore<Division, DivisionId> _divisions = null!;
    private EntityStore<Competition, CompetitionId> _competitions = null!;
    private EntityStore<Player, PlayerId> _players = null!;
    private EntityStore<Contract, ContractId> _contracts = null!;

    private ImportSession(ImportMode mode) => _mode = mode;

    /// <summary>Loads the existing records the import is checked against.</summary>
    public static async Task<ImportSession> LoadAsync(IServiceProvider services, ImportMode mode, CancellationToken cancellationToken)
    {
        var session = new ImportSession(mode);
        session._countries = await Store<Country, CountryId>(services, c => c.Code, c => c.Name, cancellationToken).ConfigureAwait(false);
        session._arenas = await Store<Arena, ArenaId>(services, DuplicateKeys.Of, a => a.Name, cancellationToken).ConfigureAwait(false);
        session._clubs = await Store<Club, ClubId>(services, DuplicateKeys.Of, c => c.Name, cancellationToken).ConfigureAwait(false);
        session._leagues = await Store<League, LeagueId>(services, DuplicateKeys.Of, l => l.Name, cancellationToken).ConfigureAwait(false);
        session._divisions = await Store<Division, DivisionId>(services, DuplicateKeys.Of, d => d.Name, cancellationToken).ConfigureAwait(false);
        session._competitions = await Store<Competition, CompetitionId>(services, DuplicateKeys.Of, c => c.Name, cancellationToken).ConfigureAwait(false);
        session._players = await Store<Player, PlayerId>(services, DuplicateKeys.Of, p => p.FullName, cancellationToken).ConfigureAwait(false);
        session._contracts = await Store<Contract, ContractId>(services, DuplicateKeys.Of, _ => "contract", cancellationToken).ConfigureAwait(false);
        return session;
    }

    public ImportRecordResult Process(RawRecord record) => record.Kind switch
    {
        ImportRecordKind.Country => Apply(record, _countries, (f, id) => new Country(id, f.Text("code"), f.Text("name")),
            (target, c) => target.Rename(c.Name), identifyByKey: true),
        ImportRecordKind.Arena => Apply(record, _arenas, (f, id) =>
            new Arena(id, f.Text("name"), f.Text("city"), CountryRef(f, "country"), f.Int("capacity")),
            (target, a) => target.UpdateDetails(a.Name, a.City, a.CountryId, a.Capacity)),
        ImportRecordKind.Club => Apply(record, _clubs, (f, id) =>
            new Club(id, f.Text("name"), f.Text("shortName"), CountryRef(f, "country"), OptionalRef(f, "homeArenaId", _arenas, "arena"), f.OptionalInt("foundedYear")),
            (target, c) => target.UpdateDetails(c.Name, c.ShortName, c.CountryId, c.HomeArenaId, c.FoundedYear)),
        ImportRecordKind.League => Apply(record, _leagues, (f, id) =>
            new League(id, f.Text("name"), f.Text("shortName"), OptionalCountryRef(f, "country")),
            (target, l) => target.UpdateDetails(l.Name, l.ShortName, l.CountryId)),
        ImportRecordKind.Division => Apply(record, _divisions, (f, id) =>
            new Division(id, Ref(f, "leagueId", _leagues, "league"), f.Text("name"), f.Int("level")),
            (target, d) =>
            {
                Immutable(target.LeagueId == d.LeagueId, "A division cannot move to another league.");
                target.UpdateDetails(d.Name, d.Level);
            }),
        ImportRecordKind.Competition => Apply(record, _competitions, (f, id) =>
            new Competition(
                id,
                f.Text("name"),
                f.Text("shortName"),
                f.Enum<CompetitionType>("type"),
                OptionalCountryRef(f, "country"),
                OptionalRef(f, "divisionId", _divisions, "division"),
                f.Optional("rules") ?? Competition.EmptyRules),
            (target, c) =>
            {
                target.UpdateDetails(c.Name, c.ShortName, c.Type, c.CountryId, c.DivisionId);
                target.UpdateRules(c.RulesJson);
            }),
        ImportRecordKind.Player => Apply(record, _players, (f, id) =>
            new Player(
                id,
                f.Text("firstName"),
                f.Text("lastName"),
                f.Date("dateOfBirth"),
                CountryRef(f, "nationality"),
                f.Int("heightCm"),
                f.Int("weightKg"),
                f.Enum("position", PositionAliases),
                Attributes(f)),
            (target, p) =>
            {
                target.UpdateDetails(p.FirstName, p.LastName, p.DateOfBirth, p.NationalityId, p.HeightCm, p.WeightKg, p.Position);
                target.UpdateAttributes(p.Attributes);
            }),
        ImportRecordKind.Contract => Apply(record, _contracts, (f, id) =>
            Contract.ForPlayer(id, Ref(f, "clubId", _clubs, "club"), Ref(f, "playerId", _players, "player"), f.Date("startDate"), f.Date("endDate"), f.Long("annualSalary")),
            (target, c) =>
            {
                Immutable(target.ClubId == c.ClubId && target.PlayerId == c.PlayerId, "The club and player of a contract cannot be changed. Import it as a new contract.");
                target.UpdateTerms(c.StartDate, c.EndDate, c.AnnualSalary);
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(record), record.Kind, "Unknown record kind."),
    };

    private static async Task<EntityStore<TEntity, TId>> Store<TEntity, TId>(
        IServiceProvider services,
        Func<TEntity, string> key,
        Func<TEntity, string> label,
        CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId>
    {
        var repository = services.GetRequiredService<IRepository<TEntity, TId>>();
        var existing = await repository.ListAsync(null, cancellationToken).ConfigureAwait(false);
        return new EntityStore<TEntity, TId>(repository, existing, key, label);
    }

    private static void Immutable(bool unchanged, string message)
    {
        if (!unchanged)
        {
            throw new DomainValidationException(message);
        }
    }

    private static PlayerAttributes Attributes(FieldReader f) => new(
        Rating(f, "shooting"),
        Rating(f, "insideScoring"),
        Rating(f, "passing"),
        Rating(f, "ballHandling"),
        Rating(f, "rebounding"),
        Rating(f, "defense"),
        Rating(f, "athleticism"),
        Rating(f, "basketballIq"));

    // Ratings are range-checked here, so every invalid rating is reported together with the other field errors.
    private static int Rating(FieldReader f, string name)
    {
        var value = f.OptionalInt(name, required: true);
        if (value is < PlayerAttributes.MinRating or > PlayerAttributes.MaxRating)
        {
            f.Errors.Add($"'{name}' must be between {PlayerAttributes.MinRating} and {PlayerAttributes.MaxRating} (got '{value}').");
        }

        return value ?? PlayerAttributes.MinRating;
    }

    private static string RawLabel(RawRecord record)
    {
        string? Field(string name) => record.Fields.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        return record.Kind switch
        {
            ImportRecordKind.Player => $"{Field("firstName")} {Field("lastName")}".Trim(),
            ImportRecordKind.Country => Field("name") ?? Field("code") ?? string.Empty,
            ImportRecordKind.Contract => "contract",
            _ => Field("name") ?? string.Empty,
        };
    }

    private CountryId CountryRef(FieldReader f, string name) => OptionalCountryRef(f, name, required: true) ?? default;

    private CountryId? OptionalCountryRef(FieldReader f, string name, bool required = false)
    {
        var code = required ? f.Text(name) : f.Optional(name);
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var country = _countries.FindByKey(code.ToUpperInvariant());
        if (country is null)
        {
            f.Errors.Add($"'{name}' refers to the unknown country code '{code}'. Add the country first (countries section or countries.csv).");
        }

        return country?.Id;
    }

    private static TId Ref<TEntity, TId>(FieldReader f, string name, EntityStore<TEntity, TId> store, string what)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        OptionalRef(f, name, store, what, required: true) ?? default;

    private static TId? OptionalRef<TEntity, TId>(FieldReader f, string name, EntityStore<TEntity, TId> store, string what, bool required = false)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId>
    {
        if (f.OptionalGuid(name, required) is not { } guid)
        {
            return null;
        }

        var id = TId.From(guid);
        if (!store.Contains(id))
        {
            f.Errors.Add($"'{name}' refers to {what} {guid}, which is neither in the database nor among the accepted records of this import.");
        }

        return id;
    }

    /// <summary>
    /// Validates one record and adds or updates it. The candidate entity is always built first, so an invalid
    /// record never touches a stored entity.
    /// </summary>
    private ImportRecordResult Apply<TEntity, TId>(
        RawRecord record,
        EntityStore<TEntity, TId> store,
        Func<FieldReader, TId, TEntity> build,
        Action<TEntity, TEntity> update,
        bool identifyByKey = false)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId>
    {
        var reader = new FieldReader(record);
        var explicitId = reader.OptionalGuid("id");
        var id = TId.From(explicitId ?? Guid.CreateVersion7());

        TEntity candidate;
        try
        {
            candidate = build(reader, id);
        }
        catch (DomainValidationException ex)
        {
            // Placeholder values of invalid fields can break domain rules too: report the field errors then.
            return Result(record, explicitId, RawLabel(record), ImportOutcome.Rejected, reader.HasErrors ? reader.Errors : [ex.Message]);
        }

        if (reader.HasErrors)
        {
            return Result(record, explicitId, RawLabel(record), ImportOutcome.Rejected, reader.Errors);
        }

        var label = store.Label(candidate);
        var byKey = store.FindByKey(store.KeyOf(candidate));
        var target = explicitId is not null ? store.Find(id) : identifyByKey ? byKey : null;
        if (target is not null && store.WasImported(target.Id))
        {
            return Result(record, target.Id.Value, label, ImportOutcome.Rejected, ["The record appears more than once in this import."]);
        }

        if (byKey is not null && (target is null || !byKey.Id.Equals(target.Id)))
        {
            var where = store.WasImported(byKey.Id) ? "earlier in this import" : "in the database";
            return Result(record, explicitId, label, ImportOutcome.Rejected,
                [$"Duplicate of '{store.Label(byKey)}' (ID {byKey.Id.Value}) {where}. Use that ID to update it."]);
        }

        if (target is null)
        {
            store.Add(candidate);
            string[] notes = explicitId is null ? [$"New ID assigned: {candidate.Id.Value}."] : [];
            return Result(record, candidate.Id.Value, label, ImportOutcome.Added, notes);
        }

        if (_mode == ImportMode.AddNew)
        {
            return Result(record, target.Id.Value, label, ImportOutcome.Rejected,
                [$"'{store.Label(target)}' already exists (ID {target.Id.Value}). Choose 'update existing records' to change it."]);
        }

        try
        {
            store.Update(target, t => update(t, candidate));
        }
        catch (DomainValidationException ex)
        {
            return Result(record, target.Id.Value, label, ImportOutcome.Rejected, [ex.Message]);
        }

        return Result(record, target.Id.Value, label, ImportOutcome.Updated, []);
    }

    private static ImportRecordResult Result(RawRecord record, Guid? id, string label, ImportOutcome outcome, IReadOnlyList<string> messages) =>
        new(record.Source, record.Position, record.Kind, id, label, outcome, [.. messages]);
}
