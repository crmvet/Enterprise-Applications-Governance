# Admin guide

For the people who run the product: the platform admin, the configuration admin and the data governance lead. Their
own day-to-day pages are in [the platform admin guide](guide/platform-admin.md),
[the configuration admin guide](guide/configuration-admin.md) and
[the data governance lead guide](guide/data-governance-lead.md). This page covers the settings and routines behind
them. The install itself is in [install.md](install.md).

Two rules hold everywhere. Nobody changes a rule in force by hand: every change of configuration is a draft, then a
decision people answer, then an activation the engine makes. And nobody holds a product role except through an Entra
group: membership is managed in Entra, never in the product.

## Configuration and presets

The configuration is every rule the engine applies: roles and their bindings, owner roles, tiers and tier questions,
lifecycle stages and gates, decision types, decision rights and approval rules, separation rules, review cadences,
document types, settings, the Tool Policy, the Known Clients, the Workload Grants and the data taxonomy. One numbered
version is in force at a time.

**Presets.** An install starts from a full preset and may add overlays (`config/presets`,
[its README](../config/presets/README.md)):

| Preset | Kind | In short |
|---|---|---|
| `balanced` | Full | The default three-tier preset. The demo runs on it |
| `starter` | Full, extends Balanced | Lighter gates: record in test, flag in production at every tier |
| `regulated` | Full, extends Balanced | Stricter gates. Nobody approves their own proposal at any tier, a second configuration admin accepts every configuration change, and applications declare their data uses (`domains-required`) |
| `microsoft-coe-baseline` | Full, extends Balanced | Balanced mapped to Microsoft's agent risk tiers and sample RACI, for a Center of Excellence that starts from Microsoft's guidance |
| `small-team` | Overlay | For a team too small to separate duties: one group may carry several roles, and no change board is needed. Conflicts with `regulated` and `strict-separation` |
| `strict-separation` | Overlay | Different people for proposer and approver, for the configuration admin who instructs and the one who accepts, and for the AI lead and the platform admin |
| `linked-change-management` | Overlay | The organization's own ITSM tool stays the system of record for change requests and their authorization |
| `data-domains-starter` | Overlay | A generic data domain tree to rename and extend, with no owners |

