# Page designs: Overview, Application 360, dependency map, timeline

The four pages of the product's interface (D-18), as React and Fluent UI v9 components. One source gives two builds:
the local harness on fictional fixture JSON, and the web app of binding C on the product's REST API (section 8).
The web app is the first release design. Binding A (generative pages in a model-driven app) belongs to a possible later
Dataverse edition, not planned (D-26). The binding A notes below and each section's fallback apply only to that
edition. Whether these components run unchanged inside a generative page is UNVERIFIED.

Sources: document 23 (P03 change management, P05 deployment record, the value stream); document 24 (tools 6, 7, 8,
25 and 28); document 25 (generative pages and their sources); the MCP tool contracts in `src/mcp/contracts/`;
Microsoft Learn on generative pages (https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-pages).

Screenshots of every page at 1280 px and 390 px: `src/ui/screenshots/`.

## 1. Rules for all four pages

| Rule | How the pages follow it |
|---|---|
| Light only (D-18) | One Fluent light theme built on the Academy palette (`harness/src/theme.ts`). There is no dark theme and no theme switch. Page background light gray `F3F5F8`, panels white, text navy `0A2A4A`, brand blue `0071DC`, gold `FFC221` for attention and for "today" |
| Read-only | No page writes. Changes go through an AI client on the product's MCP server or a pipeline's record step, and decisions are made on approval cards. The engine checks every write. Application 360 says so on the page |
| Numbers come from the server | The pages count nothing. Every figure is a field of a tool output (stored rollups at scale). A list shown next to its count is the same server list. Until a figure is read, its tile shows a placeholder, never a zero |
| Every number drills to rows | KPI tiles and bars hand a drill-down to the host (`Host.drill`). In a possible later Dataverse edition, not planned, binding A would open the matching view. The web app opens the applications list for the Applications tile and the stage, tier and kind bars, and shows the request for an AI client for the others. The harness shows a toast |
| Each panel fails on its own | Every panel holds the state of one tool call. A failure shows inside that panel with the error message and the correlation ID, and the rest of the page keeps working. `?fail=<tool>` in the harness shows it |
| Hidden rows are said, not shown | `couldNotRead` becomes a note in the panel that owns that kind, for example "1 decision is hidden by your access". A list that shows one page says so when the answer carries a `skipToken` |
| Text people typed is data | Fields in `untrustedFields` render as plain text only. External links are https only and open in a new tab |
| Labels from configuration | Status labels come with each value. Keys returned without a label (dependency type, target stage, deployment route, owner role) are labeled through `get_configuration`; if that read fails, the key shows in plain words |
| Fixed meanings drive the visuals | Badge tone and icon come from the engine meaning, never from the label. WORKING DEFAULT (open design question 1) |
| Status never by color alone | Every status badge has an icon and a text label; chart marks have a legend and a list view with the same items |
| Phones | Below 720 px every page is one column with 16 px gutters and no sideways scroll; tables are lists; the graph and the timeline chart switch to their list views by default |
| Times | The harness shows UTC so every render is the same. The web app shows the viewer's time zone and names it ("13:00 to 16:00 EST"). Calendar dates, such as a review's due date, never move between zones; "today" is the viewer's date. The tools filter by UTC date, so the web app asks for the UTC dates that cover the viewer's days and keeps the items of those days |

## 2. Overview (portfolio and "needs attention")

**Calls.**

| Panel | Tool | Input |
|---|---|---|
| Key figures, "Applications by stage", owner gaps | `get_portfolio_summary` | `{ groupBy: "stage", includeOwnerGaps: true, top: 50 }` |
| By tier | `get_portfolio_summary` | `{ groupBy: "tier" }` |
| By kind | `get_portfolio_summary` | `{ groupBy: "kind" }` |
| Flagged deployments | `get_deployments` | `{ latestOnly: false, flaggedOnly: true, from: today - 30, to: today }` |
| Coming up | `get_change_schedule` | `{ from: today, to: today + 14 }` |
| Owner role labels | `get_configuration` | `{ entity: "owner-role" }` |

**Layout.** Five key figures: applications, owner gaps, reviews overdue, documents missing or stale, old drafts. The
three that need someone carry a gold bar and a warning icon when above zero. Then "Needs attention" (owner gaps,
then flagged deployments with one badge per flag) beside "Coming up" (change windows of the next 14 days, with the
change type and the authorization state). Then the distribution bars: by stage in configured stage order, by tier
(with "No tier yet" in gray), by kind.

**Notes.** "Old drafts" uses `draftsOlderThanDays`; the tool does not return the age itself, so the caption says
"Older than the configured draft age". A standard change with `authorized` true shows "Pre-authorized".
WORKING DEFAULT (open design question 8). An AI-only code approval shows as a flag like the other three.
WORKING DEFAULT (open design question 7).

