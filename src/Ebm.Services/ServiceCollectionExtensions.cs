using Ebm.Domain.Contracts;
using Ebm.Domain.Development;
using Ebm.Domain.Finances;
using Ebm.Domain.Medical;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Scouting;
using Ebm.Domain.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Contracts;
using Ebm.Services.Development;
using Ebm.Services.Finances;
using Ebm.Services.Chemistry;
using Ebm.Services.Medical;
using Ebm.Services.Editing;
using Ebm.Services.History;
using Ebm.Services.Management;
using Ebm.Services.Matches;
using Ebm.Services.News;
using Ebm.Services.Players;
using Ebm.Services.Scouting;
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
    /// Every service that needs the game date must take the shared <see cref="GameClock"/> (career date, else the
    /// <see cref="TimeProvider"/> date) from DI so tests can substitute a fake clock. Use cases that write data create their own DI scope
    /// per unit of work, so they are registered as singletons.
    /// </remarks>
    public static IServiceCollection AddGameServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<GameClock>();
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
        services.TryAddSingleton(ScoutingEstimator.Default);
        services.TryAddSingleton<IScoutingService, ScoutingService>();
        services.TryAddSingleton(PlayerDevelopment.Default);
        services.TryAddSingleton(RetirementRules.Default);
        services.TryAddSingleton<IDevelopmentService, DevelopmentService>();
        services.TryAddSingleton(InjuryRules.Default);
        services.TryAddSingleton<IMedicalService, MedicalService>();
        services.TryAddSingleton<IStaffService, StaffService>();
        services.TryAddSingleton(FinanceModel.Default);
        services.TryAddSingleton<IFinanceService, FinanceService>();
        services.TryAddSingleton<ITacticsService, TacticsService>();
        services.TryAddSingleton<IChemistryService, ChemistryService>();
        services.TryAddSingleton<IMatchService, MatchService>();
        services.TryAddSingleton<INewsService, NewsService>();
        services.TryAddSingleton<IHistoryService, HistoryService>();
        services.TryAddSingleton<IClubManagementService, ClubManagementService>();
        services.TryAddSingleton<ICareerService, CareerService>();

        return services;
    }
}
