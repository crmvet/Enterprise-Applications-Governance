# flows: the approval runner as Power Automate flows

The product never approves anything. A person approves, on a Power Automate approval card in Outlook, Teams or the
Power Automate action center, and the engine decides whether that answer counts. This folder holds the template of the
**approval runner**: two solution-aware cloud flows in the client's tenant that carry approval requests from the
product to the people asked, and carry each person's answer back (document 23, S1; working default Q19).

Early work, not a release: nobody has imported this template into a tenant yet, so no card has gone out from it. The
tests check the template's files, and a stand-in plays the runner in the local steps. "Not verified yet" at the end
lists what a first test import must confirm.

| File | What it is |
|---|---|
| `approval-runner/solution.json` | The solution manifest: its name, the six environment variables, the four connection references and the two flows. `{{prefix}}` stands for the client's publisher prefix |
| `approval-runner/workflows/pull-approval-requests.json` | The parent flow. Every five minutes it claims waiting requests from the product's API and starts the child flow once for each |
| `approval-runner/workflows/send-card-and-wait.json` | The child flow. It sends one approval card for one request, reports the card as sent, waits for the first answer and posts it to the product |
| `build-solution.mjs` | Writes the unpacked solution for one prefix, outside the repository, for `pac solution pack`. Node 20 or later, no packages |

Nothing generated is kept here: no solution XML, no zip, no tenant ID, client ID, certificate or URL of a real tenant.
The tests in `../tests/Product.Azure.Tests/FlowTemplateTests.cs` check that.

## How it works

The runner pulls. The product's API keeps an **approval outbox**: when the engine opens an Approval Record, the same
database transaction adds an outbox row, and when the engine writes the record's final outcome the same transaction
settles the row. The runner reads the outbox through three routes and answers through a fourth. All four take only an
app-only token with the `Approval.Write` app role, and the product serves the caller only when it holds a current
Workload Grant for the `approval-writer` role in the configuration in force.

| Step | Flow | Call |
|---|---|---|
| 1. Claim | Parent, every five minutes | `POST /engine/v1/approval-runner/claims` with `{"max": 10, "leaseSeconds": 900}`. The answer lists each leased request: the Approval Record and Decision IDs, a claim ID, the card's title and Markdown details (text a person or an AI client wrote is escaped), an https record link, Requested On and Expires On, whether a typed reason is required, whether it is a consultation, the channel, who to ask, and the configuration keys to answer with |
| 2. Start the child | Parent | One child run per request, up to five at a time |
| 3. Resolve who to ask | Child | For an owner role, the current holders the product lists (users only). For a product role, the members of each Entra group bound to it, through the Office 365 Groups connector, keeping only members whose type is `#microsoft.graph.user`. No one to ask: the child gives the request back (`release`, reason `no-approvers`) |
| 4. Send the card | Child | Approvals, **Create an approval**, type Basic (first to respond), assigned to the resolved users by object ID, reassignment off, with the product's title, details and record link |
| 5. Report it | Child | `POST /engine/v1/approval-runner/items/{id}/sent` with the claim ID, the approval's name as the card reference and the run's link. The child then answers the parent, so the parent's run ends |
| 6. Wait | Child, in parallel with step 5 | Approvals, **Wait for an approval**, at most 27 days |
| 7. Post the answer | Child | `POST /engine/v1/approval-records/{id}/outcome` with the responder's object ID, the outcome key for Approve or Reject from the claim's hints, the actor kind key for a person, Responded On, the comments as the typed reason, whether the responder was among the users asked, and the run's link, with the header `Idempotency-Key: <recordId>:<responderId>:<respondedOn>` |
| 8. Give it back | Child | On a timeout, a failed card or a refused answer other than 409: `POST /engine/v1/approval-runner/items/{id}/release` with a reason from a closed list. The request is pending again with one more attempt, and becomes failed after `Product:ApprovalRunner:MaxAttempts` |

What the product does with the answer:

- The engine counts an answer only from a person who signed in as a user, holds the role the card asks for and passes
  the separation rules. An answer from an AI client, an agent user, an agent identity or any other non-user principal
  is stored as recommendation only. When the responder's Principal Reference records a non-user kind, that kind
  replaces the one the runner sent. When the Office 365 Users lookup of the responder fails, the child sends
  `service-principal`, so the answer is stored but never counts.
- The same answer sent again with the same Idempotency-Key returns the first result and writes nothing. The same key
  with another body is a conflict. Another answer for a record that already holds a final outcome is a conflict (409),
  never a second outcome.
- An answer given at or after the record's Expires On is refused with 409, and the outbox row is settled as expired.
  A request whose record has expired, or whose decision is no longer proposed, is never offered again.

### One card per Approval Record

The engine opens one Approval Record for each required approval of the winning rule group, and each record holds one
answer. So every card is **first to respond**, whatever the rule's Approval Mode says: the first answer is the record's
answer, and a later answer to the same record is refused. Two consequences of how the engine works today:

