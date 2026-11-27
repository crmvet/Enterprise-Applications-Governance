# D-38 A person answers an approval on the decision page; Teams and Outlook link to it

- **Status:** Accepted. Amends D-16 and D-26.
- **Date:** 2026-11-23
- **Decided by:** the product owner

## Context

D-16 made approval cards in Outlook and Teams the way a person answers, and D-26 kept them on Azure: a small group of runner flows sends Power Automate approval cards and writes each answer back to the product. The design's working default was cards first, with a page of the product's own only where a security review asked for one.

D-36 then made the web app act, and each decision already had its own page in the web app. The question was where a person should answer an approval: in a card, or on that page.

## Options considered

- **Cards first, the page later.** People answer in Outlook or Teams.
  - Pros: people answer where they already work.
  - Cons: the answer comes back through the approval runner, so who answered is read from the card's output, not proved by the person's own sign-in to the product. The runner needs a Power Automate Process license, and approvers need Power Automate rights. The product cannot ask for a fresh sign-in before an answer given in a card.
- **The web page now, cards later.** People answer on the product's decision page, and Teams and Outlook messages link to it.
  - Pros: one place where an answer is written. The person's own sign-in token proves who answered, the organization's Conditional Access applies, and a fresh sign-in can be asked for. Answering needs no Power Platform license.
  - Cons: one more click, from the message to the page.

## Decision

The web page now, cards later.

- **The decision page is the default place a person answers,** and the person's sign-in token proves who answered.
- **Teams and Outlook messages become notices** that link to the page. Answering inside a card stays an option for each install, through the approval runner.
- **Step-up sign-in** for an approval answer is a setting, off in the demo. With it on, a card can only link to the page.

## Why

- Proof of who answered matters more than where the answer is typed. A token the product validates is stronger proof than a card's output relayed by a flow.
- AI never approves (D-02, D-09). On the page, the engine checks that the person who answers is a person signed in as a user, and the one the request asks, before anything is written.
- The organization's own Conditional Access policy can govern the answer.
- It keeps D-26's aim that no user needs a Power Platform license for the product.

## Consequences

- D-16 and D-26 are amended: approval cards are no longer the default way a person answers.
- **Answering is a tool served only to a person in the web app.** The MCP endpoint never lists it, and an AI client that calls it on any entry is refused.
- **The engine's approval answer check** runs before anything is written. The person must be signed in as a user, be one the request asks, hold the role or owner role now, and not be kept out by a separation rule. The request must still be pending and not expired. A typed reason is needed to reject, and to approve where the rule asks for one. A refused answer writes nothing.
- **The page asks the same check first.** It shows Approve and Reject only when the answer would count. Otherwise it names the rule that keeps the person out, so the page and the engine never disagree.
- **Step-up sign-in** is a setting of the API with three modes: off, a sign-in within a set number of minutes, or a Conditional Access authentication context. When it is on, it applies to every approval answer, whatever the tier, and the approval runner refuses an answer relayed from a card, so a card can only link to the page.
- **The approval runner, as built.** Its routes are off unless an install switches them on. With step-up sign-in on, the runner is told to send a notice that only links to the decision page, and its outcome route refuses an answer relayed from a card. With step-up sign-in off, the default, the runner is told to send an answerable card: its flow template creates a Power Automate approval and relays the answer to the outcome route, through the approval writer workload role. The product's side of both paths is tested through a stand-in for the runner, and the flow template through checks of its files. The flows have not run in a real tenant.
- **Status.** The decision page, the answer check and step-up sign-in are built and tested. They run as a demo install with fictional data. There is no release yet. The Teams and Outlook messages come from the approval runner's flow template, which is designed to send them.

## Related

- [D-02 How people and AI clients use the product](D-02-how-people-and-ai-clients-use-the-product.md)
- [D-09 The product can have its own MCP server](D-09-the-product-has-its-own-mcp-server.md)
- [D-16 No Copilot or Teams agent](D-16-no-copilot-or-teams-agent.md)
- [D-26 The first release runs in the organization's own Azure subscription](D-26-first-release-runs-in-the-clients-azure.md)
- [D-36 The web app acts, and the engine checks every write](D-36-the-web-app-acts-and-the-engine-checks-every-write.md)
- [D-40 Self-approval stays as the Balanced preset has it](D-40-self-approval-stays-as-the-balanced-preset-has-it.md)
- [Sign-in and security](../architecture/sign-in-and-security.md), sections "Approvals and the responder check" and "Step-up sign-in"
- [Security model, design document 26](../design/step2/26-security-model.md)
- The engine's approval answer check: [src/engine/Product.Engine.Core/Operations/ApprovalAnswer.cs](../../src/engine/Product.Engine.Core/Operations/ApprovalAnswer.cs), with its fixtures in [src/fixtures/approval-answer/](../../src/fixtures/approval-answer/)
- Step-up sign-in: [src/adapters/azure/Product.Azure.Host/Identity/StepUp.cs](../../src/adapters/azure/Product.Azure.Host/Identity/StepUp.cs)
- The decision page: [src/ui/harness/src/pages/Decision.tsx](../../src/ui/harness/src/pages/Decision.tsx)
