# Auditor

**Role key:** `auditor`. **Under Balanced:** reads everything, including audit entries, and changes nothing.

The auditor answers questions from the record: who decided what, when, as which kind of actor, through which client,
for whom, and under which rules. Under Balanced an auditor answers no approval and holds no decision right.

## What an auditor does

- **Reads the audit trail.** **Audit trail** (`/app/audit`, auditor and platform admin only; `get_audit_trail`): every
  write and every refused call, with the actor kind (person, AI client for a person, agent identity, pipeline, approval
  runner, engine), the client and the person an AI client acted for. Filters narrow it to one record, one person, one
  application, one actor kind or one client ("Everything through <client>").
- **Opens an evidence pack.** The **evidence pack** of an application or a decision
  (`/app/evidence/business-application/<id>`, `get_evidence`): every decision and approval answer with who, when and
  whether it counted, the deployments and their flags, the AI marks, and the configuration version stamped on each.
- **Reads the decision log.** **Decisions** (`/app/decisions`): what was decided, by whom and why, with the
  exceptions as their own view.
- **Reads the rest.** The gap check's claims and runs, the configuration as files (`export_configuration`) and the
  Admin page, which most roles do not see.

## In the demo

Bianca Ferro plays the auditor. Nothing waits for her: My work shows the open approvals across the estate. She reads
every record, intake requests and the decisions about them included. The Tool Policy lets every role call the person's
own acts (answering a request they were asked, recording their own leave), which is why the tool table below lists a
few writes; the engine checks each one, and an auditor is asked for nothing.

- **S-15:** Invoice Matching's evidence pack: the Finance Approvals Agent's answer stored as a recommendation only,
  Clara Voss's answer that counts, the AI-only code approval flags on 1.12.0 and 1.12.1, and the configuration version
  on each. Then the audit trail: the calls an AI client made, and each refused call with the rule that refused it.
- **S-19:** the decision log, the decisions an AI client drafted, the exceptions with their review dates, and the
  documents.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Bianca Ferro (`bianca`).
- **Reads:** 60 of the 61 read tools, among them the ones only some roles get: `list_claims`, `export_configuration`, `get_audit_trail`, `list_gap_check_runs`, `get_admin_status`.

The shipped Tool Policy lets this role call 4 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
