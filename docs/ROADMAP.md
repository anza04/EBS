# European Basketball Manager
## Development Roadmap & Step Specifications

**Project type:** Single-player basketball management game  
**Platform:** Windows PC  
**Mode:** Offline, single-player  
**Scope:** European basketball, top two domestic divisions per selected country  
**Database:** Real clubs and players, subject to data availability and usage rights  
**Development approach:** Incremental, modular, test-driven

---

# 1. Development principles

Every development step must be independently executable and must leave the project in a stable state.

A step is considered complete only when:

1. Its objectives have been implemented.
2. The application builds successfully.
3. Relevant automated tests pass.
4. Existing functionality has not been broken.
5. Any required database migrations are included.
6. Documentation has been updated.
7. A development log has been generated.
8. The next step can start using the log and the current project files alone.

## 1.1 Mandatory rules for every step

- Do not implement features belonging to future steps unless they are necessary dependencies.
- Do not leave incomplete implementations, temporary workarounds or placeholder logic without explicitly documenting them.
- Do not silently change existing architecture or data structures.
- Preserve backward compatibility with existing save files whenever possible.
- Use mock data when real data is not yet available.
- Separate game logic from UI logic.
- Make competition rules, player data and league configurations data-driven.
- Avoid hardcoding country-specific rules into the core game engine.
- Prefer small, testable modules over large interconnected classes.
- Do not introduce external services or mandatory online dependencies.

## 1.2 Required output for every step

Every completed step must generate:

| File | Purpose |
|---|---|
| `logs/STEP_XX_LOG.md` | Detailed record of the completed step |
| `PROJECT_STATE.md` | Updated global project status |
| `CHANGELOG.md` | Summary of relevant changes |
| Tests | Automated validation of the implementation |

The step log must contain:

- Step identifier and title.
- Implementation date.
- Objectives.
- Files created, modified or removed.
- Features implemented.
- Architectural decisions.
- Database changes and migrations.
- Tests executed and their results.
- Known limitations.
- Outstanding issues.
- Exact instructions for the next step.
- Current build/run status.

**A step must not be marked completed if its acceptance criteria are not met.**

---

# 2. Standard step execution protocol

Every step follows the same workflow.

### Phase A — Inspect

Before making changes:

1. Read `PROJECT_STATE.md`.
2. Read the previous step log.
3. Inspect the current project structure.
4. Identify existing implementations and dependencies.
5. Confirm that the requested work has not already been completed.
6. Identify potential conflicts with existing features.

### Phase B — Plan

Before coding:

1. Define the scope.
2. Identify affected modules.
3. Define expected files and database changes.
4. Define acceptance criteria.
5. Identify risks and dependencies.

Keep the implementation within the boundaries of the current step.

### Phase C — Implement

1. Implement the requested functionality.
2. Add or update automated tests.
3. Update database schemas and migrations if necessary.
4. Update technical documentation.
5. Avoid unrelated refactoring.

### Phase D — Validate

Execute the applicable checks:

- Application compilation/build.
- Unit tests.
- Integration tests.
- Database integrity checks.
- Manual UI verification where relevant.
- Regression tests for existing functionality.

Resolve all critical errors before completing the step.

### Phase E — Close and document

1. Update `CHANGELOG.md`.
2. Update `PROJECT_STATE.md`.
3. Generate `logs/STEP_XX_LOG.md`.
4. Confirm the application can be launched.
5. Confirm the next step has sufficient information to begin.

If validation fails, document the failure and continue working on the current step. Do not hand over an unfinished implementation as completed.

---

# MILESTONE 1 — PLAYABLE CORE GAME

**Objective:** Build a functional basketball management game with one country, two divisions, real-player data support, basic club management and a working basketball simulation.

At the end of this milestone, it must be possible to start a career, manage a club, play a complete season and progress into another season.

The initial country should be configurable. Italy is a suitable starting point, but the architecture must not depend on it.

## STEP 01 — Project foundation and application architecture

**Objective:** Create the initial Windows application and establish the technical architecture.

Tasks:
- Create the application project.
- Configure the development environment.
- Establish the main application entry point.
- Define the application architecture and module boundaries.
- Configure logging and error handling.
- Establish a consistent folder structure.
- Configure automated testing.
- Configure application build and execution.

Expected architecture:
- Application/UI layer.
- Game domain layer.
- Game services layer.
- Persistence layer.
- Data import layer.
- Testing layer.

Deliverables:
- Working application shell.
- Defined project structure.
- Build configuration.
- Test configuration.
- Initial technical documentation.

