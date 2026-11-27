# Data governance lead

**Role key:** `data-governance-lead`. **Under Balanced:** appoints the owners of top-level data domains, keeps data
locations and mappings current, and sees every data gap.

The data governance lead looks after the data taxonomy as a whole: that every domain has an owner, that the stores
where data sits are known, and that every gap has someone working on it. The data owners decide about their own
domains; the lead makes sure there is a data owner to ask ([concepts.md](../concepts.md#the-data-taxonomy)).

## What a data governance lead does

- **Reads the whole taxonomy.** **Data** (`/app/data`) has five tabs: Domains (the tree with its owners, rules and
  gaps), Locations, Mapping, Uses and Gaps. Each has its own search, and each count opens its rows.
- **Appoints top-level owners.** Under Balanced an ownership decision on a top-level domain asks the data governance
  lead, beside the person proposed. The lead also proposes owners and deputies (`propose_owner_change`), for a domain
  or a data location.
- **Keeps locations and mappings current.** Records the stores where data sits (`save_data_location`) and which
  domain's data sits where (`save_data_mapping`). A location not seen for longer than the setting
  `data-location-stale-days` (180 days in Balanced) counts as stale.
- **Works the gaps.** **Data**, Gaps lists every finding: a domain with no owner or no deputy, a location with no
  technical owner, an application that reads restricted data with no decision, a copy that breaks its rule, a leaver
  who still holds data roles. Each names the act that fixes it.
- **Moves references** when a domain is split or merged (`move_data_references`, in the web app): the mappings and
  uses move to the new domain, and their decisions stay only while the data owner stays the same.
- **Works with the configuration admin** on the domain tree itself, which is configuration
  ([admin.md](../admin.md#the-data-taxonomy)).

## In the demo

Hana Lindqvist plays the data governance lead. She is also data owner of People.

- **S-07:** Data, Domains as Hana: Commercial shows no owner, and Payroll shows "AI never" and no deputy. The other
  tabs show the copy that breaks its rule in Customer Test, a location with no technical owner and one not seen since
  May, and a leaver who still holds data roles.
- **S-09:** the retention of Legacy Timesheet App's timesheet entries waits for her, as data owner of People.
- **S-22:** she adds the data location "Depot safety binders" and maps the safety findings there.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Hana Lindqvist (`hana`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 12 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

| No. | Tool | What it does | Who exactly | Served |
|---|---|---|---|---|
| 16 | `propose_owner_change` | Hand over or add an owner of an application, a data domain or a data location | Owner of the app, ai-lead (agents), platform-admin; for a data domain, the domain's effective data owner, data-governance-lead and platform-admin; for a data location, its technical owner, data-governance-lead and platform-admin | Web app and AI clients |
| 17 | `save_decision_proposal` | Record a decision for acceptance | Holders of R, A or C for the decision type | Web app and AI clients |
| 18 | `submit_record` | Send it for approval | The record's requester or creator | Web app and AI clients |
| 19 | `withdraw_record` | Take it back | The record's requester or creator | Web app and AI clients |
| 45 | `save_link` | Link this work item or this evidence | Owner of the subject's app, change-manager, pmo; evidence on records the caller can write | Web app and AI clients |
| 46 | `record_owner_leave` | This owner leaves on this date | The leaving person (any role), an owner of an app the person owns, platform-admin | Web app and AI clients |
| 52 | `answer_approval` | Approve or reject a request I was asked | The person the approval request asks, through any role (the engine checks the role or owner role, the separation rules and the request's state) | Web app only |
| 62 | `complete_review` | This review is done | The person whose review it is (the engine checks the lead owner role, the decision right or the approval rule, and the accepted outcome decision) | Web app only |
| 83 | `save_data_location` | This store exists, moved, or is gone (up to 100 items per call) | platform-admin, architect, application-owner, the location's technical owner, data-governance-lead | Web app and AI clients |
| 84 | `save_data_mapping` | This domain's data sits here, or it left | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | Web app and AI clients |
| 85 | `move_data_references` | Move mappings and uses off a domain | platform-admin, data-governance-lead | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
