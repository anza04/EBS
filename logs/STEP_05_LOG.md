# STEP 05 — Player model and attributes

## 1. Status
- Status: COMPLETED
- Date: 2026-11-09
- Build version: 0.5.0
- Commit/reference: uncommitted on top of `0964df7` (STEP 04)
- Previous step: STEP 04 — Countries, leagues and competitions
- Next step: STEP 06 — Club and roster management

## 2. Objectives
- Complete player model: name, date of birth, nationality, height, weight, wingspan, position and secondary
  positions, current club, contract, experience, reputation, potential, fitness, morale, injury status.
- Attributes in seven categories (shooting, finishing, playmaking, defense, rebounding, physical, mental).
- Attribute ranges, position-independent ratings, position suitability, overall rating, potential rating,
  player tendencies; no reliance on a single overall rating.
- Deliverables: player domain model, attribute calculation service, player profile interface, player comparison
  interface.
- Acceptance: players can be created and edited; attributes are persisted; overall ratings are calculated
  consistently; position suitability can be evaluated; player profiles display all relevant information.

## 3. Implementation summary
- **Model** (`Ebm.Domain.Players`): `Player` gains `WingspanCm`, `SecondaryPositions` (`PositionSet`),
  `Experience`, `Reputation`, `Potential`, `Fitness`, `Morale`, `InjuryStatus`, `InjuryReturnDate`, `Tendencies`,
  `AgeOn(date)`, and `UpdateProfile` / `UpdateCondition` / `UpdateTendencies`. The constructor is unchanged and
  applies documented defaults. Current club and contract stay in `PlayerClubHistory` / `Contracts`.
- **Catalogue**: `PlayerAttribute` (27, in category order), `AttributeCategory` (7), `PlayerTendency` (8) and
  `PlayerCatalog` (category of an attribute, display names, import field names).
- **Rating sets**: `RatingSet<TKey>` (1–100, complete, indexer, `ToDictionary`); `PlayerAttributes` (27 columns,
  `Uniform`, `With`) and `PlayerTendencies` (8 columns, `Suggest` from attributes, `With`).
- **Attribute calculation service** (`Ebm.Domain.Players.Ratings`): `RatingModel` (position weights, ideal reach,
  reach penalties, familiarity penalties, suitability gaps; `Default`, `Validate`) and `PlayerRatingCalculator`
  (`CategoryRating`, `PositionRating`, `Evaluate` → `PlayerEvaluation`: overall, potential rating, categories,
  per-position rating/effective rating/familiarity/suitability, best position). Registered as a singleton.
- **Profile service** `IPlayerProfileService` (`Ebm.Services.Players`): `GetProfileAsync` (nationality, age from
  `TimeProvider`, current club, running contract, evaluation) and `CompareAsync` (rows for profile, ratings,
  categories, positions, attributes, tendencies, condition; single leader where higher is better).
- **Screens**: *Player profile* and *Compare players* tabs (`ViewModels/Players`, `Themes/Players.xaml`); the player
  editor edits every field, attributes grouped by category, tendencies with *Suggest from attributes*.
- **Persistence**: migration `PlayerModel`, hand-written so no table is rebuilt (see §6).
- **Import**: 27 required attribute columns; optional profile, condition and tendency columns (defaults /
  suggested tendencies); all field errors of a record are reported before domain rules run.
- **Data**: sample dataset (24 players), JSON template and `players.csv` converted to the new format with
  plausible profiles (deterministic conversion script, not shipped).

## 4. Files changed
### Created
- `docs/PLAYER_MODEL.md` — definitions, ranges, formulas, position evaluation, upgrade mapping (handoff)
- `logs/STEP_05_LOG.md` — this log
- `src/Ebm.Domain/Players/PlayerAttribute.cs` (enums + `PlayerCatalog`), `RatingSet.cs`, `PlayerTendencies.cs`
- `src/Ebm.Domain/Players/Ratings/RatingModel.cs`, `PlayerRatingCalculator.cs`
- `src/Ebm.Services/Players/IPlayerProfileService.cs`, `PlayerProfileService.cs`
- `src/Ebm.Persistence/Migrations/20261109125814_PlayerModel.cs` (+ `.Designer.cs`)
- `src/Ebm.Desktop/ViewModels/Players/PlayerViewModels.cs`, `src/Ebm.Desktop/Themes/Players.xaml`
- `tests/Ebm.Tests.Unit/Players/PlayerModelTests.cs`, `PlayerRatingCalculatorTests.cs`
- `tests/Ebm.Tests.Integration/Players/PlayerProfileTests.cs`, `PlayerScreensRenderTests.cs`
- `tests/Ebm.Tests.Integration/TestSupport/FixedTimeProvider.cs`