- A rule with **Minimum Count above 1** (the strict separation overlay asks two change board members to decide an
  exception) gets one record and one card, so it cannot be met: the decision stays proposed. Document 23, S1, step 3
  asks for one record per required response; the engine needs that change before such a rule can be satisfied.
- **Everyone must approve** and **sequential** rules collapse to one answer per record in the same way.

### Pull, not push

Document 23 (S1, bindings) has the API push each request to an HTTP-triggered flow. This template pulls instead: the
API stays inbound-only, with no outgoing credential, no timer and no queue to run. The cost is the poll interval (five
minutes by default) and the Power Platform requests of the polls. This is a proposal for the design pass.

## Before you import

1. **An approvals environment.** A Power Platform environment with Dataverse, used only for the runner. Microsoft
   ([Get started with approvals](https://learn.microsoft.com/power-automate/get-started-approvals)): "When you create
   approval flows, they're saved in Dataverse." The environment then holds the cards' titles and details, not the
   product's records.
2. **The runner's identity.** An app registration with a certificate credential, and on the product's API app
   registration the app role `Approval.Write` assigned to the runner's service principal. Then record a Workload
   Grant for the `approval-writer` role for that service principal, with a reason and an expiry, through a configuration
   change, and a Principal Reference that records it as a service principal. The grant records whether the identity
   uses a federated credential: this one does not (it uses a certificate), so the configuration health check reports
   it as a credential-bearing grant. No secretless way for a cloud flow's HTTP action to sign in to an Entra-protected
   API was confirmed for this template.
3. **Key Vault.** Two secrets: the certificate as base64 PFX content, and its password. The HTTP action's Active
   Directory OAuth authentication with the certificate credential type takes both (`pfx` and `password`; for the
   password, Microsoft's table for the same HTTP action in Azure Logic Apps reads "Yes, but only for the "Certificate"
   credential type";
   [Secure access and data for workflows in Azure Logic Apps](https://learn.microsoft.com/azure/logic-apps/set-up-security-permissions#access-for-outbound-calls-to-other-services-and-systems)).
   Give the Power Platform environment read access to them as Microsoft describes in
   [Use environment variables for Azure Key Vault secrets](https://learn.microsoft.com/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets).
4. **Connections.** One connection each for Approvals, Office 365 Users, Office 365 Groups and Microsoft Dataverse,
   owned by the account that owns the flows. Microsoft notes for the Approvals connector
   ([Standard approvals connector](https://learn.microsoft.com/connectors/approvals/)): "The creator of the flow will
   always be shown in the Approval details (email content and all clients)."
5. **The product.** Set `Product:ApprovalRunner:Enabled` to `true` on the API. The other settings have defaults:
   `DefaultLeaseSeconds` 900, `MaxClaim` 10, `MaxAttempts` 3.

## Build, pack and import

```
node build-solution.mjs --prefix <prefix> --out <folder outside the repository> --publisher-name "<publisher>"
pac solution pack --zipfile <file>.zip --folder <that folder> --packagetype Unmanaged
pac solution create-settings --solution-zip <file>.zip --settings-file <settings>.json
pac solution import --path <file>.zip --settings-file <settings>.json
```

The prefix is the client's own publisher prefix. A second build for the same prefix gives the same flow IDs, so an
import upgrades the flows. Fill the settings file with the connection IDs and the environment variable values below,
or set them in the maker portal after import. The flows import turned off: turn on the child flow, then the parent.

| Environment variable | Type | Value |
|---|---|---|
| `<prefix>_ApiBaseUrl` | Text | The product's public base URL, with no trailing slash |
| `<prefix>_ApiAudience` | Text | The Application ID URI of the product's API app registration |
| `<prefix>_TenantId` | Text | The client's tenant ID |
| `<prefix>_RunnerClientId` | Text | The runner app registration's client ID |
| `<prefix>_RunnerCertificate` | Secret (Azure Key Vault) | The Key Vault secret that holds the PFX content |
| `<prefix>_RunnerCertificatePassword` | Secret (Azure Key Vault) | The Key Vault secret that holds the PFX password |

The flows read the two secrets at run time with the Dataverse action `RetrieveEnvironmentVariableSecretValue`, with
secure inputs and outputs, because Microsoft states
([Use environment variables for Azure Key Vault secrets](https://learn.microsoft.com/power-apps/maker/data-platform/environmentvariables-azure-key-vault-secrets)):
"Environment variables referencing secrets aren't currently available from the dynamic content selector for use in
Power Automate flows." Every HTTP action has secure inputs, so
the certificate never shows in the run history.

## Licenses: what Microsoft says

Checked on Microsoft Learn on 2026-11-23. Prices change and are not given here: see Microsoft's
[Power Automate pricing](https://www.microsoft.com/power-platform/products/power-automate/pricing) page.

**The runner's flows.** Both flows use the HTTP action, and the flows run unattended for many requesters. Microsoft on
the HTTP action
([Migrate from classic workflows to Power Automate flows in SharePoint](https://learn.microsoft.com/sharepoint/dev/business-apps/power-automate/guidance/migrate-from-classic-workflows-to-power-automate-flows)):
"Flow also has a generic HTTP connector (as an action), but it is a Premium connector." The working default is one
Process license on a flow group that holds both flows.

- [Power Automate licensing FAQ](https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs):
  "You can assign a Process license directly to one solution-aware cloud flow or to a flow group. A flow group lets up
  to 25 solution-aware cloud flows share one Process license. You must explicitly add every parent and child flow that
  should use the group's capacity."
- The same FAQ lists, among the cases that need a Process license: "Your flows are running under Application user
  (flow owner is a Service Principal)." and "Your premium flow is invoked by multiple users. In this case, either
  everyone needs a Premium license, or the flow needs a Process license."
- The same FAQ: "The flow must be in a solution before a Process license can be assigned."
- [Share Process license capacity with flow groups](https://learn.microsoft.com/power-automate/flow-groups): "A group
  can contain up to 25 solution-aware cloud flows. Parent and child flows are separate group members; adding a parent
  flow to a group doesn't add its child flows automatically." and "Only flows that you explicitly add to the group use
  its shared Process capacity." Its limits table also says "A flow can belong to only one group at a time" and gives
  the scope as "Single environment".
- [Create child flows](https://learn.microsoft.com/power-automate/create-child-flows): "A child flow doesn't inherit
  Process license capacity from a parent flow's flow group. To use the same shared capacity, explicitly add both the
  parent and child flow to the same flow group. Each flow counts separately toward the 25-flow limit."
- [Create and manage a flow group](https://learn.microsoft.com/power-automate/create-flow-group): "Flow groups start
  rolling out in July 2026. The feature becomes available automatically; no admin action is required to enable it."

So: create one flow group in the approvals environment, assign it one Process license, and add **both** flows to it
(the parent and the child), as two of the group's 25 members.

**The approvers.**

- [Power Automate licensing FAQ](https://learn.microsoft.com/power-platform/admin/power-automate-licensing/faqs),
  answering whether approvers of a premium flow need a Premium license: "Users who respond to approval requests don't
  need a Premium license."
- [Get started with approvals](https://learn.microsoft.com/power-automate/get-started-approvals): "After you assign an
  approval request to a user, they can respond directly from an Outlook email, a Microsoft Teams adaptive card, or the
  Power Automate action center if they have a Power Automate license or an Office 365 or a Dynamics 365 license with
  built-in Power Automate capabilities."
- The same page: "When you assign an approval to users who aren't in your environment, they're automatically given the
  Approvals User Dataverse security role." It lists the tenant configurations that don't allow this: "When the
  AllowAdHocSubscriptions setting in Microsoft Entra is disabled.", "If a security group was used to control which
  users have access to the Dataverse environment." and "Power Automate US Government plans." Do not lock the approvals
  environment to a security group unless every approver is in it.
- [Approvals known issues](https://learn.microsoft.com/power-automate/approvals-known-issues), for guests: "The guest
  user must be assigned a valid Power Automate license (per user license or Microsoft 365-based user license) to view
  or respond to the approval."

## Limits to know

- **28 days.** Microsoft ([Approvals known issues](https://learn.microsoft.com/power-automate/approvals-known-issues)):
  "An approval flow can wait for 28 days. If the wait time exceeds 28 days, that flow fails.
  This only impacts the flow itself, meaning that the approval continues to exist in the action center." The child
  waits at most 27 days and then gives the request back; the old card stays in the action center until someone
  cancels it there, and a new card goes out on the next poll if the record has not expired.
- **Create and wait together.** Microsoft warns that an answer given before the flow reaches **Wait for an approval**
  can leave the flow stuck
  ([Split create and wait actions](https://learn.microsoft.com/power-automate/approvals-known-issues#split-create-and-wait-actions)),
  so the child starts waiting right after it creates the card, in parallel with its sent report.
- **No cancel from the product.** The engine does not cancel or expire records yet, and the Approvals connector lists
  no action that cancels an open approval ([its actions](https://learn.microsoft.com/connectors/approvals/#actions)).
  A card for an expired record can still be answered; the product then answers 409 and records
  nothing.
- **Reassignment is off**, so only the people asked can answer. A person who answers without holding the role is
  recorded as not holding it, and the answer does not count. For an owner role the product checks the application's
  Owner Assignments itself, whatever the flow reports, and it refuses an answer whose responder is the runner's own
  identity or any other workload.

## Not verified yet

These need a test import in an approvals environment, which nobody has done for this template:

- That the generated solution imports as is: the environment variable definitions (type and Key Vault store), the
  connection references, and the child flow link by workflow ID. `pac solution pack` packs it, but warns that the
  environment variables are root components it does not find in the customizations file.
- That `responses[].responder.id` of **Wait for an approval** is the responder's Microsoft Entra object ID, and that
  `responseDate` is the time of the answer.
- That **List group members** returns `@odata.type` for each member, and whether it lists agent users. If it returns no
  type, the child asks no one and gives the request back.
- That **Run a Child Flow** ends when the child answers with **Respond to a PowerApp or flow**, not when the child's
  run ends.
- That a cloud flow's HTTP action can sign in with a federated credential instead of a certificate.
