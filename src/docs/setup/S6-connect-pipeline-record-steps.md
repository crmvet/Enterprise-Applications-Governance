# Guide S6. Connect pipeline record steps

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run in Azure DevOps, GitHub or a pipelines host. The record step samples are tested against a local stub of the record endpoint, and the record endpoint itself is tested in the local server.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 23, P05; document 24, sections 4.6 and 9.1; document 25, section 12.2; document 26, sections 3.4 and 8; document 28, question 12 and question 7; `src/samples/pipeline-steps/README.md`; `mcp/contracts/record-endpoint.json`.
- **Open design questions that touch this guide:** 7 and 12 (markers below).

## Purpose

The product records deployments from the client's own pipelines and never builds, hosts or runs a pipeline (D-19). Each pipeline gets one small step after its deploy that sends one HTTPS call to the record endpoint, `POST https://<host>/api/deployments`. The step fails soft: a failed record never fails the release, and the step never asks whether a deployment may go ahead (check steps wait for D-03).

This guide gives each pipeline its own workload identity, the app role and the Workload Grant it needs, and the settings of the copy-paste samples in `src/samples/pipeline-steps/`.

## Who

| Who | Part |
|---|---|
| The pipeline's owner | Adds the record step, sets its variables, owns the federated credential |
| The client's Entra admin | Registers each pipeline's identity (or approves the service connection's), assigns the `Deployment.Record` app role |
| The client's configuration admin | Instructs one Workload Grant per pipeline identity as a configuration change; a person accepts it on a card |
| The client's platform admin | Runs `check_role_bindings` and reviews the first records |

## Prerequisites

- The product is deployed (guide [S4](S4-prepare-entra-and-deploy.md)) and a configuration version is in force (guide [S1](S1-pick-presets-and-overlays.md)).
- The applications and deployment targets the pipeline deploys are in the register, so a record can name them (`businessApplicationId`, and `deploymentTargetId` or `targetExternalId`).
- The record endpoint contract: `mcp/contracts/record-endpoint.json` and `mcp/contracts/record-endpoint/deployment.request.schema.json`.

## Steps

1. **One workload identity per pipeline, with no secret.** Workload identity federation only:
   - Azure DevOps: an Azure Resource Manager service connection of type workload identity federation. The sample's AzureCLI@2 task signs in with it, and the script asks the Azure CLI for a token.
   - GitHub Actions: an app registration (or a user-assigned managed identity) with a federated credential that trusts the repository and its environment or branch; the job asks for `id-token: write`.
   - Power Platform Pipelines: a managed identity or federated credential where the tenant offers one for cloud flows. UNVERIFIED; see the sample's README.

   Never a client secret, a certificate in a connection, a personal access token or a key in the URL. One identity per pipeline keeps each pipeline visible and revocable on its own (document 28, question 12, option A).

2. **Assign the `Deployment.Record` app role** of the product's API directly to each pipeline's service principal, as `infra/README.md`, section "3. Pipelines and the approval runner", shows. Never through a group. Note each assignment's ID.

3. **Record one Workload Grant per pipeline identity.** The configuration admin instructs a configuration change that upserts a `workload-grant` row for each: `principalObjectId` (the pipeline's service principal object ID), `workloadRoleKey` `deployment-recorder`, a `reason`, `usesFederatedCredential` true, `bindingDetail` (the assignment ID from step 2) and `expiresOn` (no more than `workload-grant-max-days`, default 365, ahead). A person accepts it on a card. Without a current grant the engine refuses the pipeline, even with the app role. WORKING DEFAULT (open design question 12)

4. **Copy the sample** for the platform (`azure-devops/`, `github-actions/` or `power-platform-pipelines/`) into the pipeline, after the deploy step, and set:

   | Variable | Value |
   |---|---|
   | `RECORD_ENDPOINT_URL` | `https://<host>/api/deployments`, the endpoint's own final URL (the script follows no redirect) |
   | `RECORD_ENDPOINT_SCOPE` | `<Application ID URI>/.default`, for example `https://<host>/mcp/.default`, or `api://<api-client-id>/.default` before the custom domain is bound. Set it explicitly: the script's default, the URL's origin plus `/.default`, does not match the API's Application ID URI (open point 1) |
   | `RECORD_AUTH` | `azure-cli` (Azure DevOps) or `github-oidc` (GitHub; with `RECORD_TENANT_ID` and `RECORD_CLIENT_ID`) |
   | `RECORD_IDEMPOTENCY_KEY` | The sample's key: unique per deployment and the same for every retry of it |
   | The body variables | `RECORD_ARTIFACT_NAME`, `RECORD_ARTIFACT_VERSION`, `RECORD_RESULT`, the target and the application, as the sample's README lists |

   `codeApprovalKind` and `reportedApprovals` are evidence only. An AI-only code approval never counts as authorization, and the shipped gate default flags it. WORKING DEFAULT (open design question 7)

5. **Run the pipeline once** and look at the step's output: a warning names the cause when the record fails, and the release goes on either way.

## Checks

- The first record answers 201 with `created` true; a rerun of the same deployment answers 200 with `created` false and the same record. The same key with a different body answers 409, which shows a key that is not unique enough.
- The record endpoint's answer shows route `pipeline-step` and performed by actor kind `pipeline`, and `get_deployments` lists the record.
- `check_role_bindings` lists each pipeline's grant with `assignmentFound` true, and reports no service principal that holds `Deployment.Record` without a current grant.
- A pipeline identity with the app role but no grant is refused with 403 `not_authorized`, and nothing is written. So is a person's token, the approval runner's token and an agent identity's token.
- The pipeline log shows no token: the script masks it, and sends it only to the origin of `RECORD_ENDPOINT_URL`.

## Open points

1. **The samples' default token scope.** The script derives the scope from the endpoint URL's origin (`https://<host>/.default`), while the API's Application ID URI is `https://<host>/mcp` (or `api://<api-client-id>`). With the default, the token request fails and the step warns. Until the samples change, step 4 sets `RECORD_ENDPOINT_SCOPE` explicitly. Proposal: the samples' README names the Application ID URI as the scope for the Azure binding.
2. **Check steps** (asking before a deploy whether the change is approved) wait for D-03; no sample exists, and nothing here ever stops a deployment.
3. **Power Platform Pipelines.** If no managed identity or federated credential works for the extension flow, that sample cannot meet the no-secret rule and should be withdrawn rather than given a secret (the sample's README, open point 3).
4. **Many pipelines, many grants.** Forty pipelines are forty grants, each renewed within `workload-grant-max-days`. `check_role_bindings` lists them by key, not by Expires On; guide [A3](../admin/A3-workload-grants.md) has the renewal routine.

## Microsoft facts used

| Fact | Source | Checked |
|---|---|---|
| "Currently, if you add a service principal to a group, and then assign an app role to that group, Microsoft Entra ID doesn't add the `roles` claim to tokens it issues."; "App roles can also be assigned to the service principal for another application, or to the service principal for a managed identity." | https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps | 2026-11-23 |
| A federated identity credential on an app registration trusts a GitHub Actions workflow by organization, repository and entity type (environment, branch, pull request or tag), with no secret | https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust | 2026-11-23 |
| Workload identity federation lets a workload outside Azure get tokens for an app registration or a user-assigned managed identity without managing secrets | https://learn.microsoft.com/entra/workload-id/workload-identity-federation | 2026-11-23 |