Acceptance criteria:
- Application builds successfully on Windows.
- Application launches.
- Logging works.
- Automated tests can execute.
- No game-specific functionality is required yet.

**Handoff:** Document the chosen technology stack, folder structure, dependencies, build commands and architectural conventions.

## STEP 02 — Core database and entity model

**Objective:** Establish the database structure for the complete European basketball ecosystem.

Implement the initial entities:

- Country.
- League.
- Division.
- Competition.
- Season.
- Club.
- Arena.
- Player.
- Player attributes.
- Staff.
- Contract.
- Player-club history.
- Fixture.
- Match.
- Match statistics.
- League standings.

Define relationships and primary keys.

Requirements:
- Players must have persistent unique IDs.
- Clubs must have persistent unique IDs.
- A player can play for different clubs over their career.
- A club can participate in multiple competitions.
- Historical data must be preserved.
- A player must not lose historical statistics when changing clubs.
- Competition rules must be stored as configurable data.
- Database migrations must be versioned.

Deliverables:
- Initial database schema.
- Migration system.
- Entity documentation.
- Repository/data access layer.
- Database initialization process.

Acceptance criteria:
- Database can be created from scratch.
- Migrations execute successfully.
- Relationships and constraints are validated.
- Sample records can be inserted and retrieved.
- Historical relationships work correctly.

**Handoff:** Document all entities, fields, relationships, constraints and migration procedures.

## STEP 03 — Data import and database editor

**Objective:** Make it possible to populate and maintain the game database.

Implement:
- CSV import.
- JSON import.
- Data validation.
- Duplicate detection.
- Unique ID generation.
- Import error reporting.
- Basic database editing interface.

The editor must support:
- Adding and editing players.
- Adding and editing clubs.
- Managing player attributes.
- Managing contracts.
- Editing league and competition information.
- Importing updated datasets.

Import operations must not silently overwrite existing information.

Deliverables:
- Import service.
- Import validation system.
- Initial database editor.
- Example import templates.
- Import logs.

Acceptance criteria:
- A sample dataset can be imported.
- Invalid records are rejected with understandable errors.
- Duplicate players and clubs are detected.
- Existing records can be updated through an explicit update operation.
- Imported data can be viewed and edited.

**Handoff:** Document the import formats, field mappings, validation rules and known data limitations.

## STEP 04 — Countries, leagues and competitions

**Objective:** Implement the European competition structure.

For the initial playable database, configure:
- One country.
- First domestic division.
- Second domestic division.
- Regular season.
- Playoffs or other applicable final stages.
- Promotion and relegation rules.

The competition engine must support:
- Different numbers of teams.
- Different competition formats.
- Configurable points systems.
- Home and away fixtures.
- Playoff qualification.
- Promotion and relegation.
- Competition registration rules.

Do not hardcode the initial country's rules into the engine.

Deliverables:
- Competition configuration system.
- League setup.
- Fixture generator.
- Standings calculator.
- Competition format documentation.

Acceptance criteria:
- A complete season schedule can be generated.
- Teams play the correct number of matches.
- Standings are calculated correctly.
- Tie-breakers are configurable.
- Playoff qualification works.
- Promotion and relegation can be calculated.

**Handoff:** Document supported competition formats and the configuration required to add another league.

## STEP 05 — Player model and attributes

**Objective:** Implement the complete player model and rating system.

Player information:
- Name and surname.
- Date of birth.
- Nationality.
- Height and weight.
- Wingspan.
- Position and secondary positions.
- Current club.
- Contract.
- Experience.
- Reputation.
- Potential.
- Fitness.
- Morale.
- Injury status.

Attributes should be divided into categories:

**Shooting**
- Close shot.
- Mid-range.
- Three-point.
- Free throws.

**Finishing**
- Layups.
- Dunking.
- Post moves.

**Playmaking**
- Passing.
- Court vision.
- Ball handling.
- Decision-making.

**Defense**
- Perimeter defense.
- Interior defense.
- Steals.
- Blocks.
- Defensive positioning.

**Rebounding**
- Offensive rebounding.
- Defensive rebounding.

**Physical**
- Speed.
- Strength.
- Vertical.
- Agility.
- Stamina.

**Mental**
- Concentration.
- Leadership.
- Consistency.
- Work rate.

Implement:
- Attribute ranges.
- Position-independent player ratings.
- Position suitability.
- Overall rating calculation.
- Potential rating.
- Player tendencies.

Avoid relying on a single overall rating for all decisions.

Deliverables:
- Player domain model.
- Attribute calculation service.
- Player profile interface.
- Player comparison interface.

