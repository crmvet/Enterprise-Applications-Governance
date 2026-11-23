# ui

The four pages: Overview, Application 360, the dependency map and the timeline. React 19 and shadcn/ui (Radix primitives, Tailwind CSS 4, lucide icons; D-32), in Microsoft's
Fluent 2 shape and colors with navy text (D-35), light only. One source gives two builds:

- **the harness**: the pages on fictional fixture JSON shaped by the MCP tool output schemas (`src/mcp/contracts/`),
  for design review and screenshots. No sign-in, no server;
- **the web app**: the same pages on the product's REST API, with Entra ID sign-in, served by the product's host under
  `/app/` on the same origin as the API. For local runs it signs in as a fictional persona instead.

The pages are read-only. Every write goes through an AI client and the engine; a person decides on an approval card.

The web app is early work, not a release. It runs locally against the Azure adapter's local server with fictional
personas; it has not been deployed, and its Microsoft Entra sign-in has not run against a real tenant yet.

| Path | What |
|---|---|
| `design/pages.md` | The page specs: calls, panels, states, phone layout, fallbacks, working defaults, the web app, open points |
| `harness/` | The one package: Vite, React 19, shadcn/ui on Tailwind CSS 4 and Radix, TypeScript, MSAL for the browser (web app only) |
| `harness/src/pages/` | The four pages. They see only the `Host` and the `ToolClient`, so both builds render the same components |
| `harness/src/components/` | `blocks.tsx`: panel, status badge, KPI tile, bar list, page header, links, the in-page switch; `rows.tsx`: list row |
| `harness/src/components/ui/` | The shadcn/ui components, MIT source copied from the shadcn registry, with their license and the changes made (`README.md`) |
| `harness/src/styles/` | `globals.css`: the theme tokens (Fluent 2 shape and colors, navy text; D-35), the `narrow:` and `medium:` breakpoints; `shadcn-tailwind.css`: shadcn's Tailwind helpers |
| `harness/src/data/` | Contract types, the `ToolClient`, the fixture client (harness), the API client (web app), labels, meanings, formatting |
| `harness/src/app/` | The web app: entry, shell and routes, `config.json` checks (including `demo`, which shows the "Demo, fictional data" badge on a demo install), sign-in pages, the applications list, dialogs |
| `harness/src/auth/` | Sign-in for the web app: Entra ID through MSAL (`msalAuth.ts`) and the development persona (`devAuth.ts`) |
| `harness/src/main.tsx`, `App.tsx` | The harness entry and shell |
| `harness/public/config.json` | The web app's settings for the Vite dev loop only (development sign-in). Never copied into a build |
| `harness/fixtures/<tool>/<case>.input.json` and `.output.json` | Fictional tool inputs and envelopes. `tools/json-check.mjs` validates both halves against the MCP tool schemas |
| `harness/scripts/check-packages.mjs` | Exact versions, registry sources, licenses, and `THIRD-PARTY.md` against the lock file (the `ui` check of `tools/ci.mjs`) |
| `harness/scripts/run-tests.mjs` | The unit tests: bundles `src/**/*.test.ts` with esbuild and runs them with Node's test runner |
| `harness/scripts/screenshots.mjs` | Renders the harness pages at 1280 px and 390 px with headless Chrome |
| `screenshots/` | `overview`, `application-360`, `dependency-map` and `timeline` from the harness, each at `-1280` and `-390` |

## Commands

You need Node 22 or later. Run everything in `src/ui/harness`.

| Command | What it does |
|---|---|
| `npm ci --ignore-scripts` | Installs the packages from `package-lock.json`, with no install scripts |
| `npm run build` | Typecheck, then the harness into `dist/harness/` |
| `npm run build:app` | Typecheck, then the web app into `dist/app/`, with the base path `/app/` |
| `npm run dev:app` | The web app on the Vite dev server at `http://127.0.0.1:4319/app/`, `/api` proxied to the local server |
| `npm run preview` | Serves `dist/harness/` on `http://127.0.0.1:4318/` |
| `npm test` | The unit tests (API client, sign-in guards, `config.json` checks, routes, time zones) |
| `npm run lint` | The strict TypeScript check of both builds and the tests, then the package check |

