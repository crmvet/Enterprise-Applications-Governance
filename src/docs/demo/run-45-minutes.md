# The 45-minute run: the deep dive

Every scenario and every screen group, with the steps that carry each one, in 45 minutes. For an audience that will
run the product: IT and platform teams, architects, security, data governance and the PMO together. Playing every step
of every script takes about 75 minutes (the scripts' own times "in full"): that is [the full run](#the-full-run-about-75-minutes)
below.

**Set up.** As for the [15-minute run](run-15-minutes.md), plus a local store to reset after the run
([preparing.md](preparing.md)). There is no planned pause: take questions at the end, or drop S-17 for a two-minute
pause at 24:00.

## Part 1: the build loop and the front door (12 minutes)

| Time | Scenario | Steps | Screens |
|---|---|---|---|
| 0:00 | [S-01](scenarios/S-01-portfolio-overview.md) The portfolio at a glance | S-01.1 to S-01.5 | Overview, Owners, Reviews |
| 2:00 | [S-02](scenarios/S-02-ai-build-loop.md) Governance inside the AI build loop | S-02.1 to S-02.8 | Claude Code, My work, the intake wizard, AI activity |
| 6:00 | [S-20](scenarios/S-20-requester.md) A request from the web app, with no AI | S-20.1 to S-20.5 | Intake, the wizard, Approvals |
| 9:00 | [S-03](scenarios/S-03-triage-gap-check-places-leavers.md) Triage, the gap check and leavers | S-03.1, S-03.2, S-03.3, S-03.6, S-03.8, S-03.9 | Intake, Gap check, Owners |

## Part 2: owners, approvals, change and architecture (14 minutes)

| Time | Scenario | Steps | Screens |
|---|---|---|---|
| 12:00 | [S-04](scenarios/S-04-business-owner-approvals.md) Approvals, owners and the lifecycle | S-04.2 to S-04.9 | Approvals, Decision, Application 360, Owners |
| 17:00 | [S-16](scenarios/S-16-teams-and-committees.md) Teams and committees | S-16.1, S-16.2, S-16.4 | Teams, team pages |
| 19:00 | [S-05](scenarios/S-05-changes-and-deployments.md) Changes and flagged deployments | S-05.1 to S-05.6 | New change, Timeline, Changes, Deployments |
| 22:00 | [S-06](scenarios/S-06-reviews-and-exceptions.md) Reviews and exceptions | S-06.3, S-06.4, S-06.6 | Reviews, Decision, Exceptions |
| 24:00 | [S-17](scenarios/S-17-dependency-map.md) The dependency map and what breaks | S-17.1 to S-17.3 | Dependency map, Application 360 |

## Part 3: data, agents and AI clients (11 minutes)

| Time | Scenario | Steps | Screens |
|---|---|---|---|
| 26:00 | [S-07](scenarios/S-07-data-taxonomy.md) Data: domains, cards, the never rule, copies | S-07.1 to S-07.8 | Data, Decision, Application 360 Data |
| 31:00 | [S-08](scenarios/S-08-agents.md) Agents | S-08.1, S-08.2, S-08.4 | Agents, AI activity, Approvals |
| 33:00 | [S-21](scenarios/S-21-claude-code-refusals.md) What an AI client may do, and what it may not | S-21.1 to S-21.5 | Claude Code, AI activity |

S-22 and S-23 (everyday records and every other action) are not in the 45 minutes: together they play every write
action the web app offers, last, on the same store, for a session about one area or for the end-to-end suite.
| 36:00 | [S-12](scenarios/S-12-guidelines.md) Guidelines and the playbook | S-12.1 | Guidelines |

## Part 4: the portfolio, the rules and the record (8 minutes)

| Time | Scenario | Steps | Screens |
|---|---|---|---|
| 37:00 | [S-09](scenarios/S-09-demand-portfolio-retirement.md) Demand, the portfolio and retirement | S-09.1, S-09.6, S-09.7, S-09.8, S-09.9, S-09.11 | Demand, Portfolios, Retirement |
| 40:00 | [S-10](scenarios/S-10-configuration.md) Change the rules safely | S-10.2 to S-10.5 | Configuration, Decision |
| 41:30 | [S-11](scenarios/S-11-roles-and-access.md) Roles and access | S-11.1, S-11.4 | Roles and access |
| 42:15 | [S-13](scenarios/S-13-measures.md) Measures and slide-back signals | S-13.1 | Measures |
| 42:45 | [S-14](scenarios/S-14-admin.md) Run the product | S-14.2 | Admin |
| 43:15 | [S-18](scenarios/S-18-search-and-lists.md) Search, filters and the reader | S-18.1, S-18.4 | Search, Applications |
| 43:45 | [S-19](scenarios/S-19-decision-log-and-documents.md) The decision log | S-19.1, S-19.3 | Decisions |
| 44:15 | [S-15](scenarios/S-15-auditor.md) The record | S-15.1, S-15.2 | Evidence pack |
| 45:00 | | End | |

S-09.1 needs S-02, S-03.3 and S-04.5 played before it on the same store (its counts), and S-09.11 answers the card
S-09.9 opens.

## Every screen group, and where this run shows it

| Screen group | Shown at |
|---|---|
| My work, Approvals, the Decision page | 2:00, 12:00 |
| Overview, Portfolios, Demand, Retirement | 0:00, 37:00 |
| Applications, Application 360, Dependency map | 12:00, 24:00, 43:15 |
| Intake and the wizard | 2:00, 6:00, 9:00 |
| Owners, people, Teams | 9:00, 12:00, 17:00 |
| Reviews, Documents | 22:00 |
| Changes, Timeline, Deployments | 19:00 |
| Gap check | 9:00 |
| Data | 26:00 |
| Agents, AI activity | 2:00, 31:00, 33:00 |
| Decisions, Exceptions, Evidence pack | 22:00, 43:45, 44:15 |
| Configuration, Roles and access, Guidelines, Measures, Admin | 36:00, 40:00 to 43:15 |
| Search and list filters | 43:15 |

Places, Documents' own list, the audit trail and S-19.4 are in the full run only.

## The full run (about 75 minutes)

Every step of every script, in the same order of parts, with two short pauses for questions (after Part 2 and after
Part 3). Each script's header gives its time in full; together they come to about 75 minutes.

| Part | Scenarios, every step | In full |
|---|---|---|
| 1 | S-01, S-02, S-20, S-03 | 2 + 4 + 3 + 4 = 13 minutes |
| 2 | S-04, S-16, S-05, S-06, S-17 | 6 + 3 + 4 + 3 + 4 = 20 minutes |
| 3 | S-07, S-08, S-21, S-12 | 6 + 3 + 4 + 2 = 15 minutes |
| 4 | S-09, S-10, S-11, S-13, S-14, S-18, S-19, S-15 | 6 + 4 + 3 + 3 + 3 + 3 + 3 + 3 = 28 minutes |

## Order matters

Play the parts in this order on one fresh store; the dependencies between scenarios are listed in
[scenarios.md](scenarios.md#order-between-scenarios). S-20 runs before S-03 here. Its request
leaves triage when Nadia accepts it in S-20.5, so S-03.1 still lists three.
