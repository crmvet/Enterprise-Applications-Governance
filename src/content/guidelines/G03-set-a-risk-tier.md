# G3. Set a risk tier for every app, on tiers you define

- **Number:** G3
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 4 (Risk tier and what each tier needs)
- **Cited in chapters:** 0, 1, 3, 4, 8, 12, 17, 18
- **Processes:** P01, P02

**Rule.** Give every application a risk tier, set from plain answers about its data, its audience, its autonomy and its impact. The tier decides how much review, approval and monitoring the app needs. Your organization decides how many tiers it uses.

**Why it matters.** One checklist for everything over-governs simple apps and under-governs critical ones. Microsoft advises matching the depth of governance to the risk.

**How the product is designed to support it.** It is designed to keep tiers as configuration records: three by default, mapped to Microsoft's support tiers, Copilot Studio zones and agent risk tiers, and any number when an organization needs more.

## Microsoft anchors

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Match the depth of governance to the risk each agent poses". It sets three tiers (low, medium and high risk) with required controls for each. "The clearest risk signal is the assist-to-execute line."
- [User support: ongoing production solution support](https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions#define-tiers-of-applications), checked 2026-11-22: three application tiers, Productivity, Important and Critical. "When defining support models, consider a graduation path."
- [Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2) (Copilot Studio), checked 2026-11-22: Zone 1 Citizen Development, Zone 2 Partnered Development and Zone 3 Professional Development.

## Working shape

- **When:** at intake, and again when an app's data, audience or autonomy changes.
- **Who:** the business owner answers; a security or risk reviewer confirms the higher tiers; the platform team keeps the tier model.
- **Steps:**
  1. Answer the tier questions: which data the app touches, whether it reads or changes data, who uses it, and how independently it acts.
  2. Let the most serious answer set the tier. Anyone may raise a tier. Lowering one needs a recorded decision (G4).
  3. Apply what the tier requires: approvals, reviews, monitoring and documents.
  4. Move the app up a tier when its scope or autonomy grows.
- **Done when:** every live app has a current tier, and the controls that tier requires are in place.
- **Evidence:** the tier answers, the resulting tier, and any decision that changed it.