`dist/` is build output: git ignores it and the file checks skip it.

## Run the harness

```
cd src/ui/harness
npm ci --ignore-scripts
npm run build
npm run preview
```

Pages: `#/overview`, `#/app/10000000-0000-4000-8000-000000000001`, `#/map/10000000-0000-4000-8000-000000000001`, `#/timeline`.
Design review switches: `?fail=get_change_schedule,get_application_owners` answers those tools with a failure,
`?slow=1` shows the loading state. The harness shows every time in UTC, so each render is the same.

Screenshots, from Git Bash (Chrome on Windows needs absolute Windows paths, so `cygpath` converts them):

```
cd src/ui/harness
npm run build
node scripts/screenshots.mjs --out "$(cygpath -aw ../screenshots)" \
  --chrome "$(cygpath -aw '/c/Program Files/Google/Chrome/Application/chrome.exe')"
```

The script starts `vite preview` on 127.0.0.1, starts Chrome headless with its own temporary profile, blocks every
host except 127.0.0.1, waits until every tool call has answered, and writes full-page PNGs. It fails when a page logs
an error or scrolls sideways.

## Run the web app locally

Everything here runs on one machine with no Azure subscription and no sign-in: the local server over a SQLite file,
the fictional demo estate, and the development identity in place of Entra ID. You need Node 22 or later and the
.NET 10 SDK.

**1. Seed the demo and start the local server.** From `src/adapters/azure`, as its README says under "Run it locally":

```
dotnet run --project Product.Azure.Server -- seed --environment Development
dotnet run --project Product.Azure.Server -- --environment Development --urls http://127.0.0.1:5080 --Product:DevIdentity:Enabled=true
```

The development identity works only in the Development environment, with the switch on the command line, and for
loopback requests with no forwarding header.

**2. Open the web app.** Either way works:

- *From the local server, one origin.* Build once with `npm run build:app` in `src/ui/harness`. The local server
  serves `dist/app/` at `http://127.0.0.1:5080/app/` (its `appsettings.Development.json` names the folder) and writes
  `config.json` itself.
- *With the Vite dev server,* for page work with instant reloads: `npm run dev:app` in `src/ui/harness`, then open
  `http://127.0.0.1:4319/app/`. Vite serves `public/config.json` and proxies `/api` to `http://127.0.0.1:5080`. The
  proxy keeps the loopback host name and adds no `X-Forwarded-*` header, so the server's development identity takes
  the requests. No CORS is involved: the page and `/api` share one origin.

**3. Choose a persona.** The sign-in page lists the fictional personas of `config.json`. The web app's personas end
in `-web`: the same people and groups as the server's `nadia`, `priya`, `owen`, `mateo` and `config-admin`, signed in
through the web app's own fictional client ID, so the server takes each call as the person (actor kind `person`).
The page sends the persona's name in the `X-Product-Dev-Identity` header with each call, and keeps the choice for the
browser tab only. The page refuses the development sign-in on any host name that is not loopback, and the server
keeps its own guards.

**4. Try it.**

- Overview is the home page. The Applications tile, or a bar of the stage, tier or kind charts, opens the matching
  applications list; each application opens its Application 360. The other tiles show the request for an AI client.
- A record link, such as `http://127.0.0.1:5080/app/records/business-application/00000000-0000-4000-8000-000000000511`,
  opens Application 360. A record link of another entity shows a plain "not shown here" panel. In a local run the
  tools write their record links on `https://localhost`, because `Product:PublicBaseUrl` is empty; put
  `http://127.0.0.1:5080` in place of `https://localhost` to open one.
- "Propose" on an owner gap shows the request to give to an AI client. The page itself writes nothing.
- The refresh button in the top bar reads every panel again. Stop the server and press it: each panel shows its own
  failure with the correlation ID, and the rest of the page keeps working.
- Below 720 px the pages switch to one column, and the map and the timeline open in their list views.
- Times show in your own time zone, and the pages name it.

