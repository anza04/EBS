# Coaching and staff management

Step 28. Every club employs non-playing staff: head coach, assistant coaches, development coaches, a fitness coach,
physios and scouts. Staff have attributes, contracts and a market; their attributes feed the systems they are
responsible for. Pure rules: `Ebm.Domain.Staff` (`StaffMember`, `StaffProfile`, `StaffRules`, `CoachProfile`);
use cases: `IStaffService` (Services/Clubs) and the hiring helper `StaffHiring`; screen: *Staff* tab.

## 1. Roles

| Role | `StaffRole` | AI wants | Most per club |
|---|---|---|---|
| Head coach | `HeadCoach` | 1 | 1 |
| Assistant coach | `AssistantCoach` | 1 + reputation / 50 (1–3) | 3 |
| Player development coach | `DevelopmentCoach` | 1 from reputation 50, else 0 | 2 |
| Fitness coach | `FitnessCoach` | 1 | 2 |
| Physio (medical staff) | `Physio` | 1 | 2 |
| Scout | `Scout` | 1 + reputation / 40 (1–3) | 6 |

## 2. Attributes (`StaffProfile`)

| Attribute | Range | Meaning |
|---|---|---|
| Tactical knowledge | 1–100 | Reading and preparing games |
| Development | 1–100 | Teaching and improving players |
| Motivation | 1–100 | Lifting the squad's morale |
| Scouting | 1–100 | Judging players |
| Medical expertise | 1–100 | Treating and preventing injuries |
| Reputation | 1–100 | Standing in the game: salary and which clubs he joins |
| Experience | 0–50 years | Every attribute works with a bonus of +1 per four years, at most +5 (`StaffRules.Effective`) |

Coaches (head, assistant, development) have a **coaching style** (`CoachingStyle`: Balanced, Attacking, Defensive,
Youth development, Disciplinarian, Motivator). Head coaches may have **tactical preferences**: a preferred offensive
and defensive system.

## 3. Role ability

`StaffMember.Ability` (derived, not stored) is the rating in his role, plus the experience bonus:

| Role | Ability |
|---|---|
| Head coach | 0.4 tactical + 0.3 motivation + 0.3 development |
| Assistant coach | 0.5 tactical + 0.5 development |
| Development coach | development |
| Fitness coach | 0.5 development + 0.5 medical |
| Physio | medical |
| Scout | scouting |

Staff from older saves (Step 20 `Ability` column) get every attribute equal to their old ability and no experience,
so their role ability is unchanged (migration `20261120145953_StaffManagement`).

## 4. Contracts, recruitment and dismissal

- **Contracts** are `Contracts` rows with `StaffMemberId` (Step 02), 1–5 years, salary paid monthly with the other wages
  (*Staff salaries* in the ledger).
- **Demand** = base salary of the role ability × (0.8 + 0.4 × reputation / 100), rounded to € 1,000. Base salary:
  head coach € 40,000 + € 1,500 × ability; assistant, development and fitness coach € 25,000 + € 700 × ability; scout
  and physio € 20,000 + € 600 × ability.
- **Joining**: a staff member joins a club whose reputation is at most 20 below his own.
- **Hiring** (`IStaffService.HireAsync`): a free staff member, offer ≥ demand, 1–5 years, at most the role's maximum
  per club. The contract starts today.
- **Dismissal** (`DismissAsync`): the contract ends today; the club pays the remaining salary at once (ledger
  *Settlements*, "Dismissal of …"). He returns to the market.
- **The market** is every staff member without a running or future contract. It is topped up at the career start and
  every Monday to `6 + clubs / 10` free members per role, generated for a random club reputation 10–95 and country.

## 5. Responsibilities and influence

| Duty | In charge | Rating used | Influences |
|---|---|---|---|
| Tactics and match coaching | Head coach | Effective tactical knowledge (none: 50) | AI tactic (style, preferences, `TacticPlanner`); in the match engine: make probability ±1% at 100/1 (0 at 50), no game plan below 40, closing review at 2:00 instead of 4:00 below 40, automatic timeout after a 6–0 run from 70 (else 8–0) |
| Motivation | Head coach | Effective motivation (+15 for a *Motivator*) | Weekly morale: ≥ 70 lifts morale below 50 by one, ≤ 30 lowers morale above 50 by one; a *Disciplinarian* takes one more point above 60 |
| Training and development | Head, assistant, development coaches | Mean effective development (none: 30) | Coaching factor of growth (docs/DEVELOPMENT.md §4.1); the head coach's style: *Youth development* × 1.10 growth up to 23, *Disciplinarian* × 1.05 |
| Fitness | Fitness coach | Role ability (none: 30) | Daily fitness recovery (docs/DEVELOPMENT.md §3) |
| Medical care | Physio | Role ability (none: 30) | Injury risk, recovery and return estimates (docs/INJURIES.md) |
| Scouting | Each scout | His role ability | Report accuracy (docs/SCOUTING.md §5) |

Coaching styles in the AI tactic: *Attacking* tempo and spacing 4; *Defensive* intensity and ball pressure 4; *Youth
development* deep rotation; *Disciplinarian* tight rotation and *Protect* foul management. Preferred systems are used
when no squad category picks a system (docs/AI_MANAGEMENT.md §3).

Measured: 300 games of equal teams, home head coach knowledge 95 vs 10: home margin +2.7 vs −0.4 points
(`StaffRulesTests`).

## 6. AI staff management

- **Recruitment** (`StaffHiring.HireMissingAsync`, at the career start and every Monday through the scouting,
  development and medical services): every club short of a role hires, best first, the free staff member with the
  highest role ability who would join, asks at most the AI cap (demand of a typical member for its reputation + 10
  ability, × 1.2) and was never dismissed by that club; otherwise a generated one (key attributes at the typical
  ability `25 + 0.55 × reputation ± 10`, others 15 lower, reputation the club's ± 10, 0–25 years of experience).
  Three-year contracts at the demand.
- **The user's club** is staffed once when the career starts; afterwards the user hires (the AI does it only while the
  assistant manages the club).
- **Dismissal**: at the start of a new season every AI club that missed its board objectives dismisses its head coach
  and hires a new one at once.

## 7. Not modelled

- No staff renewals: an expiring staff member returns to the market (the user re-hires him there; AI clubs refill).
- No negotiation rounds, agents or competing offers for staff; demands do not change with the club.
- Staff attributes and reputation do not change over time; staff do not retire or age out.
- Missing head coach counts as average (50) in the match, not as a weak coach.
- Responsibilities are fixed per role (no user delegation); the assistant coach does not stand in for a missing head
  coach.
- The datasets carry no staff; staff are always generated.
