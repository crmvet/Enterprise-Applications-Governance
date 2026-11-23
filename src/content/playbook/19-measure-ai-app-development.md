# Chapter 19. Measure AI app development

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** leaders and sponsors, the PMO, the AI lead, change control, the platform team
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G2](../guidelines/G02-name-owners-and-deputies.md), [G4](../guidelines/G04-record-each-decision.md), [G24](../guidelines/G24-measure-flow-and-coverage.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P03](processes.md#p03-change-management), [P05](processes.md#p05-deployment-record), [P06](processes.md#p06-gap-check-and-claim), [P08](processes.md#p08-owner-leaves)

## Why this chapter exists

"Is AI app development working?" "We built hundreds of apps this year. Is that good?" Leaders need a short, honest answer. Without measures, teams cannot tell success from activity. Governance then grows heavier with no evidence that it helps, or fades with no warning that it is gone. This chapter gives a few measures, each with a baseline, a target and a person who watches it, and puts the signals of a slide back into old habits next to them.

## How AI changes this

Build effort is no longer the constraint. An AI client can produce a working app in a day, so a count of apps says almost nothing about value or risk. What matters now is flow (how fast a good idea reaches production, safely), coverage (how much of what runs is registered, owned and current) and decisions (whether people decide on time). AI also helps with the counting: an AI client can gather the numbers and draft the report, so people spend their time checking it, not counting.

## Old paradigm to avoid

[3. The yearly inventory spreadsheet](17-old-paradigms-and-how-not-to-slide-back.md#3-the-yearly-inventory-spreadsheet). Here it shows up as counting apps shipped: a number taken from the latest export. A count of apps rewards volume, hides apps nobody owns, and says nothing about whether anyone checked them. A big number of apps can sit next to a stale list and a board that approves every change, and the report still looks good. Measure flow, coverage and decisions from the live records instead. **Signal:** the monthly report leads with the number of apps built, or a target is set in apps per quarter.

## What good looks like

- A few measures, each with a definition, a baseline, a target and a named person who watches it.
- Measures of flow, coverage and decisions, not of volume.
- Each measure comes from records the organization already keeps, not from a survey or a spreadsheet built for the report.
- The ten slide-back signals from chapter 17 sit next to the measures.
- A signal that crosses its threshold leads to a recorded decision by the role that owns the fix.
- Leaders see the same numbers every month, and review them with the signals every quarter.

## The measures (default)

| Measure | What it counts | Where it comes from | Watched by | Healthy direction |
|---|---|---|---|---|
| Lead time from idea to production | Days from an intake being submitted to its first production deployment on record, by tier | The intake date (P01) and the deployment record (P05) | PMO | Down at the lower tiers; steady at the top tier |
| Share registered | Of the items the last gap check found, the share that belong to a registered app, were claimed, or were dismissed with a reason | The gap check run and its claims (P06) | Platform admin | Up, toward all of them |
| Owner coverage | Live apps where every owner role is held by a person who has a named deputy | Owner assignments and the owner check (P08) | PMO | Up, toward every live app |
| Decisions on time | Approval cards answered within the agreed number of days, by tier and decision type | The decision log | PMO | Up |
| Standard change share | Changes that went through a standard change model, out of all changes | Change records (P03) | Change manager | Up, while failed changes stay flat |
| Deployments on record | Production deployments that came through the expected route with their approval, out of all production deployments | Deployment records and their flags (P05) | Change manager | Up, toward all of them |
| Documents current | Required documents that are current, out of all required documents for live apps | Document links and their status | Platform admin | Up |
| AI work accepted | AI drafts a person accepted through the right channel, and AI drafts left open past the agreed number of days | Performed-by marks and draft status | AI lead | Accepted through the right channel: all. Left open: down |
| Slide-back signals | The ten signals, one per old paradigm, each with a threshold | Chapter 17 | Platform owner | Quiet |

- **Read the measures in pairs.** Lead time next to deployments on record: a fast deployment that skipped the route is not a win. Standard change share next to failed changes: more standard changes only help if they still succeed. AI work accepted next to decisions on time: fast acceptance of drafts nobody read is a warning, not a result.
- **Why these.** Microsoft's agent Center of Excellence guidance asks you to measure "governance coverage, such as the percentage of agents registered, assigned an owner, and reviewed against your risk policy" ([Define your vision, metrics, and goals](https://learn.microsoft.com/agents/center-of-excellence/vision-metrics-goals)), and lists "time from idea to production" as a measure of adoption and delivery ([Monitor, measure, and report value](https://learn.microsoft.com/agents/center-of-excellence/measure-report-value)). The playbook applies the same measures to apps built with AI.
- **Not the same lead time as in Azure Boards.** In Azure DevOps, "Lead time is the total time from when a work item is created to when it's finished." ([Configure lead time and cycle time widgets](https://learn.microsoft.com/azure/devops/report/dashboards/cycle-time-and-lead-time)). Work item lead time stays in Azure Boards. The playbook's lead time runs from intake to production, across every tool that builds and deploys.

## Leading and lagging signals

The measures above are leading signals: they move early and help you steer. Value is proven by lagging ones, such as the benefit review. Microsoft puts it plainly: "You need both: leading signals to steer and lagging signals to prove results." ([Monitor, measure, and report value](https://learn.microsoft.com/agents/center-of-excellence/measure-report-value)). At intake, the requester states the expected benefit. At the benefit review (chapters 6 and 12), the business owner compares it with what happened.

## How it works

1. **Pick the measures.** Start from the default table. Keep it short: a few measures leaders will actually read.
2. **Agree each definition.** Write down how each measure is counted, from which records, and for which tiers.
3. **Set the baseline.** Measure the first month as it is, before any target is set.
4. **Agree the targets.** The sponsor agrees one target for each measure, with the PMO, the AI lead, the platform owner and the change manager.
5. **Report monthly.** The PMO, or the PMO's AI client, gathers the numbers and drafts the report. The PMO checks it and sends it.
6. **Review quarterly.** At the portfolio review (chapter 6), the measures and the ten signals are read together.
7. **Act on what fires.** When a signal crosses its threshold, the role that watches it records a decision: what changes, who does it, and by when (G4).
8. **Prune.** Drop a measure nobody acts on. Add a new one only with a baseline.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Choose the measures, their definitions and targets | Sponsor | PMO | AI lead, platform owner, change manager | Propose definitions and compute the baseline |
| Publish the monthly report | PMO | PMO | Platform admin, change manager, AI lead | Gather the numbers and draft the report |
| Act on a signal that crosses its threshold | The role that watches it (table above) | That role | Platform owner, PMO | Gather the evidence and draft the decision |
| Change a threshold or drop a measure | Sponsor | PMO | The role that watches it | Draft the change |

## Guidelines this chapter applies

- **G24 Measure flow and coverage, not apps shipped.** This chapter is its home: a few measures, each with a baseline and a target, the slide-back signals, and no counts of apps.
- **G1 Keep one register of business applications.** Share registered measures how well the register covers what runs.
- **G2 Name the owners of every app, each with a deputy.** Owner coverage measures it directly.
- **G4 Record each decision in one line.** Decisions on time read the decision log, and acting on a signal is itself a recorded decision.

## Processes this chapter uses

- **P01 Intake and triage.** The intake date starts the lead time.
- **P03 Change management.** Change types give the standard change share.
- **P05 Deployment record.** The first production deployment ends the lead time, and the flags give deployments on record.
- **P06 Gap check and claim.** The last gap check gives the share registered.
- **P08 Owner leaves.** The owner check feeds owner coverage.

## How the product is designed to support it

- It is designed to give portfolio counts from its own data: owner gaps, reviews overdue, documents missing or stale, and drafts older than a set number of days. No Power BI is needed.
- It is designed to keep the dates these measures use: when an intake was submitted, when each decision was proposed and answered, and when each deployment landed.
- It is designed to flag deployments that came another way, lacked their approval or landed outside their window.
- It is designed to keep each gap check run, with its claims and their outcomes.
- It is designed to mark every AI-made record with the AI client and the person it acted for, and to keep its draft status.
- It is designed to link work items in Azure Boards, and never to copy their lead time.

## What you can tailor

- Which measures you use, their definitions and the tiers they cover.
- The targets, the thresholds and who watches each one.
- The report cadence and the review cadence.
- Which slide-back signals you watch.

## Microsoft guidance it applies

Most of this guidance is written for AI agents. The playbook applies the same measures to apps built with AI.

- [Define your vision, metrics, and goals](https://learn.microsoft.com/agents/center-of-excellence/vision-metrics-goals) (agent Center of Excellence guidance), checked 2026-11-22: "Measure governance coverage, such as the percentage of agents registered, assigned an owner, and reviewed against your risk policy." Also "Set a small number of goals you can actually measure, and give each one a baseline and a target. A goal without a baseline is a wish."
- [Monitor, measure, and report value](https://learn.microsoft.com/agents/center-of-excellence/measure-report-value) (agent Center of Excellence guidance), checked 2026-11-22: "Measure it by the outcomes it enables and the risk it prevents, not by the number of agents you ship." Also "You need both: leading signals to steer and lagging signals to prove results."
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door) (agent Center of Excellence guidance), checked 2026-11-22: "Don't measure success by request volume alone." Its measures include "Time from request to decision".
- [Power Platform adoption maturity model: Detailed capabilities](https://learn.microsoft.com/power-platform/guidance/adoption/maturity-model-details#business-value), checked 2026-11-22: at level 300, "Business pain points are quantified before project start and compared after finish".
- [Configure lead time and cycle time widgets](https://learn.microsoft.com/azure/devops/report/dashboards/cycle-time-and-lead-time) (Azure DevOps), checked 2026-11-22: "Lead time is the total time from when a work item is created to when it's finished."

## Done when

- Every measure has a definition, a baseline, a target, a person who watches it and a last report date.
- Every slide-back signal has an owner and a threshold.
- The monthly report and the quarterly review are on the calendar, and the last ones took place.
- No target counts apps shipped.
