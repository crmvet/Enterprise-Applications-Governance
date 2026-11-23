# P07 Documentation tracking

**Status: in design.** Nothing here is released or deployed. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

This process is designed to say which documents each application needs, track each document's link and status, and remind owners. The product never writes a document. The owner's own AI tool writes it, for example Claude Code through the Azure DevOps MCP Server or git, and the product records the link.

Each required document has one of four statuses: missing, draft, current or stale.

## Trigger

- A document requirement starts to apply: the app's tier, kind or stage changed ([P02](p02-tier-assessment.md)).
- A document passes its stale date (the daily refresh).
- An owner, or their AI tool, records a link, a status or a check.
- A gate needs a current document.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Set which documents each kind and tier needs (a configuration change, [P11](p11-configuration-change.md)) | | Platform admin | Architect | Builder, technical owner, business owner | none |
| Write or update the document, outside the product | Builder | Technical owner | | | Writes it in the owner's own tool |
| Record the link and status | Builder | Technical owner | | | Records it for the person |
| Confirm that a document a gate needs is current | | Technical owner | Business owner | | none |

Each document has a named document owner, who is a person. By default it is the technical owner.

## Steps

1. **Require.** The engine works out the missing documents from the requirements that apply to the app's kind, tier and stage. Nothing is stored for a missing document. A document record exists only once a link has been recorded.
2. **Ask.** The owner, or their AI client, asks "what documentation is missing or stale for my app?"
3. **Write outside.** The owner's AI tool writes the page, for example with the Azure DevOps MCP Server's wiki tool, or through git. The product is not involved.
4. **Record.** The AI tool records the link with the status draft or current. The product does not assume the write succeeded. It stores what it is told, with who performed it.
5. **Confirm when it matters.** For a document a gate needs, "current" set by an AI client stays a claim until the document owner confirms it on a confirmation card ([P10](p10-ai-draft-and-human-accept.md)). Other documents accept "current" from the AI client, with the AI shown as the performer. Which act counts as a person's here is an open design question.
6. **Check.** When a person or their AI tool confirms the document is still true, the product records the date it was last checked. The product does not read the document.
7. **Go stale.** At the stale date (the last check plus the freshness period for that document type), the daily refresh moves the document from current to stale, reminds the document owner, and escalates to the deputy.
8. **Gate.** A gate decision that needs a current document cannot be accepted while that document is missing or stale.

## What is recorded

| Record | What happens |
|---|---|
| Document link | The link, the status, the document owner, the last update date, the last check date and the stale date |
| Evidence link | When a document is evidence for a decision |
| Audit entry | Each reminder and escalation |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| A document a gate needs is current | The document owner, on a confirmation card | First to respond |

No other approvals. The documents themselves are approved, if at all, in the tool where they live.

## What an organization can configure

- The document types: for example a wiki page, a runbook or an architecture decision record. Each has a default kind of link, a freshness period and a template link.
- Whether "current" set by an AI client needs a person's confirmation, per document type.
- Which documents are required, per tier, kind of app and starting stage.
- Which gates need which documents to be current.
- When reminders go out and when they escalate.

## Designed MCP tools

Every tool runs as the signed-in person.

| Tool | What it does |
|---|---|
| `get_documentation_status` | "What documentation is missing or stale for my app?" |
| `record_document_link` | Records the link and sets the status |
| `confirm_document_checked` | Records that a document was checked and is still true |

## Microsoft tools linked, never rebuilt

- The Azure DevOps wiki and the [Azure DevOps MCP Server](https://learn.microsoft.com/azure/devops/mcp-server/remote-mcp-server), which the owner's own AI tool uses to write pages.
- Repositories in Azure DevOps or GitHub, and SharePoint pages, as link targets.

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Missing documents | Worked out by the rules engine in the one Azure Functions app when read. No row is stored |
| Stale refresh | A timer-triggered function that runs the daily refresh |
| Reminders | The notification flow next to the approval runner |
| Confirmation cards | The approval runner ([approval cards](README.md#approval-cards)) |

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- The remote Azure DevOps MCP Server has a `wiki_upsert_page` tool, described as "Create or update a wiki page". https://learn.microsoft.com/azure/devops/mcp-server/remote-mcp-server

Previous: [P06 Gap check and claim](p06-gap-check-and-claim.md). Next: [P08 Owner leaves](p08-owner-leaves.md).
