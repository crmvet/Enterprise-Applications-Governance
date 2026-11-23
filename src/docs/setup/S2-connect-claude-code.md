# Guide S2. Connect Claude Code to the product's MCP server

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant. The server side of path 1 is built and tested on the local server with test tokens; a test in a real tenant still has to confirm both paths and count the admin steps.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **Design sources:** document 24, sections 4.1 to 4.5, 12 and 13; document 26, section 8; document 28, Q14 (working default, settled).
- **Open design questions that touch this guide:** 4 and 9 (markers below).

## Purpose

Let a person use Claude Code against the product's MCP server, signed in as themselves, so every call reaches the data store as that person (on behalf of, never a shared identity). The guide leads with a client registered in the adopter's tenant, which is generally available and what Microsoft prefers, and offers Client ID Metadata Documents (CIMD) for pilots. CIMD becomes the lead once it is generally available (working default, 28, Q14).

## Who

| Who | Part |
|---|---|
| The adopter's Entra admin | Registers the client app and grants consent (path 1), or adds the Claude Code CIMD client in advance (path 2). Needs Cloud Application Administrator or Application Administrator for app registrations and consent; Global Administrator, Agent ID Administrator or AI Administrator to add a CIMD agent identity |
| The adopter's Azure team | Deployed the product's MCP server and its app registration from the template |
| Each person who uses Claude Code | Adds the server and signs in |

## Prerequisites

- The product's MCP server runs at `https://<host>/mcp` (one per adopter tenant and product store) and answers an unauthenticated call with 401 and a `WWW-Authenticate` header that points to its protected resource metadata at `https://<host>/.well-known/oauth-protected-resource/mcp` (the root form `/.well-known/oauth-protected-resource` answers the same). Guide [S4](S4-prepare-entra-and-deploy.md) deploys it.
- The server's app registration (the product's API app registration, guide S4, part B): Application ID URI `https://<host>/mcp` (the canonical endpoint URL, a non-empty path, no trailing slash) and one delegated scope, `mcp.access`. The fully qualified scope is `https://<host>/mcp/mcp.access`.
- The person holds at least one product role through a bound Entra group (guide S1, step 8). Without one, the server returns an empty or narrowed tool list.
- A Claude Code version that supports remote MCP servers over streamable HTTP with OAuth. The fixed localhost redirect form needs v2.1.231 or later (see path 1, step 2).
- [branch: Dataverse] The person holds a license that covers Dataverse use. Every person who reaches Dataverse through any MCP route needs one, because pooling access through a server does not reduce the count (multiplexing; document 24, section 10.1). The exact license for MCP-only use is UNVERIFIED there. The server's app registration holds the delegated Dataverse permission `user_impersonation` with admin consent.
- [branch: Azure] No Power Platform license for MCP use. Group-based app role assignment needs Entra ID P1 or P2 (guide S4, open point 2). The person's role group is assigned to the API's enterprise application, which requires assignment, so a person outside every role group gets no token at all.

## Steps

### Path 1 (lead): a client registered in the adopter's tenant

1. **Register the client.** In the Microsoft Entra admin center, create an app registration named for the client, for example "`<product>` Claude Code client", with supported account types "Accounts in this organizational directory only". It is a public client: no secret and no certificate.

2. **Add the redirect URI.** Under Authentication, add the platform "Mobile and desktop applications" with the redirect URI `http://localhost/callback`. Claude Code sends `http://localhost:<port>/callback`. Entra ignores the port when it matches a localhost redirect URI, so one entry covers any port. Claude Code's own documentation registers the form with a port (`http://localhost:<port>/callback`, as `infra/README.md`, section 2, does); because Entra ignores the port, either form works, but keep exactly one. Do not register a second localhost URI that differs only by port. Microsoft advises `127.0.0.1` over `localhost` in general, but Claude Code sends the `localhost` form (v2.1.229 sent `127.0.0.1`; v2.1.231 restored `localhost`), so register `localhost`.

3. **Give the client the product's scope.** Either:
   - On the client: API permissions, add the delegated permission `mcp.access` of the product's server app, then grant admin consent for the tenant; or
   - On the server app: Expose an API, Authorized client applications, add the client's Application (client) ID with the `mcp.access` scope (pre-authorization; users are not asked to consent).

   Pre-authorize only clients the adopter trusts, because users get no chance to decline.

4. **Put the client on the Known Client list.** The configuration admin adds a `known-client` row with the client's Application (client) ID as `clientIdentifier`, the label "Claude Code", `isAiClient` true and `isAllowed` true, in the client binding patch (guide [S1](S1-pick-presets-and-overlays.md), step 8) or later through a configuration change. The label becomes the Client App on every record the client writes. Once any Known Client row is in force, the list is an allow list: a client whose app ID matches no allowed row is refused with `not_authorized`. Until then the check does not apply and the client is labeled by its app ID.

5. **Hand out the client ID.** The client ID is not a secret. Publish it with the server URL on the internal page where people find approved MCP servers, or push both through managed settings (guide [S3](S3-adopter-allowlist-entries.md)): a `managedMcpServers` entry may carry an `oauth` member with the client ID and callback port.

