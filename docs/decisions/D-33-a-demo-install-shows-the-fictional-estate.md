# D-33 A demo install shows the fictional estate; the real bootstrap comes next

- **Status:** Accepted
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

A fresh install starts with an empty database. Until version 1 of the configuration is in force, every page and tool answers `configuration_unavailable`. The engine's bootstrap of version 1 is built, but no route or MCP tool calls it yet (guide S1, step 7). The local `seed` command loads a fictional demo estate, but it runs only in the Development environment, on purpose, because it writes past the engine. The product owner wants to see the product running in a sandbox subscription.

## Options considered

- **A demo install now, the real bootstrap route next.** The sandbox shows the fictional estate soon, clearly marked, and the real install path follows.
- **The real bootstrap route only.** The sandbox would prove the real install path, but it stays empty until the route exists.
- **A demo install only.** Fastest, but it proves nothing about a real install.

## Decision

A demo install now; the real bootstrap route next.

- A switch, `PRODUCT_DEMO` in the azd template (`Product:WebApp:Demo` in the app settings), tells the web app to show a "Demo, fictional data" badge on every page.
- The `seed` command also runs in an environment named `Demo`, into an empty store. It still refuses every other environment and any store that already has a version in force.
- The demo binding must bind the product roles to the install's own Entra group IDs. That file is kept out of source control.

## Why

The product can be seen working end to end without pretending the demo data is real, and without waiting for the bootstrap route. The badge keeps the fictional data from being read as a real estate.

## Consequences

- A demo install is never a real install: its data did not go through the engine, and no person is recorded as the decider of version 1.
- The bootstrap route of version 1 (guide S1, open points 1, 2 and 5) is the next piece of build work.

## Related

- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [D-32 The web app is built on shadcn/ui](D-32-the-web-app-uses-shadcn-ui.md)
- [The Azure template](../../src/adapters/azure/infra/README.md)
