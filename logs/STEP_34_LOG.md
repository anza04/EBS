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
- Not run: full unit and fast integration suites, slow suite (`Category=Slow`).

## 6. Known limitations
- Recovery uses automatic backups only (not the user's own saves) and tells the user through the log, not a dialog.
- The season-2 speed-up is not measured (needs a real-leagues season-2 run, slow suite).
- Integrity uses `quick_check` (no foreign-key check; the game's database validator covers references).

## 7. Handoff instructions
- Before publishing 1.0.0: run the full fast suite (`--filter "Category!=Launch&Category!=Slow"`), the launch test
  and the slow suite in the background; time a season-2 game day on `data/real-leagues` to confirm the speed-up.
- Look into the `NewsServiceTests` order sensitivity reported in Step 33.

## 8. Outstanding issues
- Full and slow suites not run on Steps 29–34 code (user instruction: the user runs them).