The demo estate has no deployments and no planned changes. To see the timeline fill, record a deployment through the
record endpoint with the `pipeline` persona (the example in the adapter's README), or ask an AI client connected as a
persona to save a change request draft with a window.

## The web app against a tenant

The product's host serves the web app under `/app/` on the API's own origin and writes `/app/config.json` from its
settings (`Product:WebApp` in the Azure adapter), with no secret in it. The Azure template builds the pages before it
packages the app, and the client's administrator registers the web app as a single-page application
(`src/adapters/azure/infra/README.md`, "4. The web app"). The configuration the page reads:

```
{
  "schemaVersion": 1,
  "apiBase": "/api",
  "auth": {
    "mode": "entra",
    "authority": "https://login.microsoftonline.com/<tenant ID>",
    "clientId": "<the web app registration's client ID>",
    "apiScope": "api://<the API registration's app ID>/api.access"
  }
}
```

- **Sign-in.** MSAL for the browser (`@azure/msal-browser`) with a single-page application registration: the
  authorization code flow with PKCE and a full-page redirect. The redirect URI is exactly `https://<host>/app/`. The
  cache is the tab's `sessionStorage`. Each API call asks `acquireTokenSilent` first and falls back to
  `acquireTokenRedirect`; a 401 from the API starts sign-in again. MSAL Browser 5 needs its redirect bridge at the
  redirect URI ([Migrate from MSAL Browser v4 to v5](https://learn.microsoft.com/entra/msal/javascript/browser/v4-migration)):
  the page runs it when the address carries a sign-in response, before anything else, and then stops. Microsoft's
  guidance is a dedicated redirect page that runs only the bridge script
  ([Sign in users](https://learn.microsoft.com/entra/msal/javascript/browser/login-user#redirecturi-considerations)).
  This page differs from that, and the difference is not checked against a real tenant yet.
- **Tokens.** The page asks for one scope, `api.access`. The bearer token goes only to `apiBase`, which is the
  page's own origin by default; the page sends no cookies. The API validates the token and never forwards it.
- **Roles.** Product roles come from Entra app roles assigned to groups, through the configuration's Role Group
  Bindings. The page shows the signed-in person's roles and the group that grants each (`GET /api/me`).
- **Checks before anything runs.** The page refuses a `config.json` with an authority that is not https, a
  cross-origin API base that is not https, a client ID that is not a GUID, or the development sign-in on a host name
  that is not loopback, and says why on the page.
- **Hosting notes.** `index.html` and `config.json` must not be cached; the files under `assets/` carry a content
  hash and can be cached for a year. The Content-Security-Policy needs `script-src 'self'`, `style-src 'self'
  'unsafe-inline'` (Radix writes a style tag when a dialog or popover locks page scrolling), and `connect-src` and `frame-src` for the sign-in host.
  The pages under `/app/` must not send a `Cross-Origin-Opener-Policy` header, because MSAL's redirect bridge does
  not work with one
  ([Set up the redirect bridge page in MSAL Browser](https://learn.microsoft.com/entra/msal/javascript/browser/redirect-bridge)).

## How the pages get data

Pages call tools by their document 24 names through one `ToolClient` (`harness/src/data/toolClient.ts`):

- the harness answers from the fixtures (`fixtureClient.ts`). All names, people, teams and URLs in the fixtures are
  made up; URLs use `example.org`;
- the web app calls `POST /api/<tool>` with the tool's input as the JSON body (`apiClient.ts`). The body of every
  answer is the tool's envelope, on every status. A timeout (30 seconds) or a network fault becomes an
  `upstream_unavailable` envelope, so the panel shows it like any other failure;
- on binding A a generative page would call the custom API `<prefix>_<ToolName>`.

The web app also reads `GET /api/me` (the signed-in caller and roles) and `GET /api/info` (engine version, MCP
endpoint, configuration in force). It reads the same tools an AI client reads, so a page and an AI client see the
same answers.

## Not in this release

- **No approval answer page.** People answer approval cards in Teams or Outlook through the approval runner. A page
  where a person answers in the product itself needs its own design first: a page spec, a guarded engine path for a
  person's answer on their own approval record, a matching database rule, and a stronger sign-in step. It is a
  proposal for the next design pass.
- **No writes.** The pages are read-only by design. "Propose" hands a request to an AI client.
- Lists other than applications (owner gaps, overdue reviews, documents, old drafts) open a dialog with the request
  for an AI client instead of a list.