Acceptance criteria:
- Players can be created and edited.
- Attributes are persisted.
- Overall ratings are calculated consistently.
- Position suitability can be evaluated.
- Player profiles display all relevant information.

**Handoff:** Document attribute definitions, ranges, rating formulas and position evaluation logic.

## STEP 06 — Club and roster management

**Objective:** Implement club management and squad construction.

Club information:
- Name.
- Country.
- City.
- Arena.
- Reputation.
- Financial balance.
- Wage budget.
- Board expectations.
- Current competitions.

Roster management:
- First team.
- Starting five.
- Bench.
- Rotation.
- Inactive players.
- Youth players.

Implement:
- Player assignment to clubs.
- Starting five selection.
- Bench management.
- Player positions.
- Squad size restrictions.
- Competition registration.
- Player availability.

Deliverables:
- Club profile.
- Squad screen.
- Starting five selection.
- Rotation management.
- Roster validation service.

Acceptance criteria:
- A club can have a complete roster.
- Players can be assigned to different roles.
- Starting five can be selected.
- Invalid roster configurations are identified.
- Competition-specific registration rules can be enforced.

**Handoff:** Document roster rules, player roles and registration validation.

## STEP 07 — Contracts and player availability

**Objective:** Implement the contract system.

Contract fields:
- Start date.
- End date.
- Salary.
- Payment frequency.
- Signing bonus.
- Performance bonuses.
- Release clause.
- Club option.
- Player option.
- Contract status.

Implement:
- New contracts.
- Contract renewals.
- Contract expiry.
- Contract termination.
- Contract history.
- Player free-agent status.
- Salary commitments.

Contract negotiations can initially use simplified calculations.

Acceptance criteria:
- Players can sign contracts.
- Contracts are linked to clubs.
- Contract expiry is handled correctly.
- Terminated players become available.
- Salary commitments are calculated.
- Historical contracts remain accessible.

**Handoff:** Document contract lifecycle, salary calculations and transaction rules.

## STEP 08 — Transfers and free agents

**Objective:** Implement the player market.

Implement:
- Free-agent list.
- Player search and filters.
- Transfer offers.
- Transfer fee negotiation.
- Contract negotiations.
- Player acceptance and rejection.
- Loans.
- Loan returns.
- Contract buyouts.
- Player transfers between clubs.

AI clubs must be able to:
- Identify roster gaps.
- Search for available players.
- Sign free agents.
- Sell players.
- Negotiate transfers.
- Respect financial limits.

Initially, AI transfer decisions can use simple rules.

Acceptance criteria:
- Players can move between clubs.
- Free agents can be signed.
- Transfers update player-club history.
- Contracts are updated correctly.
- AI clubs can complete transactions.
- Financial and roster restrictions are respected.

**Handoff:** Document transfer lifecycle, negotiation formulas and AI recruitment logic.

## STEP 09 — Tactical system

**Objective:** Implement offensive and defensive tactical configurations.

Offensive systems:
- Motion offense.
- Pick and roll.
- Five-out.
- Four-out-one-in.
- Post-oriented offense.
- Isolation.
- Transition offense.

Offensive settings:
- Tempo.
- Shot selection.
- Ball movement.
- Spacing.
- Pick-and-roll frequency.
- Offensive focus.

Defensive systems:
- Man-to-man.
- 2-3 zone.
- 3-2 zone.
- 1-3-1 zone.
- Full-court press.
- Half-court press.

Defensive settings:
- Defensive intensity.
- Ball pressure.
- Help defense.
- Switching.
- Pick-and-roll coverage.
- Rebounding priorities.

Implement player tactical roles and assignments.

Acceptance criteria:
- Tactical configurations can be created and saved.
- Teams can use different offensive and defensive systems.
- Player assignments are stored.
- Tactics can be changed before a match.
- Tactical configurations are accessible to the match engine.

**Handoff:** Document tactical options, settings, player roles and how the match engine should interpret them.

## STEP 10 — Basketball match simulation engine

**Objective:** Implement a complete possession-based match simulation.

This is one of the most important steps in the project.

Implement:
- FIBA match rules.
- Four 10-minute quarters.
- Overtime.
- Shot clock.
- Two-point and three-point shots.
- Free throws.
- Fouls and bonus.
- Rebounds.
- Assists.
- Steals.
- Blocks.
- Turnovers.
- Substitutions.
- Timeouts.
- Player fatigue.

Each possession should consider:
1. Offensive system.
2. Players involved.
3. Defensive system.
4. Individual attributes.
5. Player matchups.
6. Tactical advantages.
7. Fatigue and condition.
8. Random variation.

