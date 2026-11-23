# Setup and admin guide drafts

- **Status:** skeletons, 0.1 draft, 2026-11-22. Nothing in them was run against a tenant, and no sign-in was made to write them.
- **Scope:** setup and admin guides for the parts that do not depend on the data store. Each guide has its steps and placeholders for branch details. A test in a real tenant still has to confirm the two client paths of guide S2.

## The guides

| Guide | For | What it covers | Main design sources |
|---|---|---|---|
| [S1 Pick a preset and overlays](setup/S1-pick-presets-and-overlays.md) | The adopter's platform owner and configuration admin | The seven shipped presets and overlays, the conflicts, the offline dry run, the bootstrap of version 1, the client binding patch | Document 21, sections 4.8, 6 and 7.6 |
| [S2 Connect Claude Code](setup/S2-connect-claude-code.md) | The adopter's Entra admin and each person | A registered Entra client first; CIMD for pilots; checks for the identity chain | Document 24, sections 4 and 13; 28, Q14 |
| [S3 Adopter allowlist entries](setup/S3-adopter-allowlist-entries.md) | Device, GitHub and Entra admins | Claude Code managed settings, GitHub Copilot managed settings, the Entra Global Secure Access MCP firewall, Defender AI agent runtime protection in audit mode | Document 24, section 12 |
| [A1 External configuration admin runbook](admin/A1-external-admin-access-runbook.md) | The adopter's Entra admin and the external admin | A guest in a PIM-eligible group with the adopter's approval; the fallback for adopters without Entra ID P2 | 28, Q17; document 26, sections 3.5 and 4.2 |
| [A2 Urgent revoke and break-glass accounts](admin/A2-urgent-revoke-and-break-glass.md) | The adopter's Entra, application and data-store admins | Cutting off a person or a workload now; keeping the top data-store roles for break-glass accounts | Document 26, section 4.4; document 25, section 11.5 |

## Conventions

- Each guide has the same shape: purpose, who, prerequisites, steps, checks, then open points and the Microsoft facts used.
- `[branch: Dataverse]` and `[branch: Azure]` mark a step that differs by data store; the choice was open under D-22, and D-26 picks Azure for the first release. A step with no marker is the same on both. The product's MCP server runs on Azure on both branches, so most steps are shared.
- Placeholders: `Product` (the dotted code prefix, as in `Product.Engine.Cli`), `<prefix>` (the Dataverse publisher prefix), `<host>`, `<tenant-id>`, `<client-id>`, `<port>` and `<server-name>`. `<product>` in group names follows document 26's naming proposal (section 3.2). No real host, tenant, organization, person or ID appears.
- `WORKING DEFAULT (open design question N)` marks each step that one of the six open design questions of document 28, section 3 (1, 4, 7, 8, 9 and 12) would change. Settled working defaults (for example 28, Q14, Q16 and Q17) are cited as "working default (28, Qn)" and carry no marker.
- Every Microsoft fact has a Learn link and the date it was checked. Facts about Claude Code and GitHub link to the vendor's own documentation, marked "(vendor)".

| Question | Where the marker appears |
|---|---|
| 1 (fixed engine meanings) | S1 step 5; A1 "What the external admin never does"; A2 part B checks |
| 4 (accepting an AI's configuration change on one card) | S1 step 9; S2 "What the person may then do"; A1 part B, step 5 |
| 7 (AI-only code approvals in client tools) | S1 step 5 |
| 8 (the locked list of decisions with no card) | S1 step 5 |
| 9 (which act counts as the person's) | S2 "What the person may then do" |
| 12 (workload grants for non-human workloads) | S1 steps 5 and 8; S3 prerequisites; A1 "What the external admin never does"; A2 part A (workload step 2) and part B (step 5) |

## Open points found while writing

Each is explained in the guide named. None changes code; most ask a design document to say more.

1. **Bootstrap entry point and decider** (S1, open points 1 and 2). Document 24 has no MCP tool for the apply of version 1, and on Azure a pipeline may run the apply, while document 21 says a person accepts version 1.
2. **Regulated does not set Requires Just In Time** (S1, open point 3). RR-06 says it should for the configuration admin and platform admin roles; the column exists in the schema as a proposal, but no preset sets it.
3. **Dataverse group team membership type** (A1, open point 1). Guests count only in "Members and guests" or "Guests" teams, and the type is fixed at creation. Guest access to Dataverse is also off by default in new environments, and the guest's license must come from the environment's tenant (one Learn page; another disagrees, A1 open point 3).
4. **Every PIM window lasts longer on Dataverse** (A1, open point 2): group memberships are cached until the next sign-in or up to 8 hours.
5. **Revocation through the product's server** (A2, open point 1; S2, open point 1). Access tokens last 60 to 90 minutes by default, not the one hour document 26 cites, and the server's on-behalf-of token cache can extend downstream access. Proposal: cap the cache or honor continuous access evaluation, and time a revoke through the server in a test tenant.
6. **PIM activation maximum** (A1, open point 4): Learn gives 1 to 24 hours on one page and a maximum of eight hours on another. The runbook uses 8 hours.
7. **The least role for the CIMD pre-add** (S2, open point 2): Learn lists Global Administrator, Agent ID Administrator or AI Administrator for creating the agent identity; a tenant test still has to find the least role for granting the scope.
8. **The template parameter for the CIMD identifier** is not named yet (S2, open point 3).
9. **New data paths for document 26** (S3, open points 1 and 2): the MCP firewall needs TLS inspection, so the edge decrypts the product's MCP traffic; Defender's prompt evidence collection sends prompts and tool calls unredacted to the cloud and is to be on by default in an upcoming release.
10. **The consistency check identity** rests on the proposed `consistency-checker` workload role and open design question 12 (A2, open point 2).
11. **Break-glass repair under a Dataverse "Setup user"** is not checked (A2, open point 3).
12. **Which preset installs first** stays with D-03 (S1, open point 4).

One design gap may close: Learn says a member is added to a Dataverse group team when a non-interactive process calls on behalf of the user (S2, branch differences). That suggests document 24's UNVERIFIED item 9 passes; a tenant test still has to confirm it for on-behalf-of tokens.

## How the facts were checked

Nothing ran against a tenant or a cloud API. Microsoft facts were read on Microsoft Learn on 2026-11-22; Claude Code and GitHub facts were read on their public documentation pages the same day. Each guide lists its facts with the link and the date checked.
