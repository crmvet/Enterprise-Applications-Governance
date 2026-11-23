# Record step: Power Platform Pipelines extension flow

A step description for a cloud flow that records each completed Power Platform Pipelines deployment.
It is a sample you build in your own pipelines host. The product builds, hosts and runs no pipeline and no flow.
Read the shared rules first: [../README.md](../README.md).

This flow cannot run on a developer machine, so the local test only posts its fixture body
(`fixtures/deployment.json`) to the stub and checks that `flow-template.json` builds the same fields.

## Before you start

- **A custom pipelines host.** Microsoft says personal pipelines, which include every platform host pipeline,
  can't be extended. Extension events need a custom host with a pipeline an admin built.
- **A solution in the host** to hold the flow and one environment variable.
- **An identity with no stored secret** for the HTTP call (step 5). If your tenant has none, do not build this
  flow (see "If there is no secretless option").

## The flow, step by step

1. **Create** a solution-aware cloud flow in the custom host environment. Add an environment variable of type
   Text whose display name is `RECORD_ENDPOINT_URL`, and set it per host:
   - Azure binding: `https://<your product host>/api/deployments`
   - Dataverse binding (a possible later edition, not planned; D-26): `https://<your org>.crm.dynamics.com/api/data/v9.2/<prefix>_RecordDeployment`

2. **Trigger.** Microsoft Dataverse, "When an action is performed". Catalog: Microsoft Dataverse Common.
   Category: Power Platform Pipelines. Table name: (none). Action name: `OnDeploymentCompleted`.
   This event fires after the deployment, so nothing the flow does can hold or fail the deployment.
   Do not use a gated extension (such as the pre-deployment step) for recording: a gated step waits for the
   flow. Asking before a deploy is a check step's job, and check steps wait for the change gate decision.

3. **Compose, named `Stage run facts`.** Paste `stageRunFacts` from `flow-template.json` as its input. Replace
   each `PICK` value with the matching output of the trigger, from the dynamic content list. This is the only
   step where you pick values.

4. **Compose, named `Record body`.** Paste `recordBody` from `flow-template.json` as its input, unchanged. Its
   expressions read `Stage run facts` and build the record endpoint body:

   | Body field | Built from | Notes |
   |---|---|---|
   | `idempotencyKey` | `pp-pipelines-` plus the stage run ID | One stage run is one deployment. A resubmitted flow run sends the same key, so the endpoint returns the first record and writes nothing new |
   | `artifactName`, `artifactVersion` | The artifact (solution) name and version | |
   | `artifactKindKey` | `power-platform-solution` | A key from the product's artifact kind list |
   | `targetExternalId` | The target environment ID | Must match the deployment target's external ID in the product |
   | `startedOn`, `finishedOn` | The stage run's times, in UTC with a Z | Without an end time the flow uses the time it runs |
   | `resultMeaning` | The stage run's result | `succeeded` and `failed` map one to one; anything else becomes `not-deployed`. Check the words your host sends |
   | `pipelineExternalId` | `pp-pipelines-` plus the deployment pipeline ID | Must match the pipeline reference's external ID. An unknown one is created as not mapped |

5. **HTTP, inside a Scope named `Record`.**
   - Method `POST`. URI: the `RECORD_ENDPOINT_URL` environment variable.
   - Headers: `Content-Type: application/json`, `Accept: application/json`, and
     `Idempotency-Key: @{outputs('Record_body')?['idempotencyKey']}`. On the Dataverse binding (a possible later edition, not planned; D-26)
     also `OData-MaxVersion: 4.0` and `OData-Version: 4.0`.
   - Body: `@outputs('Record_body')` (the whole object, not text).
   - Authentication: an app-only token for the record endpoint's audience (the URL's origin, or the app ID URI
     of the record endpoint), from an identity with no stored secret. See "Authentication" below.
   - Settings: timeout `PT1M`; retry policy exponential, count 3. The action retries 408, 429 and 5xx answers
     with the same body, so with the same idempotency key.

6. **Fail soft: a Scope named `Warn`,** configured to run after `Record` has failed, has timed out or was
   skipped. In it, post a message to the platform admin channel (or write a Compose) with the status code and
   the error message from the HTTP action. End the branch with Terminate, status Succeeded. A record that
   could not be written never fails anything: the deployment finished before the flow started.

## Authentication

WORKING DEFAULT (open design question 12). The flow's identity holds the deployment recorder role directly:
the `Deployment.Record` app role on its service principal (Azure binding), or the deployment recorder role on a
Dataverse application user (Dataverse binding, a possible later edition, not planned; D-26). Each such identity is recorded as a workload grant with a
reason, the deciding decision and an expiry.

What the record endpoint accepts: an app-only token from that identity, obtained without a stored secret
(workload identity federation or a managed identity).

What it never accepts, and what this flow must never use:
- a client secret or a certificate stored in a connection or in the flow;
- a personal access token or a key in the URL;
- a person's own connection: a delegated user token is refused (403). People record deployments through the
  product's page or their AI tool instead.

UNVERIFIED (2026-11-22): whether a cloud flow's HTTP action, or the Dataverse connector, can call with a
managed identity or a federated credential in your tenant. Check Microsoft Learn before you build. Nothing in
this folder has been run against a real host.

### If there is no secretless option

Do not fall back to a secret. Leave this flow out, and either record from the Azure DevOps or GitHub Actions
step of the pipeline that ran (if one did), or let the deployer record the deployment through the product's
page or AI tool. The record then arrives on another route, and the gate level decides whether it is flagged.

## What is not verified

- The output names of `OnDeploymentCompleted` and the stage run's status words. The flow template keeps them in
  one Compose (`Stage run facts`) so you set them once.
- Whether a later retry of a failed deployment in the host reuses the stage run ID. If it does, the second
  completion gets the first record back (200, created false) instead of a new record.
- A run link for the host's run page: the template sends none.
- Secretless authentication from a cloud flow (above).
