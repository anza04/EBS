using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Tactics;

/// <summary>Implements <see cref="ITacticsService"/> over the repositories and <see cref="IClubService"/>.</summary>
internal sealed class TacticsService(IServiceScopeFactory scopes, IClubService clubs) : ITacticsService
{
    public async Task<ClubTactics?> GetClubTacticsAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster)
        {
            return null;
        }

        using var scope = scopes.CreateScope();
        var tactics = await Repo(scope).ListAsync(t => t.ClubId == clubId, cancellationToken).ConfigureAwait(false);
        var squad = roster.Players
            .Where(p => p.Role.IsFirstTeam())
            .OrderBy(p => p.Role).ThenBy(p => p.LineupPosition).ThenBy(p => p.RotationOrder).ThenBy(p => p.Player.LastName)
            .Select(p => new TacticSquadPlayer(p.Player, p.Role, p.LineupPosition, TacticalRoles.Suggest(p.Player)))
            .ToList();
        return new ClubTactics(roster.Club, [.. tactics.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)], squad);
    }

    public async Task<Tactic> SaveAsync(ClubId clubId, TacticDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var roster = await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The club no longer exists in the database.");
        var members = roster.Players.Select(p => p.Player.Id).ToHashSet();
        if (draft.Assignments.FirstOrDefault(a => !members.Contains(a.PlayerId)) is { } stranger)
        {
            throw new DomainException($"Only {roster.Club.Name} players can be assigned (player {stranger.PlayerId} is not in the squad).");
        }

        using var scope = scopes.CreateScope();
        var repo = Repo(scope);
        var existing = await repo.ListAsync(t => t.ClubId == clubId, cancellationToken).ConfigureAwait(false);
        var name = draft.Name?.Trim() ?? string.Empty;
        if (existing.Any(t => t.Id != draft.Id && string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new DomainValidationException($"{roster.Club.Name} already has a tactic called '{name}'.");
        }

        Tactic tactic;
        if (draft.Id is { } id)
        {
            tactic = existing.FirstOrDefault(t => t.Id == id) ?? throw new DomainException("The tactic no longer exists in the database.");
            tactic.Update(name, draft.OffensiveSystem, draft.Offense, draft.DefensiveSystem, draft.Defense);
        }
        else
        {
            tactic = new Tactic(TacticId.New(), clubId, name, draft.OffensiveSystem, draft.Offense, draft.DefensiveSystem, draft.Defense);
            if (!existing.Any(t => t.IsSelected))
            {
                tactic.Select();
            }

            repo.Add(tactic);
        }

        tactic.SetAssignments(draft.Assignments);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tactic;
    }

    public async Task SelectAsync(TacticId tacticId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var repo = Repo(scope);
        var tactic = await repo.FindAsync(tacticId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The tactic no longer exists in the database.");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Two saves: the filtered unique index allows one selected tactic per club at any moment.
        foreach (var other in await repo.ListAsync(t => t.ClubId == tactic.ClubId && t.IsSelected && t.Id != tacticId, cancellationToken).ConfigureAwait(false))
        {
            other.Deselect();
        }

        await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        tactic.Select();
        await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(TacticId tacticId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var repo = Repo(scope);
        var tactic = await repo.FindAsync(tacticId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("The tactic no longer exists in the database.");
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        repo.Remove(tactic);
        await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (tactic.IsSelected
            && (await repo.ListAsync(t => t.ClubId == tactic.ClubId, cancellationToken).ConfigureAwait(false))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).FirstOrDefault() is { } next)
        {
            next.Select();
            await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TeamTactics> GetMatchTacticsAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        Tactic? selected;
        using (var scope = scopes.CreateScope())
        {
            selected = (await Repo(scope).ListAsync(t => t.ClubId == clubId && t.IsSelected, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        if (selected is null)
        {
            return TeamTactics.Default;
        }

        var members = (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false))?.Players.Select(p => p.Player.Id).ToHashSet() ?? [];
        var tactics = selected.ToTeamTactics();
        return tactics with { Assignments = tactics.Assignments.Where(a => members.Contains(a.Key)).ToDictionary() };
    }

    private static IRepository<Tactic, TacticId> Repo(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IRepository<Tactic, TacticId>>();
}