The engine must generate realistic individual and team statistics.

Implement three simulation modes:
- Instant result.
- Possession-by-possession commentary.
- Live management with tactical interventions.

The first version can use simplified visual presentation.

Acceptance criteria:
- Complete matches can be simulated.
- Scores follow basketball rules.
- Individual statistics are generated.
- Team statistics are generated.
- Fatigue affects performance.
- Tactical settings influence match outcomes.
- Overtime works.
- No invalid match states occur.

Run large-scale statistical simulations to validate:
- Average points.
- Shooting percentages.
- Three-point frequency.
- Free-throw frequency.
- Rebounds.
- Turnovers.
- Fouls.
- Individual player usage.

The engine should produce plausible basketball distributions rather than guaranteeing a particular result.

**Handoff:** Document possession flow, formulas, probabilities, match rules, simulation modes and statistical validation results.

## STEP 11 — Match presentation and statistics

**Objective:** Create the user interface for following and reviewing matches.

Implement:
- Match preview.
- Starting lineups.
- Bench.
- Live score.
- Quarter clock.
- Play-by-play commentary.
- Substitution controls.
- Timeout controls.
- Tactical adjustments.
- Box score.
- Player statistics.
- Team statistics.
- Match report.

Acceptance criteria:
- Matches can be followed from beginning to end.
- Live statistics update correctly.
- Tactical changes affect the ongoing match.
- Match results are saved.
- Historical box scores can be reopened.

**Handoff:** Document the match UI, event presentation and available in-game controls.

## STEP 12 — Season progression and career management

**Objective:** Connect all existing modules into a complete playable career.

Implement:
- Season calendar.
- Fixture progression.
- Matchday processing.
- League standings.
- Playoffs.
- Season completion.
- Promotion and relegation.
- Player contract expiry.
- Offseason.
- New season generation.

The user must be able to:
- Select a club.
- Start a career.
- Progress through the calendar.
- Play matches.
- Review standings.
- Complete the season.
- Start another season.

Acceptance criteria:
- A complete season can be played.
- All fixtures are processed.
- Standings are updated.
- Champions are correctly determined.
- Promotion and relegation work.
- The next season can be generated.
- Player and club histories are preserved.

**Handoff:** Document season lifecycle, calendar progression and all automated offseason processes.

## STEP 13 — AI club management and career testing

**Objective:** Ensure the game world functions independently of the user.

Implement or complete AI management of:
- Rosters.
- Starting lineups.
- Player rotations.
- Tactical selections.
- Contracts.
- Transfers.
- Free agents.
- Finances.
- Season objectives.

Run simulated careers to identify:
- Clubs with incomplete rosters.
- Unrealistic transfer activity.
- Financial instability.
- Excessive player turnover.
- Inconsistent league results.
- Broken season progression.

Acceptance criteria:
- AI clubs operate without user intervention.
- AI clubs maintain valid rosters.
- AI clubs can recruit and release players.
- AI clubs select lineups and tactics.
- Multiple seasons can be simulated without critical errors.

**Handoff:** Document AI decision rules, known weaknesses and simulation test results.

## STEP 14 — Milestone 1 integration and release

**Objective:** Produce the first complete playable version.

Tasks:
- Integrate all modules.
- Perform regression testing.
- Fix critical bugs.
- Validate career creation.
- Validate match simulation.
- Validate season progression.
- Validate save and load.
- Test long-term simulation.
- Prepare Windows executable.

Acceptance criteria:
- The game can be installed and launched.
- A career can be created.
- A club can be managed.
- Matches can be simulated.
- Transfers can be completed.
- A full season can be completed.
- A second season can be started.
- Save files can be loaded successfully.

**Milestone 1 deliverable:** A playable basketball management game with one country's first two divisions.

---

# MILESTONE 2 — COMPLETE EUROPEAN BASKETBALL ECOSYSTEM

**Objective:** Expand the game from a single-country prototype into a European basketball management simulation.

The existing game engine should be reused. New countries and competitions should primarily be introduced through data and configuration rather than country-specific code.

## STEP 15 — European country and league expansion

**Objective:** Add the remaining target countries and their first two divisions.

Initial target countries:

- Italy.
- Spain.
- Germany.
- France.
- Greece.
- Turkey.
- United Kingdom.
- Lithuania.
- Serbia.
- Croatia.
- Slovenia.
- Belgium.
- Netherlands.
- Portugal.
- Poland.
- Czech Republic.
- Finland.
- Sweden.

