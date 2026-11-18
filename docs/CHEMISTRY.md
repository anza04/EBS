# Squad hierarchy, morale and chemistry

Step 26. Pure rules in `Ebm.Domain.Chemistry` (`ChemistryModel`, `PlayerRelationship`); `IChemistryService`
(`ChemistryService`) applies them; the match engine reads `TeamSheet.Chemistry` and each player's morale. Every
constant below is a `public const` in `ChemistryModel` unless stated otherwise.

Chemistry is deliberately small next to ability and tactics: perfect chemistry is worth less than six points of
overall (§6).

## 1. Squad hierarchy and roles

`ChemistryModel.Hierarchy` ranks the first team (youth excluded) by overall, ties by id.

| Rank | Expected role (`SquadRole`) | Expected minutes |
|---|---|---|
| 1–2 | Star | 30 |
| 3–5 | Starter | 25 |
| 6+ aged ≤ 21 | Prospect | 12 |
| 6–8 | Rotation | 17 |
| 9+ | Reserve | 8 |

These are the Step 25 `PromiseRules.Expected` / `PromiseRules.Minutes` values, so contract demands and the dressing
room agree. A contract promise (role or minutes) raises the expectation when larger.

**Leadership.** Leader score = 0.7 × Leadership + 0.3 × Experience. The best score is the captain; every player with
a score of at least **70** (`LeaderThreshold`) is a leader.

## 2. Role satisfaction (playing time)

Minutes per club game over the last 28 days (`PromiseRules.ReviewDays`), judged only when the club played at least
3 games (`PromiseRules.MinGames`) and the player is healthy; a player who did not play has 0 minutes.

| Minutes against expectation | `RoleSatisfaction` |
|---|---|
| ≥ 100% | Happy |
| ≥ 60% | Content |
| < 60% | Unhappy |

A player whose contract carries a promise is judged by the promise review (docs/CONTRACTS.md §4.6) for morale, so
his weekly satisfaction does not change morale a second time; it still counts for conflicts.

## 3. Morale

Morale is `Player.Morale`, 1–100, 50 neutral. Every Monday (`ProcessWeekAsync`, after the week's games) every club
that played in the last seven days updates each first-team player:

| Source | Change |
|---|---|
| Results of the week | wins − losses, capped at ±2 |
| Happy with minutes | +1 |
| Unhappy with minutes | Star/Starter −3, Rotation −2, Prospect −1, Reserve 0 |
| Drift | −1 above 60; +1 below 40 when the club has a leader |

Promise reviews (±3 / −8) and these weekly changes are the only morale sources. A player is **unhappy** at morale
≤ 30 (`UnhappyMorale`).

## 4. Relationships and conflicts

`PlayerRelationship` stores one row per pair (`PlayerAId` < `PlayerBId`), score −100…100, created on the first
change and kept when the players part.

Weekly change for each pair of first-team teammates:

| Event | Change |
|---|---|
| Shared court time (both ≥ 10 minutes in a game of the week) | +2; +1 more from 3 shared games; +1 more with the same nationality |
| Conflict | −6 |

**Internal conflict.** A player unhappy (morale ≤ 30) *and* unhappy with his minutes is in conflict. His rival is the
teammate at his primary position with the most minutes ahead of him; their relationship takes the −6. Conflicts are
reported in `ChemistryWeek.Conflicts` and, for the career club, as news on the *Career* screen.

## 5. Team chemistry (`ChemistryModel.Team`)

Computed for the **core**: the starters and the first three rotation players (`CoreRotation`); in a match, players
with sheet priority ≤ 3. Each component is clamped to −1…+1:

| Component | Formula | Weight |
|---|---|---|
| Morale | (mean morale − 50) / 40 | 0.35 |
| Relationships | mean pair score / 50 | 0.25 |
| Leadership | (best leader score − 60) / 30 | 0.15 |
| Tactical fit | −(mean fit gap) / 10, never positive | 0.25 |

Fit gap = the best `TacticalRoles.Fit` among the roles typical of the player's position minus the fit of his
assigned role (0 when the assigned role fits as well or better). Ability itself is not counted: fit only measures
whether the role suits him.

Score = weighted sum − 0.1 × (unhappy core players), clamped to −1…+1. Shown as `Rating` = 50 + 50 × score.
A new database (morale 50, no relationships, suggested roles) is neutral apart from leadership.

## 6. Effect on matches

| Where | Effect |
|---|---|
| Make probability of every shot | + 0.02 × chemistry |
| Turnover chance | − 0.008 × chemistry |
| Player form of the day | + 0.01 × (morale − 50) / 50 |

`MatchService` sets `TeamSheet.Chemistry` from `IChemistryService.RateAsync` when it builds each matchday squad; it
stays fixed for the match. Measured over 300 neutral-venue games between equal teams
(`Chemistry_changes_results_in_a_controlled_way`): margin +2.1 at chemistry +1, −1.6 at 0, −3.4 at −1 (≈ 5.5
points between the extremes), against +8.7 for six points of overall; a team at −1 still wins about a third of its
games.

## 7. AI clubs and squad balance

* **Minutes for the unhappy.** In the AI lineup (`LineupPlanner`, docs/AI_MANAGEMENT.md §2) an unhappy player
  (morale ≤ 30) counts 4 points higher (`BalanceBonus`) for the rotation and bench order: he gets minutes ahead of
  a teammate up to 3 points better. Starters are still picked by effective rating.
* **Releases.** When an AI club trims a squad over the limit, unhappy players count 4 points weaker, so a
  disruptive player leaves before a slightly weaker happy one.

## 8. Screen

*Dressing room* tab: chemistry and components of the current core, captain, hierarchy (rank, role, expected and
recent minutes, satisfaction, morale, leader score, conflict) and the five strongest and weakest relationships.

## 9. Limitations

* Morale is not changed by individual performances, contract talks, transfers or the manager's words.
* Relationships are formed only on court (no off-court friendships, language groups or personalities).
* No captain choice by the user; no player complaints or transfer requests (unhappy players do not ask to leave).
* Chemistry is fixed for a match; substitutions do not change it.
