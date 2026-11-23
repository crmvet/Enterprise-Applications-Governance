# Chapter 4. Risk tier and what each tier needs

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** the business owner, IT (the platform team and security), architects, change control
- **Guidelines:** [G3](../guidelines/G03-set-a-risk-tier.md), [G4](../guidelines/G04-record-each-decision.md), [G12](../guidelines/G12-approve-production-changes-on-record.md), [G20](../guidelines/G20-keep-required-documents-current.md)
- **Processes:** [P02](processes.md#p02-tier-assessment-and-required-approvals), [P04](processes.md#p04-architecture-review), [P07](processes.md#p07-documentation-tracking)

## Why this chapter exists

One checklist for everything over-governs a team's tracker and under-governs an app that pays suppliers. The tier is the single dial that sets how much review, approval, monitoring and documentation an app needs. It must come from plain answers, be decided by a person, and be easy to raise when an app grows.

## What good looks like

- Every application has a current tier, set from plain answers and accepted by a person.
- The most serious answer sets the tier. Anyone may raise it. Lowering it needs a recorded decision.
- What each tier needs is written down: approvals, reviews, documents, review cadence and the gate level.
- Your tiers map to Microsoft's words, so people who read Microsoft's guidance know what you mean.

## The tier questions (default)

| Question | Lowest tier answer | Middle tier answer | Top tier answer |
|---|---|---|---|
| Does it change data? | Reads only, or changes only the person's own items | Changes shared business data as the signed-in person | Changes data under its own identity, or in a system of record |
| Does it touch sensitive data? | No | Internal data only | Personal, financial, regulated or otherwise restricted data |
| Who can reach it? | The builder or a small team | A department or business unit | The whole organization, or anyone outside it |
| Does an agent act on its own? | No, it assists a person who stays in the loop | It answers or routes; a person acts | It executes changes in systems |
| What happens if it fails for a day? | Little | A team is slowed | Revenue, compliance or trust is hit |

## The default tiers and Microsoft's words

The default configuration is designed to ship three tiers. Each mapping is itself a configuration record, so an organization with five tiers maps those too. No Microsoft page joins these vocabularies; the mapping is the product's own default.

| Default tier | Power Platform support tier | Copilot Studio zone | Agent risk tier |
|---|---|---|---|
| 1 | Productivity | Zone 1, citizen development | Low |
| 2 | Important | Zone 2, partnered development | Medium |
| 3 | Critical | Zone 3, professional development | High |

## What each tier needs (default)

| Need | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|
| Prioritization | Only when central help is needed | When central capacity or money is needed | Always |
| Design review | Self-check | When a trigger fires | Design review, with architecture decision records |
| Tier decision | Platform admin | Platform admin | Platform admin and security reviewer, plus the business owner's risk acceptance |
| Change authority | Business owner | Business owner | Change manager |
| Security review | No | When a trigger fires | Before go-live |
| Required documents | A wiki page | Wiki page and runbook | Wiki page, runbook, architecture decision records and a support guide |
| Owner and access checks | Yearly | Twice a year | Quarterly |
| Tier check and benefit review | Yearly | Yearly | Yearly |

## How it works

1. **Answer.** The business owner, or the builder, answers the tier questions. An AI client may propose answers from the intake; it never sets the tier.
2. **Compute and explain.** The most serious answer gives the computed tier. Ask why, and the answer that drove it is shown.
3. **Decide.** A tier decision goes to the platform admin, and at the top tier to the security reviewer as well. The business owner accepts the residual risk of a top-tier app.
4. **Raise or lower.** Anyone may raise the tier on the same decision. Lowering it below the computed tier needs a typed reason and the full approval.
5. **Apply what the tier needs.** Approvals per decision type, gate requirements, documents, review cadence and gate levels follow from the tier.
6. **Trigger the reviews.** Some answers add a review on their own: an architecture review, a security review or a responsible AI review.
7. **Re-tier when things change.** A new data use, a wider audience, more autonomy, or a graduation trigger opens a new tier assessment.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Own the tier rules and what each tier needs | Configuration admin | Platform admin | Security reviewer, change manager, architect | Draft a change |
| Confirm an app's tier | Platform admin | Builder | Business owner, data owner, security reviewer, architect | Propose answers and explain the result |
| Accept residual risk at the top tier | Business owner | Platform admin | Data owner, security reviewer, architect | Nothing |

## Guidelines this chapter applies

- **G3 Set a risk tier for every app, on tiers you define.** The questions, the most serious answer rule, and moving up a tier when scope grows.
- **G4 Record each decision in one line.** The tier is a decision, and lowering it is a recorded decision with a reason.
- **G12 Approve every production change on record.** The tier decides which approvals a change needs and who may give them.
- **G20 Keep each app's required documents current.** The tier decides which documents an app must have.

## Processes this chapter uses

- **P02 Tier assessment and required approvals.** Turns answers into a tier decision and works out everything the tier needs.
- **P04 Architecture review.** Starts when a tier answer adds an architecture review.
- **P07 Documentation tracking.** Starts tracking the documents the tier now requires.

## How the product is designed to support it

- It is designed to keep tiers, questions, answers and what each tier needs as configuration records, three tiers by default and any number when needed.
- It is designed to store the tier as a decided value, never a formula, so approvals and reminders can react to it.
- It is designed never to branch on a fixed tier number, but to read each tier's own configuration.
- It is designed to answer "what does my tier need?" in plain words, with links to the guidelines.

## What you can tailor

- How many tiers, their names and their mapping to Microsoft's words.
- The questions, the answers and which answer implies which tier.
- Which answers add a review, and what each tier requires.

## Microsoft guidance it applies

- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: "Match the depth of governance to the risk each agent poses", three tiers with required controls, and "The clearest risk signal is the assist-to-execute line."
- [User support: ongoing production solution support](https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions#define-tiers-of-applications), checked 2026-11-22: three application tiers, Productivity, Important and Critical.
- [Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2) (Copilot Studio), checked 2026-11-22: Zone 1 Citizen Development, Zone 2 Partnered Development and Zone 3 Professional Development.

## How AI changes this

- Effort no longer signals risk. An AI can build a critical app in a day, and a small-looking app can change a system of record. The tier comes from the answers, never from how long the build took or which tool made it.
- The answers on autonomy and on sensitive data, including data that reaches an AI model, carry more weight than before.
- The AI client proposes answers and explains them. A person sets the tier.

## Old paradigm to avoid

[1. One board approves every change](17-old-paradigms-and-how-not-to-slide-back.md#1-one-board-approves-every-change). Here it shows up as one checklist for every app, whatever its tier. Against AI builders that blocks the small apps and gives the critical ones no more attention than the rest.

**Slide-back signal:** lowest-tier apps get the full review, or every app sits in the same tier.

## Done when

- Every live app has a current tier, decided by a person, with the answers behind it.
- What each tier needs is published and matches the configuration.
- Every tier that was lowered has a recorded decision with a reason.
