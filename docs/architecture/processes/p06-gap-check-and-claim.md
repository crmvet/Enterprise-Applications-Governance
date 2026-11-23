# P06 Gap check and claim

**Status: in design.** Nothing here is built or running. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

The gap check is designed to answer one question, live, as the admin who asks: "what is running that nobody registered?" It is designed to put only the gaps on a claim list, so owners can claim them. There is no scheduled import and no copy of Microsoft's inventory. For each item that is claimed, the product keeps only a Microsoft ID, a link and a "last seen" date.

## Trigger

An admin asks, in Claude Code or another MCP client. Nothing runs unattended.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Run the gap check | Platform admin | Platform admin | | | Runs it for the admin |
| Claim an item for an application | Candidate owner | Business owner (accepts ownership) | Technical owner | Platform admin | Drafts the claim |
| Dismiss an item, with a reason | Platform admin | Platform admin | | | none |
| Confirm a vanished component (retired, moved or replaced) | Technical owner | Platform admin | | Business owner | Drafts the answer |
| Decide on an agent Microsoft flags (keep, reassign, consolidate or retire) | Platform admin | AI lead | Business owner | | Drafts the options |

## Steps

1. **Ask.** The admin starts the gap check and names the estates to read, for example the Power Platform inventory.
2. **Check the admin's role.** Microsoft's inventory query accepts only a delegated user sign-in. A service principal or managed identity gets HTTP 403. What the admin can see depends on their Entra role (see the sources). The run records which role was used.
3. **Read live.** The product's MCP server queries Microsoft's inventory with the admin's own delegated token. The inventory lists agents, apps, flows and Power Pages sites (preview).
4. **Compare.** The engine matches each item's Microsoft ID to the registered technical components. Each item ends up in one of three groups:
   - **Matched:** the "last seen" date is updated. If Microsoft's listed owner differs from the owners in the register, the component is marked "owner differs". Microsoft's owner is not stored.
   - **Unregistered:** a new claim opens, with a suggested claimant as a hint only.
   - **Vanished:** a registered component of a live application that the run did not see opens a claim. Components of kinds the run cannot see are never marked vanished.
5. **Record the run.** The product records who ran it, the role used, the estates, the client app and the counts. Open claims for the same Microsoft ID are updated, not duplicated.
6. **Claim.** A candidate owner claims an item:
   - **to an existing application:** the engine adds the technical component. If the claimant is not an owner of that application, a confirmation card goes to its technical owner ([P10](p10-ai-draft-and-human-accept.md)). Which act counts as a person's here is an open design question;
   - **to a new application:** the claim starts an intake ([P01](p01-intake-and-triage.md)) with the component filled in.
7. **Dismiss.** The platform admin dismisses an item with a reason, for example "not a business application".
8. **Resolve vanished items.** The technical owner says what happened: retired (this sets the component's retirement date; [P09](p09-retirement.md)), moved (a migration created a new app ID, and the new component records which one it replaces), or still there in an estate the run could not read.
9. **Agents.** Microsoft's agent registry already shows agents without owners. The product links to that evidence and records the business decision, made by the AI lead. It never reassigns owners in Agent 365. That happens in the Microsoft 365 admin center.

## What is recorded

| Record | What happens |
|---|---|
| Gap check run | Who ran it, the role used, the estates, the client app and the counts |
| Claim | Open, claimed or dismissed; unregistered or vanished |
| Technical component | Created on a claim, or updated: "last seen" date, "owner differs", the component it replaces, the retirement date |
| Business application | Created through [P01](p01-intake-and-triage.md) when the claim is for a new app |
| Decision | Ownership, and decisions on agents |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| Ownership of a new application | The named business owner | Everyone must approve |
| Attaching a component to an application by someone who is not its owner | The application's technical owner, on a confirmation card | First to respond |
| Decision on a flagged agent | AI lead | First to respond |

## What an organization can configure

- The estates the gap check can read.
- The kinds of component, and whether the gap check can see each kind.
- How long a claim may stay open before a reminder, and who is reminded.
- Who decides ownership.
- Who can read claims. The default is "admins and the claimant".

## Designed MCP tools

Every tool runs as the signed-in person.

| Tool | What it does |
|---|---|
| `run_gap_check` | Runs the gap check, for admins only, with the admin's own delegated sign-in |
| `list_claims` | Lists open claims |
| `claim_resource` | Claims an item for an existing or a new application |
| `dismiss_claim` | Dismisses an item with a reason |
| `link_component` | Resolves a vanished item: retired, or moved and replaced by a new component |

## Microsoft tools linked, never rebuilt

- The [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory) and its API, read live as the admin.
- The [Agent 365 registry](https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview) in the Microsoft 365 admin center, which shows agents without owners and where admins reassign them.

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Live read | The MCP server in the one Azure Functions app, with the admin's delegated token. No background job and no stored inventory |
| Write the results | One API call and one transaction in Azure SQL Database. The rules engine does the comparison and the claim updates |
| Long runs | The MCP server returns a run ID and reports progress. The work continues through an Azure queue |

## Open design questions

- What happens to an open "vanished" claim when its component is seen again.
- How to guard against a run that sees much less than expected, for example when the admin's role can see fewer kinds of resource than the run claims to cover, which could mark many components vanished at once.

## Example (fictional people and apps)

Owen Achterberg, a platform admin, asks Claude Code to run the gap check on the Power Platform inventory. In the design, the inventory would return four apps. Three are registered, so only their "last seen" dates would move. The fourth, a crew overtime tracker, is not, so one claim would open with Priya Castell as the suggested claimant. The product would gain one gap check run and one claim, and no inventory rows.

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- "Power Platform inventory gives tenant administrators a unified view of all agents, apps, flows, and sites built on Power Platform across their organization." The Query Power Platform resources action "doesn't support service principal or managed identity authentication and returns HTTP 403 Forbidden for those identities." Global administrator, Power Platform administrator, Dynamics 365 administrator and Global reader see all inventory resources. AI administrator and AI reader see "Agents, agentic apps, agent flows, environments, and environment groups only". "Model-driven apps don't have an owner." For cloud flows and agent flows, the Owner column "currently shows the user who created the flow. It doesn't update when the owner changes." https://learn.microsoft.com/power-platform/admin/power-platform-inventory
- The agent registry shows "Agents without owners": "The number of agents that no longer have owners at your organization." "Shared agents can become ownerless when you delete the user who created them from the organization." https://learn.microsoft.com/microsoft-agent-365/admin/agent-registry-overview
- "IT administrators can reassign ownership for agents that don't have an owner or that are active." https://learn.microsoft.com/microsoft-agent-365/admin/agent-actions

Previous: [P05 Deployment record](p05-deployment-record.md). Next: [P07 Documentation tracking](p07-documentation-tracking.md).
