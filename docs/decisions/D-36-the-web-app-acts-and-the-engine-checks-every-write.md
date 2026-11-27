# D-36 The web app acts, through the same engine as AI clients

- **Status:** Accepted. Amends D-18 and D-26.
- **Date:** 2026-11-23
- **Decided by:** the product owner
- **Title reworded:** 2026-11-27. The title was "The web app acts, and the engine checks every write". The tests show writes from the web app, an MCP client and a pipeline step reaching the same engine, not every write. The decision text below is kept as decided, and the file name stays.

## Context

Under D-18 and D-26, the web app's pages were read-only. They showed the portfolio, an application's full picture, its dependencies and its timeline. Every write went through the API, the product's MCP tools or an approval card. So a person needed an AI client to file an intake, propose a decision or assign an owner.

After the first demo of a sandbox install, the product owner found that the web app did not show the use cases the design describes. Data, owners, reviews and approvals had no screens, and the applications list had no search or filters. The question was whether the web app should act, or stay read-only with an AI client doing the writes.

## Options considered

- **Keep the web app read-only.** People read in the web app and write through an AI client or an approval card.
  - Pros: a smaller web app. The MCP tools stay the one way to write.
  - Cons: a person who does not use an AI client cannot do their work in the product. Owners, reviewers and approvers need a second tool for every act. A demo cannot show the use cases end to end.
- **The web app acts.** A signed-in person does their work in the web app, through the same operations an AI client uses.
  - Pros: every role can work in one place. A demo can show every use case.
  - Cons: many more screens to build and test. The API must tell a person's entry apart from an AI client's.

## Decision

The web app acts. A signed-in person files intakes, proposes and answers, records decisions, proposes owner changes, completes reviews, links documents, plans changes and approves, in the web app.

- **The engine checks the web app's writes** the same way it checks an AI client's: the same operations, the same rules, the same separation of duties and the same ledger.
- **A person decides every approval.** Nobody approves their own request where the rules say so. AI drafts, and a person accepts (D-28).
- **Every use case of the eleven processes is designed to get screens,** plus owners, reviews, approvals, data, decisions, documents, configuration and the gap check. The browser tests play the 23 demo scenarios against the demo estate, with a guided demo story. The use cases that the [processes index](../architecture/processes/README.md) lists as not built are not played.
- The applications register, with search, filters and sort, came first.

## Why

- Most of the people the product is designed for are owners, reviewers and approvers, not builders (D-26). Many of them may not work in an AI client.
- Safety does not depend on the screen. The engine holds the rules, so a write from the web app goes through the same engine, rules and separation of duties as a write from an AI client. An AI client's write also meets the AI rules.
- AI drafts and a person accepts (D-28). The person needs a place to accept, and the web app is that place.
- A demo has to show the use cases end to end, with fictional data, not only describe them.

## Consequences

- D-18 and D-26 are amended: the web app's pages are no longer read-only.
- **The person's entry.** The product's API takes the web app's own sign-in tokens as the person. Any other client on the API, an AI client among them, is treated as an AI client: on the API it gets the read tools only, and it writes through the MCP endpoint, where the AI rules apply.
- **Some acts belong to a person only.** A few tools are served only to a person in the web app and never to an AI client, among them answering an approval (D-38), completing a review and closing a change.
- **Status.** The web app, the API, the MCP server and the engine are built and tested. They run as a demo install with fictional data. There is no release yet. Browser tests drive each scenario at desktop and phone widths.
- D-38 makes the decision page the place a person answers an approval. D-39 lets a demo presenter act as each fictional person. D-40 keeps the default rule on self-approval.

## Related

- [D-18 Modern pages are the core interface](D-18-pages-are-the-core-interface.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [D-28 Govern AI development of the apps](D-28-govern-ai-development-of-the-apps.md)
- [D-38 A person answers an approval on the decision page](D-38-a-person-answers-an-approval-on-the-decision-page.md)
- [D-39 The demo switches roles with a demo-only persona switch](D-39-the-demo-switches-roles-with-a-demo-only-persona-switch.md)
- [D-40 Self-approval stays as the Balanced preset has it](D-40-self-approval-stays-as-the-balanced-preset-has-it.md)
- [Screens](../architecture/screens.md)
- [The processes](../architecture/processes/README.md)
- [Sign-in and security](../architecture/sign-in-and-security.md)
- [MCP tools](../architecture/mcp-tools.md)
- The web app's source: [src/ui/](../../src/ui/)
- The browser tests: [src/tests/e2e/](../../src/tests/e2e/), and the scenarios they play: [src/docs/demo/scenarios.md](../../src/docs/demo/scenarios.md)