6. **Each person adds the server.**

   ```
   claude mcp add --transport http --client-id <client-id> --callback-port <port> <server-name> https://<host>/mcp
   ```

   No `--client-secret`: the client is public. Pick any free port; it only has to stay the same for that person.

7. **Each person signs in.** In Claude Code run `/mcp`, choose the server and finish the sign-in in the browser with the work account. From a shell, `claude mcp login <server-name>` does the same. To sign out, use "Clear authentication" in `/mcp` or `claude mcp logout <server-name>`.

8. **First call.** Ask Claude Code to call `whoami`. It returns the person, their product roles and the toolsets they may use.

### Path 2 (pilots only): Client ID Metadata Document

CIMD is public preview and "isn't recommended for production workloads". Use it only for a pilot that accepts that.

1. **Switch the server metadata.** The server's protected resource metadata must advertise `https://login.microsoftonline.com/<tenant-id>/oauth21/2026-08` as its authorization server: the versioned identifier that selects the CIMD endpoints. The server publishes the ordinary `/v2.0` identifier (document 24, section 4.2): it builds the value from `Product:Auth:Instance` and the tenant ID, and has no setting for the versioned identifier yet (open point 3).

2. **Add the Claude Code client in advance.** Under the default consent policy, users can grant a CIMD client only a very limited set of scopes such as `openid` and `User.Read`, so `mcp.access` needs an admin. In the Microsoft Entra admin center: Entra ID, Agents, Agent identities, Add agent identity, and enter `https://claude.ai/oauth/claude-code-client-metadata`. Then grant only `mcp.access` for all users. For a strict posture, block user consent to unverified apps, so no other CIMD client can be added by a user.

3. **Each person adds the server without a client ID.**

   ```
   claude mcp add --transport http <server-name> https://<host>/mcp
   ```

   Claude Code discovers a CIMD server automatically. If discovery fails, use path 1.

4. **Sign in and call `whoami`,** as in path 1, steps 7 and 8. The Known Client list needs a row for this client too, as in path 1, step 4. Which identifier a CIMD client's token carries for the check (the agent identity's app ID, which Entra derives from the URL, or the URL) is not confirmed (open point 4).

Know the limits before a pilot: the client owner controls the metadata document; the document URL is the identity and cannot change; there is no tenant-managed domain allowlist or blocklist for CIMD URLs; no publisher verification; no app-only tokens.

### Other MCP clients

Visual Studio Code and GitHub Copilot are Microsoft first-party clients and do not use CIMD; give each its own registered client as in path 1. Each extra client is one more app registration and one more Known Client row (path 1, step 4).

### What the person may then do

- The tool list depends on the person's product roles and the Tool Policy rows (document 24, section 2.2). The `X-Toolsets` header narrows it further.
- Writes leave records in Draft, Proposed, Submitted, as reported, as a link, or as a candidate configuration draft. No tool approves, accepts or activates anything.
- An AI client may submit intake requests, change requests, decisions and configuration drafts for the person; registering an application and marking a gate-relevant document current send the person a confirmation card; links and deployment records need nothing. WORKING DEFAULT (open design question 9)
- A configuration admin's Claude Code session can propose configuration drafts. A person accepts the engine's diff on one card; the instructing admin may accept their own AI's draft, and strict separation needs a second admin. WORKING DEFAULT (open design question 4)

### Branch differences

- [branch: Dataverse] The server exchanges the person's token for a Dataverse token (on behalf of, `user_impersonation`) and calls the product's custom APIs through the Web API. Dataverse applies the person's security roles from Entra group teams. Learn says a group member is added to the group team when a non-interactive process calls on behalf of the user, so the first call should work for a person who never opened the environment. A tenant test still has to confirm it for on-behalf-of tokens.
- [branch: Azure] The engine runs in the server's own process, as the caller: it reads the person's object ID and group IDs from the validated token, resolves the product roles from the Role Group Binding rows of the configuration in force, and stamps the write as an AI client acting for the person. The token goes nowhere else. The database hop uses the service's managed identity with the person's ID, the actor kind and the role keys in `SESSION_CONTEXT`.

## Checks

- A call to `https://<host>/mcp` with no token returns 401, with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource/mcp"`. A valid token without the scope returns 403, with `WWW-Authenticate: Bearer error="insufficient_scope", scope="mcp.access", resource_metadata="https://<host>/.well-known/oauth-protected-resource/mcp"`.
- `whoami` returns the signed-in person and the expected product roles.
- A token for another audience, or from another tenant, is refused.
- The Entra sign-in logs show the client: the registered app (path 1) or an agent identity whose app ID is derived from the CIMD URL (path 2).
- A draft the person creates through Claude Code shows Performed By Actor Kind "AI client for a person", On Behalf Of the person, and Client App "Claude Code", the label of its Known Client row (document 24, section 4.5).
- A person without a role sees no write tools, and an agent user signed in through the same client holds no role (`whoami` answers `not_authorized`).
- A client that is not on the Known Client list is refused with `not_authorized` once the list holds any row.
- [branch: Dataverse] Dataverse auditing shows the person, not the server, as the user of the write.

