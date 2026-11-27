# Data owner

**Role key:** `data-owner`. **Under Balanced:** decides data use for the data domains they own or steward, and for
applications that declare no data use.

A data owner answers for one area of the organization's data: a domain such as Customers or People, and the levels
below it unless those name their own owner. Who owns a domain is an owner assignment on the domain itself
([concepts.md](../concepts.md#the-data-taxonomy)); the product role lets the person act on it.

## What a data owner does

- **Decides data uses.** When an application declares that it reads or writes a domain's data in a way the rules ask
  about, such as AI use of confidential data, a data-use decision asks the domain's data owner by name. The request
  shows in **My work** and **Approvals** and is answered on the **decision page**. Some uses need a second answer:
  under Balanced, AI use of highly confidential data also asks the security reviewer, and the page shows the count,
  such as 1 of 2.
- **Decides copies.** A copy of the domain's data outside production (a test or development store) needs the data
  owner's decision when the copy rule asks for one. The rule may allow only masked copies, or none.
- **Decides retention.** When an application that wrote the domain's data retires, a data retention decision asks
  what happens to the data it wrote.
- **Looks after the domain.** **Data** (`/app/data`): the domain's record page (`/app/records/data-domain/<key>`)
  shows its owners up the chain, its rules and where each is set, where its data sits, who uses it and its gaps.
  The owner records where the data sits (`save_data_mapping`) and proposes a deputy, a steward or a successor
  (`propose_owner_change`).
- **Closes gaps.** **Data**, Gaps lists what is missing or waiting, such as a use with no decision or a copy that
  breaks its rule, each with the act that fixes it.

A data owner who proposed a tier 3 use cannot answer its request: the separation rule refuses it.

## In the demo

Kai Ostrowski plays the data owner. He owns Safety findings.

- **S-07:** Visit Report Agent's use of safety findings with AI waits for two answers. Kai approves on the decision
  page (1 of 2), then Noor Safadi approves as security reviewer, and the use is allowed.

Other data owners in the demo are people of the estate, not personas, such as Clara Voss for the masked copy of
supplier contracts; Nadia Ferreira and Hana Lindqvist also hold data owner roles.

## What the role may call

<!-- generated: role-tools (node tools/docs-gen.mjs) -->
- **Demo persona:** Kai Ostrowski (`kai`).
- **Reads:** 55 of the 61 read tools. It gets none of the reads kept to a few roles.

The shipped Tool Policy lets this role call 10 write tools. The engine then checks the relation each one names, so a tool on this list can still refuse a person who is not, for example, an owner of the application.

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
| 84 | `save_data_mapping` | This domain's data sits here, or it left | The location's technical owner, the domain's effective owner or steward, platform-admin, data-governance-lead | Web app and AI clients |
| 100 | `renew_approval_request` | Ask an expired approval request again | The proposer of the decision, or a platform admin (the engine checks it) | Web app only |
<!-- end generated: role-tools -->
