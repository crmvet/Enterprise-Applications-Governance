# G8. Decide how each app reaches Dataverse

- **Number:** G8
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 13 (Data owners and access)
- **Cited in chapters:** 1, 8, 11, 13, 16, 21
- **Processes:** P02, P04, P10

**Rule.** For every app, agent or AI client that reads or writes Dataverse, choose and record how it connects: as the signed-in person, as its own identity, or read-only on the person's behalf. Prefer the person's own identity, so their security roles apply.

**Why it matters.** An app that runs as its maker, or as a powerful account, can show or change data the user should never reach. In Copilot Studio, for example, makers can let tools run with their own credentials, and that option is on by default.

**How the product is designed to support it.** It is designed to record each app's access approach as a fact, and its own MCP tools are designed to act on behalf of the signed-in person, so that person's own permissions decide each call.

## Microsoft anchors

- [Impersonate another user](https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user) (Dataverse), checked 2026-11-22: when code acts for another user, the privileges used are the intersection of both users' privileges. The Act on Behalf of Another User privilege must be assigned directly: "Users can't inherit this privilege through a Team."
- [Control maker-provided credentials for authentication](https://learn.microsoft.com/microsoft-copilot-studio/configure-no-maker-authentication) (Copilot Studio), checked 2026-11-22: with maker credentials, "the agent uses the maker's credentials, not the end user's credentials". "By default, both End-user and Maker-provided credentials are enabled." Admins can require end-user credentials for an environment or an environment group.

## Working shape

- **When:** at design review, and whenever an app gains a new connection or a new AI client.
- **Who:** the technical owner chooses; the data owner agrees; a security reviewer joins for higher tiers.
- **Steps:**
  1. Pick one approach per connection: as the person, as the app, or read-only for the person.
  2. For agents, require end-user credentials unless a recorded decision (G4) allows otherwise.
  3. Record the approach and the reason on the app record.
- **Done when:** every connection to Dataverse has a recorded approach that the data owner accepted.
- **Evidence:** the access approach on the app record, and the decision behind any exception.
