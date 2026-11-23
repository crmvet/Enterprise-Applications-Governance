# ui

The four page designs: Overview, Application 360, the dependency map and the timeline.
React and Fluent UI v9, the Academy palette, light only. They render in a local harness from fictional fixture JSON
shaped by the MCP tool output schemas (`src/mcp/contracts/`).

| Path | What |
|---|---|
| `design/pages.md` | The page specs: calls, panels, states, phone layout, fallbacks, working defaults, open points |
| `harness/` | The local harness: Vite, React 18, Fluent UI v9, TypeScript |
| `harness/src/pages/` | The four pages |
| `harness/src/components/` | Panel, status badge, KPI tile, bar list, list row |
| `harness/src/data/` | Contract types, the tool client, the fixture client, labels, meanings, formatting |
| `harness/fixtures/<tool>/<case>.input.json` and `.output.json` | Fictional tool inputs and envelopes. `tools/json-check.mjs` validates both halves against the MCP tool schemas |
| `harness/scripts/check-packages.mjs` | Exact versions, registry sources, licenses, and `THIRD-PARTY.md` against the lock file (the `ui` check of `tools/ci.mjs`) |
| `harness/scripts/screenshots.mjs` | Renders the pages at 1280 px and 390 px with headless Chrome |
| `screenshots/` | `overview`, `application-360`, `dependency-map` and `timeline`, each at `-1280` and `-390` |

## Run it

You need Node 22 or later and Google Chrome. No sign-in, no tenant; the only network use is the npm registry for `npm ci`.

```
cd src/ui/harness
npm ci --ignore-scripts          packages from package-lock.json, no install scripts
npm run build                    typecheck, then the Vite build into dist/
npm run preview                  serves dist/ on http://127.0.0.1:4318/
```

Pages: `#/overview`, `#/app/10000000-0000-4000-8000-000000000001`, `#/map/10000000-0000-4000-8000-000000000001`, `#/timeline`.
Design review switches: `?fail=get_change_schedule,get_application_owners` answers those tools with a failure,
`?slow=1` shows the loading state.

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

## How the pages get data

Pages call tools by their document 24 names through one `ToolClient` (`harness/src/data/toolClient.ts`). On binding A
a generative page would call the custom API `<prefix>_<ToolName>`; on binding C the web app would call the API
endpoint. The harness answers from the fixtures. All names, people, teams and URLs in the fixtures are made up;
URLs use `example.org`.
