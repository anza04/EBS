using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Matches;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Services.Management;
using Ebm.Services.Tactics;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ebm.Tests.Integration.Careers;

/// <summary>
/// Simulated careers (Step 13): three seasons (or EBM_SIM_SEASONS) of the sample world with AI clubs running themselves. Checks rosters,
/// transfer activity, finances, results and season progression, and prints the statistics for docs/AI_MANAGEMENT.md.
/// </summary>
public sealed class CareerSimulationTests(ITestOutputHelper output)
{
    /// <summary>Seasons to simulate: 3, or the <c>EBM_SIM_SEASONS</c> environment variable (long runs, Step 14).</summary>
    private static readonly int Seasons = int.TryParse(Environment.GetEnvironmentVariable("EBM_SIM_SEASONS"), out var n) && n > 0 ? n : 3;

    [Fact]
    public async Task Seasons_run_without_user_intervention()
    {
        await using var db = await TestDatabase.CreateAsync();
        var data = typeof(CareerSimulationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;
        var import = await db.Services.GetRequiredService<IDataImportService>().ImportAsync(
            [Path.Combine(data, "sample", "italy-sample.json"), Path.Combine(data, "sample", "italy-squads.json")], new ImportOptions());
        Assert.True(import.Committed);
        var careers = db.Services.GetRequiredService<ICareerService>();
        var clubs = db.Services.GetRequiredService<IClubService>();
        var competitions = db.Services.GetRequiredService<ICompetitionService>();
        var tactics = db.Services.GetRequiredService<ITacticsService>();
        var all = await db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var user = all.Single(c => c.Name == "Laguna Venezia");
        var state = await careers.StartAsync(user.Id, "Simulation");
        state = await careers.SetAssistantAsync(true); // nobody manages the user's club: the assistant does

        var positionsByStrength = new List<(int StrengthRank, int Position)>();
        var totalRenewals = 0;
        for (var season = 1; season <= Seasons; season++)
        {
            var transfersBefore = (await db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync())).Count;
            var strength = new Dictionary<ClubId, double>();
            foreach (var club in all)
            {
                var roster = (await clubs.GetRosterAsync(club.Id))!;
                strength[club.Id] = roster.FirstTeam.OrderByDescending(p => p.Evaluation.Overall).Take(8).Average(p => p.Evaluation.Overall);
            }

            var days = 0;
            var renewals = 0;
            var lineups = 0;
            var created = 0;
            long salaries = 0, income = 0;
            while (true)
            {
                var report = await careers.ContinueAsync();
                days += report.Days.Count;
                renewals += report.Days.Sum(d => d.Management.Renewals);
                lineups += report.Days.Sum(d => d.Management.LineupsChanged);
                salaries += report.Days.Sum(d => d.Management.SalariesPaid);
                income += report.Days.Sum(d => d.Management.Income);
                created += report.Days.Sum(d => d.Management.PlayersCreated);
                if (report.StoppedBecause == CalendarStop.SeasonComplete)
                {
                    break;
                }

                Assert.Equal(CalendarStop.DayLimit, report.StoppedBecause); // the assistant plays the user's matches
            }

            state = (await careers.LoadAsync())!;
            var review = await careers.GetSeasonReviewAsync();
            var transfers = (await db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync())).Count - transfersBefore;
            var matches = await db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync());
            Assert.DoesNotContain(matches, m => m.IsForfeit);

            output.WriteLine($"Season {state.Season.Name}: {days} days, {transfers} transfers, {renewals} renewals, {lineups} line-up changes, {created} players created, salaries {salaries:N0}, income {income:N0}");
            foreach (var competition in review.Competitions)
            {
                var table = await competitions.GetStandingsAsync(competition.Competition.Id, state.Season.Id);
                var byStrength = table.OrderByDescending(r => strength[r.ClubId]).Select((r, i) => (r, i + 1)).ToList();
                positionsByStrength.AddRange(byStrength.Select(x => (x.Item2, x.r.Position)));
                output.WriteLine($"  {competition.Competition.Name}: champion {competition.Champion}, objectives met {competition.Objectives.Count(o => o.Met)}/{competition.Objectives.Count}");
            }

            // Every club (the user's one run by the assistant) keeps a valid squad, a selected tactic and money in the bank.
            foreach (var club in all)
            {
                var roster = (await clubs.GetRosterAsync(club.Id))!;
                var first = roster.FirstTeam.Count();
                Assert.InRange(first, SquadRules.Default.MinPlayers, SquadRules.Default.MaxPlayers);
                Assert.Equal(5, roster.Starters.Count());
                Assert.Equal(IClubManagementService.AiTacticName, (await tactics.GetClubTacticsAsync(club.Id))!.Selected?.Name);
                var stored = (await db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(club.Id)))!;
                Assert.True(stored.Balance >= 0, $"{club.Name} balance {stored.Balance:N0}");
                output.WriteLine($"    {club.Name}: {first} players, balance {stored.Balance:N0}");
            }

            Assert.InRange(transfers, 0, all.Count * 6); // at most six moves per club and season
            totalRenewals += renewals;

            if (season < Seasons)
            {
                state = await careers.StartNextSeasonAsync();
                Assert.Equal(season + 1, state.Career.SeasonsPlayed);
            }
        }

        Assert.True(totalRenewals > 0); // renewals start five months before 30 June, in the offseason of the first season

        // Stronger squads finish higher on average.
        var strong = positionsByStrength.Where(p => p.StrengthRank <= 3).Average(p => p.Position);
        var weak = positionsByStrength.Where(p => p.StrengthRank >= 4).Average(p => p.Position);
        output.WriteLine($"Average position: three strongest squads {strong:0.0}, the others {weak:0.0}");
        Assert.True(strong < weak);
    }
}
