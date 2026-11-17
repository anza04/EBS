using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Scouting;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Scouting;

/// <summary>
/// What each club believes about every player, for decisions made on many players at once (AI recruitment,
/// docs/SCOUTING.md §6): the club's newest report when it has one, else background knowledge — an estimate as
/// accurate as its best scout gives in <see cref="ScoutingRules.BackgroundStudyDays"/> days. Reads within the caller's scope.
/// </summary>
internal sealed class ScoutingKnowledge
{
    private readonly Dictionary<ClubId, int> _bestScout;
    private readonly Dictionary<(ClubId, PlayerId), ScoutReport> _reports;
    private readonly DateOnly _today;

    private ScoutingKnowledge(Dictionary<ClubId, int> bestScout, Dictionary<(ClubId, PlayerId), ScoutReport> reports, DateOnly today)
    {
        _bestScout = bestScout;
        _reports = reports;
        _today = today;
    }

    public static async Task<ScoutingKnowledge> LoadAsync(IServiceProvider s, DateOnly today, CancellationToken cancellationToken)
    {
        var best = (await ScoutingService.ScoutsAsync(s, today, null, cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.Contract.ClubId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Member.Ability));
        var reports = (await s.GetRequiredService<IRepository<ScoutReport, ScoutReportId>>().ListAsync(null, cancellationToken).ConfigureAwait(false))
            .GroupBy(r => (r.ClubId, r.PlayerId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id.Value).First());
        return new ScoutingKnowledge(best, reports, today);
    }

    /// <summary>
    /// The overall rating <paramref name="club"/> believes the player has. Without scouts the club relies on the
    /// typical ability of its reputation. The background estimate stays the same for a club and player within a year.
    /// </summary>
    public int Overall(Club club, Player player, PlayerEvaluation truth, int visibility)
    {
        ArgumentNullException.ThrowIfNull(club);
        ArgumentNullException.ThrowIfNull(player);
        if (_reports.TryGetValue((club.Id, player.Id), out var report))
        {
            return report.EstimatedOverall;
        }

        var ability = _bestScout.TryGetValue(club.Id, out var best) ? best : ScoutingRules.TypicalAbility(club.Reputation);
        var accuracy = ScoutingRules.Accuracy(ability, ScoutingRules.BackgroundStudyDays, visibility);
        return ScoutingEstimator.EstimateRatings(truth, accuracy, ScoutingRules.StableSeed(club.Id.Value, player.Id.Value, _today.Year)).Overall;
    }
}
