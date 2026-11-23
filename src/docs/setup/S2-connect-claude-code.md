# Guide S2. Connect Claude Code to the product's MCP server

- **Status:** skeleton, 0.1 draft, 2026-11-22. Nothing here was run against a tenant. A test in a real tenant still has to confirm both paths and count the admin steps.
- **Guide set:** setup guides S1 to S3 and admin guides A1 and A2. Index: [README](../README.md).
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

- The product's MCP server runs at `https://<host>/mcp` (one per adopter tenant and product store) and answers an unauthenticated call with 401 and a `WWW-Authenticate` header that points to its protected resource metadata at `https://<host>/.well-known/oauth-protected-resource`.
- The server's app registration: Application ID URI `https://<host>/mcp` (the canonical endpoint URL, a non-empty path, no trailing slash) and one delegated scope, `mcp.access`. The fully qualified scope is `https://<host>/mcp/mcp.access`.
- The person holds at least one product role through a bound Entra group (guide S1, step 8). Without one, the server returns an empty or narrowed tool list.
- A Claude Code version that supports remote MCP servers over streamable HTTP with OAuth. The fixed localhost redirect form needs v2.1.231 or later (see path 1, step 2).
- [branch: Dataverse] The person holds a license that covers Dataverse use. Every person who reaches Dataverse through any MCP route needs one, because pooling access through a server does not reduce the count (multiplexing; document 24, section 10.1). The exact license for MCP-only use is UNVERIFIED there. The server's app registration holds the delegated Dataverse permission `user_impersonation` with admin consent.
- [branch: Azure] No Power Platform license for MCP use. Group-based app role assignment needs Entra ID P1 or P2; without it the server reads group membership through Graph instead (document 24, section 10.2).

## Steps

### Path 1 (lead): a client registered in the adopter's tenant

1. **Register the client.** In the Microsoft Entra admin center, create an app registration named for the client, for example "`<product>` Claude Code client", with supported account types "Accounts in this organizational directory only". It is a public client: no secret and no certificate.

2. **Add the redirect URI.** Under Authentication, add the platform "Mobile and desktop applications" with the redirect URI `http://localhost/callback`. Claude Code sends `http://localhost:<port>/callback`. Entra ignores the port when it matches a localhost redirect URI, so one entry covers any port. Do not register a second localhost URI that differs only by port. Microsoft advises `127.0.0.1` over `localhost` in general, but Claude Code sends the `localhost` form (v2.1.229 sent `127.0.0.1`; v2.1.231 restored `localhost`), so register `localhost`.

3. **Give the client the product's scope.** Either:
   - On the client: API permissions, add the delegated permission `mcp.access` of the product's server app, then grant admin consent for the tenant; or
   - On the server app: Expose an API, Authorized client applications, add the client's Application (client) ID with the `mcp.access` scope (pre-authorization; users are not asked to consent).

   Pre-authorize only clients the adopter trusts, because users get no chance to decline.

4. **Hand out the client ID.** The client ID is not a secret. Publish it with the server URL on the internal page where people find approved MCP servers, or push both through managed settings (guide [S3](S3-adopter-allowlist-entries.md)): a `managedMcpServers` entry may carry an `oauth` member with the client ID and callback port.

5. **Each person adds the server.**

   ```
   claude mcp add --transport http --client-id <client-id> --callback-port <port> <server-name> https://<host>/mcp
   ```

   No `--client-secret`: the client is public. Pick any free port; it only has to stay the same for that person.

6. **Each person signs in.** In Claude Code run `/mcp`, choose the server and finish the sign-in in the browser with the work account. From a shell, `claude mcp login <server-name>` does the same. To sign out, use "Clear authentication" in `/mcp` or `claude mcp logout <server-name>`.

7. **First call.** Ask Claude Code to call `whoami`. It returns the person, their product roles and the toolsets they may use.

### Path 2 (pilots only): Client ID Metadata Document

CIMD is public preview and "isn't recommended for production workloads". Use it only for a pilot that accepts that.

1. **Switch the server metadata.** The server's protected resource metadata must advertise `https://login.microsoftonline.com/<tenant-id>/oauth21/2026-08` as its authorization server: the versioned identifier that selects the CIMD endpoints. The template publishes the ordinary `/v2.0` identifier by default (document 24, section 4.2), so the pilot changes that template value (open point 3).

2. **Add the Claude Code client in advance.** Under the default consent policy, users can grant a CIMD client only a very limited set of scopes such as `openid` and `User.Read`, so `mcp.access` needs an admin. In the Microsoft Entra admin center: Entra ID, Agents, Agent identities, Add agent identity, and enter `https://claude.ai/oauth/claude-code-client-metadata`. Then grant only `mcp.access` for all users. For a strict posture, block user consent to unverified apps, so no other CIMD client can be added by a user.

3. **Each person adds the server without a client ID.**

   ```
   claude mcp add --transport http <server-name> https://<host>/mcp
   ```

   Claude Code discovers a CIMD server automatically. If discovery fails, use path 1.

4. **Sign in and call `whoami`,** as in path 1, steps 6 and 7.

Know the limits before a pilot: the client owner controls the metadata document; the document URL is the identity and cannot change; there is no tenant-managed domain allowlist or blocklist for CIMD URLs; no publisher verification; no app-only tokens.

### Other MCP clients

