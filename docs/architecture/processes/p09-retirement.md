# P09 Retirement

**Status: in design.** Nothing here is built or running. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

This process is designed to take an application out of use on purpose. It has a recorded decision, a check of what depends on the app, a scheduled removal done in Microsoft's tools, linked evidence that nothing is left, and a final stage. The product does none of the removal itself.

## Trigger

- A portfolio decision with the outcome retire, usually at the Review stage.
- An owner asks to retire something nobody uses.
- A vanished component resolved as "retired" in the gap check ([P06](p06-gap-check-and-claim.md)).
- An app that replaces this one went live.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Decide the future: keep, invest, replace or retire | PMO | Business owner | Data owner, platform admin, architect | Builder, change manager | none |
| Approve the removal change | As in [P03](p03-change-management.md) | As in [P03](p03-change-management.md) | | | |
| Retire and clean up, with evidence | Platform admin | Business owner | Builder, data owner, security reviewer, architect | Change manager, PMO | Drafts the evidence list |
| Declare retirement complete | Platform admin | Business owner | | Change manager, PMO | none |

## Steps

1. **Decide.** A portfolio decision with the outcome retire is accepted on a card. The PMO prepares the facts. The business owner decides.
2. **Check what depends on it.** The product walks the dependencies in both directions. Owners of dependent apps are told. A dependent app that needs a replacement gets its own change or intake.
3. **Settle the data.** The data owner decides what happens to the data: a data-use decision about retention, with evidence links to where the data went.
4. **Schedule the removal.** A change request ([P03](p03-change-management.md), normal by default) carries the window and the targets. Who authorizes it follows the tier.
5. **Remove, in Microsoft's tools.** People and their AI tools disable before they delete. Examples: block or delete in the Power Platform admin center inventory; remove identities and grants in Microsoft Entra; handle agents in Agent 365. The product does none of this. Each step done is recorded as an evidence link with an evidence level. A full retirement checklist comes in a later release.
6. **Confirm nothing is left.** The admin runs the gap check ([P06](p06-gap-check-and-claim.md)). The app's components should now show as vanished. Each one is resolved as retired. A component that is still seen keeps the retirement open.
7. **Complete.** A retirement decision with the outcome complete is accepted. The engine moves the application to the retired stage, cancels due reviews, stops document reminders, and archives the registration where configured. Dependencies stay, marked by the retired stage, so the history still reads.

## What is recorded

| Record | What happens |
|---|---|
| Decision | Portfolio; data use (retention); retirement |
| Change request and deployment records | For the removal |
| Evidence link | Each removal step done, with its evidence level |
| Claim | Vanished components, resolved as retired |
| Business application | Moves to the retired stage; registration archived where configured |
| Review | Due reviews canceled |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| Portfolio (retire) | Business owner | First to respond |
| Data use (retention) | Data owner | First to respond |
| Removal change | As in [P03](p03-change-management.md), by tier and change type | As in [P03](p03-change-management.md) |
| Retirement complete | Business owner | First to respond |

## What an organization can configure

- The portfolio outcomes, and the retirement outcomes with the stage each leads to.
- Which stage means retired.
- What must hold before retirement can complete, for example "data use decided" and "the gap check shows no component".
- Which dependency types count when the dependencies are walked.
- Who decides and who approves.

## Designed MCP tools

Every tool runs as the signed-in person.

| Tool | What it does |
|---|---|
| `save_decision_proposal` | Opens a portfolio or retirement decision as Proposed. It cannot accept it |
| `get_dependency_impact` | Walks the dependencies in both directions |
| `save_change_request_draft` | Drafts the removal change |
| `save_link` | Records an evidence link for each removal step |
| `run_gap_check` | Confirms that the components are gone |
| `link_component` | Resolves a vanished component as retired |
| `get_application_card` and `get_requirements` | Together, answer "how far along is this retirement?" There is no single tool for it yet |

## Microsoft tools linked, never rebuilt

- The [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), where admins block or delete resources.
- Microsoft Entra, where identities and grants are removed.
- [Agent 365](https://learn.microsoft.com/microsoft-agent-365/admin/agent-actions), where agents are blocked or deleted.
- Microsoft's guidance on [safe decommissioning](https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments) and on [retiring agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards).

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Stage move and clean-up of due items | The rules engine in the one Azure Functions app, when the retirement decision is accepted |
| Confirmation | The gap check in the MCP server ([P06](p06-gap-check-and-claim.md)) |
| Dependency walk | A recursive query in Azure SQL Database, run by the API |
| Records | Decisions and evidence links are ledger tables. Dependencies stay, so the history still reads |

## Example (fictional people and apps)

Mateo Brandt, a field operations planner with read access, asks Claude Code what breaks if the old Inspection Scheduler app is retired. In the design, the product would walk its dependants: a route planner reads its data, and a crew roster calls its API, with a crew shift mobile app one step further. That list is what a retirement decision would have to answer for.

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- "Retirement should disable agent execution and revoke associated permissions. Stored information should follow applicable retention requirements. Agent owners should confirm that retirement is complete." https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/operational-standards
- On safe decommissioning, Microsoft says to disable before deleting: "When possible, move the component to a disabled or offline mode instead of immediately deleting it." https://learn.microsoft.com/azure/well-architected/operational-excellence/safe-deployments
- In the Power Platform inventory, admins can "Delete a resource" and "Block a published agent or canvas app". "Blocking is reversible." https://learn.microsoft.com/power-platform/admin/power-platform-inventory

Previous: [P08 Owner leaves](p08-owner-leaves.md). Next: [P10 AI draft and human accept](p10-ai-draft-and-human-accept.md).