## Open points

1. Document 26's SM18b says access tokens last one hour by default. Learn now says the default is variable, 60 to 90 minutes (75 on average). Guide [A2](../admin/A2-urgent-revoke-and-break-glass.md) uses the Learn figure.
2. The least admin role for the CIMD pre-add is UNVERIFIED. Learn's "Manage CIMD clients" page lists Global Administrator, Agent ID Administrator or AI Administrator for creating the agent identity. Granting `mcp.access` to it still needs a role that can grant consent; a tenant test still has to find the least one.
3. The server has no setting that switches its protected resource metadata from the `/v2.0` identifier to the versioned CIMD identifier, so path 2 needs a host change before a pilot. Proposal: a setting under `Product:Auth` with the `/v2.0` identifier as its default.
4. The Known Client check matches the token's client app ID, or a CIMD URL, against the `clientIdentifier` of each row. For a CIMD client the token carries the agent identity's app ID; a tenant test should confirm which value to put in the row.

## Microsoft and vendor facts used

| Fact | Source | Checked |
|---|---|---|
| "Client ID Metadata Document support in Microsoft Entra ID is in public preview. This preview is provided without a service-level agreement and isn't recommended for production workloads."; the clients table lists Claude Code with `https://claude.ai/oauth/claude-code-client-metadata` as "Supported."; for Visual Studio Code and GitHub Copilot, "Microsoft first-party clients do not use CIMD."; "Microsoft Entra ID represents a CIMD client as an agent identity in each tenant where the client is approved."; "Users can only grant a very limited set of OAuth scopes to CIMD clients, such as `openid` and `User.Read` in Microsoft Graph."; "An administrator can instead add the CIMD client in advance."; "No app-only tokens: CIMD clients can't use the client credentials grant."; "CIMD clients don't support publisher verification or Microsoft 365 certification."; "Entra doesn't provide a tenant-managed domain allowlist or blocklist for CIMD URLs."; "Implementers should always prefer pre-registration of a client with Entra when possible." | https://learn.microsoft.com/entra/agent-id/client-id-metadata-documents | 2026-11-23 |
| Add a CIMD client: Entra ID, Agents, Agent identities, Add agent identity; roles Global Administrator, Agent ID Administrator or AI Administrator; strict posture blocks user consent to unverified apps | https://learn.microsoft.com/entra/agent-id/manage-client-id-metadata-document-clients | 2026-11-22 |
| "An HTTPS endpoint with a nonempty path and no trailing slash"; "Set the Application ID URI to the MCP server's canonical endpoint URL"; "Define application roles only if the server supports app-only access."; "For CIMD support, advertise the versioned identifier `https://login.microsoftonline.com/<tenant>/oauth21/2026-08`." and "The ordinary `/v2.0` discovery identifier is used for conventional integrations, but doesn't select the versioned CIMD endpoints."; "For v2 access tokens, the token's `aud` is the resource application's client-ID GUID, not this URL."; "The authorization policy returns `403 Forbidden` when a valid token lacks both the required delegated scope and application role." | https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id | 2026-11-23 |
| Pre-authorize a client under Expose an API, Authorized client applications; users are not prompted for consent | https://learn.microsoft.com/entra/identity-platform/quickstart-configure-app-expose-web-apis | 2026-11-22 |
| "the port component (for example, `:5001` or `:443`) is ignored for the purposes of matching a localhost redirect URI"; "Do not register multiple localhost redirect URIs where only the port differs."; "use the IP literal loopback address `127.0.0.1` in your redirect URI instead of `localhost`"; "An app that runs natively on a mobile device or desktop machine" uses the platform "Mobile and desktop applications" | https://learn.microsoft.com/entra/identity-platform/reply-url | 2026-11-23 |
| A group member is added to the Dataverse group team when they access Dataverse interactively or a non-interactive process calls on behalf of the user | https://learn.microsoft.com/power-apps/developer/data-platform/aad-group-team | 2026-11-22 |
| Claude Code OAuth: "If the server uses a public OAuth client with no secret, use only `--client-id` without `--client-secret`"; "Use `--callback-port` to fix the port so it matches a pre-registered redirect URI of the form `http://localhost:PORT/callback`."; "In v2.1.229, Claude Code sent `http://127.0.0.1:PORT/callback` instead ... Claude Code v2.1.231 restored the `localhost` form."; "Claude Code first checks RFC 9728 Protected Resource Metadata at `/.well-known/oauth-protected-resource`, then falls back to RFC 8414 authorization server metadata"; "Claude Code also supports servers that use a Client ID Metadata Document (CIMD) instead of Dynamic Client Registration, and discovers these automatically."; `claude mcp login <name>`, `claude mcp logout <name>`, and "Clear authentication" and **Re-authenticate** in `/mcp` | https://code.claude.com/docs/en/mcp (vendor) | 2026-11-23 |
