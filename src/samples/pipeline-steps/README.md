# Pipeline record step samples

Copy-paste samples that record a deployment in the product from the client's own pipeline: an Azure DevOps step,
a GitHub Actions step and a Power Platform Pipelines extension flow. The product builds, hosts and runs no
pipeline (D-19). Clients copy these samples into their own pipelines.

Each sample calls the record endpoint with the contract in `mcp/contracts/record-endpoint.json` and
`mcp/contracts/record-endpoint/deployment.request.schema.json`. Pipelines do not use MCP: no model chooses
tools in a pipeline, so the record step calls one plain HTTPS endpoint.

**Record steps only. Check steps wait for D-03.** A check step would ask before the deploy whether the change
is approved (`check_change_approval`, app role `GateCheck.Ask`) and could stop it at Advise or Enforce. The
change gate decision (D-03) is still open, so no check step sample exists yet, and nothing here ever stops a
deployment.

## Files

| Path | What |
|---|---|
| `record-step/record-step.mjs` | The shared record step script. Node 20 or later, no packages. The Azure DevOps and GitHub samples run it |
| `azure-devops/` | `record-step.yml` (a steps template with one AzureCLI@2 task), setup notes, fixtures |
| `github-actions/` | `record-deployment.yml` (an example workflow with the record step), setup notes, fixtures |
| `power-platform-pipelines/` | The extension flow, step by step (`README.md`), `flow-template.json` (the two Compose inputs), a fixture |
| `stub/server.mjs` | A local stub of the record endpoint that validates every request against the contract schemas |
| `test/record-steps.test.mjs` | The local test: posts each sample's fixture body to the stub, then checks fail soft, retries, both branches, auth and where the token may go |

## The rules every sample keeps

**Fails soft.** A failed record never fails the release. The script always exits 0 and writes a warning in the
platform's own format; the YAML steps also run with `condition: always()` or `if: always()` and with
`continueOnError` or `continue-on-error`, and the flow catches its own failure. The record step never asks
whether a deployment may go ahead.

**Idempotent.** Every body carries `idempotencyKey`, the upsert key of the deployment record. The key is unique
per deployment and the same for every retry of that deployment:

| Sample | Key |
|---|---|
| Azure DevOps | `ado-<collection ID>-<build ID>-<stage>-<job>-<job attempt>` |
| GitHub Actions | `gh-<repository ID>-<run ID>-<job>-<run attempt>` |
| Power Platform Pipelines | `pp-pipelines-<stage run ID>` |

A retry sends the same bytes with the same key, and the endpoint answers 200 with `created: false`. The same key
with a different body is refused (409), which shows a key that is not unique enough. The script retries only
where `mcp/contracts/errors.json` says a retry may help (`rate_limited`, `upstream_unavailable`) and on transport
faults, honoring `Retry-After`. It never retries `configuration_unavailable`: an admin must fix the
configuration first.

**No secret.** Workload identity federation only:
- Azure DevOps: an Azure Resource Manager service connection of type workload identity federation, with the
  Microsoft Entra issuer. The AzureCLI@2 task signs in with it; the script asks the Azure CLI for a token.
- GitHub Actions: the job's OIDC token (`id-token: write`), exchanged with Microsoft Entra ID through a
  federated credential on the app registration.
- Power Platform Pipelines: a managed identity or federated credential where the tenant offers one for cloud
  flows (UNVERIFIED, see that sample).

Never a client secret, a certificate in a connection, a personal access token, or a key in the URL. The script
refuses a URL that carries a key (`code`, `sig` and similar) or plain http to anything but this machine, and it
never sends a client secret: the stub refuses any token request that carries one, and the test proves none is
sent. The token is masked in the log (`task.setsecret` or `add-mask`) and never printed otherwise.

**The token goes only to the record endpoint.** The script sends the bearer token to one origin only: the scheme,
host and port of `RECORD_ENDPOINT_URL`. That one origin is the whole allowlist. It must be https; the one exception,
as for every URL here, is plain http to this machine (`127.0.0.1`, `localhost`, `[::1]`) for the local stub. Every
send is checked against the allowlist before a byte goes out, so another host, another port, another scheme or a
look-alike host name (`record.example.org.elsewhere.invalid`) never gets the token. The script follows no redirect:
a 3xx answer is reported as a warning that names where it pointed, is not retried, and sends neither the token nor
the body there. Set `RECORD_ENDPOINT_URL` to the endpoint's own final URL.

