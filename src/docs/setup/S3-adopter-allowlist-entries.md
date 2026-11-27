# Guide S3. Allow the product's MCP server: adopter allowlist entries

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant or a device.
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **See also:** [mcp.md](../mcp.md), [install.md](../install.md).
- **Open design questions that touch this guide:** 12 (marker below).

## Purpose

Tell an adopter's administrators where to allow the product's MCP server, and what to enter in each place. There are up to eight such places. This guide covers the four an adopter sets on devices and the network (Claude Code managed settings, GitHub Copilot managed settings, the Entra Global Secure Access MCP firewall, Defender AI agent runtime protection in audit mode) and points to the others.

None of these places is a security control of the product. The engine enforces every rule on every write, whatever an allowlist says. An allowlist decides which clients may reach the server and which servers people may reach.

## Who

| Who | Part |
|---|---|
| The adopter's endpoint or device management team | Claude Code managed settings, Defender settings through Intune |
| The adopter's GitHub enterprise owner | GitHub Copilot managed settings |
| The adopter's Entra admins | Global Secure Access Administrator for MCP policies; Conditional Access Administrator for the linked policy |
| The adopter's security operations team | Reviews Defender detections and Global Secure Access logs |

## Prerequisites

The values the product publishes for each install:

| Value | Example form |
|---|---|
| Server URL | `https://<host>/mcp` |
| Protected resource metadata URL | `https://<host>/.well-known/oauth-protected-resource/mcp` (the root form answers the same) |
| Application ID URI | `https://<host>/mcp` |
| Delegated scopes | `mcp.access` for MCP clients; `api.access` for the product's web app only (guide [S4](S4-prepare-entra-and-deploy.md)) |
| App roles (app-only callers only) | `Deployment.Record` for pipeline record steps and `Approval.Write` for the approval runner, each assigned directly to one workload identity and recorded as a Workload Grant. WORKING DEFAULT (open design question 12) |
| Tool names | The names in [mcp.md](../mcp.md#the-tools), "The tools" (for tool-level network rules) |
| Claude Code CIMD URL | `https://claude.ai/oauth/claude-code-client-metadata` (pilots only, guide [S2](S2-connect-claude-code.md)) |

## Steps

### 1. Claude Code managed settings (the device or the organization)

Pick one pattern and deliver it from a managed settings source: server-managed settings, an MDM profile or HKLM registry policy, or a system `managed-settings.json`.

- **Approved catalog.** Users add the server themselves; anything unlisted is blocked.

  ```json
  {
    "allowManagedMcpServersOnly": true,
    "allowedMcpServers": [
      { "serverUrl": "https://<host>/mcp" }
    ]
  }
  ```

  Add the adopter's other approved servers to the same list. Without `allowManagedMcpServersOnly`, a user's own settings can broaden the allowlist.

- **Provided server.** Every user gets the server and keeps their own (Claude Code v2.1.259 or later; earlier clients ignore the key). The entry may carry the registered client's ID so people need no `claude mcp add` (guide S2, path 1):

  ```json
  {
    "managedMcpServers": {
      "<server-name>": {
        "type": "http",
        "url": "https://<host>/mcp",
        "oauth": { "clientId": "<client-id>", "callbackPort": <port> }
      }
    }
  }
  ```

  Entries must use `https`, no `${VAR}` references and no `headers` with a secret: anyone who can read the managed settings can read a header value.

- Match by `serverUrl`, never by `serverName`: "A `serverName` entry, in either list, is not a security control."
- With `strictPluginOnlyCustomization` covering `mcp`, users cannot add servers themselves; provided servers still load, or ship the entry in a plugin from a managed marketplace.

### 2. GitHub Copilot enterprise managed settings (the organization)

- Add the server to `allowedMcpServers` with a `serverUrl` entry in the enterprise's `managed-settings.json`. GitHub calls this "the more secure, generally available method". Most enterprises keep the file in a `.github-private` repository; MDM can install it on machines instead.

  ```json
  {
    "allowedMcpServers": [
      { "serverUrl": "https://<host>/mcp" }
    ]
  }
  ```

- Deny entries win over allow entries. Built-in servers, such as the built-in GitHub MCP server, are always allowed.
- The MCP registry allowlist policy (a registry URL, for example an Azure API Center registry, plus "Registry only") is public preview and not GitHub's recommended method. Use it only in addition.
- GitHub Copilot connects through its own registered client (it does not use CIMD); see guide S2, "Other MCP clients".

### 3. Entra Global Secure Access MCP firewall (the network, preview)

Prerequisites from Learn: a Microsoft Entra Internet Access license; the Global Secure Access client on Entra joined or hybrid joined devices; TLS inspection on (MCP messages travel in the encrypted payload); the Internet Access traffic forwarding profile on.

1. As a Global Secure Access Administrator: Global Secure Access, Secure, MCP policies (Preview), Create policy.
2. Choose the default action. To allow only approved servers: default Block, plus a rule with action Allow and Server URL `https://<host>/mcp` (and the other approved servers).
3. Optional tool-level rules: add the server, Discover its tools (or add them by hand if discovery needs a token), and set conditions on tool names. These are network rules on top of the product's own Tool Policy, not a replacement for it.
4. Link the MCP policy to a security profile (Secure, Security profiles, Link a policy, Existing MCP policy).
5. As a Conditional Access Administrator: a Conditional Access policy for the users, target resources "All internet resources with Global Secure Access", session "Use Global Secure Access Security Profile" with that profile.

Limits from Learn: only remote servers over streamable HTTP and Server-Sent Events are inspected; stdio and local servers are not visible; JSON-RPC batches are not inspected.

### 4. Defender for Endpoint AI agent runtime protection (the device, preview)

Prerequisites from Learn, in Microsoft's words: "Your organization has a Microsoft Defender for Endpoint Plan 2, Microsoft 365 E5, Microsoft Agent 365, or Microsoft 365 E7 license."; the devices are onboarded to Defender for Endpoint, with Microsoft Defender Antivirus in active mode and real-time protection on.

1. Start in Audit mode, as Microsoft recommends, before Block. On one device: `Set-MpPreference -AiAgentProtection Audit` (needs `AntivirusSignatureVersion` 1.451.224.0 or later). Across the fleet: an Intune endpoint security Antivirus policy, profile "Microsoft Defender AI agent runtime protection", setting Ai Agent Protection set to Audit. The numeric values are 0 disabled, 1 block, 2 audit.
2. Claude Code is covered through its hooks (agent-native event inspection).
3. Review the "Suspicious AI prompt injection" alerts with the security team. In Audit mode they are informational.
4. Decide on Prompt Evidence Collection before turning protection on (see the caution below), and set it explicitly: on one device `Set-MpPreference -PromptEvidenceCollection $false` (or `$true`), across the fleet the Prompt Evidence Collection setting in the same Intune profile. Microsoft: "Explicitly configured settings take precedence over this default." The setting needs the October 2026 Defender Antivirus platform and engine updates or later.
5. Move to Block only after the audit review, and only knowing the Claude Code known issue below.

Known issues from Learn (preview): in Audit mode an alert can wrongly say "blocked"; in Block mode "Claude Code might continue processing a tool response" after a reported post-tool-response block.

Caution: with Prompt Evidence Collection on, Defender sends prompt data and tool calls, with their arguments, to the cloud without redaction, and an upcoming Defender platform release turns collection on by default. The product's tool calls and results carry register data, so the adopter should decide this setting explicitly against its data handling rules.

### 5. The other places (pointers only)

- **Entra consent** for the server's app registration and scope, and the CIMD pre-add for pilots: guide [S2](S2-connect-claude-code.md).
- **Agent 365 bring-your-own MCP server** (optional, preview) and an **Azure API Center** entry (optional). Not covered in this guide.
- [branch: Dataverse] **Dataverse MCP allowed clients**, for route C only: turn on the Dataverse MCP server for the environment and enable the client. The allow list applies only to the `/api/mcp` entry point and never restricts the product's custom APIs. Not needed for route A, the product's own server.
- [branch: Azure] No extra place: the only route is the product's own server. Inside the product, the Known Client list of the configuration in force decides which client apps may call it at all (guide S2, path 1, step 4); unlike the places above, that list is enforced by the product on every call.

## Checks

- On a managed device, `claude mcp list` shows the server, and `claude mcp add --transport http test https://example.org/mcp` fails with "not allowed by enterprise policy" (approved catalog pattern).
- A provided server appears under "Managed MCPs" in `/mcp`, and `claude mcp remove` reports that the organization provides it.
- Global Secure Access, Monitor, Traffic logs show allowed calls to the server and blocked calls to an unlisted server; Generative AI Insights shows the MCP activity.
- Defender: run Microsoft's AI agent runtime protection demonstration on a test device in Audit mode and find the informational alert.
- The product still refuses a write the engine forbids even when every allowlist allows the server (the allowlists are not the product's control).

## Open points

1. TLS inspection for the MCP firewall means the edge decrypts the product's MCP traffic, bearer tokens included. The product's security design should mention it for adopters' security reviews.
2. Defender's prompt evidence collection is new data that leaves through the AI client path. The product's security design should list it with the other data that leaves.

## Microsoft and vendor facts used

| Fact | Source | Checked |
|---|---|---|
| Global Secure Access MCP firewall: preview; prerequisites (Entra Internet Access license, Global Secure Access Administrator, Conditional Access Administrator, client on Entra joined or hybrid joined devices, TLS inspection); MCP policies, default action, Server URL rules, tool rules, security profile link, Conditional Access session control; streamable HTTP and SSE only, remote only, no JSON-RPC batches | https://learn.microsoft.com/entra/global-secure-access/how-to-configure-mcp-firewall | 2026-11-22 |
| "Microsoft recommends starting in audit mode to observe detections and validate accuracy before switching to block mode for active enforcement."; prompt evidence: "This content is sent in its original form, without redaction." and "An upcoming Microsoft Defender Antivirus platform release will enable prompt evidence collection by default."; known issue: "During preview, Claude Code might continue processing a tool response even when AI agent runtime protection is configured in **Block** mode and Defender reports a post-tool-response block." | https://learn.microsoft.com/defender-endpoint/ai-agent-runtime-protection-overview | 2026-11-23 |
| "Verify that `AntivirusSignatureVersion` is `1.451.224.0` or later"; `Set-MpPreference -AiAgentProtection <mode>`; "For both settings, the numerical values are `0` (`Disabled`), `1` (`Block`), and `2` (`Audit`)."; Intune profile "Microsoft Defender AI agent runtime protection"; the license prerequisite quoted in part 4; `Set-MpPreference -PromptEvidenceCollection $true` or `$false`, which "requires the October 2026 Microsoft Defender Antivirus platform and engine updates or later" | https://learn.microsoft.com/defender-endpoint/configure-ai-agent-runtime-protection | 2026-11-23 |
| `AiAgentProtection` CSP: 0 "AI Agent protection is disabled (default).", 1 "AI Agent protection is enabled in block mode", 2 "AI Agent protection is enabled in audit mode" | https://learn.microsoft.com/windows/client-management/mdm/defender-csp | 2026-11-23 |
| Claude Code managed MCP: for `managedMcpServers`, "Requires Claude Code v2.1.259 or later. Earlier clients ignore the key.", "`url` is an `https://` URL.", "No value contains a `${VAR}` reference.", and "Anyone who can read the managed settings on a machine, including the user, can read a header value you set here."; "A `serverName` entry, in either list, is not a security control."; "Without `allowManagedMcpServersOnly`, allowlists from every settings scope merge, including a user's own `~/.claude/settings.json`, so a user can broaden what your allowlist permits."; the errors "not allowed by enterprise policy" and "is provided by your organization (managed settings) and cannot be removed locally"; provided servers listed under **Managed MCPs** | https://code.claude.com/docs/en/managed-mcp (vendor) | 2026-11-23 |
| GitHub Copilot `managed-settings.json` allowlist: `allowedMcpServers` and `deniedMcpServers`, deny wins, built-in servers always allowed, `.github-private` repository or MDM | https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-mcp-usage/configure-enterprise-allowlist (vendor) | 2026-11-22 |
| GitHub: managed settings are "the more secure, generally available method"; the registry allowlist is public preview; Azure API Center supported as a registry | https://docs.github.com/en/copilot/how-tos/administer-copilot/manage-mcp-usage/configure-mcp-server-access (vendor) | 2026-11-22 |