### Modified
- `src/Ebm.Domain/Players/Player.cs`, `PlayerAttributes.cs` (27 attribute properties), `Position.cs`
  (`PositionSet`)
- `src/Ebm.Persistence/Configurations/PeopleConfigurations.cs`, `Migrations/EbmDbContextModelSnapshot.cs`
- `src/Ebm.Services/Editing/IDatabaseEditor.cs` (`PlayerDraft` carries every field), `DatabaseEditor.cs`,
  `ServiceCollectionExtensions.cs`
- `src/Ebm.DataImport/Sources/ImportFields.cs`, `Validation/ImportSession.cs`, `Validation/FieldReader.cs`
  (`OptionalDate`)
- `src/Ebm.Desktop/App.xaml`, `MainWindow.xaml`, `Themes/Editor.xaml`, `ViewModels/Editor/PlayersPageViewModel.cs`,
  `DatabaseEditorViewModel.cs` (profile and comparison pages, refreshed on reload)
- `data/sample/italy-sample.json`, `data/templates/dataset-template.json`, `data/templates/players.csv`
- Tests: `TestData`, `SampleWorld`, `TestDatabase` (optional clock), `DatabaseEditorTests`, `ImportTests`,
  `ConstraintTests`, `DatabaseInitializerTests`, `RepositoryTests`
- `Directory.Build.props` (0.5.0), `README.md`, `CHANGELOG.md`, `PROJECT_STATE.md`, `docs/DATABASE_SCHEMA.md`,
  `docs/IMPORT_FORMATS.md`, `docs/TECHNICAL_ARCHITECTURE.md`

### Removed
- none (the old 8 rating properties of `PlayerAttributes` were replaced)

## 5. Architecture and technical decisions
- **Ratings are calculated, never stored.** Pure functions over a `RatingModel`, so formula changes apply to every
  player and nothing can go stale. Overall = effective rating at the primary position (stable, explainable);
  `PlayerEvaluation` exposes categories, all positions and best position so later steps (AI, tactics, simulation)
  do not depend on one number.
- **Position rating = skill fit + physical fit.** Weighted attribute mean per position, minus a capped penalty for
  reach (height plus half the wingspan surplus) outside the position's ideal range; familiarity (primary /
  secondary / unfamiliar) is a separate penalty. Suitability is the gap to the player's best position, so it is
  relative to the player, not absolute.
- **Catalogue as enums with one column per value.** Type-safe, queryable columns with a range check constraint;
  the enum order is the storage/display order and groups attributes by category. `RatingSet<TKey>` avoids
  duplicating validation between attributes and tendencies.
- **Tendencies separate from ability**, defaulting to a documented suggestion (same formula in C# and in the
  migration SQL; an upgrade test asserts they agree).
- **Current club and contract are not duplicated on `Player`**; the profile service derives them (open club spell,
  else the running contract).
- **Migration without rebuilds.** Changing `PlayerAttributes` columns and its check constraint would make EF
  rebuild the table outside a transaction. The hand-written `Up` renames the old table, creates the new one, copies
  with SQL, drops the old one, and only adds columns to `Players`; the generated snapshot/designer are untouched,
  so the "no pending model changes" test still guards the model. New `Players` columns are range-checked by the
  domain only (decision 21).
- **Import reads every field first**, then applies the domain rules, so one bad record reports all its problems.
- Recorded as decisions 21 (extended), 22 and 23 in `PROJECT_STATE.md`.

## 6. Database changes
- New entities: `PlayerTendencies` (owned 1:1 table, cascade with the player)
- Modified entities: `Players` (+ `WingspanCm`, `SecondaryPositions`, `Experience`, `Reputation`, `Potential`,
  `Fitness`, `Morale`, `InjuryStatus`, `InjuryReturnDate`, all with defaults); `PlayerAttributes` (27 attribute
  columns replace the 8 initial ratings)
