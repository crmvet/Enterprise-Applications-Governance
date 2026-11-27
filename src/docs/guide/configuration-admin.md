# Configuration admin

**Role key:** `configuration-admin`. **Under Balanced:** instructs and accepts configuration changes.

The configuration admin looks after the rules themselves: who decides what, which approvals each tier needs, the
Tool Policy, the data taxonomy. Every change is a draft first, then a decision people answer, then a version the engine
activates ([admin.md](../admin.md#configuration-and-presets)). It is the only role that may propose a configuration
change.

## What a configuration admin does

- **Reads the rules in force.** **Configuration** (`/app/configuration`), Rules in force: every area in plain words,
  the data taxonomy among them. Presets and overlays shows what ships; Versions shows each version with who accepted
  it and when.
- **Proposes a change.** From the Configuration page, or through an AI client, `propose_configuration_change` saves a
  draft. Its check first shows the diff, the rules check and the data uses the change would touch, and writes
  nothing. An AI client's change is a candidate draft: it may send it for approval, and a person answers the
  decision.
- **Imports data domains.** Import data domains on the Configuration page (`import_data_taxonomy`) turns a CSV file, or
  a catalog's rows, into a draft of the domain tree. The domains the file leaves out are kept, and owners come back as
  prepared owner changes, never live owners.
- **Sends it and answers others.** Drafts (`/app/configuration?view=drafts`) lists the waiting drafts; each draft's
  page (`/app/records/configuration-draft/<id>`) shows the diff, the check and who is asked. Submitting opens a
  configuration change decision. Under Balanced the configuration admin role is asked, with the data owners of the
  domains it touches consulted; another configuration admin, or the proposer under Balanced, approves on the decision
  page. When the decision is accepted, the engine activates the draft as the next version.
- **Exports and checks.** `export_configuration` gives the configuration as files, and `get_configuration_health`
  says whether the version in force loads and which drafts wait. The Admin page shows the host settings that are safe
  to show.

## In the demo

Yusuf Brennan and Jonah Pereira both play configuration admins, so another person accepts what one proposes.

- **S-10:** Yusuf's draft makes the AI use rule of Workplace / Visitor log never. Its page shows the diff, the rules
  check, the data use it touches (Visitor Sign-In Kiosk) and who accepts it, with the data owner of Workplace, Hugo
  Brandvold, consulted by name. Jonah accepts it on the decision page, and version 2 is in force.

Jonah is also a member of the Finance Apps Support team.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Yusuf Brennan (`yusuf`) and Jonah Pereira (`jonah`).
- **Reads:** 57 of the 61 read tools, among them the ones only some roles get: `export_configuration`, `get_admin_status`.

The shipped Tool Policy lets this role call 8 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 42 | `propose_configuration_change` | Change the rules the way the admin said | configuration-admin | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 99 | `import_data_taxonomy` | Turn this CSV or catalog list into a configuration draft of data domains | configuration-admin | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