For each country, configure:
- Domestic leagues.
- Division hierarchy.
- Competition format.
- Season calendar.
- Promotion and relegation.
- Registration rules.
- Foreign-player restrictions where applicable.
- Nationality and homegrown rules where applicable.

Verify the official competition names and formats for the selected game database season.

Acceptance criteria:
- All configured leagues can be initialized.
- Fixtures can be generated.
- Standings work.
- Promotion and relegation work where applicable.
- Country-specific rules do not break the core engine.

**Handoff:** Produce a country-by-country implementation report, identifying complete, partial and missing datasets.

## STEP 16 — European player database expansion

**Objective:** Populate the European basketball world with real players.

Implement:
- Player database expansion.
- Player nationality and citizenship.
- Multiple nationalities where appropriate.
- Player history.
- Player positions.
- Attributes.
- Contract information where available.
- Player market value estimates.

Use a consistent data model across countries.

Missing information must be handled explicitly:
- Unknown attributes.
- Estimated values.
- Missing contract details.
- Unverified player information.

Do not invent real-world contract information and present it as verified.

Acceptance criteria:
- Players can be imported from all supported countries.
- Duplicate players are identified across leagues.
- Player IDs remain stable.
- Player-club relationships are correct.
- Missing data is distinguishable from verified data.

**Handoff:** Document database coverage, sources, data quality and outstanding gaps.

## STEP 17 — Domestic cups and additional competitions

**Objective:** Implement domestic cup competitions.

Support:
- Knockout tournaments.
- Single-match elimination.
- Two-leg ties where applicable.
- Group stages.
- Draw generation.
- Seeding.
- Qualification rules.
- Finals.
- Competition history.

Configure domestic cups for supported countries where applicable.

Acceptance criteria:
- Cup competitions can be configured.
- Draws can be generated.
- Matches can be played.
- Winners are determined correctly.
- Cup results are stored historically.

**Handoff:** Document supported cup formats and country configurations.

## STEP 18 — International club competitions

**Objective:** Implement European club competitions.

Initial competitions:
- EuroLeague.
- EuroCup.
- Basketball Champions League.
- FIBA Europe Cup.

Implement:
- Qualification rules.
- Participating clubs.
- Competition registration.
- Group or league phases.
- Playoffs.
- Finals.
- International calendars.
- Prize money and reputation rewards.

The engine must support overlapping domestic and international competitions.

Acceptance criteria:
- Clubs can participate in multiple competitions.
- International fixtures are generated.
- Calendar conflicts are detected.
- Domestic and international results coexist.
- Competition winners are correctly recorded.

**Handoff:** Document qualification logic, competition formats and calendar interaction.

## STEP 19 — European transfer market

**Objective:** Create a unified European player market.

Implement:
- Cross-country transfers.
- International free-agent recruitment.
- Transfer negotiations.
- Loan agreements.
- Player release clauses.
- Contract buyouts.
- Foreign-player restrictions.
- Registration deadlines.

AI clubs must evaluate players across all supported countries.

Acceptance criteria:
- Players can transfer between countries.
- Contracts remain consistent.
- Registration restrictions are enforced.
- AI clubs recruit internationally.
- Transfer activity is economically plausible.

**Handoff:** Document international transfer rules and any country-specific exceptions.

## STEP 20 — Scouting and player evaluation

**Objective:** Introduce a scouting system that helps users identify players.

Implement:
- Player search.
- Scouting assignments.
- Regional scouting.
- League scouting.
- Position-specific scouting.
- Youth scouting.
- Player reports.
- Attribute uncertainty.
- Estimated player value.
- Estimated salary expectations.

Scouting quality should depend on staff, scouting time and player visibility.

Acceptance criteria:
- Scouting assignments can be created.
- Reports become available after processing.
- Attribute estimates can differ from actual values.
- Scouting quality affects information accuracy.
- AI clubs can use scouting information.

**Handoff:** Document scouting mechanics, report generation and uncertainty calculations.

## STEP 21 — Training, development and player aging

**Objective:** Implement player progression over time.

Implement:
- Individual training.
- Team training.
- Shooting development.
- Physical development.
- Tactical development.
- Defensive development.
- Recovery.
- Age-related development.
- Player decline.
- Potential realization.
- Retirement.

Development should depend on:
- Age.
- Potential.
- Playing time.
- Training quality.
- Staff.
- Competition level.
- Professionalism.
- Fitness.
- Injuries.

Acceptance criteria:
- Players can improve.
- Players can decline.
- Development is not deterministic.
- Retirement occurs through configurable rules.
- AI clubs manage player development.

