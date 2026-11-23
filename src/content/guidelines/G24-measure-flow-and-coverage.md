# G24. Measure flow and coverage, not apps shipped

- **Number:** G24
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 19 (Measure AI app development)
- **Cited in chapters:** 1, 6, 19
- **Processes:** P01, P06, P08

**Rule.** Measure AI app development by flow, coverage and decisions on time, each with a baseline and a target. Watch the slide-back signals. Do not count apps shipped.

**Why it matters.** With AI, build effort is no longer the constraint, so a count of apps says almost nothing. It rewards volume and hides apps nobody owns. Flow, coverage and decisions on time show whether good ideas reach production safely and whether people still decide.

**How the product is designed to support it.** It is designed to keep the dates and counts these measures use: when an intake was submitted, when each decision was proposed and answered, each deployment with its flags, each gap check with its claims, and portfolio counts of owner gaps, reviews overdue, documents missing or stale and drafts left open. No Power BI is needed.

## Microsoft anchors

- [Define your vision, metrics, and goals](https://learn.microsoft.com/agents/center-of-excellence/vision-metrics-goals) (agent Center of Excellence guidance, written for agents), checked 2026-11-22: "Measure governance coverage, such as the percentage of agents registered, assigned an owner, and reviewed against your risk policy." Also "A goal without a baseline is a wish."
- [Monitor, measure, and report value](https://learn.microsoft.com/agents/center-of-excellence/measure-report-value) (agent Center of Excellence guidance), checked 2026-11-22: "Measure it by the outcomes it enables and the risk it prevents, not by the number of agents you ship."
- [Configure lead time and cycle time widgets](https://learn.microsoft.com/azure/devops/report/dashboards/cycle-time-and-lead-time) (Azure DevOps), checked 2026-11-22: "Lead time is the total time from when a work item is created to when it's finished." The playbook's lead time runs from intake to the first production deployment, across every tool.

## Working shape

- **When:** when the organization opens AI app development (the baseline), every month (the report) and every quarter (the review).
- **Who:** the sponsor agrees the measures and targets; the PMO gathers and reports them; the AI lead, the platform owner and the change manager are consulted; the role that watches a measure acts on it.
- **Steps:**
  1. Pick a few measures: lead time from idea to production, share registered, owner coverage, decisions on time, standard change share, deployments on record, documents current and AI work accepted.
  2. Write down how each one is counted, and from which records.
  3. Measure a baseline before you set any target.
  4. Agree a target and a watcher for each measure, and a threshold for each slide-back signal.
  5. Report monthly. Review the measures and the signals together each quarter.
  6. When a signal crosses its threshold, record a decision on what changes (G4).
- **Done when:** every measure has a baseline, a target, a watcher and a last report date, and no target counts apps shipped.
- **Evidence:** the definitions and baselines, the monthly reports, and the decisions taken when a signal fired.
