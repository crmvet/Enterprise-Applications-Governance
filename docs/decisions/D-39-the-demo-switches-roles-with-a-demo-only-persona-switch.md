# D-39 The demo switches roles with a demo-only persona switch

- **Status:** Accepted
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

A demo install shows a fictional estate (D-33). With D-36 the web app acts, and with D-38 a person answers an approval on the decision page. So a demo has to show work passing between people: a requester files an intake, an owner proposes, an approver answers, and an admin changes the rules. Each of these people holds different roles, and roles come only through Entra groups (D-11). Signing in and out as a separate account for each role is slow, and it needs real accounts in a directory for people who are fictional.

## Options considered

- **Real test accounts for every role.** The presenter signs in as each one in turn.
  - Pros: every step is a real sign-in.
  - Cons: real accounts and groups for every fictional person. Slow to present, with a new sign-in for each switch.
- **A demo-only persona switch.** The presenter signs in once and acts as any fictional persona of the demo estate.
  - Pros: fast and clear to present. No real accounts for fictional people.
  - Cons: it must never reach a real install, and anyone signed in to the demo can act as any persona.

## Decision

A demo-only persona switch.

- **Only in a Demo install.** The signed-in operator may act as any fictional persona the install lists. Outside the environment named Demo the switch is ignored, and a server that lists demo personas refuses to start.
- **Every write is stamped** with the persona as the actor and "demo operator" as the client. The operator's own ID is never written.
- **One real approval between real people** is designed to be proved by a browser test with three real test users.

## Why

- A demo has to show every role's work in one sitting, with fictional data that nobody mistakes for real (D-33).
- The engine checks a persona's write exactly as it checks a person's: the persona's roles, the rules and the separation of duties all apply.
- The stamp keeps the demo honest. Every row it writes says that a demo operator acted as the persona.

## Consequences

- **What a persona is:** a name, a label, a fictional object ID and fictional groups. Both IDs follow a fixed placeholder pattern that a real Entra object ID never matches, so no real person or group can be acted as. The persona's ID and groups replace the operator's for the request, and the operator's own groups and roles do not carry over.
- **The web app** shows an "Acting as" menu marked Demo, only when the install lists demo personas. A name the install does not list is refused.
- **AI clients.** On a demo install, the operator's AI client may act as a persona on the MCP endpoint. It acts as an AI client for that persona, under every AI rule, and it never approves.
- **Limits.** Anyone who can sign in to a demo install can act as any persona, the configuration admin among them. The install notes ask for assignment to be required on the API's app registration, so that only demo operators sign in. Live directory reads refuse a persona: a demo install reads its people from the demo personas.
- **Status.** The persona switch is built and tested, and the demo install uses it. The browser test with three real test users needs real accounts in a directory and is not built yet.

## Related

- [D-11 Roles come only through Entra groups](D-11-roles-only-through-entra-groups.md)
- [D-33 A demo install shows the fictional estate](D-33-a-demo-install-shows-the-fictional-estate.md)
- [D-36 The web app acts, and the engine checks every write](D-36-the-web-app-acts-and-the-engine-checks-every-write.md)
- [D-38 A person answers an approval on the decision page](D-38-a-person-answers-an-approval-on-the-decision-page.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [Security model, design document 26](../design/step2/26-security-model.md)
- The host's persona handling: [src/adapters/azure/Product.Azure.Host/WebApp/ProductDemoPersonas.cs](../../src/adapters/azure/Product.Azure.Host/WebApp/ProductDemoPersonas.cs), with its tests in [DemoPersonaTests.cs](../../src/adapters/azure/tests/Product.Azure.Tests/DemoPersonaTests.cs)
- The web app's menu: [src/ui/harness/src/app/DemoPersonaMenu.tsx](../../src/ui/harness/src/app/DemoPersonaMenu.tsx)
- Running the demo: [src/demo/kit/README.md](../../src/demo/kit/README.md)
