# The scripted walkthrough (fictional data)

A story in eight steps that drives the product's MCP server core exactly as an MCP client such as Claude Code would:
`initialize`, `tools/list`, then `tools/call` with JSON arguments, with the Balanced preset in force. It records every
request and response envelope, and the reason codes, in `transcript.json`. `render-page.mjs` turns the transcript into
a local page, by default `docs/design/step2/demo-transcript.html` (outside `src/`, next to the step 2 design page
whose styles it reuses). Neither HTML page is in the public repository, so there pass `--css-from` (any HTML page
with a `<style>` block) and `--out`.

It is a design walk, not a product: offline, the in-memory store, the mock sign-in, no network listener, no tenant.
Every person, app, team, ID and URL is fictional (the names of the page harness fixtures, `ui/harness/fixtures`).
For Claude Code against a running server, see [`../../docs/mcp.md`](../../docs/mcp.md); for when to regenerate the
transcript, [`../../docs/testing.md`](../../docs/testing.md).

## Where the runner lives, and why

The runner is a test: `mcp/tests/Product.Mcp.Core.Tests/Walkthrough/` (`WalkthroughWorld.cs` holds the world and the
recorder, `WalkthroughTests.cs` the story). It is not a separate demo project because:

- The mock identity (`Product.Mcp.Testing`) and the in-memory store (`Product.Engine.Testing`) may be referenced only by
  test projects (`DependencyRuleTests.OnlyTestProjectsReferenceTheTestingAssembly` and `RuntimeDependencyRuleTests`). A
  demo project would need that rule widened, which is a review decision.
- Step 4 maps an engine refusal to its fixed MCP error code with the server's own map (`EngineErrorMap`, internal to
  `Product.Mcp.Core` and visible to this test project only).
- It runs inside the `test` step of `tools/ci.mjs` (about one second), so the story's outcomes are checked on every run.

## What the test checks

- Every outcome the page states: each call's `ok`, its fixed error code and its reason codes, the statuses and flags the
  steps name (the change authorization accepted only after the business owner's answer, AI writes refused with
  `ai_cannot_do_this`, the store unchanged by every refused attempt but for the audit entry that records the call the server refused, the deployment flags, one claim and no inventory
  copy, the owners and the dependants).
- That the checked-in `transcript.json` still holds the same outcome digest (`ok`, error code and reason codes per
  exchange) as a fresh run. If the product's behavior changes, the test fails and names the first exchange that differs.
  Wording changes (messages, summaries) do not fail it; regenerate when they matter.

## Regenerate the transcript and the page

From `src/`, after a build:

```
WALKTHROUGH_WRITE=1 dotnet test --project mcp/tests/Product.Mcp.Core.Tests -c Release --no-build
node demo/walkthrough/render-page.mjs
```

On Windows PowerShell set the variable with `$env:WALKTHROUGH_WRITE = "1"` first. The test then writes
`demo/walkthrough/transcript.json` instead of comparing with it. `render-page.mjs --check` writes nothing and exits 1
when the page differs from a fresh render.

## What the walk does not show, on purpose

- **The triage, tier and ownership decisions are not answered.** Their update-subject effect (built 2026-11-24,
  document 25, B25-8) writes the registration status, the tier and the Owner Assignment rows when they are accepted.
  The walk answers only the go-live card, so after the AI saves the application as a draft with proposed owners, the
  runner writes that end state as test setup, and the transcript marks it as a seed, not an engine write. Step 5 shows
  the effect for real: the accepted change authorization moves the change request to in progress.
- **The record endpoint's HTTP route is not called.** The Azure host serves it (`POST /api/deployments`, app role
  `Deployment.Record` and a Workload Grant; `EngineRefusalTests.TheRecordEndpointRequiresTheWorkloadRoleAndAGrant` in
  the host tests). The walk has no network listener, so step 6 validates the pipeline's body against
  `mcp/contracts/record-endpoint/deployment.request.schema.json` and calls the engine entry the host calls
  (`EngineRuntime.RecordDeployment`, `<prefix>_RecordDeployment`) as the pipeline's workload identity. The host tests
  replay the whole walk over HTTP, step 6 through that route
  (`WalkthroughReplayTests.TheWalkthroughOverRawHttpMatchesTheInProcessRun`).
- **The approval runner is not part of the walk.** Step 5 makes the write the runner makes (the approval writer path)
  with the business owner's answer.
