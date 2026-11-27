# D-10 Rules are configuration, the engine is code, and AI makes the configuration changes

- **Status:** Accepted. Superseded in part by D-26, which replaces the Dataverse binding (plug-ins and custom APIs). The rules stay configuration.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

The product has hard rules: who must approve, which gate applies, what each stage requires, and that AI never approves. The question was how to build them. Should they be no-code first, with business rules and low-code flows, or code in plug-ins?

## Options considered

- **No-code first.** Table business rules, low-code flows and maker-built logic. Familiar to makers, but spread across many places, easy to bypass from another path, and hard to test.
- **Code-first engine.** Code that reads configuration and checks every write, whoever the caller is.
- **Where configuration lives.** Configuration rows, JSON documents, or a deployed file such as a web resource that is edited in place.

## Decision

- **No hand-built rules.** Every rule is configuration data: tiers, approvals, gates, decision rights, stage requirements, change types, and everything else. Only an admin role can change it. The format is whatever performs best: rows, JSON, or both.
- **A code-first engine.** Code in the product reads the configuration and enforces it on every write, whoever the caller is: the app, MCP tools, imports or pipelines. Business rules, low-code flows and maker-built logic are not the enforcement layer. This reversed an earlier "no-code first" recommendation.
- **Nobody edits configuration by hand.** An AI makes configuration changes on an admin's instruction. Each change is recorded with "performed by" (the AI, on behalf of the admin) and versioned as a configuration set (D-07). Changing configuration never needs a rebuild or a redeploy.

As decided on this date, the engine was to run as synchronous Dataverse plug-ins and custom APIs.

## Why

Rules that live in many low-code places drift apart, and any path that skips them skips governance. One engine that every path must pass gives the same answer everywhere. Configuration as data lets each organization tailor the rules without code. AI-made, versioned changes keep a full record of who changed what and why.

## Consequences

- **The engine binding changed with D-26.** The engine is now designed as a portable .NET library inside the one Azure service. The API is the only write path. The rules stay configuration.
- **The library is portable on purpose.** It has no database driver and makes no outbound calls, and it targets more than one .NET version. A Dataverse edition could be added later without rewriting the rules. That is an option, not a plan.
- **Configuration lives as rows in the data store,** with JSON where a structure is complex. Presets ship as JSON seed files. The seed is never the live copy: the live copy is the active configuration version in the organization's own database. The engine reads a cached snapshot of that version.
- **A configuration change is itself a decision.** An admin tells an AI what to change. The AI drafts it, the engine validates it, and the change takes effect only when a configuration admin accepts it on an approval card. Later, [D-38](D-38-a-person-answers-an-approval-on-the-decision-page.md) moved the default place a person answers to the decision page in the web app.
- **Some rules no configuration can turn off.** They are part of the engine contract. AI never approves, and every decision is accepted by a person, apart from a short locked list.
- **The source is open.** The engine's source is public under the MIT License (D-27), so anyone can read how the rules are enforced.

## Related

- [D-06](D-06-risk-tiers-are-configuration.md), [D-07](D-07-decision-rights-are-configuration-with-presets.md), [D-09](D-09-the-product-has-its-own-mcp-server.md), [D-26](D-26-first-release-runs-in-the-clients-azure.md), [D-27](D-27-technical-work-is-public-under-mit.md)
- [Rules engine](../architecture/rules-engine.md)
- [Configuration and presets](../architecture/configuration-and-presets.md)
- [P11 Configuration change](../architecture/processes/p11-configuration-change.md)
- The engine: [src/engine/](../../src/engine/)
- The configuration schemas and presets: [src/config/](../../src/config/)
