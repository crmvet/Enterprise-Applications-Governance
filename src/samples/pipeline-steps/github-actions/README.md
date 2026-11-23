# Record step: GitHub Actions

A workflow step that records each deployment, at the end of the job that deploys. A copy-paste sample.
Read the shared rules first: [../README.md](../README.md).

| File | What |
|---|---|
| `record-deployment.yml` | An example workflow: a deploy job whose last step records the deployment |
| `../record-step/record-step.mjs` | The script. Copy it into your repository, for example as `.github/scripts/record-step.mjs` |
| `fixtures/env.json`, `fixtures/deployment.json` | What the step hands the script for one fictional run, and the body the stub must receive. Used by the local test |

## Set up once

1. **An app registration for the workflow,** with no client secret and no certificate.
2. **A federated credential** on it: issuer `https://token.actions.githubusercontent.com`, audience
   `api://AzureADTokenExchange`, and a subject that names the repository and the environment, for example
   `repo:<owner>/<repository>:environment:production`. Only jobs in that environment can then get a token.
3. **The role.** WORKING DEFAULT (open design question 12).
   - Azure binding: assign the record endpoint's `Deployment.Record` app role directly to the workflow's service
     principal. A group's app roles do not reach a service principal's token.
   - Dataverse binding: add the app registration as an application user in the product's environment, with the
     deployment recorder security role.
   - Either way, the platform admin records a workload grant for the identity: a reason, the deciding decision
     and an expiry. Without a current grant the endpoint answers 403, and the step warns.
4. **Configuration variables** (repository or environment variables; none of them is a secret):
   - `RECORD_ENDPOINT_URL`: `https://<your product host>/api/deployments` (Azure binding) or
     `https://<your org>.crm.dynamics.com/api/data/v9.2/<prefix>_RecordDeployment` (Dataverse binding).
   - `RECORD_TENANT_ID` and `RECORD_CLIENT_ID`: the tenant ID and the app registration's application ID.
   - `RECORD_TARGET_EXTERNAL_ID`, and `RECORD_BUSINESS_APPLICATION_ID` if you know it.
   - `RECORD_ENDPOINT_SCOPE`, only when the record endpoint's app ID URI is not its origin.

## Use it

Copy the step named "Record the deployment" into your deploy job, after the deploy step, and copy
`record-step.mjs` into your repository. The workflow (or the job) needs `permissions: id-token: write`.
The deploy step should have `id: deploy`, so the record step can read its outcome and outputs.

The step uses no third-party action to get the token: the script asks GitHub for the job's OIDC token and
exchanges it with Microsoft Entra ID itself. If your organization prefers `azure/login`, sign in with it (OIDC,
with `allow-no-subscriptions: true` when the identity has no Azure role) and set `RECORD_AUTH: azure-cli`
instead; the rest stays the same.

GitHub-hosted runners have Node. On a self-hosted runner, install Node 20 or later.

## What the step sends

| Body field | From | Notes |
|---|---|---|
| `idempotencyKey` | `gh-<repository ID>-<run ID>-<job>-<run attempt>` | Unique per deployment; the same for every retry inside the step |
| `artifactName`, `artifactVersion`, `artifactKindKey` | The step's env and the deploy step's outputs | |
| `targetExternalId` | `vars.RECORD_TARGET_EXTERNAL_ID` | Exactly one target |
| `sourceCommit`, `sourceRepositoryUrl` | `github.sha`, `github.server_url` and `github.repository` | |
| `pipelineExternalId` | `gh-<repository ID>-<workflow name>` | Must match the pipeline reference's external ID; an unknown one is created as not mapped |
| `runUrl` | The run and attempt link | |
| `startedOn`, `finishedOn` | The deploy step's outputs `started_on` and `finished_on` | Without `finished_on` the script uses the time it runs |
| `resultMeaning` | `steps.deploy.outcome` | success is succeeded, failure is failed, cancelled and skipped are not-deployed |
| `codeApprovalKind` | `unknown` in the sample | Evidence only. WORKING DEFAULT (open design question 7) |

## Output

The step writes `deployment-record-id` to `GITHUB_OUTPUT` when the record was written or matched an earlier
call. Give the step an `id` to read it.