Visual Studio Code and GitHub Copilot are Microsoft first-party clients and do not use CIMD; give each its own registered client as in path 1. Each extra client is one more app registration.

### What the person may then do

- The tool list depends on the person's product roles and the Tool Policy rows (document 24, section 2.2). The `X-Toolsets` header narrows it further.
- Writes leave records in Draft, Proposed, Submitted, as reported, as a link, or as a candidate configuration draft. No tool approves, accepts or activates anything.
- An AI client may submit intake requests, change requests, decisions and configuration drafts for the person; registering an application and marking a gate-relevant document current send the person a confirmation card; links and deployment records need nothing. WORKING DEFAULT (open design question 9)
- A configuration admin's Claude Code session can propose configuration drafts. A person accepts the engine's diff on one card; the instructing admin may accept their own AI's draft, and strict separation needs a second admin. WORKING DEFAULT (open design question 4)

### Branch differences

- [branch: Dataverse] The server exchanges the person's token for a Dataverse token (on behalf of, `user_impersonation`) and calls the product's custom APIs through the Web API. Dataverse applies the person's security roles from Entra group teams. Learn says a group member is added to the group team when a non-interactive process calls on behalf of the user, so the first call should work for a person who never opened the environment. A tenant test still has to confirm it for on-behalf-of tokens.
- [branch: Azure] The engine runs in the server's own process and reads the person's object ID and product roles from the validated token; the database hop uses the service's managed identity with the person's ID in `SESSION_CONTEXT`.

## Checks

- A call to `https://<host>/mcp` with no token returns 401, with `WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource", scope="https://<host>/mcp/mcp.access"`.
- `whoami` returns the signed-in person and the expected product roles.
- A token for another audience, or from another tenant, is refused.
- The Entra sign-in logs show the client: the registered app (path 1) or an agent identity whose app ID is derived from the CIMD URL (path 2).
- A draft the person creates through Claude Code shows Performed By Actor Kind "AI client for a person", On Behalf Of the person, and Client App "Claude Code" from the `client-app` value list (document 24, section 4.5).
- A person without a role sees no write tools.
- [branch: Dataverse] Dataverse auditing shows the person, not the server, as the user of the write.

## Open points

1. Document 26's SM18b says access tokens last one hour by default. Learn now says the default is variable, 60 to 90 minutes (75 on average). Guide [A2](../admin/A2-urgent-revoke-and-break-glass.md) uses the Learn figure.
2. The least admin role for the CIMD pre-add is UNVERIFIED. Learn's "Manage CIMD clients" page lists Global Administrator, Agent ID Administrator or AI Administrator for creating the agent identity. Granting `mcp.access` to it still needs a role that can grant consent; a tenant test still has to find the least one.
3. The template's switch between the `/v2.0` and the versioned CIMD identifier has no defined parameter name yet.

## Microsoft and vendor facts used

| Fact | Source | Checked |
|---|---|---|
| CIMD is public preview, not recommended for production; Claude Code is listed "Supported" with `https://claude.ai/oauth/claude-code-client-metadata`; Visual Studio Code and GitHub Copilot do not use CIMD; a CIMD client is an agent identity per tenant; default consent allows only scopes such as `openid` and `User.Read`; admins can add the client in advance; no client credentials, no publisher verification, no tenant domain allowlist; "Implementers should always prefer pre-registration of a client with Entra when possible" | https://learn.microsoft.com/entra/agent-id/client-id-metadata-documents | 2026-11-22 |
| Add a CIMD client: Entra ID, Agents, Agent identities, Add agent identity; roles Global Administrator, Agent ID Administrator or AI Administrator; strict posture blocks user consent to unverified apps | https://learn.microsoft.com/entra/agent-id/manage-client-id-metadata-document-clients | 2026-11-22 |
| Server registration: Application ID URI is the canonical endpoint URL; delegated scopes; app roles only for app-only access; protected resource metadata at `/.well-known/oauth-protected-resource`; the versioned `oauth21/2026-08` identifier for CIMD, the ordinary `/v2.0` identifier otherwise; 401 with `resource_metadata`; `aud` is the client ID GUID, `scp` holds `mcp.access`; 403 when a valid token lacks the scope | https://learn.microsoft.com/entra/agent-id/secure-mcp-server-with-entra-id | 2026-11-22 |
| Pre-authorize a client under Expose an API, Authorized client applications; users are not prompted for consent | https://learn.microsoft.com/entra/identity-platform/quickstart-configure-app-expose-web-apis | 2026-11-22 |
| The port is ignored when matching a localhost redirect URI; do not register localhost URIs that differ only by port; prefer `127.0.0.1` in general; desktop apps use the "Mobile and desktop applications" platform | https://learn.microsoft.com/entra/identity-platform/reply-url | 2026-11-22 |
| A group member is added to the Dataverse group team when they access Dataverse interactively or a non-interactive process calls on behalf of the user | https://learn.microsoft.com/power-apps/developer/data-platform/aad-group-team | 2026-11-22 |
| Claude Code OAuth: `--client-id`, `--client-secret`, `--callback-port`; redirect `http://localhost:PORT/callback` (v2.1.229 sent `127.0.0.1`, v2.1.231 restored `localhost`); discovery through RFC 9728 then RFC 8414; CIMD servers discovered automatically; `/mcp`, `claude mcp login` and `claude mcp logout` | https://code.claude.com/docs/en/mcp (vendor) | 2026-11-22 |
