# STEP 09 — Tactical system

**Date:** 2026-11-12
**Version:** 0.9.0
**Status:** Completed. All acceptance criteria met.

## 1. Objectives

From `docs/ROADMAP.md`, Step 09: offensive systems (motion, pick and roll, five-out, four-out-one-in, post-oriented,
isolation, transition) and settings (tempo, shot selection, ball movement, spacing, pick-and-roll frequency, offensive
focus); defensive systems (man-to-man, 2-3, 3-2, 1-3-1, full-court press, half-court press) and settings (intensity,
ball pressure, help defense, switching, pick-and-roll coverage, rebounding priorities); player tactical roles and
assignments.

| Criterion | Proof |
|---|---|
| Tactical configurations can be created and saved | `TacticsServiceTests.Tactics_are_saved_selected_changed_and_handed_to_the_match_engine`; render test saves a tactic through the *Tactics* screen |
| Teams can use different offensive and defensive systems | Same test (two tactics with different systems per club); `TacticRulesTests.A_tactic_stores_systems_settings_and_assignments` |
| Player assignments are stored | Assignment round trip in the service test and the render test |
| Tactics can be changed before a match | `SelectAsync` / edit of the selected tactic in the service test |
| Tactical configurations are accessible to the match engine | `GetMatchTacticsAsync` → `TeamTactics` (service test, `Assignments_of_players_who_left_are_ignored_by_the_match_engine`) |

## 2. Files

### Created
| File | Purpose |
|---|---|
| `src/Ebm.Domain/Tactics/TacticId.cs`, `TacticEnums.cs`, `TacticSettings.cs`, `Tactic.cs`, `TacticalRoles.cs` | Systems, settings, levels, `Tactic`, `TacticalAssignment`, `TeamTactics`, role suggestions |
| `src/Ebm.Persistence/Configurations/TacticConfigurations.cs` | Mapping (prefixed settings columns, JSON assignments, selection index) |
| `src/Ebm.Persistence/Migrations/20261112140336_Tactics*.cs` | Migration |
| `src/Ebm.Services/Tactics/ITacticsService.cs`, `TacticsService.cs` | Use cases |
| `src/Ebm.Desktop/ViewModels/Tactical/TacticsViewModel.cs`, `Themes/Tactics.xaml` | *Tactics* screen |
| `docs/TACTICS.md` | Handoff documentation |
| `tests/Ebm.Tests.Unit/Tactics/TacticRulesTests.cs`, `tests/Ebm.Tests.Integration/Tactics/TacticsServiceTests.cs` | Tests |

### Modified
`ServiceCollectionExtensions` (Services), `EbmDbContextModelSnapshot`, `App.xaml`, `MainWindow.xaml`,
`DatabaseEditorViewModel`, `Directory.Build.props` (0.9.0), `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`,
`docs/DATABASE_SCHEMA.md`; tests `DatabaseInitializerTests`, `ContractServiceTests` (latest migration),
`DatabaseEditorTests`, `PlayerScreensRenderTests` (constructor, tactics flow and screenshot).

### Removed
None.

## 3. Features implemented

* Tactic model with validation (levels 1–5, names, unique assignments, at most 30 assignments).
* Ten tactical roles with attribute fits and position-based suggestions.
* Several tactics per club, one selected (used in the next match); automatic selection of the first tactic and after
  deleting the selected one.
* `TeamTactics` for the engine, with suggested roles for unassigned players.
* *Tactics* screen: list, form, role grid, select for next match, delete.

## 4. Architectural decisions

Decision 38 in `PROJECT_STATE.md`. Also:
* Assignments are a JSON column (EF `ToJson`) rather than a table: they are replaced as a whole on every save and
  never queried by player, and a JSON column avoids key clashes when an owned collection is rebuilt.
* Selection flag on the tactic (filtered unique index) instead of a column on `Clubs`: no change to an existing table.
* The engine's exact formulas are defined in Step 10; this step documents the intended effects (`docs/TACTICS.md`
  §2–§4).

## 5. Database changes

Migration `20261112140336_Tactics`: new table `Tactics` (create-table only). 21 tables. Columns in
`docs/DATABASE_SCHEMA.md` → *Tactics*.

## 6. Tests

| Run | Result |
|---|---|
| `dotnet build` (Debug, Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 365/365 (261 unit, 104 integration) |

New: 4 unit, 3 integration, render test extended (screenshot reviewed).

## 7. Known limitations

* The match engine does not exist yet; tactics have no effect until Step 10.
* AI clubs have no tactics of their own (they play `TeamTactics.Default` until Step 12 adds AI lineups and tactics).
* Combo boxes show enum names (`PickAndRoll`, `Zone23`).

## 8. Outstanding issues

None new.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log and `docs/TACTICS.md`, then the Step 10 specification.
2. Read tactics only through `ITacticsService.GetMatchTacticsAsync` / `TeamTactics`; scale effects with
   `TacticLevel.Offset`; use `TeamTactics.AssignmentOf` for roles, shot priority and minutes targets.
3. Lineups come from `IClubService.GetRosterAsync` (starters, rotation order, bench, availability).
4. Store match results through the existing `Matches` / `PlayerMatchStatistics` tables and
   `ICompetitionService.RecordResultAsync` for standings.
