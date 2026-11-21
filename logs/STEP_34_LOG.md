# STEP 34 — Final optimization and release

## 1. Status
- Status: COMPLETED (2026-11-21). Version 1.0.0 (Milestone 3 release).
- Build: PASS (Debug and Release, 0 warnings, 0 errors); Windows package PASS (§5).

## 2. Objectives
Performance and database optimization, save-game integrity, backup and recovery, error handling, UI consistency,
accessibility and usability, Windows packaging, full regression testing, long-term career simulation testing.

| Acceptance criterion | Where |
|---|---|
| The application builds reliably | Debug build 0 warnings; `build/publish.ps1` (self-contained publish, smoke test, zip) |
| Save/load is stable | Verified saves, damaged saves refused, automatic backups before *Load*/*New game* (`SaveIntegrityTests`, `MilestoneOneTests`) |
| Long-term simulations complete successfully | `LongTermWorldTests` (3 seasons, Step 32) and `MilestoneTwoTests` (accepted 2026-11-18); not re-run on this code (§8) |
| No critical data corruption occurs | Integrity check on save and load; startup recovery from the newest intact backup (`A_damaged_game_database_is_restored_from_the_newest_backup_at_startup`) |
| All major features are documented | `docs/` (map in docs/RELEASE.md §5); new docs/SAVE_FILES.md §4 |
| A clean Windows installation works | `build/publish.ps1` smoke test against an empty data folder |

Already available and reused: global exception handler and error policy (Step 01), smoke-test mode and release
script (Step 14), UI consistency pass and global value formatting (Step 33 UI commits).

## 3. Implementation summary
- `Ebm.Persistence.DatabaseFiles` (new): backup-API copy, `PRAGMA quick_check`, career detection, automatic backups
  (`Autobackup <yyyy-MM-dd HHmmss>.ebmsave` in the saves folder, newest five kept).
- `SaveGameStore`: a new save is verified before it replaces the old one; *Load* refuses a damaged save and backs up
  the live career first; *New game* backs it up too. `TimeProvider` injected for backup names.
- `DatabaseInitializer.StartAsync`: recovery of a damaged game database (kept as `ebm.db.damaged-<timestamp>`, newest
  intact backup restored, logged as an error), backup before pending migrations, `PRAGMA optimize` in `StopAsync`.
- Performance: `ClubManagementService.ClubsShortOfPlayersAsync` returns only clubs that can afford a signing (balance
  ≥ minimum salary; above the squad minimum also wage-budget room ≥ minimum salary, the same rule `FillSquadsAsync`
  applies). The Monday window therefore stops reloading the market for clubs that could sign nobody, the suspected
  cause of the season-2 slowdown. Wage bills moved to a shared `WageBillsAsync`.
- Release testing round (after the first slow-suite run):
  - Renewals: AI clubs renew their best 12 players (`RenewalPolicy.CoreSquad`, was 10); when a player rejects the
    current salary, a second offer at his asking salary in the same talks (within `MaxRaise` and the wage budget).
    Measured on the stopped season-2 database: only 190 of 1,262 contracts expiring on 30 Jun 2027 were renewed, 517
    talks broken off.
  - The 1 July stall was the emergency squad filling, not the game day (2.7 s): 2,172 demand reads took 177 s of
    183 s. Demands are now cached per club and read in chunks of 100 (was 25): the same 51 signings in 54 s.
  - Below the squad minimum an AI club signs the cheapest free agent even into debt (`FindSigningAsync` fallback no
    longer requires the balance; `ClubsShortOfPlayersAsync` always includes such clubs). Cause of 76 forfeits in
    season 2 of `MilestoneTwoTests`: clubs in debt with 4-8 players could sign nobody.
- UI: `DisplayFormatting.Humanize` shows zone defences as "2-3 zone" etc. and parses them back. The "tactics screen
  shows enum names" known issue had already been fixed by the Step 33 formatting pass; it is removed from the list.
- Version 1.0.0; versioning note in docs/TECHNICAL_ARCHITECTURE.md.
- Decisions: backups are ordinary saves (no new UI, loadable from *Saved games*); no WAL journal mode (the backup-API
  load overwrites the live file in place, and WAL would add side files to every save); an empty game is never
  backed up.
- No database migration.

## 4. Files changed
- New: `src/Ebm.Persistence/DatabaseFiles.cs`, `tests/Ebm.Tests.Integration/Persistence/SaveIntegrityTests.cs`,
  `tests/Ebm.Tests.Integration/Tactics/DisplayFormattingTests.cs`, this log.
- Modified: `SaveGameStore.cs`, `DatabaseInitializer.cs`, `ClubManagementService.cs`, `IClubManagementService.cs`,
  `DisplayFormatting.cs`, `MilestoneOneTests.cs` (the load now leaves one automatic backup), `Directory.Build.props`,
  `docs/SAVE_FILES.md`, `docs/RELEASE.md`, `docs/TECHNICAL_ARCHITECTURE.md`, `CHANGELOG.md`, `PROJECT_STATE.md`.

## 5. Tests
- Integration (affected classes, by user decision): `SaveIntegrityTests` (4 new: rotation, new game backup, damaged
  save refused, startup recovery), `MilestoneOneTests`, `DatabaseInitializerTests`, `MedicalServiceTests`,
  `HostCompositionTests` — 29 PASS; `CareerServiceTests`, `DevelopmentServiceTests`, `CareerSimulationTests` (fast) — 10
  PASS; `DisplayFormattingTests` (5 new) and `PlayerScreensRenderTests` — 6 PASS; tactics/architecture tests 15 PASS.
- Windows package: PASS — `build/publish.ps1` published the self-contained single-file build, the smoke test passed
  against an empty data folder, `artifacts/EuropeanBasketballManager-1.0.0-win-x64.zip` (64 MB).
- Full release run (2026-11-21, detached): unit 490/490 PASS, fast integration 182/182 PASS, launch 1/1 PASS.
- Slow suite (`Category=Slow`, 88 min): 9/10 PASS (`LongTermWorldTests`, `CareerSimulationTests`,
  `RealLeagues*`, `EuropeanTransferWindowTests` …). `MilestoneTwoTests` FAIL: both seasons completed (season 1 in
  33 min, season 2 in 54 min with 49 window days, no 1 July stall) but 76 forfeits of 3,382 matches (limit 33).
- After the squad-minimum fix: management, transfer window, medical, careers, development tests 21 PASS;
  `MilestoneTwoTests` re-run pending (`artifacts/test-results/milestone2b.log`).

## 6. Known limitations
- Recovery uses automatic backups only (not the user's own saves) and tells the user through the log, not a dialog.
- The renewal rate after the renewal changes is not measured (the test database is deleted at the end of the run).
- Integrity uses `quick_check` (no foreign-key check; the game's database validator covers references).

## 7. Handoff instructions
- Before publishing 1.0.0: run the full fast suite (`--filter "Category!=Launch&Category!=Slow"`), the launch test
  and the slow suite in the background; time a season-2 game day on `data/real-leagues` to confirm the speed-up.
- Look into the `NewsServiceTests` order sensitivity reported in Step 33.

## 8. Outstanding issues
- `MilestoneTwoTests` must pass again after the squad-minimum fix before 1.0.0 is final.
