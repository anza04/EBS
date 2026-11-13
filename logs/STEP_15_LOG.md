# STEP 15 — European country and league expansion

**Date:** 2026-11-13
**Version:** 0.15.0
**Status:** Completed. All acceptance criteria met; datasets are partial by design (placeholder clubs, no players —
Step 16), see `docs/COUNTRY_REPORT.md`.

## 1. Objectives

From `docs/ROADMAP.md`, Step 15: add 18 countries with their first two divisions (leagues, hierarchy, format,
calendar, promotion/relegation, registration, foreign and nationality rules), verified for the game season.

| Criterion | Proof |
|---|---|
| All configured leagues can be initialized | `EuropeDatasetTests`: the dataset imports (35 competitions, 512 clubs, entries) |
| Fixtures can be generated | Same test: every schedule generated from the league's own start offset, all fixtures inside the season |
| Standings work | Same test: full table per competition, wins = losses; second-phase tables (Slovenia, Czech Republic, BNXT) |
| Promotion and relegation work where applicable | Same test: promoted/relegated counts match the rules, relegated = bottom of the final table, every division keeps its size after `SeasonTransition` |
| Country-specific rules do not break the core engine | No country code in the engine; all formats are rules JSON. Milestone 1 suites green |

## 2. Files

### Created (part 2)
| File | Purpose |
|---|---|
| `build/generate-europe-dataset.py` | Deterministic generator of the European dataset (the source to edit) |
| `data/europe/europe-2026-27.json` | Generated dataset |
| `docs/COUNTRY_REPORT.md` | Country-by-country report (handoff) |
| `tests/Ebm.Tests.Integration/Competitions/EuropeDatasetTests.cs` | Acceptance test |

### Modified (part 2)
`docs/COMPETITION_FORMATS.md` (second phase, play-in, byes, playoff places, cross-border, start offsets),
`docs/IMPORT_FORMATS.md` (`euEea`, dataset), `docs/ROSTER_RULES.md` (`maxNonEuPlayers`), `README.md`,
`Directory.Build.props` (0.15.0), `CHANGELOG.md`, `PROJECT_STATE.md`. Part 1 (engine, EU limits): commit `b1e0c87`.

## 3. Verification of names and formats

Web sources checked for 2026-27: Primera FEB (18 clubs, leader promoted, 2nd–9th playoff), BBL (18 clubs, top six
direct), LBA (16 clubs, last relegated), Super League Basketball (10 clubs; GBBL delayed to 2027). The other formats
use the last known (2025-26) regulations and are marked *2025-26 basis* in the report.

## 4. Architectural decisions

Decisions 50–52 in `PROJECT_STATE.md`: whole-season engine `CompetitionSeason`; EU/EEA as a country flag;
the European dataset is generated from a script and uses placeholder clubs until Step 16.

## 5. Database changes

Migration `20261113102815_CountryEuEea` (part 1): `Countries.IsEuEea` (add-column, default false; existing EU/EEA/CH
countries set by SQL). No change in part 2.

## 6. Tests and validation

| Run | Result |
|---|---|
| `dotnet build` (Release) | PASS, 0 warnings, 0 errors |
| `dotnet test` (all, including launch) | PASS 424/424 (310 unit, 114 integration) |
| `EuropeDatasetTests` | PASS (≈ 36 s, 35 competitions played to completion) |

## 7. Known limitations

* Clubs are fictional placeholders and have no players: the European leagues cannot be played as a career yet
  (every match would be a forfeit). Step 16.
* Engine gaps (mid-season entrants such as ABA League clubs, play-outs, two-legged ties, final fours, licensing):
  `docs/COUNTRY_REPORT.md` §3.
* One shared season record; real matchday dates and international windows are not modelled.
* The dataset and the Italian sample are alternative worlds (both define a 2026-27 season and Italian leagues).

## 8. Outstanding issues

None.

## 9. Instructions for the next step

1. Read `PROJECT_STATE.md`, this log, `docs/COUNTRY_REPORT.md` and `docs/ROADMAP.md` (Step 16: European player database).
2. Replace the placeholder clubs through the generator (keep league, division, competition and season IDs).
3. Keep `MilestoneOneTests`, `CareerSimulationTests` and `EuropeDatasetTests` green.
