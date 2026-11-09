# Roster Rules

Club profile, squad roles, roster validation and competition registration (Step 06).
Code: `Ebm.Domain.Clubs.Club`, `Ebm.Domain.Rosters`, `Ebm.Services.Clubs.IClubService`.

## 1. Who belongs to a club

A player's **current club** is the club of his open `PlayerClubHistory` spell; without one, the club of his latest
contract running today. The club's squad is every player whose current club it is (the same rule as the player
profile). Free agents have no club.

`IClubService.AssignPlayerAsync(player, club, role)` moves a player to a club from today: it closes his open spell
(end date = today), opens a new one and moves his roster spot. It joins the bench, the inactive list or the youth
squad; starting and rotation roles are chosen in the squad. Contracts are not touched: signing, termination and free
agency are Step 07, so a moved player keeps his old contract until then (the squad shows "Contract: none").

## 2. Roles

Each club player has exactly one role, stored in `RosterSpots` (one row per player). A player without a stored spot
(new data, an import, a move made in the editor) is on the **bench**.

| Role | First team | Matchday squad | Extra field |
|---|---|---|---|
| `Starter` | yes | yes | `LineupPosition`: PG, SG, SF, PF or C |
| `Rotation` | yes | yes | `RotationOrder`: 1 = first off the bench |
| `Bench` | yes | yes | — |
| `Inactive` | yes | no | — |
| `Youth` | no | no | — |

* **First team** = every role except `Youth`: these are the registered players counted by the squad-size,
  nationality and age rules.
* **Matchday squad** = starters, rotation and bench.
* The starter/lineup-position and rotation/order pairs are enforced by the domain (`RosterSpot.Assign`) and by
  check constraints; everything else is a validation rule (§3), so an invalid squad can still be saved and fixed in
  steps.
* `SaveRosterAsync` replaces all roles at once and must list every club player exactly once. Rotation order is the
  order of the rotation players in the list (renumbered 1..n).
* Lineup position and player position are independent: a power forward can start at center. The starting-five
  rating is the effective rating at the lineup position (`docs/PLAYER_MODEL.md` §4).

## 3. Validation (`RosterValidator`)

Pure and deterministic. `Validate` runs every check with `SquadRules.Default` (the club's own roster);
`ValidateRegistration` runs only the registration checks (✓ below) with a competition's rules.
Errors make the roster invalid; warnings are advice.

| Code | Severity | Rule |
|---|---|---|
| `StartingFiveIncomplete` | Error | Exactly 5 starters |
| `StartingPositionTaken` | Error | Starters have different lineup positions |
| `UnavailableStarter` | Error | No injured (`Injured`) starter |
| `TooFewPlayers` ✓ | Error | First team ≥ `minPlayers` |
| `TooManyPlayers` ✓ | Error | First team ≤ `maxPlayers` |
| `MatchdaySquadTooLarge` ✓ | Error | Starters + rotation + bench ≤ `matchdayPlayers` |
| `TooManyForeignPlayers` ✓ | Error | Foreign first-team players ≤ `maxForeignPlayers` (when set) |
| `TooFewDomesticPlayers` ✓ | Error | Domestic first-team players ≥ `minDomesticPlayers` |
| `UnderMinimumAge` ✓ | Error | Each first-team player's age ≥ `minAge` (when set) |
| `DoubtfulStarter` | Warning | Starter is day-to-day |
| `UnavailableInSquad` | Warning | Injured player in the rotation or on the bench |
| `UnfamiliarPosition` | Warning | Starter's lineup position is neither his primary nor a secondary position |
| `ShortRotation` | Warning | Available starters + rotation < `recommendedRotation` |
| `NoContract` | Warning | First-team player without a contract running at the club |

* **Availability** (`RosterRoles.AvailabilityOf`): `Injured` → unavailable, `DayToDay` → doubtful, else available.
  Fitness and suspensions are not considered yet (Steps 22 and 08).
* **Foreign** = nationality differs from the club's country. Homegrown/locally trained status is not modelled.
* **Age** is calculated on the validation date (today).

## 4. Squad rules (`SquadRules`)

| Setting | Default | Range |
|---|---|---|
| `minPlayers` | 10 | 5–30 |
| `maxPlayers` | 18 | `minPlayers`–30 |
| `matchdayPlayers` | 12 | 5–`maxPlayers` |
| `recommendedRotation` | 8 | 5–`matchdayPlayers` |
| `maxForeignPlayers` | none | 0–`maxPlayers` |
| `minDomesticPlayers` | 0 | 0–`maxPlayers` |
| `minAge` | none | 14–40 |

The defaults apply to every club. A competition sets its own registration rules in its rules JSON under
`registration.squad` (any subset; missing settings take the defaults; unknown settings are refused):

```json
{ "registration": { "maxTeams": 16, "squad": { "maxPlayers": 16, "maxForeignPlayers": 6, "minDomesticPlayers": 5 } } }
```

The squad screen and `ClubRoster.Registrations` check the club against every competition it is entered in for the
**current season** (the earliest season that has not ended). Country-specific rules therefore live in data, never
in code.

## 5. Club profile

`Club` gains `City` (optional; shown as the home arena's city when empty), `Reputation` (1–100, default 50),
`Balance` (euros, may be negative, ±10 billion), `WageBudget` (euros a year, 0–1 billion) and `BoardExpectation`
(`WinTitle`, `ReachPlayoffs`, `MidTable` (default), `AvoidRelegation`, `WinPromotion`). The profile service adds the
country, arena, current season and competitions, and the **wage bill** (annual salaries of player contracts running
today; staff wages come with Step 27). Board expectations are stored but not yet judged (Steps 12 and 27).

## 6. Screens

* **Clubs** (editor): every club field, including city, reputation, balance, wage budget and board expectation.
* **Club profile**: details, finances, current competitions and a squad summary.
* **Squad**: one row per player with editable role, lineup position ("Starts at") and rotation number; the
  starting-five panel, live validation of the unsaved configuration (club rules and each competition), *Save squad*,
  *Discard changes*, and *Add a player to the squad* (`AssignPlayerAsync`).

## 7. For later steps

* Match engine (Step 08–10): read `ClubRoster.Starters` (lineup positions) and `ClubRoster.Rotation` (order);
  refuse to simulate when `ClubRoster.Validation.IsValid` is false, or let the AI fix the roster first (Step 13).
* Contracts (Step 07): signing a free agent should call `AssignPlayerAsync`; releasing a player should close his
  spell and remove his roster spot.
* Not implemented: roster import (spots are created in the game), shirt numbers, homegrown rules, registration
  deadlines and in-season registration changes.
