# Setup and admin guide drafts

- **Status:** 0.2 draft, 2026-11-23. Nothing in them was run against a tenant or a subscription, and no sign-in was made to write them. Where a step needs product code that is not built yet, the step says so.
- **Scope:** the first release is designed to run in the client's own Azure subscription, on the Azure binding (D-26). The guides follow that binding from an empty tenant to daily operation: Entra groups and app registrations, the deployment, the configuration, the approval runner, pipelines, the directory reads, the MCP clients and the allowlists, then the admin routines. Steps for the Dataverse binding, a later option, are kept and marked, so the design stays portable.

## Install order

| Order | Guide | What it leaves in place | Built today |
|---|---|---|---|
| 1 | [S4 Prepare Microsoft Entra ID and deploy](setup/S4-prepare-entra-and-deploy.md) | Role groups, the API app registration, the service on Azure, the first checks | The service and its template; never provisioned |
| 2 | [S1 Pick a preset and overlays](setup/S1-pick-presets-and-overlays.md) | The chosen preset, version 1, the client binding patch | The offline dry runs; the bootstrap of version 1 and the binding patch apply have no entry point yet |
| 3 | [S5 Connect the approval runner](setup/S5-connect-the-approval-runner.md) | Approval cards in Outlook and Teams, answers back to the engine | The routes and the outbox; the flows are a template nobody has imported |
| 4 | [S6 Connect pipeline record steps](setup/S6-connect-pipeline-record-steps.md) | Deployment records from each pipeline | The record endpoint and the copy-paste samples |
| 5 | [S7 Turn on the directory reads](setup/S7-turn-on-directory-reads.md) (optional) | Role holders, the owner check, the role binding check and the gap check, read as the person who asks | The readers, tested over recorded responses |
| 6 | [S2 Connect Claude Code](setup/S2-connect-claude-code.md) | People working through Claude Code as themselves | The server side of path 1 |
| 7 | [S3 Adopter allowlist entries](setup/S3-adopter-allowlist-entries.md) | The device, GitHub and network allowlists | Nothing in the product; client-side settings |

## The guides

| Guide | For | What it covers | Main design sources |
|---|---|---|---|
| [S1 Pick a preset and overlays](setup/S1-pick-presets-and-overlays.md) | The client's platform owner and configuration admin | The seven shipped presets and overlays, the conflicts, the offline dry runs, the bootstrap of version 1, the client binding patch | Document 21, sections 4.8, 6 and 7.6 |
| [S2 Connect Claude Code](setup/S2-connect-claude-code.md) | The client's Entra admin and each person | A registered Entra client first, the Known Client list, CIMD for pilots, checks for the identity chain | Document 24, sections 4 and 13; 28, Q14 |
| [S3 Adopter allowlist entries](setup/S3-adopter-allowlist-entries.md) | Device, GitHub and Entra admins | Claude Code managed settings, GitHub Copilot managed settings, the Entra Global Secure Access MCP firewall, Defender AI agent runtime protection in audit mode | Document 24, section 12 |
| [S4 Prepare Microsoft Entra ID and deploy](setup/S4-prepare-entra-and-deploy.md) | The client's Entra admin and Azure team | One group per role, the API app registration and what the product needs from it, the client registrations, `azd up`, the first checks | Document 21, section 11.2; document 25, section 12; document 26, sections 4 and 11 |
| [S5 Connect the approval runner](setup/S5-connect-the-approval-runner.md) | The client's Entra and Power Platform admins, the configuration admin | The runner's identity and app role, its Workload Grant, the flow group, the import, the switch, the first card | Document 23, S1; document 26, section 7; 28, Q19 |
| [S6 Connect pipeline record steps](setup/S6-connect-pipeline-record-steps.md) | Pipeline owners and the Entra admin | One federated identity per pipeline, its app role and Workload Grant, the sample's settings | Document 23, P05; document 24, section 9.1 |
| [S7 Turn on the directory reads](setup/S7-turn-on-directory-reads.md) | The client's Entra admin and platform admin | Live reads on behalf of the person: the federated credential, the delegated permissions, the inventory role | Document 24, section 5; document 26, section 4.5 |
| [A1 External configuration admin runbook](admin/A1-external-admin-access-runbook.md) | The client's Entra admin and the external admin | A guest in a PIM-eligible group with the client's approval; the fallback for clients without Entra ID P2 | 28, Q17; document 26, sections 3.5 and 4.2 |
| [A2 Urgent revoke and break-glass accounts](admin/A2-urgent-revoke-and-break-glass.md) | The client's Entra, application and data-store admins | Cutting off a person or a workload now; keeping the top data-store roles for break-glass accounts | Document 26, section 4.4; document 25, section 11.5 |
| [A3 Workload grants](admin/A3-workload-grants.md) | The configuration admin, the platform admin, workload owners | Granting, checking, renewing and retiring the grants that let pipelines and the approval runner in | Document 26, sections 3.4 and 4.5 |
| [A4 Run the approval runner day to day](admin/A4-approval-runner-operations.md) | The platform admin and the runner's owner | Where to look, the outbox states, released and failed requests, late answers, answers that do not count, old cards | Document 23, S1 and S2; document 26, section 7 |

