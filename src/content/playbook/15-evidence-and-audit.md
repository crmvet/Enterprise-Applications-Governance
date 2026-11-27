# Chapter 15. Evidence and audit

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** auditors, change control, security, app owners, the configuration admin
- **Guidelines:** [G4](../guidelines/G04-record-each-decision.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G15](../guidelines/G15-keep-an-evidence-pack.md), [G17](../guidelines/G17-ai-drafts-people-approve.md)
- **Processes:** [P03](processes.md#p03-change-management), [P05](processes.md#p05-deployment-record), [P10](processes.md#p10-ai-draft-and-human-accept), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

"Six months later I can't prove who said yes, to what, and when." "We always do that" is not evidence. When each tool keeps its own approvals and each team its own spreadsheet, an audit becomes a hunt. Evidence that sits with the record, linked to Microsoft's own logs, makes audits and incident reviews short.

## What good looks like

- An auditor can answer "who approved this change, when, and under which rule" from the record alone.
- Every decision is an append-only entry, with its decider, date, reason and evidence links.
- Every record says who performed it: a person, a pipeline, or an AI acting for a person.
- Every record keeps the guideline version and the configuration version in force when it was made.
- Every production deployment is recorded and linked to its change and its pipeline run.
- Microsoft's audit logging is on for production environments, and pipeline run history is kept.

## The evidence pack for an app

| Evidence | Where it lives | How the pack holds it |
|---|---|---|
| Decisions and approvals | The shared decision log, the decision pages | The entries themselves, with approval history |
| Changes and deployments | The change record; your pipeline's run history | The records, with links to each run |
| Reviews | Review records | The records, with outcomes |
| Documents | Your wiki, repository or SharePoint | Links, status and last check |
| Microsoft's logs | Microsoft Purview audit, pipeline hosts | Links, never copies |
| Configuration changes | The configuration history | Each version, with who instructed it and who accepted it |

## How it works

1. **Decide what each tier needs.** Agree which evidence each tier must have, and where each item lives.
2. **Turn on Microsoft's logs.** Turn on audit logging for production environments in Microsoft Purview, and keep pipeline run history.
3. **Record as you go.** Decisions, approvals, deployments and reviews are recorded when they happen, not rebuilt before an audit.
4. **Link, do not copy.** Each item is linked to the application record.
5. **Mark AI work.** Records made by AI carry "performed by AI client X for person Y". Approvals carry a named human approver.
6. **Keep versions.** When a rule changes, old records keep pointing to the rule they followed.
7. **Answer from the record.** For an audit or an incident review, start from the application record and follow the links.

## Who decides (default decision rights)

| Task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Keep each app's evidence pack complete | Business owner | Technical owner | Change manager | Find gaps and draft the index |
| Decide which evidence each tier needs | Configuration admin | Platform admin | Auditor, security reviewer, change manager | Draft the list |
| Turn on audit logging in production | Platform admin | Platform admin | Security reviewer | Nothing |

## Guidelines this chapter applies

- **G15 Keep an evidence pack, not assurances.** The pack itself, linked to Microsoft's logs.
- **G4 Record each decision in one line.** The decision log is the spine of the pack.
- **G9 Release the tested artifact through your own pipeline.** Deployment records and pipeline run history show what went where.
- **G17 Let AI draft and recommend; people approve.** The pack shows a named human approver for every approval, and marks AI work.

## Processes this chapter uses

- **P03 Change management.** Change records, authorizations and reviews after the change.
- **P05 Deployment record.** Every deployment, its flags and its links.
- **P10 AI draft and human accept.** The performed-by marks and the person's confirmations.
- **P11 Configuration change.** Each configuration version, who instructed it and who accepted it.

## How the product is designed to support it

- It is designed to stamp every record with who performed it, the guideline version and the configuration version in force at the time, so evidence is a query, not a hunt.
- It is designed to keep decisions, approval records and audit entries append-only.
- It is designed to keep its own deployment record as durable evidence, even when a pipeline tool deletes old runs.
- It is designed to keep every configuration version with its content, who instructed it and the decision that accepted it.
- It is designed to link to Microsoft Purview, pipeline run history and other Microsoft logs, and to copy none of them.

## What you can tailor

- Which evidence each tier needs.
- Which document types count as evidence for which decisions.
- How long audit entries are kept.

## Microsoft guidance it applies

- [Power Platform activity logs and auditing in Microsoft Purview](https://learn.microsoft.com/power-platform/admin/activity-logging-auditing/activity-logs-overview), checked 2026-11-22: Purview can capture activity across Power Platform; a production environment needs its "Enable SAS Logging in Purview" setting turned on.
- [Recommendations for designing a reliable monitoring and alerting strategy](https://learn.microsoft.com/power-platform/well-architected/reliability/monitoring-alerting-strategy) (Power Platform Well-Architected), checked 2026-11-22: "Most events are available within 24 hours of the activity. Don't use this information for real-time monitoring."
- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: solutions are exported and stored in the pipelines host for every deployment.
- [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record), checked 2026-11-22: "The ADR serves as an append-only log."

## How AI changes this

- Auditors now ask what the AI did, for whom, and who decided.
- The product is designed to answer from the record: each AI-made record says "performed by AI client X for person Y", each approval names its human approver, and each record keeps the guideline and configuration versions in force.
- These marks are made as the work happens, so the AI's part never has to be rebuilt from chat history before an audit.
- An AI can find gaps in an evidence pack and draft its index. It cannot fill a gap with an approval.

## Old paradigm to avoid

[4. Governance checked only at the end](17-old-paradigms-and-how-not-to-slide-back.md#4-governance-checked-only-at-the-end). Here it shows up as evidence gathered at audit time, rebuilt from memory, email and chat logs.

**Slide-back signal:** evidence packs built in the week before an audit.

## Done when

- For any production change, the approver, the date, the rule and the deployment can be shown from the record alone.
- Audit logging is on for every production environment.
- Every approval has a named human approver, and every AI-made record is marked.