**Handoff:** Document development formulas, age curves and retirement rules.

## STEP 22 — Injuries and medical management

**Objective:** Add injuries and player availability management.

Implement:
- Injury types.
- Severity.
- Recovery estimates.
- Medical assessment.
- Rehabilitation.
- Training restrictions.
- Return-to-play fitness.
- Recurrence risk.
- Injury history.

Injury risk should be affected by workload, fatigue, player condition and physical attributes.

Acceptance criteria:
- Injuries can occur during matches and training.
- Injured players are unavailable when appropriate.
- Recovery progresses over time.
- Medical information is stored.
- AI clubs manage injured players.

**Handoff:** Document injury mechanics, recovery calculations and known limitations.

## STEP 23 — Financial management

**Objective:** Expand the basic financial model.

Implement:
- Club budgets.
- Wage budgets.
- Player salaries.
- Staff salaries.
- Transfer fees.
- Signing bonuses.
- Sponsorship revenue.
- Ticket revenue.
- Arena costs.
- Travel costs.
- Prize money.
- Financial reports.
- Board expectations.

AI clubs must manage their finances and avoid systematic bankruptcy.

Acceptance criteria:
- Financial transactions are recorded.
- Budgets are updated.
- Salary commitments are calculated.
- Revenue and expenditure reports are available.
- AI clubs make financially constrained decisions.

**Handoff:** Document financial calculations, budget rules and AI financial behavior.

## STEP 24 — Milestone 2 integration and release

**Objective:** Validate the complete European basketball ecosystem.

Test:
- All supported countries.
- All supported leagues.
- Domestic cups.
- International competitions.
- Cross-country transfers.
- Scouting.
- Player development.
- Injuries.
- Finances.
- Multi-season progression.

Run long simulations with AI-controlled clubs.

Acceptance criteria:
- All configured leagues operate.
- International competitions function.
- Player transfers work across countries.
- Player histories are preserved.
- AI clubs remain operational.
- Multiple seasons can be simulated without critical errors.

**Milestone 2 deliverable:** A European basketball management simulation with domestic and international competitions.

---

# MILESTONE 3 — ADVANCED MANAGEMENT AND SIMULATION

**Objective:** Improve strategic depth, realism, AI intelligence and match presentation.

## STEP 25 — Advanced contract negotiations

Implement:
- Agent relationships.
- Player salary expectations.
- Contract length preferences.
- Playing time promises.
- Squad role promises.
- Bonuses.
- Options.
- Release clauses.
- Negotiation rounds.
- Competing offers.
- Player demands.

AI agents and players should make decisions based on their own objectives.

Acceptance criteria:
- Negotiations have multiple possible outcomes.
- Players can reject offers.
- Players can counteroffer.
- Promises affect player satisfaction.
- AI negotiations are functional.

**Handoff:** Document negotiation factors, formulas and AI behavior.

## STEP 26 — Advanced roster management and chemistry

Implement:
- Squad hierarchy.
- Player roles.
- Playing time expectations.
- Leadership.
- Player relationships.
- Team chemistry.
- Tactical compatibility.
- Morale.
- Internal conflicts.
- Role satisfaction.

Chemistry should influence team performance without overpowering individual ability or tactical quality.

Acceptance criteria:
- Player morale changes over time.
- Playing time affects satisfaction.
- Relationships can develop.
- Chemistry affects match performance in a controlled way.
- AI clubs manage squad balance.

**Handoff:** Document chemistry calculations and morale behavior.

## STEP 27 — Advanced tactical engine

Expand the tactical system with:
- Individual defensive assignments.
- Matchup targeting.
- Pick-and-roll coverage variations.
- Offensive play calling.
- Special situations.
- End-of-quarter plays.
- End-of-game tactics.
- Zone attack variations.
- Defensive switching rules.
- Foul management.
- Rotation strategies.

Implement situational tactical decisions for AI coaches.

Acceptance criteria:
- Tactical instructions have measurable effects.
- Teams adapt to opponent strengths and weaknesses.
- AI coaches can adjust tactics.
- Tactical decisions are observable in match reports.

**Handoff:** Document new tactical options and their match-engine effects.

## STEP 28 — Coaching and staff management

Implement:
- Head coaches.
- Assistant coaches.
- Scouts.
- Medical staff.
- Fitness coaches.
- Player development staff.

Staff attributes:
- Tactical knowledge.
- Player development.
- Motivation.
- Scouting.
- Medical expertise.
- Reputation.
- Experience.

Implement:
- Staff contracts.
- Staff recruitment.
- Staff dismissal.
- Staff responsibilities.
- Coaching styles.
- Tactical preferences.

