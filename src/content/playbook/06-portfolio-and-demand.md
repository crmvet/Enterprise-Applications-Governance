# Chapter 6. Portfolio and demand

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** the PMO, the business (sponsors and owners), IT
- **Guidelines:** [G4](../guidelines/G04-record-each-decision.md), [G5](../guidelines/G05-define-status-words.md), [G18](../guidelines/G18-retire-apps-on-purpose.md), [G24](../guidelines/G24-measure-flow-and-coverage.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P09](processes.md#p09-retirement)

## Why this chapter exists

"Requests arrive by email and chat, and the loudest voice wins." "Projects close, but the app stays." The PMO's job here is the portfolio: which work gets money and people, in what order, and whether it paid off. Plans and tasks stay in the PMO's own tools. The register holds the decisions about applications.

## What good looks like

- One demand list for every kind of app, visible to the PMO and to each requester for their own requests.
- Requests that need central capacity or money are scored the same way: value, feasibility and resources. Each gets a horizon (now, next, later) and a decision with a reason.
- Every work item, epic or plan links to the application it changes.
- The application record outlives every project.
- A regular portfolio review decides, per app: keep, invest, replace or retire.

## The PMO's touch points

| Touch point | What the PMO does | Where the detail lives |
|---|---|---|
| Demand | Sees every request, by business area, with age and status | The intake records |
| Prioritization | Scores requests that need central capacity, sets a horizon, and prepares the sponsor's decision | The score and decision on the register; the scoring sheet can stay where the PMO keeps it |
| Capacity | Matches ranked work to teams: self-build, fusion team or pro team | Azure Boards, Planner plans or GitHub Projects, linked |
| Benefits | Records the expected benefit at intake and checks it later | Two lines on the register; evidence linked from where it is measured |
| Portfolio review | Runs a regular review of duplicates, unowned apps, apps past review, and each app's future | A review pack from the register; the decision is the business owner's |

## How it works

1. **See demand.** Every request enters through the front door (chapter 3). The PMO sees them all.
2. **Prioritize when needed.** When a request needs money or central capacity, the PMO scores it and sets a horizon. The sponsor funds, defers or stops it on the decision page. That is the Commit gate.
3. **Link the plan.** The epic, work item or plan is linked to the application. The product is designed to copy no plan facts.
4. **Track the stage.** Each application moves through the agreed lifecycle stages. Reports use only those words.
5. **Review the portfolio.** On a set cadence the PMO prepares the review pack: apps by business area, with owner, tier, stage, last review, open decisions and dependants.
6. **Decide the future.** The business owner decides keep, invest, replace or retire, with a reason. A retire decision starts the retirement (chapter 14).

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Score and rank demand that needs central capacity | PMO | PMO | Business owner, platform admin, architect | Prepare score inputs from the intake |
| Fund, defer or stop | Sponsor | PMO | Platform admin, architect | Nothing |
| Run the portfolio review | PMO | PMO | Business owner, platform admin, architect | Prepare the review pack |
| Decide an app's future | Business owner | PMO | Data owner, platform admin, architect | Nothing |

## Guidelines this chapter applies

- **G4 Record each decision in one line.** Priority and portfolio decisions are entries in the shared decision log, with reasons.
- **G5 Define your status words once.** Portfolio reports use the agreed stages, so "live" means the same thing in every report.
- **G18 Retire apps on purpose.** A retire decision from the portfolio review is where retirement starts.
- **G24 Measure flow and coverage, not apps shipped.** The portfolio review reports flow, coverage and decisions on time, each with a baseline and a target (chapter 19).

## Processes this chapter uses

- **P01 Intake and triage.** Its priority step is where the PMO scores and the sponsor decides.
- **P09 Retirement.** Starts from a portfolio decision to retire.

## How the product is designed to support it

- It is designed to hold one demand list for every kind of app, with score, horizon, size band, expected benefit and the decision with its reason.
- It is designed to link each work item, epic or plan to the app it changes, Azure DevOps first, then Planner plans and GitHub Projects, and to track no work itself.
- It is designed to keep portfolio decisions on the application record, with decider, reason and next review date.
- It is designed never to hold task lists, schedules, resource plans, timesheets, budgets or status reports. Those stay in the PMO's tools.

## What you can tailor

- The scoring questions, horizons and size bands.
- When a request needs prioritization (the triggers for the Commit gate).
- The portfolio review cadence and the portfolio outcomes.

## Microsoft guidance it applies

- [Intake and prioritize agent ideas](https://learn.microsoft.com/agents/center-of-excellence/intake-prioritization), checked 2026-11-21: score value, feasibility and resources the same way, and record the decision and its reasoning.
- [Manage the agent lifecycle](https://learn.microsoft.com/agents/center-of-excellence/agent-lifecycle), checked 2026-11-21: "Agents are products, not projects", and the intake decision is tracked apart from delivery.
- [Modernize applications with Power Platform](https://learn.microsoft.com/power-platform/guidance/adoption/application-modernization), checked 2026-11-22: "first determine whether it's still needed and useful or should be retired".
- [Power Platform adoption maturity model details](https://learn.microsoft.com/power-platform/guidance/adoption/maturity-model-details), checked 2026-11-21: at level 300, "Business pain points are quantified before project start and compared after finish".
- [What is Azure Boards?](https://learn.microsoft.com/azure/devops/boards/get-started/what-is-azure-boards), checked 2026-11-21: work items, portfolio backlogs, Delivery Plans and team capacity live there.

## How AI changes this

- Build effort drops, so demand turns into many small apps instead of a few large projects.
- The PMO moves from rationing build capacity to deciding which apps earn support, investment or retirement.
- Under G24 it measures flow (how long an idea takes to reach production, and whether decisions come on time) and coverage, not the number of apps shipped. Chapter 19 sets out the measures.
- Lowest-tier requests that need no money or central capacity skip prioritization, so builders with AI are not held in a queue.

## Old paradigm to avoid

[2. IT as a ticket queue](17-old-paradigms-and-how-not-to-slide-back.md#2-it-as-a-ticket-queue). Here it shows up as one queue for every request, however small, so a builder with a working app waits for a ranking it does not need. Keep prioritization for requests that need money or central capacity.

**Slide-back signal:** lowest-tier requests wait for prioritization they do not need.

## Done when

- Every request that needed central capacity has a score, a horizon and a decision with a reason.
- Every live app links to the plans or work items that change it.
- Every app has had a portfolio decision within its review cadence.
