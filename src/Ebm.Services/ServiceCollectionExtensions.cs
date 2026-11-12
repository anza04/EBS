using Ebm.Domain.Contracts;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Contracts;
using Ebm.Services.Editing;
using Ebm.Services.Matches;
using Ebm.Services.Players;
using Ebm.Services.Tactics;
using Ebm.Services.Transfers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ebm.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the game services layer.
    /// </summary>
    /// <remarks>
    /// Every service that needs the current real-world time must take the shared <see cref="TimeProvider"/>
    /// from DI so tests can substitute a fake clock. Use cases that write data create their own DI scope
    /// per unit of work, so they are registered as singletons.
    /// </remarks>
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDatabaseEditor, DatabaseEditor>();
        services.TryAddSingleton<ICompetitionService, CompetitionService>();
        services.TryAddSingleton(PlayerRatingCalculator.Default);
        services.TryAddSingleton<IPlayerProfileService, PlayerProfileService>();
        services.TryAddSingleton<IClubService, ClubService>();
        services.TryAddSingleton(ContractNegotiator.Default);
        services.TryAddSingleton<IContractService, ContractService>();
        services.TryAddSingleton(TransferNegotiator.Default);
        services.TryAddSingleton(ClubRecruiter.Default);
        services.TryAddSingleton<ITransferService, TransferService>();
        services.TryAddSingleton<ITacticsService, TacticsService>();
        services.TryAddSingleton<IMatchService, MatchService>();

        return services;
    }
}