The engine command line checks a selection offline, with no sign-in: `validate <preset> --overlay <key>` and
`build-presets` ([src/README.md](../README.md#the-engine-command-line); guide
[S1](setup/S1-pick-presets-and-overlays.md) walks through the choice).

**The Configuration page** (`/app/configuration`, for every role that may read the configuration) has four views:
the rules in force by area, in plain words; the presets and overlays; the versions, with who accepted each and when;
and the drafts. Each draft has its own page (`/app/records/configuration-draft/<id>`) with the diff, the rules check
and what the change touches.

**A change, step by step.**

1. A configuration admin proposes it (`propose_configuration_change`), from the Configuration page or through an AI
   client. A check first (`validateOnly`) shows the diff, the rules check and the data uses the change would affect,
   and writes nothing. An AI client's change is a candidate draft only.
2. The admin submits the draft (`submit_record`). The engine opens a configuration change decision and asks the
   people the approval rules name: under Balanced, the configuration admin role, plus the data owners of the domains
   the change touches as consulted, or as approvers when it loosens a data rule.
3. When the counted answers meet the rule, the engine accepts the decision and activates the draft as the next
   version. Balanced lets a configuration admin accept a change they proposed; Regulated and the strict separation
   overlay need a second admin.

`export_configuration` gives the configuration as files (configuration admin, platform admin and auditor), and
`get_configuration_health` says which versions exist, which drafts wait and whether the configuration in force loads.
When it does not load, every governed write is refused with `configuration_unavailable` until a configuration admin
fixes it.

## Roles and access

**Roles and access** (`/app/roles`) shows each product role with its group, the people in it, the decision rights and
approval rules it holds, the owner roles it covers, the tools it may call and how far an AI client may go with each.

- **Binding a role** to a group is configuration: a Role Group Binding row, which names an Entra group and nothing
  else. The first bindings arrive with the install's binding patch; later ones through a configuration change. Adding
  a person to a role is an Entra change: add them to the group.
- **The binding check** (`check_role_bindings`, platform admin only) checks that every human role is bound to a group
  that exists and is held only through it, and that every workload role is held through a current Workload Grant. With
  the directory reads off, the group members and the binding check cannot be read from Entra, and the page says so; on
  a demo install, and on a local run with the directory mode Demo, they come from the fictional demo personas.
- **Teams** (`/app/teams`) shows the same groups as teams and committees: who is in each, what it decides, what it
  supports and what waits on it.
- **Workload Grants** let the pipelines and the approval runner in. Each one is recorded, expires (at most
  `workload-grant-max-days`, 365 days in Balanced) and is checked on every call. Guide
  [A3](admin/A3-workload-grants.md) covers granting, renewing and retiring them.

## Tool Policy

Each tool has one Tool Policy row: enabled or not, the product roles that may call it, whether an owner role is
needed, Max AI State (the furthest an AI client may take a write), AI May Submit (which records an AI client may send
for approval), and an optional limit of calls per person per hour. **Admin** and **Roles and access** show the rows in
force. The rows are configuration, so a change goes through a configuration change. Max AI State can only tighten the
states each tool's contract allows. Balanced ships no Tool Policy rows, so the shipped defaults apply; a configuration
that adds Tool Policy rows replaces the whole set, so it lists every tool. The defaults are in
[the tool list](mcp.md#the-tools), and each guide lists what its role may call.

**Known Clients** are the AI clients that may act for people: one row per client app ID, with the label the product
stamps on its writes. Once any row is in force, a client that matches no allowed row is refused.

## The data taxonomy

The data governance lead and the configuration admin set it up together.

1. **The domain tree is configuration.** Domains, subdomains and data sets are configuration rows, so the tree changes
   through a configuration change. **Import data domains** on the Configuration page (`import_data_taxonomy`,
   configuration admin) turns a CSV file, or the rows of a Microsoft Fabric or Purview list, into a configuration draft.
   It lists the domains the configuration has and the file leaves out, and never retires them. Owners in the file come
   back as prepared owner changes, never as live owners.
2. **Classifications and rules.** Each domain has a classification, and each classification gives the default use,
   AI use and copy rules ([concepts.md](concepts.md#the-data-taxonomy)). A domain may set its own rules, and the levels
   below it inherit them. The setting `data-taxonomy-mode` says whether applications must declare their data uses.
3. **Owners.** Data owners and stewards are owner assignments on domains, changed through ownership decisions
   (`propose_owner_change`). Under Balanced the data governance lead approves the owners of top-level domains, and the
   owner of the domain above approves a subdomain's.
4. **Locations and mappings.** The stores where data sits (`save_data_location`) and which domain's data sits where
   (`save_data_mapping`). A platform admin, an architect, an application owner, a location's technical owner and the
   data governance lead may record locations.
5. **Moving references.** When a domain is split or merged, `move_data_references` (platform admin and data
   governance lead, in the web app) moves its mappings and data uses to another domain. Decisions stay only while the
   data owner stays the same; the others are asked again. An AI client may only preview a move.
6. **Gaps.** The Data page's Gaps tab (`get_data_gaps`) lists what is missing, broken or waiting, each with the act
   that fixes it.

## The daily check

The engine's daily due-state refresh brings everything with a date up to date: it opens the reviews that came due,
turns stale the documents past their date, expires the approval requests past their time and opens their restarts,
and records each reminder, escalation and flag notice with who is told and how. Each item is idempotent, so a second
run the same day changes and sends nothing. The notices are recorded as audit entries; no flow sends them as
messages yet.

It runs three ways:

- **The Functions timer** `due-state-refresh` fires at five past every hour and runs the refresh in the hour the host
  setting `Product:DueStateRefresh:HourUtc` names (0 to 23, UTC). Unset, the timer does nothing. The Azure template
  does not set it yet.
- **On the Admin page,** a platform admin runs it now (`run_due_state_refresh`, web app only). "Check what it would
  do" runs every item and writes nothing.
- **On the local server,** `dotnet run --project Product.Azure.Server -- refresh-due-states --environment Development`
  (from `src/adapters/azure`) runs it once, now, as the engine, over the local store; `--dry-run` writes nothing.

## The Admin page

**Admin** (`/app/admin`, for the platform admin, the configuration admin and the auditor) shows:

- **Host and engine:** the settings that are safe to show, such as the environment name, the demo flag, the step-up
  mode, the refresh hour, the store kind and the schema version. Never a key, a connection string, a client ID or a
  tenant.
- **Configuration in force** and the role bindings.
- **The daily check:** its last runs, with the reviews opened, the documents turned stale and the notices sent, and
  the button to run it now.
- **Clients:** every client that wrote, drafted or was refused, and every client on the Known Client list, each
  opening AI activity for that client.
- **The approval outbox:** the requests the approval runner sends as cards or links, by state. A platform admin sends
  a failed card, or one sent more than two days ago, again with **Send again**, or closes it when its request has
  settled (`retry_approval_card`). Guide [A4](admin/A4-approval-runner-operations.md) covers the runner day to day.
- **Tool Policy:** who may call each tool, and how far an AI client may go.

## The demo install

A demo install runs on fictional data with the "Demo, fictional data" badge on every page ("Demo data" on a phone), and its operator acts as
the fictional demo personas through the **Acting as** menu in the top bar ([install.md](install.md#a-demo-install)).

- **The Demo guide** (`/app/demo-guide`, demo installs and local demo runs only) walks the story step by step, names
  each step's persona and switches to it. Its **Is the demo fresh?** panel reads `get_demo_freshness`: the story's
  approval requests that expired or expire within 48 hours.
- **Before a demo,** run the demo kit from a machine that reaches the install:
  `node src/demo/kit/demo.mjs refresh --base <url>` asks each such request again, as the platform admin persona, and
  runs the demo director again for anything missing; `node src/demo/kit/demo.mjs check --base <url>` gives a go or
  no-go for each scenario and exits 1 when one is not ready. On a demo install both need the operator's own web app
  token in `PRODUCT_DEMO_TOKEN`. [The demo kit README](../demo/kit/README.md) has every option.
- **Ask again.** On the decision page, the proposer of a decision or a platform admin may ask an expired request again,
  or one with less than 48 hours left (`renew_approval_request`). The engine closes the old request and opens a new
  one with the expiry its rule gives.
- **The stamp.** Every write the operator makes as a persona names the persona, with the Client App "Web app, demo
  operator". The operator's own ID is never written to the store; the request log keeps it with the correlation ID.

## Admin runbooks

- [A1 External configuration admin](admin/A1-external-admin-access-runbook.md): a guest admin through Privileged
  Identity Management, with the client's approval.
- [A2 Urgent revoke and break-glass accounts](admin/A2-urgent-revoke-and-break-glass.md).
- [A3 Workload grants](admin/A3-workload-grants.md).
- [A4 Run the approval runner day to day](admin/A4-approval-runner-operations.md).

These are drafts of 2026-11-23; where one says a part is not built, this page says what is built today.
