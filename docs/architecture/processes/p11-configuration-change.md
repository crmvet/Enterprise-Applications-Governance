# P11 Configuration change

**Status: in design.** Nothing here is released or deployed. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

Every rule in the product is designed as configuration: tiers, stages, gates, change types, roles, who decides, approval rules and separation of duties. This process is designed to change any rule without code, rebuild or redeploy. The admin decides, an AI performs the change, the engine checks it and makes it a new version, and the record shows both.

Nobody edits configuration tables by hand. Live configuration changes only when a new version is activated.

## Trigger

- An admin asks for a change in plain words, in Claude Code or another MCP client.
- An organization applies or switches a preset: one full preset, plus declared overlays such as "small team" or "strict separation". The engine checks the result as one set.
- A product upgrade ships new default rows.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Ask for and accept a configuration change | Configuration admin | Configuration admin | Platform admin | Auditor | Performs the change |
| Change tier rules and tier questions | Configuration admin | Configuration admin | Platform admin, security reviewer, change manager, architect | Auditor | Performs the change |
| Change a gate policy row | Configuration admin | Configuration admin | Platform admin, security reviewer, change manager | Auditor | Performs the change |
| Change AI rules (autonomy levels, the AI lead's duties) | Configuration admin | Configuration admin | Platform admin, security reviewer, AI lead | Auditor | Performs the change |
| Change roles and their Entra group bindings | Configuration admin | Configuration admin | Platform admin, security reviewer | Auditor | Performs the change |

There is one accountable for every configuration decision: the configuration admin. The roles for each area are consulted, not accountable. Who owns the gate policy at an organization is part of the open change gate question ([P03](p03-change-management.md)).

## Steps

1. **Ask.** The admin says what should change, for example "add a fourth tier between 2 and 3" or "require a runbook for every agent".
2. **Prepare.** The AI client reads the current configuration and prepares the change as a patch of add, update and retire operations, or as a configuration file in the same format. It uses stable keys, never internal IDs.
3. **Propose.** The AI client proposes the change as the admin. The engine checks that the admin holds a role that may change configuration.
4. **Check.** The engine runs its full set of validation rules. It refuses a change that would break the engine or the configuration's own integrity. Examples: a key that does not exist; a decision with no one, or two people, answering for it; an approval rule whose approver is not a person's role; a rule that requires zero approvals; a change to a locked setting; a gate requirement that points to a retired row; a sequential approval that repeats an approver.
5. **Show the difference, then submit.** The engine stores the patch as a configuration draft, with the version it is based on and a summary. The admin can see the difference and its effect on live applications first, for example "12 applications move to the new tier's review cadence". When the draft passes the checks, the AI client submits it. The engine freezes the draft and opens a configuration change decision (or a gate policy change decision) as Proposed.
6. **Decide.** One card goes to the configuration admin role. It shows the difference. In the current design, the admin who asked may accept their own AI's draft, and the "strict separation" overlay needs a second configuration admin. Whether the admin should accept the exact change on a card at all is an open design question. The consulted roles get consultation cards, which never count. The AI cannot answer any card.
7. **Activate.** On acceptance, the engine activates the draft in one step: it rebases the draft if needed, checks it again, writes the changed configuration rows, and creates the next configuration version. Each version records its content, a hash of the content, when it was published, who asked for it, and the decision that accepted it. Versions are never edited. The engine and the API reload their caches when they see a higher version number.
8. **Stamp.** New records carry the new version. Old records keep the version they were made under, so any record can show which rules applied to it.
9. **Withdraw or undo.** A draft that is not yet accepted can be withdrawn. The engine marks it discarded and, if it was submitted, cancels its decision and its card. Undoing an active version is a new change that restores an earlier version's content and supersedes the decision. Nothing is edited in place.
10. **Upgrade.** A product upgrade proposes a configuration draft that adds missing default rows and updates default rows the organization has not changed. A configuration admin accepts it on a card. The organization's own changes are never overwritten, only reported.
11. **Check health.** The product shows the version history and the engine's own information, such as its version and a hash of the configuration it loaded.

## What is recorded

| Record | What happens |
|---|---|
| Configuration draft | The patch, its base version and its summary. Frozen when submitted; discarded if withdrawn |
| Configuration rows | Written only when a version is activated |
| Configuration version | Created when a draft is activated. Never edited |
| Decision | Configuration change; gate policy change |
| Approval record | One per required answer |
| Audit entry | "Configuration activated" |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| Configuration change | Configuration admin (a second one under "strict separation") | First to respond |
| Gate policy change | Configuration admin, with the change manager consulted | First to respond |
| A product upgrade's draft | Configuration admin | First to respond |

## What an organization can configure

- Its configuration set: which preset it is based on and which overlays it uses.
- Which settings are locked. Some settings, such as the "AI never approves" lock, ship locked and cannot be changed.
- Which roles may change configuration.
- Who decides and who approves configuration changes.
- The separation rules, for example a second configuration admin under "strict separation".

## Designed MCP tools

Every tool runs as the signed-in admin.

| Tool | What it does |
|---|---|
| `get_configuration` | Reads the current configuration by keys |
| `propose_configuration_change` | Proposes a patch or a preset. With a "validate only" option, it returns the difference and its effect on live applications |
| `submit_record` | Submits the configuration draft |
| `withdraw_record` | Withdraws a draft that is not yet accepted |
| `get_configuration_health` | Returns the version history and the engine's information |
| `explain_rules` | Returns the engine's rule trace |

## Microsoft tools linked, never rebuilt

None directly. Presets name which Microsoft controls an organization should set in Microsoft's own tools, instead of rebuilding them.

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Live configuration | Rows in their own schema in Azure SQL Database, with JSON columns for complex rules. Only the API's managed identity may write them, and activation is one transaction |
| Drafts | A configuration draft table in the same database |
| Versions | An append-only ledger table, so no version can be edited |
| Checking a change | The rules engine in the one Azure Functions app |
| Cache reload | The API compares its cached version number with the current one |
| The card | The approval runner ([approval cards](README.md#approval-cards)) |

## Open design questions

- Whether the admin who asked for a change then accepts the exact change on a card, and whether they may accept their own AI's draft.
- Whether a short list of fixed engine meanings (such as "this status is final") is acceptable, while every label stays configurable.

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- "You can't assign the same approver to different steps of your approval." The engine's checks refuse a sequential approval rule that repeats an approver for this reason. https://learn.microsoft.com/power-automate/set-up-sequential-approvals
- Append-only ledger tables "block updates and deletions at the API level." https://learn.microsoft.com/sql/relational-databases/security/ledger/ledger-overview

Previous: [P10 AI draft and human accept](p10-ai-draft-and-human-accept.md). Back to the [processes index](README.md).
