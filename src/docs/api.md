# The REST API

The REST API serves the same tools, with the same contracts, as the MCP endpoint. The product's web app uses it for
everything a person does. Another API client may use it to read.

## One route per tool

```
POST <base>/api/{tool}
Authorization: Bearer <token>
Content-Type: application/json

{ ...the tool's arguments... }
```

- **The path** names the tool, such as `/api/find_applications` or `/api/save_intake_draft`. Every tool of
  [the tool list](mcp.md#the-tools) has a route.
- **The body** is the tool's arguments as one JSON object, sent as `application/json`, checked against
  `mcp/contracts/tools/<tool>.input.schema.json`. An empty body is the same as `{}`. A body that is not one JSON object
  answers `validation` on the field `body`, and so does a body larger than `Product:MaxRequestBytes` (1 MiB by
  default).
- **The answer** is the tool's envelope, the same as on MCP (`mcp/contracts/envelope.schema.json`): `ok`, `summary`,
  `data` and the rest on success; `ok: false` and `error` on failure. The status is 200 when the call succeeded, else
  the HTTP status of the error code below. A `rate_limited` answer carries `Retry-After`.
- **Idempotency and dry runs** work as on MCP: `clientRequestId` (or `idempotencyKey` for the two deployment record
  tools) and `validateOnly` ([mcp.md](mcp.md#what-the-client-sees)).

## Sign-in and the two kinds of caller

The tool routes, `/api/me` and `/api/info` need a delegated Microsoft Entra ID access token (v2) for the API app
registration with the scope `api.access`. (The record and approval runner routes take app-only tokens instead; see
[the other routes](#the-other-routes).) A missing or invalid token
answers 401 `not_signed_in`; a token without the scope answers 403. The engine resolves the caller's product roles
from the groups in the token on every call.

Who the caller is decides what the route serves:

| Caller | How the API knows | Actor kind | What it may call |
|---|---|---|---|
| A person in the product's web app | The token's verified `azp` is one of the web app client IDs the host declares (`Product:WebApp:ClientIds`) | `person`, with no On Behalf Of | Every tool the Tool Policy gives the person, writes included, and the tools served only to a person, such as `answer_approval` |
| Any other client | Any other `azp` | `ai-client-for-person`, as on MCP, after the Known Client check | The read tools only. A write tool answers `not_found` (it writes through the MCP endpoint), and a tool served only to a person answers `ai_cannot_do_this` |

An agent user or agent identity holds no product role on either entry. App-only tokens never reach a tool.

**Step-up sign-in.** When the host setting `Product:WebApp:StepUp` is on (it is off by default and in the demo), an
answer through `answer_approval` from a sign-in older than the setting allows, or without the authentication context
it names, answers 401 `step_up_required` with a claims challenge in `WWW-Authenticate`, and records nothing. A dry run
(`validateOnly`) needs no fresh sign-in.

## Request headers

| Header | Who sends it | What it does |
|---|---|---|
| `Authorization: Bearer <token>` | Every caller | The delegated token, scope `api.access` |
| `X-Toolsets` | Optional | Narrows the tools a call may reach to the named toolsets, as on MCP |
| `X-Product-Local-Day: YYYY-MM-DD` | The web app | The person's own calendar day. The server uses it for date-only defaults, such as the day a review is done, when it is within a day of the UTC day |
| `X-Product-Demo-Persona: <name>` | The web app on a demo install | Acts as one of the install's fictional demo personas for this request. Only in the environment named `Demo`, only on the person entry. A name the install does not know answers `validation` on that header, and nothing runs. Elsewhere the header is ignored |
| `X-Product-Dev-Identity: <persona>` | A local run only | Signs in as a fictional development persona, in place of a token. Only in the Development environment, with the switch on, from a loopback address |

Every answer of the product's routes carries `X-Correlation-Id`, the same ID as the envelope's `correlationId`.

## The other routes

| Route | Who calls it | What it does |
|---|---|---|
| `GET /api/me` | The web app | The caller's name, actor kind, product roles (each with the group that grants it), the configuration in force and, on a demo install, the persona acted as (`demoPersona`) |
| `GET /api/info` | The web app | The engine version, the MCP endpoint and protocol, and the configuration version in force (null before version 1) |
| `POST /api/deployments`, `POST /api/deployments/corrections` | Pipeline record steps, with an app-only token and the app role `Deployment.Record` | Records a deployment, or corrects one, as the pipeline's workload identity. The identity also needs a current Workload Grant for `deployment-recorder`. The contract is `mcp/contracts/record-endpoint.json`; copy-paste steps are in [samples/pipeline-steps](../samples/pipeline-steps/README.md) |
| `POST /engine/v1/approval-records/{id}/outcome` and the runner's routes under `/engine/v1/approval-runner/` | The approval runner, with an app-only token and the app role `Approval.Write` | Carries answers given on Power Automate approval cards back to the engine. The identity also needs a current Workload Grant for `approval-writer`. See [the flows README](../adapters/azure/flows/README.md) |
| `GET /health/live`, `GET /health/ready` | Platform probes, no token | The process is up; the store answers in time. A status word only, or 503 |
| `GET /.well-known/oauth-protected-resource/mcp` | MCP clients, no token | The protected resource metadata of the MCP endpoint |
| `GET /app/`, `GET /app/config.json` | Browsers, no token | The web app and its runtime configuration (the sign-in values, no secret) |

## Limits

Each signed-in caller may send 300 requests a minute to the REST API (`Product:Limits:ApiPerMinute`) and 60 a minute
to the record routes (`Product:Limits:RecordPerMinute`). The count lives in the store, so every instance sees the same
count. Over a limit the answer is 429 `rate_limited` with `Retry-After`. On a demo install, the operator's requests as
the demo personas are counted on their own, under `Product:Limits:DemoOperatorPerMinute` (1,200 a minute by default).
A Tool Policy row may also limit calls per person per hour; the shipped rows set none.

## Error codes

Every failure carries one of these codes in `error.code`, with `error.message` in one plain sentence,
`error.field` when one input is at fault, and `error.recordUrl` when the refusal points to a record, such as the document
link to update instead of linking a second one. The codes never change and are never reused (`mcp/contracts/errors.json`).

<!-- generated: errors (node tools/docs-gen.mjs) -->
| Code | HTTP status | Retry the same call? | What it means | What the client does |
|---|---|---|---|---|
| `not_signed_in` | 401 | No | No valid token. The HTTP layer returns 401 with the protected resource metadata link. | Sign in through the MCP client, then call again. |
| `not_authorized` | 403 | No | Valid token, but the caller lacks the role, the owner role, or the row access. A missing scope also returns 403. | Tell the person which role or ownership the call needs. Do not retry. |
| `step_up_required` | 401 | No | The call needs a fresh sign-in, and the token is older than the setting allows, or lacks the authentication context it names. Only answer_approval asks for one, and only when the step-up setting is on. The HTTP layer returns 401 with a claims challenge. | Sign the person in again, with the claims challenge when the error carries one, then send the same answer again. |
| `ai_cannot_do_this` | 403 | No | The write would set a state that only a person or the approval runner may set. AI never approves. | Do not retry. Point the person to the decision page in the web app, or to the page where a person acts. |
| `rule_violation` | 422 | No | A configuration rule refused the write, for example a gate requirement or a window rule. ruleKey names the refusing row. | Explain the rule to the person. Change the input, or start the decision the rule needs. |
| `validation` | 400 | No | An input is missing, malformed or not an allowed key. | Fix the field named in error.field, then call again. |
| `not_found` | 404 | No | The ID or key does not exist, or the caller cannot see it. | Check the ID or key, for example with find_applications. |
| `conflict` | 409 | No | The record changed since it was read, the baseVersionNumber is stale, or a clientRequestId was reused with a different payload. | Read the record again, then call again with the fresh state. |
| `locked` | 422 | No | The write touches a locked setting or an engine contract value. | Do not retry. A locked value does not change through any tool. |
| `rate_limited` | 429 | Yes, later | The Tool Policy limit of calls per person per hour was reached. The error carries retryAfterSeconds. | Wait for retryAfterSeconds, then call again. |
| `upstream_unavailable` | 503 | Yes, later | A Microsoft API the tool needs did not answer, for example the inventory or Microsoft Graph. | Wait a little, then call again. For the gap check, compare_inventory_list is the fallback. |
| `configuration_unavailable` | 503 | No | The engine cannot load a valid active configuration version, or a rule row it needs is missing, such as a gate policy row. Governed writes are refused. There is no built-in fallback. | Tell the person the configuration is broken and a configuration admin must fix it. get_configuration_health shows the cause. |
<!-- end generated: errors -->

A server failure answers 500 with a failure envelope whose message gives only the correlation ID, for an
administrator to look up. Outside Development the log keeps the exception's type and stack, never its message.

## An example

Against a local run (no token: the development identity names the persona):

```
curl -s http://127.0.0.1:5080/api/find_applications \
  -H 'Content-Type: application/json' -H 'X-Product-Dev-Identity: demo-nadia' \
  -d '{"text":"Inspection"}'
```

`demo-nadia` is Nadia Ferreira signed in to the web app, so the call runs on the person entry. The answer is the
envelope with the matching applications in `data.items`.
