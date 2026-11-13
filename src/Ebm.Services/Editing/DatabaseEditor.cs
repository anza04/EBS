using System.Globalization;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Editing;

/// <summary>Implements the editor use cases over the repositories, one DI scope per call.</summary>
internal sealed class DatabaseEditor : IDatabaseEditor
{
    private readonly IServiceScopeFactory _scopes;

    public DatabaseEditor(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        _scopes = scopes;
    }

    public async Task<EditorSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        return new EditorSnapshot(
            await List<Country, CountryId>(services, cancellationToken).ConfigureAwait(false),
            await List<Arena, ArenaId>(services, cancellationToken).ConfigureAwait(false),
            await List<Club, ClubId>(services, cancellationToken).ConfigureAwait(false),
            await List<League, LeagueId>(services, cancellationToken).ConfigureAwait(false),
            await List<Division, DivisionId>(services, cancellationToken).ConfigureAwait(false),
            await List<Competition, CompetitionId>(services, cancellationToken).ConfigureAwait(false),
            await List<Player, PlayerId>(services, cancellationToken).ConfigureAwait(false),
            await services.GetRequiredService<IRepository<Contract, ContractId>>()
                .ListAsync(c => c.PlayerId != null, cancellationToken).ConfigureAwait(false));
    }

    public Task<PlayerId> SavePlayerAsync(PlayerDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SaveAsync<Player, PlayerId>(
            draft.Id,
            () => Apply(new Player(PlayerId.New(), draft.FirstName, draft.LastName, draft.DateOfBirth, draft.NationalityId, draft.HeightCm, draft.WeightKg, draft.Position, draft.Attributes), draft),
            p =>
            {
                p.UpdateDetails(draft.FirstName, draft.LastName, draft.DateOfBirth, draft.NationalityId, draft.HeightCm, draft.WeightKg, draft.Position);
                p.UpdateAttributes(draft.Attributes);
                Apply(p, draft);
            },
            DuplicateKeys.Of,
            p => $"A player named {p.FullName} born {p.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} already exists.",
            "player",
            cancellationToken);
    }

    private static Player Apply(Player player, PlayerDraft draft)
    {
        player.UpdateProfile(draft.WingspanCm, draft.SecondaryPositions, draft.Experience, draft.Reputation, draft.Potential);
        player.UpdateCondition(draft.Fitness, draft.Morale, draft.InjuryStatus, draft.InjuryReturnDate);
        player.UpdateTendencies(draft.Tendencies);
        player.UpdateSecondNationality(draft.SecondNationalityId);
        player.UpdateDataQuality(draft.ProfileStatus, draft.AttributesStatus, draft.Source);
        return player;
    }

    public Task<ClubId> SaveClubAsync(ClubDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SaveAsync<Club, ClubId>(
            draft.Id,
            () => Apply(new Club(ClubId.New(), draft.Name, draft.ShortName, draft.CountryId, draft.HomeArenaId, draft.FoundedYear), draft),
            c =>
            {
                c.UpdateDetails(draft.Name, draft.ShortName, draft.CountryId, draft.HomeArenaId, draft.FoundedYear);
                Apply(c, draft);
            },
            DuplicateKeys.Of,
            c => $"A club named {c.Name} already exists in this country.",
            "club",
            cancellationToken);
    }

    private static Club Apply(Club club, ClubDraft draft)
    {
        club.UpdateProfile(draft.City, draft.Reputation, draft.Balance, draft.WageBudget, draft.BoardExpectation);
        return club;
    }

    public Task<ContractId> SaveContractAsync(ContractDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SaveAsync<Contract, ContractId>(
            draft.Id,
            () => Contract.ForPlayer(ContractId.New(), draft.ClubId, draft.PlayerId, draft.Terms),
            c =>
            {
                if (c.ClubId != draft.ClubId || c.PlayerId != draft.PlayerId)
                {
                    throw new DomainValidationException("The club and player of a contract cannot be changed. Create a new contract instead.");
                }

                c.UpdateTerms(draft.Terms);
            },
            DuplicateKeys.Of,
            _ => "This player already has a contract with this club starting on the same date.",
            "contract",
            cancellationToken);
    }

    public Task<LeagueId> SaveLeagueAsync(LeagueDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SaveAsync<League, LeagueId>(
            draft.Id,
            () => new League(LeagueId.New(), draft.Name, draft.ShortName, draft.CountryId),
            l => l.UpdateDetails(draft.Name, draft.ShortName, draft.CountryId),
            DuplicateKeys.Of,
            l => $"A league named {l.Name} already exists.",
            "league",
            cancellationToken);
    }

    public Task<CompetitionId> SaveCompetitionAsync(CompetitionDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return SaveAsync<Competition, CompetitionId>(
            draft.Id,
            () => new Competition(CompetitionId.New(), draft.Name, draft.ShortName, draft.Type, draft.CountryId, draft.DivisionId, draft.RulesJson),
            c =>
            {
                c.UpdateDetails(draft.Name, draft.ShortName, draft.Type, draft.CountryId, draft.DivisionId);
                c.UpdateRules(draft.RulesJson);
            },
            DuplicateKeys.Of,
            c => $"A competition named {c.Name} already exists in this country.",
            "competition",
            cancellationToken);
    }

    private static Task<IReadOnlyList<TEntity>> List<TEntity, TId>(IServiceProvider services, CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>().ListAsync(null, cancellationToken);

    /// <summary>Creates or updates one entity, refuses natural-key duplicates, and saves.</summary>
    private async Task<TId> SaveAsync<TEntity, TId>(
        TId? id,
        Func<TEntity> create,
        Action<TEntity> update,
        Func<TEntity, string> duplicateKey,
        Func<TEntity, string> duplicateMessage,
        string description,
        CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId>
    {
        using var scope = _scopes.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity, TId>>();
        TEntity entity;
        if (id is { } existingId)
        {
            entity = await repository.FindAsync(existingId, cancellationToken).ConfigureAwait(false)
                ?? throw new DomainException($"The {description} no longer exists in the database.");
            update(entity);
        }
        else
        {
            entity = create();
            repository.Add(entity);
        }

        // Tracked entities come back with their pending values, so the comparison sees the edit.
        var key = duplicateKey(entity);
        var others = await repository.ListAsync(null, cancellationToken).ConfigureAwait(false);
        if (others.Any(o => !o.Id.Equals(entity.Id) && duplicateKey(o) == key))
        {
            throw new DuplicateRecordException(duplicateMessage(entity));
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }
}