**Fallback.** A classic dashboard with system charts and the key views.

## 3. Application 360

In a possible later Dataverse edition, not planned, binding A would put it in a tab of the Application main form,
where it receives the record ID
(https://learn.microsoft.com/power-apps/maker/model-driven-apps/generative-pages).

**Calls.** Two engine calls, so the generative page six-table limit does not bind it (the card reads its tables on the server).

| Panel | Tool | Input |
|---|---|---|
| Header, deployments, open changes, dependencies, decisions, documents, reviews, components, delivery links | `get_application_card` | `{ businessApplicationId, sections: [every section except owners] }` |
| Owners, owner gaps, "Microsoft's listed owner differs", last owner check | `get_application_owners` | `{ businessApplicationId }` |
| Labels | `get_configuration` | `{ entity: "owner-role" }`, `{ entity: "target-stage" }` |

**Layout.** Header: name, registration status, data stamp, actions (dependency map, timeline; "open the form" only in a possible later Dataverse edition).
Summary: purpose, a "Needs attention" strip that repeats facts from the panels (owner gaps, stale or missing documents,
a failed last deployment, the Microsoft owner flag), and six facts (kind, tier, stage and since, build path, uses AI or
the agreed autonomy level, next review). Two columns below: deployments (latest per target, production first),
open changes, dependencies (counts and the most important edges), decisions (latest five), delivery links; and
owners, documents, reviews, components. On a phone the order is owners, deployments, changes, documents, reviews,
dependencies, decisions, components, links.

**Notes.** The card has no portfolio and no stage list, so the page shows no breadcrumb and no stage stepper (open
points 2 and 3). An owner gap offers "Propose", which hands over to `propose_owner_change`; it never assigns. In the
web app, "Propose" opens a dialog with the request to give to an AI client, with a copy button. "Open the form" would
show only on binding A, in a possible later Dataverse edition, not planned, because the web app's record link is
this page.

**Fallback.** The main form alone.

## 4. Dependency map

**Calls.** One walk per direction, on the server, because the built-in query tool appears to lack recursion (document 24, tool 7).

| Part | Tool | Input |
|---|---|---|
| Right side: what depends on it | `get_dependency_impact` | `{ businessApplicationId, direction: "dependants", depth: 3 }` |
| Left side: what it needs | `get_dependency_impact` | `{ businessApplicationId, direction: "dependencies", depth: 3 }` |
| Relation words | `get_configuration` | `{ entity: "dependency-type" }` (label and inverse label) |

**Layout.** A layered graph: what it needs on the left by hops, the application in the middle (light blue with a gold
top bar), what depends on it on the right by hops. Each node sits level with the node it hangs from. Arrows point to
what is needed. Edge weight and style show the criticality of the link (high, medium, low, not set dashed); a legend
names them. A node shows the name, the relation ("Reads data from", or the inverse "Data read by" on the left), and
the tier and lifecycle, or "No business owner" with a warning icon. Hover or focus lights the path back to the
application and dims the rest; selecting a node opens its 360. Controls: show both, what breaks, or what it needs;
graph or list view. A note says when the walk stopped at the depth limit with more beyond (`truncatedAtDepth`).
Below: counts by tier and by criticality for each direction, from the server's `counts`.

**List view.** Every node, grouped by hops, with the relation sentence, the business owner (or the warning), the
criticality, the tier and the lifecycle. It is the default on phones and the keyboard and screen reader path; every
node is reachable from it.

**Notes.** Depth is fixed at 3 in this design; a depth picker would offer 1 to the `impact-max-depth` setting.

**Fallback.** Two subgrids on the Application form, "depends on" and "used by"; a PCF control if pages cannot run.

## 5. Timeline (changes and deployments)

**Calls.**

| Part | Tool | Input |
|---|---|---|
| Change windows | `get_change_schedule` | `{ from: today - 14, to: today + 28, top: 100 }` |
| Deployments | `get_deployments` | `{ latestOnly: false, from: today - 14, to: today, top: 100 }` |
| Labels | `get_configuration` | `{ entity: "target-stage" }`, `{ entity: "deployment-route" }` |

**Layout.** Lanes are deployment targets, grouped by target stage, production first (configured rank), and a
"No target yet" group for planned changes without a target. A week axis with a gold "Today" line; the past is
shaded. Change windows are bars: solid blue when authorized, hollow blue when not yet authorized, hollow gray for a
draft, pale blue when closed, a red outline for an emergency. Deployments are dots: navy succeeded, amber partly
succeeded, red failed, with a gold ring when flagged. Hover or focus gives the details. Below the chart: the same
items as two lists, planned changes and deployments, with flags as badges. The window is read-only (D-13): no booking
and no freeze tables; enforcing a window stays in the deployment tool. Filters: target stage; chart or agenda.

**Agenda view.** One list by day, oldest first, with a "Today" rule. It is the default on phones.

**Notes.** "Recorded by" shows the deployment route label. A pipeline step record arrives under the pipeline's own
workload identity with an expiring workload grant. WORKING DEFAULT (open design question 12).

**Fallback.** A grid view grouped by environment and date.

## 6. Working defaults these pages rest on

The questions are the open design questions of document 28, section 3.

| Question | Where | What changes if the question is answered differently |
|---|---|---|
| 1, fixed engine meanings | `harness/src/data/meanings.ts`, `contracts.ts` | Badge tones and icons would need a configured tone per status row instead of the meaning table |
| 7, AI-only code approvals never count | `meanings.ts` (flag text), Overview and timeline flag badges | The "AI-only code approval" flag would go, or change its wording |
| 8, the locked list the engine may accept without a card | Overview "Coming up", timeline rows ("Pre-authorized") | Standard changes would show "Authorized" after a card like any other |
| 12, workload grants for pipelines | Timeline deployment rows ("Recorded by: Pipeline step") | Only the wording; the record and its route stay the same |

Questions 4 and 9 do not reach these read-only pages: no page proposes configuration or accepts an AI draft.

## 7. Open points

1. Reusing these components inside a generative page is UNVERIFIED (React and Fluent versions of the page runtime,
   custom API calls are preview; document 25, source L16). A test in a real environment should load one panel in a page.
2. `get_application_card` returns no portfolio, so Application 360 shows none. Document 24 could add it to the card.
3. A stage stepper on Application 360 needs the configured stage list (`get_configuration` for `lifecycle-stage`, a third call).
4. `draftsOlderThanDays` carries no age; the page cannot say "older than 30 days". The tool contract could add it.
5. `get_dependency_impact` nodes carry no direction, so the map makes one call per direction rather than one `both` call.
6. The page harness needs `picocolors` (ISC, build time only) through Vite and postcss. See `src/THIRD-PARTY.md`.
7. The impact walk on the local server labels a node two hops away with the dependency type of the first hop (for
   example "Calls API of" where the seeded link is "Reads data from"). The map shows what the tool returns; the tool
   should return the type of the node's own link.

## 8. The web app (binding C)

The same four pages, on the product's REST API, served by the product's host under `/app/` on the API's own origin.
`npm run build:app` builds it into `harness/dist/app`; `ui/README.md` has the local run guide.

**Addresses.** `/app/` is the overview and the home page. `/app/timeline`. `/app/records/business-application/<id>` is
Application 360: it is the record link every tool returns (`links.recordUrl`), so a link an AI client shows opens the
page. `/app/records/business-application/<id>/map` is the map. `/app/records/<entity>/<id>` of another entity shows a
plain "not shown here" panel with the ID. `/app/applications?stage=<key>&tier=<key>&kind=<key>` is the applications
list a drill-down opens (`find_applications`, 50 a page, "Show more" with the skip token). Any other address shows
"This page does not exist". The Application 360 and map tabs appear once an application is open.

**Sign-in.** Entra ID through MSAL for the browser: authorization code with PKCE, a full-page redirect to and from
`https://<host>/app/`, the tab's `sessionStorage` for the cache, and one scope, the API's `api.access`. Each call asks
for a token silently first and falls back to a redirect; a 401 from the API starts sign-in again, with a guard against
a redirect loop. For local runs only, the development sign-in sends a fictional persona's name in
`X-Product-Dev-Identity`; the page takes it only on a loopback host name, and the server keeps its own guards.
`config.json`, which the host writes, says which sign-in, which tenant, which client ID and which API base. The page
checks it before anything runs and says what is wrong.

**The top bar.** The product mark, the page tabs, a button that reads every panel again, and the signed-in person: a
card with the name, the product roles and the group that grants each (`GET /api/me`), and sign out. Below the tabs, a
banner when the person holds no product role or the session cannot be read. The footer names the time zone, the engine
version and the configuration in force (`GET /api/info`).

**States.** Each panel shows a skeleton while it loads, its own empty text, and its own failure with the correlation
ID. A timeout (30 seconds) or a network fault is an `upstream_unavailable` failure inside the panel. A list's count
shows only when its read succeeded.

**Accessibility.** A "Skip to content" link; after each in-app navigation the focus moves to the page area and the
title changes; every control is reachable by keyboard; status never by color alone; text in navy on white or light
fills, light only.

**Approval answers.** Not in this release. People answer approval cards through the approval runner. A page where a
person answers in the product needs its own spec, a guarded engine path for a person's answer, a matching database
rule and a stronger sign-in step; it goes to the next design pass as a proposal.
