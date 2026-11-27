# Enterprise Applications Governance: the documentation

Enterprise Applications Governance keeps one register of an organization's business applications, agents and the
data they use, and runs the decisions about them: intake, tiers, owners, changes, reviews, data use and retirement.
People do their work in the product's web app. AI clients such as Claude Code work through the product's MCP server
for the person who signed in. Every write path goes through the engine's write guard (OP-08): a person's write meets
the same rules under the person's own roles, and an AI client's write also meets the AI rules. A person answers every
approval.

These pages describe the product as the code in `src/` builds it today. The tool, error and role tables are generated
from the contracts (`node tools/docs-gen.mjs`; the `docs` check of `tools/ci.mjs` fails when they drift), every link is
checked, and the rest was checked against the code, its tests or the shipped configuration by review, statement by
statement.

**Status: built and tested; runs as a demo install with fictional data; no release yet.** The product runs end to end
on one machine with fictional data, and on Azure as a demo install with fictional data. A real install with an organization's own data cannot reach its first configuration
version yet ([install.md](install.md), "A real install").

## Start here

| Page | For | What it covers |
|---|---|---|
| [Concepts](concepts.md) | Everyone | The register, owners, tiers, decisions and approvals, the data taxonomy, AI clients and their stamps, Tool Policy, configuration versions |
| [Guide per role](#the-guide-per-role) | Each person | What a person in each product role does, on which screens, with the demo persona who plays the role |
| [Admin guide](admin.md) | Platform and configuration admins | Configuration and presets, roles and access, Tool Policy, the data taxonomy setup, the daily check, the Admin page, the demo install |
| [Install guide](install.md) | The Azure team and the Entra admin | The Azure install with the Azure Developer CLI, the app registrations, the checks after deploy, a demo install, a local run |
| [Claude Code and MCP](mcp.md) | Each person who uses an AI client, and the Entra admin | Connecting Claude Code, the tool list, what an AI client may and may not do, refusals |
| [REST API](api.md) | Developers | `POST /api/{tool}`, sign-in, headers, the envelope, the error codes, the other routes |
| [Testing](testing.md) | Developers | The full check run, the Linux run on Windows, the web app checks, the end-to-end suite, accessibility, the demo kit check, the docs check |
| [The product demo](demo/README.md) | Presenters and evaluators | The 23 demo scenarios with their presenter scripts, the 5, 15 and 45 minute runs, a route per audience, the Claude Code prompts, and the demo kit |

## The guide per role

One page per product role. Each says what the person does and where, and lists what the shipped Tool Policy lets the
role call. The persona column names who plays the role in the fictional demo
(`adapters/azure/seed/demo-personas.json`).

<!-- generated: guides (node tools/docs-gen.mjs) -->
| Guide | Role key | What the role does (Balanced) | Demo persona |
|---|---|---|---|
| [Requester](guide/requester.md) | `requester` | Submits intake requests. | Lena Okoro (`lena`) |
| [Maker](guide/maker.md) | `maker` | Builds in approved environments and records links. | Priya Castell (`priya`) |
| [Application owner](guide/application-owner.md) | `application-owner` | Holds owner roles on applications. | Nadia Ferreira (`nadia`) |
| [Data owner](guide/data-owner.md) | `data-owner` | Decides data use for the data domains they own or steward, and for applications that declare no data use. | Kai Ostrowski (`kai`) |
| [Architect](guide/architect.md) | `architect` | Reviews designs and grants exceptions to standards. | Elif Dunmore (`elif`) |
| [Security reviewer](guide/security-reviewer.md) | `security-reviewer` | Runs security reviews and agrees agent autonomy levels. | Noor Safadi (`noor`) |
| [Change manager](guide/change-manager.md) | `change-manager` | Runs the change schedule; authorizes tier 3 changes and emergencies. | Hugo Brandvold (`hugo`) |
| [Change board](guide/change-board.md) | `change-board` | Decides exceptions about changes, and windows that cover several applications, as a group. | Tomas Rendel (`tomas`) |
| [PMO](guide/pmo.md) | `pmo` | Scores demand and runs portfolio reviews. | Mateo Brandt (`mateo`) |
| [AI lead](guide/ai-lead.md) | `ai-lead` | Keeps the AI guidelines, is consulted on agent autonomy, decides on agents Microsoft flags. | Dmitri Halloran (`dmitri`) |
| [Platform admin](guide/platform-admin.md) | `platform-admin` | Triages intake, confirms tiers, runs the gap check. | Owen Achterberg (`owen`) |
| [Data governance lead](guide/data-governance-lead.md) | `data-governance-lead` | Appoints the owners of top-level data domains, keeps data locations and mappings current, and sees every data gap. | Hana Lindqvist (`hana`) |
| [Configuration admin](guide/configuration-admin.md) | `configuration-admin` | Instructs and accepts configuration changes. | Yusuf Brennan (`yusuf`) and Jonah Pereira (`jonah`) |
| [Auditor](guide/auditor.md) | `auditor` | Reads everything, including audit entries, and changes nothing. | Bianca Ferro (`bianca`) |
| [Reader](guide/reader.md) | `reader` | Reads the register, decisions and the change schedule. No audit entries, no restricted columns. Never writes. | Theo Marsh (`theo`) |
<!-- end generated: guides -->

## Setup and admin guide drafts

Eleven longer guides, written on 2026-11-23, follow an install from an empty tenant to daily operation, with the
Microsoft facts each step rests on and the date each was checked. They were written before the web app acted, so
where a draft says a part is not built, the pages above say what is built today. Each draft ends with its own open
points.

| Guide | For | What it covers |
|---|---|---|
| [S1 Pick a preset and overlays](setup/S1-pick-presets-and-overlays.md) | The platform owner and the configuration admin | The shipped presets and overlays, the offline dry runs, the bootstrap of version 1 (designed, no entry point yet), the client binding patch |
| [S2 Connect Claude Code](setup/S2-connect-claude-code.md) | The Entra admin and each person | A registered Entra client, the Known Client list, Client ID Metadata Documents for pilots, the checks of the identity chain |
| [S3 Adopter allowlist entries](setup/S3-adopter-allowlist-entries.md) | Device, GitHub and Entra admins | Claude Code and GitHub Copilot managed settings, the Entra MCP firewall, Defender's runtime protection for AI agents |
| [S4 Prepare Microsoft Entra ID and deploy](setup/S4-prepare-entra-and-deploy.md) | The Entra admin and the Azure team | One group per role, the API app registration, the client registrations, `azd up`, the first checks |
| [S5 Connect the approval runner](setup/S5-connect-the-approval-runner.md) | The Entra and Power Platform admins | The runner's identity and app role, its Workload Grant, the flow import, the first card |
| [S6 Connect pipeline record steps](setup/S6-connect-pipeline-record-steps.md) | Pipeline owners and the Entra admin | One federated identity per pipeline, its app role and Workload Grant, the samples' settings |
| [S7 Turn on the directory reads](setup/S7-turn-on-directory-reads.md) | The Entra admin and the platform admin | Live reads on behalf of the person: the federated credential, the delegated permissions, the inventory role |
| [A1 External configuration admin runbook](admin/A1-external-admin-access-runbook.md) | The Entra admin and an external admin | A guest in a group eligible through Privileged Identity Management, with the client's approval |
| [A2 Urgent revoke and break-glass accounts](admin/A2-urgent-revoke-and-break-glass.md) | The Entra, application and data store admins | Cutting off a person or a workload now; keeping the top data store roles for break-glass accounts |
| [A3 Workload grants](admin/A3-workload-grants.md) | The configuration admin, the platform admin, workload owners | Granting, checking, renewing and retiring the grants that let pipelines and the approval runner in |
| [A4 Run the approval runner day to day](admin/A4-approval-runner-operations.md) | The platform admin and the runner's owner | The outbox states, released and failed requests, late answers, answers that do not count, old cards |

## Other READMEs in the source

These describe one part of the code each. The pages above link to them instead of repeating them.

| README | What it covers |
|---|---|
| [src/README.md](../README.md) | The source tree, the check runner, the engine command line, the placeholders |
| [adapters/azure/README.md](../adapters/azure/README.md) | The Azure binding: its projects, the routes, identity and actor kinds, the persona switch, the approval runner, the directory reads, the data tier, a local run |
| [adapters/azure/STATUS.md](../adapters/azure/STATUS.md) | What works today, the test counts, what is designed but not built |
| [adapters/azure/infra/README.md](../adapters/azure/infra/README.md) | The Azure template: every resource, parameter and app setting |
| [adapters/azure/flows/README.md](../adapters/azure/flows/README.md) | The approval runner's Power Automate flows |
| [demo/kit/README.md](../demo/kit/README.md) | The demo kit: `demo local up`, `check`, `refresh`, `play`, `sandbox rebuild`, and the rehearsal aids `local age-request` and `local fail-card` |
| [demo/walkthrough/README.md](../demo/walkthrough/README.md) | The scripted walkthrough through the MCP server core |
| [mcp/contracts/README.md](../mcp/contracts/README.md) | The tool contracts, the envelope, the Tool Policy seed |
| [ui/README.md](../ui/README.md) | The web app's pages and how they are built |
| [samples/pipeline-steps/README.md](../samples/pipeline-steps/README.md) | Copy-paste record steps for Azure DevOps, GitHub Actions and Power Platform Pipelines |

## How these pages are written

- Fictional data only. Every person, group, application and ID in an example is fictional, and every host is a
  placeholder such as `<host>` or a loopback address.
- Placeholders: `<host>` (the install's public host name), `<tenant-id>`, `<api-client-id>`, `<web-client-id>`,
  `<client-id>` (an MCP client's registration), `<port>` and `<environment-name>`. No real tenant, host, subscription
  or client value appears.
- Every source a page cites is one a reader can open: these pages, a README or a file in `src/`, or a Microsoft or
  vendor page with the date it was checked. A page never cites the maintainers' unpublished design documents or
  decision numbers; it states the rule itself, such as "People get product roles only through Entra groups". The
  setup and admin drafts keep their open points as text. Their "WORKING DEFAULT (open design question N)" markers
  stay, because the shipped preset files carry the same markers in their `$comment`
  ([`config/presets/README.md`](../config/presets/README.md)), so a reader can find the rows that rest on one.
  Why: the drafts cited design documents a public reader cannot follow (wave 4B, 2026-11-25).
- A generated part sits between two marker lines that name it. Edit the source file named at the top of
  `tools/docs-gen.mjs`, then run `node tools/docs-gen.mjs` from `src/`; never edit between the markers by hand.