## Conventions

- Each guide has the same shape: purpose, who, prerequisites, steps, checks, then open points and the Microsoft facts used.
- `[branch: Azure]` marks a step of the Azure binding, the first release's (D-26). `[branch: Dataverse]` marks a step of the Dataverse binding, kept as a later option and not built. A step with no marker is the same on both.
- "Not built yet" marks a step whose product part does not exist. The step describes the design and its open points, never a command that does not work.
- Placeholders: `Product` (the dotted code prefix, as in `Product.Engine.Cli`), `<prefix>` (the publisher prefix of the client's Power Platform solutions), `<host>`, `<tenant-id>`, `<client-id>`, `<api-client-id>`, `<web-client-id>`, `<port>`, `<server-name>`, `<environment-name>` and `<region>`. `<product>` in group names follows document 26's naming proposal (section 3.2). No real host, tenant, organization, person or ID appears.
- `WORKING DEFAULT (open design question N)` marks each step that one of the six open design questions of document 28, section 3 (1, 4, 7, 8, 9 and 12) would change. Settled working defaults (for example 28, Q14, Q16, Q17 and Q19) are cited as "working default (28, Qn)" and carry no marker.
- Every Microsoft fact has a Learn link and the date it was checked; a fact taken from another file of this repository without a new check says so. Facts about Claude Code and GitHub link to the vendor's own documentation, marked "(vendor)".

| Question | Where the marker appears |
|---|---|
| 1 (fixed engine meanings) | S1 step 5; A1 "What the external admin never does"; A2 part B checks |
| 4 (accepting an AI's configuration change on one card) | S1 step 9; S2 "What the person may then do"; A1 part B, step 5; A3 parts A and C |
| 7 (AI-only code approvals in client tools) | S1 step 5; S6 step 4 |
| 8 (the locked list of decisions with no card) | S1 step 5 |
| 9 (which act counts as the person's) | S2 "What the person may then do" |
| 12 (workload grants for non-human workloads) | S1 steps 5 and 8; S3 prerequisites; S4 part A; S5 step 4; S6 step 3; A1 "What the external admin never does"; A2 part A (workload step 2) and part B (step 5); A3 purpose |

## Open points found while writing

Each is explained in the guide named. None changes code; some ask for code, most ask a design document to say more.

1. **Bootstrap entry point, decider and install rights** (S1, open points 1 and 2; S4, part F). The engine's bootstrap operation is built and refuses a pipeline; no route or MCP tool calls it, and what proves install rights on Azure is not designed. A deployed install cannot reach version 1 yet.
2. **The binding patch after version 1** (S1, open point 5; document 21, B21-5). With no role bound, nobody can instruct or accept it as an ordinary change.
3. **Regulated does not set Requires Just In Time** (S1, open point 3). RR-06 says it should for the configuration admin and platform admin roles.
4. **Entra ID P1 as a requirement** (S4, open point 2). Without it, people are assigned to the API one by one, and `check_role_bindings` reports each of them as a direct assignment.
5. **Who runs migrations once the SQL administrator is break-glass only** (S4, open point 3; A2, part B).
6. **The samples' default token scope** does not match the API's Application ID URI (S6, open point 1). Until the samples change, set `RECORD_ENDPOINT_SCOPE`.
7. **Checks designed but not in `check_role_bindings` yet:** nested groups (CHK-03), just-in-time roles (CHK-08), guests (CHK-11), grants ordered by expiry (CHK-09), credential-bearing grants, and CHK-10b on Azure (S7, open point 3; A3, open points 1 and 2; A1 and A2 checks).
8. **Principal References for workloads** are written only by the local seed (S5, open point 2; A3, open point 4).
9. **No product view of the approval outbox, and no way to send a failed request again** other than withdraw and submit (A4, open points 1 and 2).
10. **Approvals needing several answers, expiry and restarts** are built (`src/adapters/azure/ENGINE-CHANGES-NEEDED.md`, items 13 and 14), and S5 and A4 describe them. Still open: nothing expires while the approval runner does not poll, because the Azure binding has no timer, and for a product role the runner cannot leave out a person whose answer already counts (S5, "What the runner cannot do yet"; A4, open points 3 and 4).
11. **A secretless sign-in for the approval runner's flows** (S5, open point 1).
12. **The CIMD path** needs a host setting for the versioned authorization server identifier, and a tenant test of the Known Client identifier (S2, open points 3 and 4).
13. **Revocation through the product's server** (A2, open point 1; A1, part B). The Azure server does not honor continuous access evaluation, so the incoming token's lifetime, 60 to 90 minutes by default, is the revoke window.
14. **Dataverse group team membership type, cache and guest licenses** (A1, open points 1 to 3), for the later Dataverse binding.
15. **PIM activation maximum** (A1, open point 4): Learn gives 1 to 24 hours on one page and a maximum of eight hours on another. The runbook uses 8 hours.
16. **The least role for the CIMD pre-add** (S2, open point 2).
17. **New data paths for document 26** (S3, open points 1 and 2): the MCP firewall needs TLS inspection; Defender's prompt evidence collection sends prompts and tool calls unredacted to the cloud.
18. **The consistency check identity** rests on the proposed `consistency-checker` workload role and open design question 12 (A2, open point 2), and the check itself is not built.
19. **Break-glass repair under a Dataverse "Setup user"** is not checked (A2, open point 4).
20. **Which preset installs first** stays with D-03 (S1, open point 4).
21. **The least permissions for the directory reads** (S7, open point 1; document 24, spike 14).
22. **The "urgent revoke recorded" audit event** of document 26, section 9.2 is not built (A2, open point 3).
23. **Exact quotes for the remaining Microsoft facts.** The rows re-read on 2026-11-23 carry Microsoft's exact words; the rows still dated 2026-11-22, and a few rows that only name the fact, wait for the same pass (below).

One design gap may close for the later Dataverse binding: Learn says a member is added to a Dataverse group team when a non-interactive process calls on behalf of the user (S2, branch differences). That suggests document 24's UNVERIFIED item 9 passes; a tenant test still has to confirm it for on-behalf-of tokens.

## How the facts were checked

Nothing ran against a tenant, a subscription or a cloud API. The offline commands in S1 (the dry runs with the engine command line) were run against fictional files. On 2026-11-23 the steps that need no tenant were run on the local server of `src/adapters/azure` with the fictional demo: the S1 dry runs, the checks of S4, part E against a store with no version, and the first card of S5, step 9, with a stand-in for the runner. Microsoft facts were read on Microsoft Learn on 2026-11-22 (S1 to S3, A1 and A2) and on 2026-11-23 (S4 to S7, A3 and A4); Claude Code and GitHub facts were read on their public documentation pages on 2026-11-22. Each guide lists its facts with the link and the date checked.

A review on 2026-11-23 read the live pages again and matched each of these facts word for word, and the fact rows now quote them: in S2, the CIMD page, the MCP server page, the redirect URI page and the Claude Code MCP page; in S3, the three Defender pages and the Claude Code managed MCP page; in S4 to S6 and A3, group assignment and app roles; in S5 and A4, the Power Automate licensing, flow group and approvals pages; in S7, the inventory pages and the permission reference; in A1, the PIM licensing, assignment and activation pages; in A1 and A2, the access token lifetime. Rows still dated 2026-11-22 (most of A1 and A2, the GitHub and Global Secure Access rows of S3, and the Dataverse rows) were not re-read in that pass.
