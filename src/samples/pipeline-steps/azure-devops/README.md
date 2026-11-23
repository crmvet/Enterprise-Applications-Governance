# Record step: Azure DevOps

A steps template that records each deployment, at the end of the job that deploys. A copy-paste sample.
Read the shared rules first: [../README.md](../README.md).

| File | What |
|---|---|
| `record-step.yml` | The steps template: one AzureCLI@2 task that runs `record-step.mjs` |
| `../record-step/record-step.mjs` | The script. Copy it into your repository, for example as `pipelines/record-step.mjs` |
| `fixtures/env.json`, `fixtures/deployment.json` | What the template hands the script for one fictional run, and the body the stub must receive. Used by the local test |

## Set up once

1. **An app registration for the pipeline**, one per pipeline or per deploying project, with no client secret
   and no certificate.
2. **A service connection.** Azure Resource Manager, workload identity federation, created manually against
   that app registration. New connections use the Microsoft Entra issuer
   (`https://login.microsoftonline.com/<tenant ID>/v2.0`); copy the issuer and the subject the service
   connection shows into a federated credential on the app registration. The identity needs no Azure role for
   this step.
3. **The role.** WORKING DEFAULT (open design question 12).
   - Azure binding: assign the record endpoint's `Deployment.Record` app role directly to the pipeline's service
     principal. A group's app roles do not reach a service principal's token.
   - Dataverse binding: add the app registration as an application user in the product's environment, with the
     deployment recorder security role.
   - Either way, the platform admin records a workload grant for the identity: a reason, the deciding decision
     and an expiry. Without a current grant the endpoint answers 403, and the step warns.
4. **Variables** (a pipeline variable or a variable group; none of them is a secret):
   - `RECORD_ENDPOINT_URL`: `https://<your product host>/api/deployments` (Azure binding) or
     `https://<your org>.crm.dynamics.com/api/data/v9.2/<prefix>_RecordDeployment` (Dataverse binding).
   - `RECORD_ENDPOINT_SCOPE`, only when the record endpoint's app ID URI is not its origin.
5. **The product side:** the deployment target's external ID (`targetExternalId`) and the pipeline reference's
   external ID (`ado-<collection ID>-<definition ID>`) should exist in the product. Unknown ones still record;
   the pipeline is created as not mapped for the platform admin.

## Use it

Copy `record-step.yml` and `record-step.mjs` into your repository, then add the template after your deploy step:

```yaml
jobs:
  - deployment: deploy_prod
    environment: production
    strategy:
      runOnce:
        deploy:
          steps:
            - checkout: self # deployment jobs do not check out by default; the script lives in the repository
            - script: echo "your existing deploy step"
              displayName: Deploy
            - template: pipelines/record-step.yml
              parameters:
                serviceConnection: record-endpoint-federated
                artifactName: FieldVisitPlanner
                artifactVersion: $(Build.BuildNumber)
                targetExternalId: env-prod-0001
                artifactKindKey: power-platform-solution
                stageOrderRun: build,test,production
```

Hosted agents have Node and the Azure CLI. On a self-hosted agent, install Node 20 or later and the Azure CLI,
or add a `UseNode@1` task before the template.

## What the template sends

| Body field | From | Notes |
|---|---|---|
| `idempotencyKey` | `ado-<collection ID>-<build ID>-<stage>-<job>-<job attempt>` | Unique per deployment; the same for every retry inside the step |
| `artifactName`, `artifactVersion`, `artifactKindKey` | Template parameters | |
| `targetExternalId` | Template parameter | Exactly one target: the external ID, or `deploymentTargetId` in your own copy |
| `sourceCommit`, `sourceRepositoryUrl` | `Build.SourceVersion`, `Build.Repository.Uri` | |
| `pipelineExternalId` | `ado-<collection ID>-<definition ID>` | |
| `runUrl` | `System.CollectionUri`, `System.TeamProjectId` and `Build.BuildId` | |
| `startedOn`, `finishedOn` | `RECORD_STARTED_ON`, `RECORD_FINISHED_ON` if your deploy step sets them | Without `RECORD_FINISHED_ON` the script uses the time it runs |
| `resultMeaning` | `Agent.JobStatus` | Succeeded is succeeded, SucceededWithIssues is partly-succeeded, Failed is failed, Canceled is not-deployed |
| `stageOrderRun`, `businessApplicationId`, `changeRequestId` | Template parameters, optional | |
| `codeApprovalKind` | Template parameter, default `unknown` | Evidence only. WORKING DEFAULT (open design question 7) |

A variable nobody defined stays as `$(NAME)` in Azure Pipelines; the script treats that as not set.

## Output

The task sets the output variable `deploymentRecordId` when the record was written or matched an earlier call.
