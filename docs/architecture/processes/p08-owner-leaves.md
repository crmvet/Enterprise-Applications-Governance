# P08 Owner leaves

**Status: in design.** Nothing here is built or running. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

This process is designed to make sure no application is left without an owner who can answer for it. A deputy acts at once. A successor is named before access goes. Open approvals move to someone who can answer them. Agents flagged by Microsoft get a business decision.

The product never removes anyone's access. That is done in Microsoft Entra and in the deployment tools.

## Trigger

- **Announced:** the owner, or their manager, records a planned leave or role change, with a date.
- **An Entra signal:** an owner's account is disabled, deleted or has a leave date. The design notices this in two ways:
  - an **owner check** that an admin runs on demand, like the gap check. The product's MCP server reads the owners' accounts from Microsoft Entra with the admin's own delegated sign-in. This is part of the product;
  - an optional **leaver workflow sample** for organizations that have Microsoft Entra ID Governance: a Lifecycle Workflows custom task calls an Azure Logic App, which calls the product's API.
  - The design does not use an unattended read with an application permission over all users.
- **An agent signal:** Microsoft's agent registry shows agents without owners.
- **Review:** the owner check review comes due.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Record the leave or the signal | Leaving owner, platform admin | Platform admin | | Deputy, business owner | Drafts the record |
| Name the successor and hand over | Deputy, platform admin | Business owner | Leaving owner, successor (who must accept) | PMO | Drafts the hand-over |
| Hand over when the business owner leaves | Deputy, platform admin | Sponsor | Leaving owner, successor (who must accept) | PMO | Drafts the hand-over |
| Move open approvals and reviews | Platform admin | Platform admin | | Deputy, successor, business owner | none |
| Decide on a flagged agent | Platform admin | AI lead | Business owner | | Drafts the options |

When the business owner is the one leaving, the default configuration passes the decision to the sponsor, then to the portfolio's accountable person. These are configuration rows.

## Steps

1. **Detect.** The leave arrives through one of the triggers. The engine records the leave date and the account state on the person's reference, and opens an owner check review, due now, for every application where that person holds an owner role.
2. **Deputy acts.** Until a successor is accepted, reminders, cards and escalations for that owner role go to the deputy. Each owner role that requires a deputy must have one.
3. **Move open approvals.** The engine finds the leaver's pending approvals. It cancels each card and sends a new one to the deputy or another holder of the required role. The canceled approval record stays, marked canceled.
4. **Name a successor.** The engine proposes an ownership decision: the deputy becomes the owner, or a named successor does. The successor must accept on a card. A new deputy is then required.
5. **End the old assignment.** On acceptance, the leaver's owner assignments end and new ones start. The leaver's name stays on past decisions, held only as an Entra ID and a display label.
6. **Access is Entra's job.** Removing the leaver's access happens in Microsoft Entra and the deployment tools, not in the product. For an urgent removal, disable the user's account in Entra.
7. **Agents.** For an agent owned by the leaver, or shown as ownerless by Microsoft, the AI lead records keep, reassign, consolidate or retire, with the reason and a link to Microsoft's evidence. Reassigning the owner happens in the Microsoft 365 admin center, not in the product.

## What is recorded

| Record | What happens |
|---|---|
| Person reference | The account state, the leave date, and the date they were read |
| Owner assignment | The old assignment ends; new ones start |
| Decision | Ownership; decisions on agents |
| Approval record | Canceled cards stay on record; new cards are created |
| Review | Owner check reviews |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| Ownership (successor) | The successor accepts, and the business owner approves (the sponsor when the business owner is leaving) | Everyone must approve |
| Decision on a flagged agent | AI lead | First to respond |

## What an organization can configure

- The owner roles, whether each needs a deputy, and how many people may hold it.
- Who decides ownership, including who decides when the business owner leaves.
- How often owner checks come due.
- When reminders escalate to the deputy.
- Whether to use the optional leaver workflow sample.
- The AI lead role: rename it, map it to another group, or drop it.

## Designed MCP tools

Every tool runs as the signed-in person.

| Tool | What it does |
|---|---|
| `record_owner_leave` | Records a planned leave or role change |
| `run_owner_check` | Reads the owners' accounts from Entra, for admins only, with the admin's own delegated sign-in |
| `propose_owner_change` | Names a successor and opens the ownership decision |
| `get_portfolio_summary` | Lists the apps with no current owner or no deputy |
| `get_application_owners` | Shows the owners and deputies of one app, and any gaps |

## Microsoft tools linked, never rebuilt

- Microsoft Entra users and groups.
- [Lifecycle Workflows](https://learn.microsoft.com/entra/id-governance/lifecycle-workflow-extensibility) in Microsoft Entra ID Governance, used by the optional leaver sample.
- The [Agent 365 registry](https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview), for agents without owners, and [agent ownership actions](https://learn.microsoft.com/microsoft-agent-365/admin/agent-actions) in the Microsoft 365 admin center.

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Owner check | The MCP server in the one Azure Functions app, with the admin's delegated token, like the gap check |
| Leaver workflow sample | An Azure Logic App, called by Lifecycle Workflows, calls the API endpoint with the same name as the tool. The Logic App signs in as a workload identity that holds a recorded grant |
| Moving approvals | The API marks the records. The approval runner cancels the old cards and sends new ones |
| Records | Owner assignments keep their start and end dates in Azure SQL Database. Decisions and approval records are ledger tables |

## Open design questions

- Whether a non-human workload, such as the Logic App in the leaver sample, may hold a narrow workload role directly, as a recorded grant that expires. This is the same question as for pipeline identities ([P05](p05-deployment-record.md)).

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- Lifecycle Workflows "currently support creating custom task extensions to call out to Azure Logic Apps." https://learn.microsoft.com/entra/id-governance/lifecycle-workflow-extensibility
- "Using this feature requires Microsoft Entra ID Governance or Microsoft Entra Suite licenses." https://learn.microsoft.com/entra/id-governance/what-are-lifecycle-workflows
- The agent registry shows "Agents without owners": "The number of agents that no longer have owners at your organization." "Shared agents can become ownerless when you delete the user who created them from the organization." https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview
- "IT administrators can reassign ownership for agents that don't have an owner or that are active." https://learn.microsoft.com/microsoft-agent-365/admin/agent-actions
- The Power Platform inventory lists, as one of its tasks, "Prevent orphaned resources: Proactively find resources owned by departing users to allow seamless ownership transfers and ensure business continuity." https://learn.microsoft.com/power-platform/admin/power-platform-inventory

Previous: [P07 Documentation tracking](p07-documentation-tracking.md). Next: [P09 Retirement](p09-retirement.md).