This matters most for `RECORD_AUTH=token`, because that token comes from outside the script. Use it for a
short-lived token another step obtained without a secret, or for the local test. Never store a long-lived token
for it; in a pipeline, use `azure-cli` or `github-oidc`.

A WORKING DEFAULT marker names an
[open design question](../../../docs/architecture/README.md#open-design-questions). The samples follow the working
default until the question is decided.

WORKING DEFAULT (open design question 12). Each pipeline's workload identity holds the deployment recorder role
directly: the `Deployment.Record` app role assigned to its service principal (Azure binding), or the deployment
recorder role on a Dataverse application user (Dataverse binding). The platform admin records each one as a
workload grant (C42) with a reason, the deciding decision and an expiry. People still get roles only through
groups, and agent identities never get a grant.

WORKING DEFAULT (open design question 7). `codeApprovalKind` and `reportedApprovals` are evidence only. An
AI-only code approval never counts as authorization, and the shipped gate default flags it.

## The transport is one variable

`RECORD_ENDPOINT_URL` is the only setting that differs between the two platform branches. The token scope
follows from it (the URL's origin plus `/.default`) unless `RECORD_ENDPOINT_SCOPE` says otherwise.

| | Azure binding | Dataverse binding |
|---|---|---|
| `RECORD_ENDPOINT_URL` | `https://<product host>/api/deployments` | `https://<org>.crm.dynamics.com/api/data/v9.2/<prefix>_RecordDeployment` |
| Token scope | `https://<product host>/.default` (the API's app ID URI; set `RECORD_ENDPOINT_SCOPE` if it differs, for example `api://<application ID>/.default`) | `https://<org>.crm.dynamics.com/.default` |
| Who holds the role | The pipeline's service principal, with the `Deployment.Record` app role | An application user with the deployment recorder security role |
| Request body | The contract body | The same contract body (`record-endpoint.json` names one request schema for both). See open point 1 |
| Extra headers | `Idempotency-Key` (optional; must equal `idempotencyKey`) | The same, plus `OData-MaxVersion: 4.0` and `OData-Version: 4.0`, which the script adds when the URL is a Web API path |
| Success answer | 201 new, 200 retry, with the response envelope | 200, with `resultJson` (the envelope's `data` as text), `recordId` and `statusMeaning` (document 24, section 9.1). The script reads both |
| Error answer | The failure envelope with a fixed error code; HTTP status from `errors.json` | The Web API's error object; the script reads `error.message` from either |

Corrections (`/api/deployments/corrections` or `<prefix>_RecordDeploymentCorrection`) are not a pipeline step.
The stub serves them so the contract is tested end to end.

## Variables the script reads

All are plain settings, none is a secret. Empty, or an Azure Pipelines macro left as `$(NAME)`, means not set.

| Variable | Body field or use |
|---|---|
| `RECORD_ENDPOINT_URL` | The transport (required) |
| `RECORD_ENDPOINT_SCOPE` | Token scope override |
| `RECORD_AUTH` | `azure-cli`, `github-oidc` or `token` (default: from the platform) |
| `RECORD_TENANT_ID`, `RECORD_CLIENT_ID` | `github-oidc` only |
| `RECORD_ACCESS_TOKEN` | `token` only: a short-lived token another step obtained without a secret (the local test uses it). Sent only to the origin of `RECORD_ENDPOINT_URL`, over https, and never along a redirect |
| `RECORD_AUTHORITY_HOST` | Default `https://login.microsoftonline.com` |
| `RECORD_TIMEOUT_SECONDS`, `RECORD_MAX_ATTEMPTS` | Per call 1 to 120 s (default 20); 1 to 5 attempts (default 3) |
| `RECORD_BODY_FILE` | A JSON file with the whole body, instead of the field variables |
| `RECORD_IDEMPOTENCY_KEY` | `idempotencyKey` (required) |
| `RECORD_ARTIFACT_NAME`, `RECORD_ARTIFACT_VERSION` | `artifactName`, `artifactVersion` (required) |
| `RECORD_RESULT` | `resultMeaning` (required). Contract words, or platform words: Succeeded, success, SucceededWithIssues, Failed, failure, Canceled, cancelled, skipped |
| `RECORD_TARGET_EXTERNAL_ID` or `RECORD_DEPLOYMENT_TARGET_ID` | `targetExternalId` or `deploymentTargetId` (exactly one) |
| `RECORD_FINISHED_ON`, `RECORD_STARTED_ON` | `finishedOn` (default: now, UTC), `startedOn` |
| `RECORD_PIPELINE_EXTERNAL_ID` or `RECORD_PIPELINE_REFERENCE_ID` | `pipelineExternalId` or `pipelineReferenceId` (at most one) |
| `RECORD_BUSINESS_APPLICATION_ID`, `RECORD_CHANGE_REQUEST_ID` | `businessApplicationId`, `changeRequestId` |
| `RECORD_ARTIFACT_KIND_KEY`, `RECORD_DIGEST` | `artifactKindKey`, `digest` |
| `RECORD_SOURCE_COMMIT`, `RECORD_SOURCE_REPOSITORY_URL`, `RECORD_RUN_URL` | `sourceCommit`, `sourceRepositoryUrl`, `runUrl` |
| `RECORD_VERSION_BEFORE`, `RECORD_VERSION_AFTER` | `versionBefore`, `versionAfter` |
| `RECORD_STAGE_ORDER_RUN` | `stageOrderRun`, comma separated |
| `RECORD_CODE_APPROVAL_KIND`, `RECORD_REPORTED_APPROVALS_JSON` | `codeApprovalKind`, `reportedApprovals` (a JSON array). Evidence only |
| `RECORD_DEPLOYED_BY_OBJECT_ID`, `RECORD_MERGE_ACTOR_OBJECT_ID`, `RECORD_MERGE_ACTOR_KIND` | `deployedByObjectId`, `mergeActorObjectId`, `mergeActorKind` |
| `RECORD_STARTED_BY_AI_CLIENT` | `startedByAiClient` |

## Run the local test

No sign-in, no tenant, no network beyond 127.0.0.1. From `src/`, after `npm ci` in `src/tools` (the stub uses the
pinned ajv there; no new package):

```
node samples/pipeline-steps/test/record-steps.test.mjs
```

`node tools/ci.mjs` runs it as the `samples` check. To try a step by hand, start the stub and point the script at it:

```
node samples/pipeline-steps/stub/server.mjs --port 7071
RECORD_ENDPOINT_URL=http://127.0.0.1:7071/api/deployments RECORD_AUTH=token \
  RECORD_ACCESS_TOKEN=stub-access-token-not-real RECORD_BODY_FILE=samples/pipeline-steps/power-platform-pipelines/fixtures/deployment.json \
  node samples/pipeline-steps/record-step/record-step.mjs
```

The test replaces the Azure CLI with `test/fake-az.mjs` on a PATH that holds no real Azure CLI, and fakes
GitHub's OIDC request URL and the Microsoft Entra token endpoint in the stub. The stub runs no engine: its route,
flags and gate level are fixed values. Its answers are validated against the response schemas, so a contract
change that the stub does not follow fails the test.

## What is verified and what is not

Verified locally: each sample's body against the contract schema, the script's behavior against the stub on
both URL shapes (fail soft, retries, idempotency, 400, 401, 409, 429, 503, no endpoint, no token, no secret),
that each YAML sample sets only variables the script reads, and that the token goes only to the origin of
`RECORD_ENDPOINT_URL`: the allowlist refuses another host, port or scheme, a look-alike host name and a user name
in the URL, and a 301, 302, 303, 307 or 308 answer is not followed, so a second listener standing in for another
host receives nothing.

Not verified (nothing here ran in Azure DevOps, GitHub or a pipelines host): the AzureCLI@2 sign-in of an identity
with no Azure role, the GitHub token exchange against the real Microsoft Entra ID, the Dataverse custom API's
parameter binding (open point 1), and the whole Power Platform Pipelines flow (see its README).

## Open points

1. **The Dataverse custom API's parameters.** `record-endpoint.json` gives `<prefix>_RecordDeployment` the same
   request schema as `/api/deployments`, so the samples send the same body on both branches. Document 25,
   section 11 lists the custom API's parameters with other names (`startedOnUtc`, `finishedOnUtc`, `resultKey`,
   `routeKey`) and without `stageOrderRun` or `reportedApprovals`; document 24, section 9.1 says nested inputs
   travel as one `payloadJson` string. Until the Dataverse binding settles this, the Dataverse branch of these
   samples is UNVERIFIED. If it lands on `payloadJson`, only the script's body wrapping changes, not the variable.
2. **`validateOnly` on the record endpoint.** `record-endpoint.json` says a 200 answer can mean "validateOnly was
   true", but neither request schema allows `validateOnly`. Either add it to both request schemas or drop the
   words. The samples never send it.
3. **Secretless calls from a cloud flow** (Power Platform Pipelines sample). If no managed identity or federated
   credential works for the HTTP action or the Dataverse connector, that sample cannot meet the no-secret rule
   and should be withdrawn rather than given a secret.
