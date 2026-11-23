# Chapter 5. One register of business applications

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Shared
- **Parties:** IT (the platform team), the business (owners), the PMO
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G2](../guidelines/G02-name-owners-and-deputies.md), [G5](../guidelines/G05-define-status-words.md), [G6](../guidelines/G06-map-dependencies.md), [G20](../guidelines/G20-keep-required-documents-current.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P06](processes.md#p06-gap-check-and-claim), [P07](processes.md#p07-documentation-tracking)

## Why this chapter exists

"I can't get one list of our business apps, or tell what each one is for." Microsoft's inventory lists technical resources one by one: apps, flows, agents, sites. It does not say which of them form one business application, who answers for it, or what it is for. The register is that business view. The inventory feeds it; it is not the register.

## What good looks like

- One record per business application, of any kind: Power Platform apps, agents, AI-built apps, code apps and SaaS from other vendors.
- Each record has a plain purpose, a kind, a portfolio, owners with deputies, a tier, a lifecycle stage, its dependencies and its required documents.
- Each technical part in another tool is held as a link: its Microsoft ID, a URL and a "last seen" date. Never a copy.
- The same status words everywhere: in the register, in reports and in conversation.
- An admin can ask at any time what is running but not registered, and owners claim what is found.

## What a register entry holds

| Item | What it is designed to record |
|---|---|
| Identity | Name, plain purpose, kind, portfolio and group |
| People | Business owner, technical owner, data owner, builder, each with a deputy and dates |
| Tier | The current tier and the decision behind it |
| Stage | The lifecycle stage, with who moved it and when |
| Technical parts | Links to apps, flows, agents, sites, environments, repositories and pipelines, with "last seen" dates |
| Dependencies | Typed, directed links to other apps, APIs, data and outside systems |
| Documents | Which documents it needs, and each one's link, status, owner and last check |
| Decisions | Every decision about it, in the shared decision log |

## How it works

1. **Register at intake.** The register entry starts at the front door (chapter 3), the day an app is proposed.
2. **Link, never copy.** Link each technical part by its Microsoft ID and URL. Do not copy inventory facts such as owners or usage.
3. **Record owners yourself.** Inventory owner fields are weak: model-driven apps have no owner there, and a flow shows its creator. Record owners on the register entry.
4. **Use the agreed stages.** Move each app through the agreed lifecycle stages, from idea to retired. Use no other words in reports.
5. **Map dependencies.** The technical owner records what the app depends on and what depends on it.
6. **Track documents.** The register shows which documents each app needs and whether each is missing, draft, current or stale.
7. **Run the gap check.** At each periodic review, an admin asks what is running but not registered. The check reads Microsoft's inventory live, with the admin's own sign-in. Likely owners claim each item, or the admin dismisses it with a reason.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Keep the register | Platform admin | Platform admin | | Draft entries and links |
| Run the gap check | Platform admin | Platform admin | | Run it as the admin, and summarize |
| Claim an item for an app | Business owner (accepts ownership) | The likely owner | Technical owner | Propose a likely owner |
| Dismiss an item | Platform admin | Platform admin | | Nothing |
| Agree the stages and status words | Configuration admin | Platform admin | PMO, architects | Draft definitions |

## Guidelines this chapter applies

- **G1 Keep one register of business applications.** The register itself, and the gap check against Microsoft's inventory.
- **G2 Name the owners of every app.** Owners are recorded on the register entry, never taken from inventory owner fields.
- **G5 Define your status words once.** One list of stages and status words, used everywhere.
- **G6 Map what each app depends on.** Dependencies are part of every register entry.
- **G20 Keep each app's required documents current.** Each entry shows its documents and their status.

## Processes this chapter uses

- **P01 Intake and triage.** Creates the register entry on accept.
- **P06 Gap check and claim.** Finds what is running but not registered, and what was registered but has vanished.
- **P07 Documentation tracking.** Keeps each entry's document status current.

## How the product is designed to support it

- It is designed to hold one record per business application of any kind, grouped by portfolio.
- It is designed to keep each technical part as a link with a Microsoft ID, a URL and a "last seen" date, and to copy nothing on a schedule.
- It is designed to run the gap check only when an admin asks, live, as that admin, with no background job and no stored copy of the inventory.
- It is designed to record owners itself and to flag where Microsoft's listed owner differs from the register.
- It is designed to hold stages and status words as configuration records, so every page and report shows the same words.

## What you can tailor

- The kinds of application, the portfolios and groups.
- The stages and status words.
- Which estates the gap check reads, and who may see open claims.

## Microsoft guidance it applies

- [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22: "a unified view of all agents, apps, flows, and sites". Its queries need delegated user sign-in: service principals and managed identities get HTTP 403.
- [Power Platform inventory, known limitations](https://learn.microsoft.com/power-platform/admin/power-platform-inventory#known-limitations), checked 2026-11-22: "Model-driven apps don't have an owner." A flow's Owner column shows its creator and does not update.
- [Architecture and governance for AI agents](https://learn.microsoft.com/azure/cloud-adoption-framework/ai-agents/architecture-governance) (Cloud Adoption Framework), checked 2026-11-22: "Every agent should be discoverable through an approved inventory process."
- [Overview of Microsoft Agent 365](https://learn.microsoft.com/microsoft-agent-365/overview), checked 2026-11-22: the agent registry lists agents with who owns them. Agents are linked to it, not copied.

## How AI changes this

- AI-built apps and agents appear faster than any list can follow, so a list made once a year is stale the day it is finished.
- The register is fed at the front door, the day an app is proposed. An admin checks it live against Microsoft's inventory to find what slipped past, and likely owners claim what is found.
- An AI client can search the register for an app that already does the job, and its owner, before it builds a new one. The product is designed to answer that search.

## Old paradigm to avoid

[3. The yearly inventory spreadsheet](17-old-paradigms-and-how-not-to-slide-back.md#3-the-yearly-inventory-spreadsheet). Here it shows up as an export of the inventory to a spreadsheet, reconciled by hand against the register while owners are chased by email. Register at the front door, link instead of copy, and run the gap check on its cadence.

**Slide-back signal:** the inventory is exported to a spreadsheet, or the last gap check is older than its cadence.

## Done when

- Every resource in the inventory belongs to a registered application, is claimed, or is dismissed with a reason.
- Every live app has owners, a tier, a stage from the agreed list, its dependencies and its document status.
- The last gap check date and its claim list are on record.
