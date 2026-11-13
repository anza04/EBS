# Player Database (Step 16)

How the European world is filled with clubs and players, how data quality is recorded, and what is still missing.
Generator: `build/generate-europe-dataset.py`. Files: `data/europe/`. Tests: `EuropePlayerDatabaseTests`,
`PlayerDataQualityTests`.

## 1. Coverage

| Part | Coverage | Status |
|---|---|---|
| Countries | 18 league countries + United States (nationality of foreign players) | as Step 15 |
| Clubs, top divisions | 240 real clubs in 17 top divisions (BNXT counts as one: 10 Belgian + 10 Dutch) | **Unverified**: compiled from 2025-26 knowledge, not checked against the 2026-27 registrations; names without sponsors where the club is commonly known without one |
| Clubs, second divisions and NBL / TDM1 / Promotiedivisie | 272 fictional placeholders named after real cities | **Generated**; same IDs as Step 15 |
| Players | 6,144: 12 per club, every country | **Generated** (fictional names from per-country pools, attributes by club strength and position) |
| Second nationality | 204 US players with an EU passport (count as EU, sporting nationality stays US) | Generated |
| Contracts | 6,144: one per player, start 1 July 2024–2026, end 30 June 2027–2029, salaries share 80 % of the club wage budget | **Generated** |
| Club history | 1,500 past spells (players aged 22+, previous club in their own country) | Generated |
| Free agents | none in the files: the AI keeps two per club from the first game day (docs/AI_MANAGEMENT.md §7) | |

Squads are built to the registration limits of their division (`maxForeignPlayers`, `maxNonEuPlayers`,
`minDomesticPlayers`, docs/ROSTER_RULES.md): foreign players take the best squad places.

**Sources.** No licensed player database is used, so no real player is in the files. Real clubs come from the
author's knowledge of the 2025-26 leagues and are marked Unverified. Real players can be added by importing files
that cite their sources (§3).

**Import.** Select all four files together in the *Import* tab (`europe-2026-27.json`, `players-…csv`,
`contracts-…csv`, `playerHistory-…csv`) into a new game, not with the Italian sample. About 15 s.

## 2. Data status

Every player (profile and attributes separately), contract and club has a `DataStatus` (`Ebm.Domain.Common`):

| Status | Meaning | Rule |
|---|---|---|
| `Unverified` | Real-world information not checked against a source | Import default; existing rows after the upgrade |
| `Verified` | Checked against the cited source | Requires `source` (player or contract); a verified contract requires its salary |
| `Estimated` | Filled in by the game from partial information | Set automatically when attributes or the salary are missing |
| `Generated` | Fictional | Default of records the game creates (generated free agents, negotiated contracts, transfers) |

The player profile shows the status of profile, attributes and contract, the source, the second nationality, the
club history and the market value (always an estimate: `TransferNegotiator.MarketValue`). The editor edits the
statuses, the source and the second nationality.

**Nationalities.** `NationalityId` is the sporting nationality: it decides domestic and foreign counts.
`SecondNationalityId` is a further citizenship and counts only for EU/EEA status (`Player.HasNationality`).

## 3. Missing information

| Missing | Handling |
|---|---|
| Some or all attributes | Estimated (`AttributeEstimator`): level = average of the known attributes (≥ 3), else from reputation (35 + 0.45 × reputation), plus the position offsets; attributes marked Estimated; the import notes how many |
| Tendencies | Suggested from the attributes (as before) |
| Salary | Estimated from player and club (`ContractNegotiator.AskingSalary`); terms marked Estimated; refused for a verified contract |
| Other contract terms | Defaults (monthly pay, no bonuses, no clause, no options), as before |
| Past clubs outside the database | Not importable: history entries need a club in the database |

Real contract figures are never shown as verified unless the file says so and names a source.

## 4. Identity and duplicates

* IDs are permanent. The generator derives them by UUID v5 from identity (player: name + birth date; real club:
  country + name; contract: player + start), so regenerating keeps them. Re-importing with *Update existing records*
  changes no ID (tested).
* Same player = same first name, last name and birth date, compared without accents and with common
  transliterations (`Đorđević` = `Djordjevic`, `Łukasz` = `Lukasz`): rejected as a duplicate wherever it comes from.
* Same name with birth dates at most 366 days apart: accepted with a *possible duplicate* note (date typing errors
  between sources). The generated files produce none.
* Club history entries of one player may not overlap and are never changed by an update.

## 5. Outstanding gaps

1. Real players: needs a licensed or user-supplied source; the import path (statuses, sources, estimates,
   duplicates) is ready.
2. Real clubs of second divisions and of the UK NBL, Belgian TDM1 and Dutch Promotiedivisie.
3. Verify the 240 real clubs against the 2026-27 registrations (promotions/relegations of 2026, renamed clubs).
4. Arenas, founding years, budgets and reputations of real clubs are estimates by division and rank.
5. No youth, staff or national-team data; no player history before the clubs in the database.
6. Performance: matchdays of the 512-club world (about 250 matches each) take noticeably longer than in the Italian sample (§6).

## 6. Performance

Import ≈ 15 s. A club's squad now loads only the club's contracts and spells (`ClubMembership.MembersOfAsync`);
before Step 16 every squad load read every running contract of the database, so a game day without matches took
about 48 s; it now takes under a second. Starting a career takes about 25 s; the career test (20 days, two
matchdays, every league) about 3 minutes.