Acceptance criteria:
- Staff can be hired and dismissed.
- Staff attributes influence relevant systems.
- AI clubs recruit staff.
- Coaching styles affect tactics and development.

**Handoff:** Document staff roles, attributes and influence calculations.

## STEP 29 — Advanced basketball simulation

Improve match realism with:
- More detailed shot selection.
- Shot location.
- Defensive contest quality.
- Player tendencies.
- Individual matchups.
- Offensive and defensive rebounding position.
- Foul drawing.
- Turnover causes.
- Transition opportunities.
- Clutch performance.
- Player-specific decision-making.

Introduce shot charts and advanced analytics.

Potential advanced statistics:
- True shooting percentage.
- Effective field goal percentage.
- Usage percentage.
- Offensive rating.
- Defensive rating.
- Net rating.
- Assist percentage.
- Rebound percentage.
- Turnover percentage.
- Plus/minus.

Acceptance criteria:
- Advanced statistics are mathematically consistent.
- Shot charts match recorded attempts.
- Tactical and player tendencies influence outcomes.
- Large-scale statistical testing remains plausible.

**Handoff:** Document all new simulation formulas and validation results.

## STEP 30 — Advanced match visualization

Implement a 2D basketball court presentation.

Potential features:
- Court rendering.
- Player positions.
- Ball movement.
- Offensive spacing.
- Defensive positioning.
- Shot animation.
- Rebounds.
- Substitutions.
- Play-by-play synchronization.

The visualization must use the match engine as its source of truth. It must not independently determine match outcomes.

Acceptance criteria:
- Match events can be visualized.
- Player and ball positions are synchronized with simulation events.
- The visualization can be paused and resumed.
- Simulation results remain identical regardless of visualization mode.

**Handoff:** Document rendering architecture, event synchronization and performance.

## STEP 31 — News, media and club reputation

Implement:
- Transfer news.
- Injury reports.
- Match previews.
- Match reviews.
- Press conferences.
- Player interviews.
- Rumors.
- Media reactions.
- Club reputation changes.
- Fan expectations.

News should be generated from actual game events.

Acceptance criteria:
- News is generated from relevant events.
- No contradictory reports are created.
- Historical news can be accessed.
- Reputation changes are linked to game events.

**Handoff:** Document news generation rules and event triggers.

## STEP 32 — Long-term world simulation and historical records

Implement:
- Player career histories.
- Club histories.
- Competition winners.
- Season records.
- Individual awards.
- Retirements.
- Hall of fame.
- Historical statistics.
- Generated youth players.
- New coaching careers.
- Long-term league evolution.

Acceptance criteria:
- The game world progresses over many seasons.
- Player and club histories remain accessible.
- Retired players are preserved.
- New players enter the database.
- Historical competitions and awards are recorded.

**Handoff:** Document historical data structures and long-term simulation rules.

## STEP 33 — Modding and custom database support

Implement:
- Custom database import.
- Custom competition definitions.
- Custom club creation.
- Custom player creation.
- Custom league rules.
- Database export.
- Database validation.
- Optional community database packages.

Ensure that custom data cannot corrupt the active save.

Acceptance criteria:
- Custom databases can be loaded.
- Custom competitions can be configured.
- Data validation catches invalid references.
- Existing careers remain unaffected by database changes.

**Handoff:** Document modding formats, supported fields and compatibility rules.

## STEP 34 — Final optimization and release

Objective: Prepare the advanced version for long-term use.

Tasks:
- Performance optimization.
- Database optimization.
- Save-game integrity.
- Backup and recovery.
- Error handling.
- UI consistency.
- Accessibility and usability improvements.
- Windows packaging.
- Full regression testing.
- Long-term career simulation testing.

Acceptance criteria:
- The application builds reliably.
- Save/load is stable.
- Long-term simulations complete successfully.
- No critical data corruption occurs.
- All major features are documented.
- A clean Windows installation works.

**Milestone 3 deliverable:** An advanced European basketball management game with detailed tactical management, realistic simulation, historical progression and modding support.

---

# 3. Standard development log template

Every step must generate a log following this template.

Save each log as `logs/STEP_XX_LOG.md`.

## Required template

