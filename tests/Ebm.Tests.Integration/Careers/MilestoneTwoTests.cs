using System.Diagnostics;
using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Finances;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Scouting;
using Ebm.Domain.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ebm.Tests.Integration.Careers;

/// <summary>
/// Milestone 2 acceptance (Step 24): the whole European dataset run by AI clubs for two seasons (or EBM_M2_SEASONS) —
/// every league, domestic cup and international competition, cross-country transfers, scouting, development, injuries,
/// finances and the season change. Prints the statistics recorded in logs/STEP_24_LOG.md.
/// </summary>
[Trait("Category", "Slow")] // full datasets or multi-season simulation: run before releases, not on every change
public sealed class MilestoneTwoTests(ITestOutputHelper output)
{
    private static readonly int Seasons = int.TryParse(Environment.GetEnvironmentVariable("EBM_M2_SEASONS"), out var n) && n > 0 ? n : 2;

    private static readonly string Folder = Path.Combine(
        typeof(MilestoneTwoTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "europe");

    [Fact]
    public async Task The_european_ecosystem_runs_for_seasons_with_ai_clubs()
    {
        await using var db = await TestDatabase.CreateAsync();
        var files = new[] { "europe-2026-27.json", "players-europe-2026-27.csv", "contracts-europe-2026-27.csv", "playerHistory-europe-2026-27.csv" };
        Assert.True((await db.Services.GetRequiredService<IDataImportService>().ImportAsync([.. files.Select(f => Path.Combine(Folder, f))], new ImportOptions())).Committed);
        var careers = db.Services.GetRequiredService<ICareerService>();
        var competitions = db.Services.GetRequiredService<ICompetitionService>();
        var clubService = db.Services.GetRequiredService<IClubService>();
        var user = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Olimpia Milano").Club;
        var state = await careers.StartAsync(user.Id, "Milestone 2");
        state = await careers.SetAssistantAsync(true);
        var clubs = (await db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id);
        var startBalance = clubs.Values.ToDictionary(c => c.Id, c => c.Balance);
        var playersAtStart = (await db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync())).Select(p => p.Id).ToHashSet();

        var purchasesAbroad = 0;
        for (var season = 1; season <= Seasons; season++)
        {
            var clock = Stopwatch.StartNew();
            var seasonId = state.Season.Id;
            var transfersBefore = (await db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync())).Select(t => t.Id).ToHashSet();
            int days = 0, windows = 0;
            while (true)
            {
                var report = await careers.ContinueAsync();
                days += report.Days.Count;
                windows += report.Days.Count(d => d.TransferWindow is not null);
                if (report.StoppedBecause == CalendarStop.SeasonComplete)
                {
                    break;
                }

                Assert.Equal(CalendarStop.DayLimit, report.StoppedBecause); // the assistant plays the user's matches
            }

            state = (await careers.LoadAsync())!;
            clubs = (await db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id);

            // Every league, domestic cup and international competition of the season is finished with a champion.
            var entered = (await db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.SeasonId == seasonId))).Select(p => p.CompetitionId).ToHashSet();
            var all = (await db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync())).Where(c => entered.Contains(c.Id)).ToList();
            var leagues = all.Count(c => c.Type == CompetitionType.League);
            var international = all.Count(c => c.Type == CompetitionType.Cup && c.CountryId is null);
            Assert.True(leagues >= 35 && international == 4, $"{leagues} leagues, {international} international competitions");
            foreach (var competition in all)
            {
                Assert.Equal(CompetitionPhase.Completed, await competitions.GetPhaseAsync(competition.Id, seasonId));
                Assert.True(clubs.ContainsKey((await competitions.GetOutcomeAsync(competition.Id, seasonId)).Champion), competition.Name);
            }

            var fixtures = (await db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.SeasonId == seasonId))).Select(f => f.Id).ToHashSet();
            var matches = (await db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Where(m => fixtures.Contains(m.FixtureId)).ToList();
            var forfeits = matches.Count(m => m.IsForfeit);
            Assert.Equal(fixtures.Count, matches.Count);
            Assert.True(forfeits <= matches.Count / 100, $"{forfeits} forfeits of {matches.Count} matches");

            // Players move across countries; every move is backed by its contract and the player's club history.
            var moves = (await db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync())).Where(t => !transfersBefore.Contains(t.Id)).ToList();
            // In season: emergency free-agent signings of foreign players; the big windows run in the offseason (counted
            // with the next season), where clubs also buy from clubs of other countries.
            var nationality = (await db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync())).ToDictionary(p => p.Id, p => p.NationalityId);
            var crossBorder = moves.Count(t => t.FromClubId is { } from ? clubs[from].CountryId != clubs[t.ToClubId].CountryId : nationality[t.PlayerId] != clubs[t.ToClubId].CountryId);
            purchasesAbroad += moves.Count(t => t.Kind is TransferKind.Transfer or TransferKind.ReleaseClause && clubs[t.FromClubId!.Value].CountryId != clubs[t.ToClubId].CountryId);
            Assert.True(crossBorder > 0, "no cross-country move");
            var spells = await db.ReadAsync(s => s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync());
            Assert.All(spells.GroupBy(x => x.PlayerId), g => Assert.True(g.Count(x => x.EndDate is null) <= 1, $"{g.Key}: several open spells"));
            var contracts = await db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId != null));
            var today = state.Today;
            Assert.All(contracts.Where(c => c.IsRunningOn(today)).GroupBy(c => c.PlayerId!.Value), g => Assert.True(g.Count(c => c.Status != ContractStatus.OnLoan) <= 1, $"{g.Key}: several contracts"));
            Assert.True(spells.Count(x => playersAtStart.Contains(x.PlayerId) && x.EndDate is not null) > 1_509, "imported history kept and extended");

            // AI clubs stay operational: squads to play with, and no systematic debt.
            var shortSquads = 0;
            foreach (var club in clubs.Values)
            {
                var roster = (await clubService.GetRosterAsync(club.Id))!;
                shortSquads += roster.FirstTeam.Count() < SquadRules.Starters ? 1 : 0;
            }

            var inDebt = clubs.Values.Count(c => c.Balance < 0);
            Assert.True(shortSquads == 0, $"{shortSquads} clubs without five players");
            Assert.True(inDebt <= clubs.Count / 20, $"{inDebt} of {clubs.Count} clubs in debt");

            // Scouting, development, injuries and finances ran.
            var reports = (await db.ReadAsync(s => s.Repo<ScoutReport, ScoutReportId>().ListAsync())).Count;
            var injuries = (await db.ReadAsync(s => s.Repo<Injury, InjuryId>().ListAsync(i => i.OccurredOn >= state.Season.StartDate))).Count;
            var ledger = await db.ReadAsync(s => s.Repo<FinancialTransaction, FinancialTransactionId>().ListAsync());
            Assert.True(reports > 0 && injuries > 0);
            var byClub = ledger.GroupBy(t => t.ClubId).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
            Assert.All(clubs.Values, c => Assert.Equal(c.Balance - startBalance[c.Id], byClub.GetValueOrDefault(c.Id)));

            output.WriteLine(
                $"Season {state.Season.Name}: {clock.Elapsed:hh\\:mm\\:ss}, {days} days, {windows} window days, {matches.Count} matches ({forfeits} forfeits), " +
                $"{leagues} leagues + {all.Count - leagues} cups, {moves.Count} moves ({crossBorder} cross-border, {purchasesAbroad} purchases abroad so far), {reports} scout reports, {injuries} injuries, " +
                $"{inDebt} clubs in debt, balances {clubs.Values.Sum(c => c.Balance):N0}");

            if (season < Seasons)
            {
                state = await careers.StartNextSeasonAsync();
                Assert.Equal(season + 1, state.Career.SeasonsPlayed);
                Assert.Contains(state.Competitions, c => c.Competition.CountryId is null); // international competitions qualified again
            }
        }

        if (Seasons > 1)
        {
            Assert.True(purchasesAbroad > 0, "no purchase from a club of another country in the offseason window");
        }
    }
}