- Removed entities: none
- Migrations: `20261109125814_PlayerModel`
- Compatibility considerations: Step 02–04 databases upgrade in place in one transaction: ratings mapped to the 27
  attributes (`docs/PLAYER_MODEL.md` §7), wingspan = height, potential = old mean, tendencies suggested (tested).
  Old-format player import files are refused (unknown/missing columns are reported).

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS | Debug and Release, 0 warnings, 0 errors |
| Unit tests | PASS | 198/198 (28 new): catalogue (27 attributes, per-category counts), attribute range/completeness, indexer, suggested tendencies, defaults, profile/condition/injury rules, secondary vs primary position, age, `PositionSet`; rating formulas with exact expected values, familiarity, wingspan reach, penalty cap, suitability by profile (center vs point guard), best position tie-break, potential rating, determinism, invalid models |
| Integration tests | PASS | 68/68 (10 new, incl. 1 constraint case): persistence of every field, profile (club, contract, age with fixed clock, free agent), comparison rows and leaders, profile/comparison view models, player page save with suggestion, import of optional fields and errors, Step 04 → 05 upgrade with exact attribute mapping, off-screen render of the three player screens with the app themes |
| Manual tests | PASS | Release `Ebm.Desktop.exe --smoke-test` with a fresh data root exits 0; log `schema version 20261109125814_PlayerModel`, no warnings/errors. Screenshots of the player editor, profile and comparison (rendered by the test from the sample dataset) reviewed: all fields shown, layout readable |
| Regression tests | PASS | All Step 01–04 tests, including the real executable launch and the no-pending-model-changes check |

Command: `dotnet test EuropeanBasketballManager.slnx` (screenshots: set `EBM_SCREENSHOT_DIR` to a folder).

## 8. Known limitations
- Experience, reputation, potential, fitness, morale and injury are stored and editable but no game system changes
  them yet (season progression Step 12, development Step 21, injuries Step 22).
- The rating model is code (`RatingModel.Default`); loading it from a data file is left for modding (Step 33).
- Ratings are calculated on demand; fine for one league, may need caching for large AI searches (Step 13/16).
- The comparison screen compares two players (the service supports more).
- Old-format (8-rating) player import files are not converted; only stored databases are.
- In update mode, optional player fields left out of the file reset to their defaults (consistent with
  "updates replace every field").

## 9. Outstanding issues
- None blocking. Editor tabs other than the player screens still have no visual review (carried over).

## 10. Handoff instructions
Step 06 — Club and roster management:
1. Read `PROJECT_STATE.md`, this log, `docs/PLAYER_MODEL.md` and `docs/DATABASE_SCHEMA.md`.
2. Player quality for roster views: use `PlayerRatingCalculator` (DI singleton) → `PlayerEvaluation`
   (overall, best position, `At(position)`, categories); availability: `Player.InjuryStatus`, `Fitness`.
3. A club's current players: open `PlayerClubHistory` spells (`EndDate IS NULL`) and/or running `Contracts`;
   `PlayerProfileService` shows how both are read.
4. The player editor/profile screens live in `ViewModels/Editor/PlayersPageViewModel.cs`,
   `ViewModels/Players/PlayerViewModels.cs`, `Themes/Editor.xaml`, `Themes/Players.xaml`;
   `PlayerScreensRenderTests` renders screens off-screen (reuse it for roster screens).
5. Bump `<Version>` to `0.6.0`; update `CHANGELOG.md`, `PROJECT_STATE.md` and write `logs/STEP_06_LOG.md`.

## 11. Project state
- Working features: application shell, configuration, logging, error handling, game database with migrations,
  CSV/JSON import, database editor, competition engine, complete player model with ratings, position suitability,
  tendencies, player profile and comparison screens
- Current application status: builds, launches, all tests pass
- Database version: `20261109125814_PlayerModel`
- Build command: `dotnet build EuropeanBasketballManager.slnx`
- Test command: `dotnet test EuropeanBasketballManager.slnx` (headless: add `--filter "Category!=Launch"`)
- Run command: `dotnet run --project src/Ebm.Desktop`, then Import tab → `data\sample\italy-sample.json`,
  then *Player profile* / *Compare players*

## 12. Completion checklist
- [x] Objectives implemented
- [x] Build successful
- [x] Tests passed
- [x] No critical regression
- [x] Documentation updated
- [x] PROJECT_STATE.md updated
- [x] Next step can start independently