```markdown
# STEP XX — [Step title]

## 1. Status
- Status: COMPLETED / BLOCKED / INCOMPLETE
- Date:
- Build version:
- Commit/reference:
- Previous step:
- Next step:

## 2. Objectives
List the objectives assigned to this step.

## 3. Implementation summary
Describe what was implemented.

## 4. Files changed
### Created
- File path — purpose

### Modified
- File path — changes

### Removed
- File path — reason

## 5. Architecture and technical decisions
Document relevant decisions and their rationale.

## 6. Database changes
- New entities:
- Modified entities:
- Removed entities:
- Migrations:
- Compatibility considerations:

## 7. Tests and validation
| Test | Result | Notes |
|---|---|---|
| Build | PASS/FAIL | |
| Unit tests | PASS/FAIL | |
| Integration tests | PASS/FAIL | |
| Manual tests | PASS/FAIL | |
| Regression tests | PASS/FAIL | |

## 8. Known limitations
List incomplete or simplified behavior.

## 9. Outstanding issues
List unresolved bugs or technical debt.

## 10. Handoff instructions
Explain exactly what the next step must inspect and implement.

## 11. Project state
- Working features:
- Current application status:
- Database version:
- Build command:
- Test command:
- Run command:

## 12. Completion checklist
- [ ] Objectives implemented
- [ ] Build successful
- [ ] Tests passed
- [ ] No critical regression
- [ ] Documentation updated
- [ ] PROJECT_STATE.md updated
- [ ] Next step can start independently
```

---

# 4. Global project state template

Maintain one `PROJECT_STATE.md` file in the project root.

This file must be updated after every completed step.

```markdown
# PROJECT STATE

## Project
European Basketball Manager

## Current milestone
Milestone 1 / 2 / 3

## Current step
STEP XX

## Last completed step
STEP XX

## Next step
STEP XX — [Title]

## Application status
- Build:
- Tests:
- Run status:

## Technology stack
- Language:
- Framework:
- Database:
- UI:
- Build system:

## Implemented modules
- [x] Module name
- [ ] Module name

## Database
- Current schema version:
- Latest migration:
- Data status:

## Important architectural decisions
List decisions that future development must preserve.

## Known issues
List unresolved bugs and limitations.

## Current dependencies
List external libraries and tools.

## Last completed step log
logs/STEP_XX_LOG.md

## Instructions for continuing
1. Read this file.
2. Read the last completed step log.
3. Inspect the existing implementation.
4. Follow the next step specification.
5. Do not repeat completed work.
6. Generate a new step log upon completion.
```

---

# 5. Definition of done

A development step is complete only if all mandatory conditions are satisfied.

| Requirement | Mandatory |
|---|---|
| Implementation finished | Yes |
| Application builds | Yes |
| Relevant tests pass | Yes |
| Existing features remain functional | Yes |
| Database changes documented | If applicable |
| Technical documentation updated | Yes |
| Step log generated | Yes |
| Global project state updated | Yes |
| Next step can resume independently | Yes |

If a step is blocked, the log must identify:
- What is blocking it.
- What has already been completed.
- What remains.
- What is required to unblock it.
- Whether the next step can safely proceed.

Do not mark blocked work as completed.

---

# 6. Recommended project structure

```text
EuropeanBasketballManager/
│
├── PROJECT_STATE.md
├── CHANGELOG.md
├── README.md
│
├── docs/
│   ├── GDD.md
│   ├── TECHNICAL_ARCHITECTURE.md
│   ├── DATABASE_SCHEMA.md
│   ├── MATCH_ENGINE.md
│   ├── COMPETITION_RULES.md
│   └── DATA_IMPORT.md
│
├── logs/
│   ├── STEP_01_LOG.md
│   ├── STEP_02_LOG.md
│   └── ...
│
├── data/
│   ├── countries/
│   ├── leagues/
│   ├── clubs/
│   ├── players/
│   └── competitions/
│
├── src/
│   ├── application/
│   ├── domain/
│   ├── services/
│   ├── persistence/
│   ├── import/
│   └── ui/
│
├── tests/
│   ├── unit/
│   ├── integration/
│   └── simulation/
│
└── tools/
    ├── database_import/
    └── validation/
```

The actual folder structure can be adapted to the chosen technology stack, but the separation of responsibilities should remain.

---

# 7. Development priority

The recommended order is to complete each step before moving to the next.

| Milestone | Steps | Expected result |
|---|---:|---|
| 1 — Playable core | 01–14 | Complete playable game with one country |
| 2 — European ecosystem | 15–24 | European leagues and international competitions |
| 3 — Advanced management | 25–34 | Advanced simulation and management depth |

**Important:** The match engine, database architecture and competition system are foundational components. Their interfaces and data contracts should be documented carefully because later milestones will expand them substantially.

The first objective is not to build the entire European basketball world immediately. It is to create a reliable, playable core that can be expanded without rewriting the game.