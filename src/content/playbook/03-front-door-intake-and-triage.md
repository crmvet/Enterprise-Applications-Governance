# Chapter 3. The front door: intake and triage

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** the business (requesters and owners), citizen developers, AI builders, the PMO, and IT (the platform team) as triage
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G2](../guidelines/G02-name-owners-and-deputies.md), [G3](../guidelines/G03-set-a-risk-tier.md), [G16](../guidelines/G16-record-ai-use.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P02](processes.md#p02-tier-assessment-and-required-approvals), [P06](processes.md#p06-gap-check-and-claim), [P10](processes.md#p10-ai-draft-and-human-accept)

## Why this chapter exists

Requests arrive by email and chat. Apps appear that nobody asked for. An app built with AI in a day is running before anyone knows it exists. A single front door for every kind of app, quick for small things and thorough for big ones, is how the register stays true and how demand becomes visible.

## What good looks like

- One front door for apps, agents, AI-built apps, code apps and SaaS from other vendors.
- It asks governance questions only: purpose, business owner, data, audience, whether it uses AI, and the tier questions.
- The lowest tier takes minutes. The tier, not the tool, decides how much review follows.
- Every request gets a triage decision with a reason that the requester can see.
- The intake decision is tracked apart from delivery. An accepted idea can still be in design.

## How it works

1. **Ask.** A requester or builder describes the need, or registers an app they already built. They can do it from an MCP client such as Claude Code, or from the product's pages. Their AI client may draft the intake; it starts as a draft.
2. **Answer the tier questions.** Which data, read or change, who uses it, how independently it acts, what happens if it fails. The most serious answer sets the initial tier.
3. **Declare AI use.** Does the app use AI, or is it an agent? What may it do without a person?
4. **Check for duplicates.** Before submitting, look for an app that already does this, and its owner.
5. **Submit.** A submission is a request, not a decision.
6. **Triage.** The platform team decides: accept, park, redirect to reuse, or decline, with a typed reason. In the default configuration the lowest tier is accepted at once, under a rule a person approved once, so a small app does not wait.
7. **Name the owner.** On accept, the named business owner confirms ownership on an approval card. Nobody becomes an owner without their own yes.
8. **Route demand.** If the request needs money or central capacity, it goes to the PMO and the sponsor (chapter 6).
9. **Link the work.** The work item, plan or repository is linked to the application, never copied.

Apps already running but never registered reach the front door another way: an admin's gap check finds them, and a likely owner claims each one or starts an intake for it.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Submit an idea or register an app | Business owner | Requester or builder | | Draft the intake, propose tier answers, find duplicates |
| Triage: accept, park, redirect, decline | Platform admin | Platform admin | Architect (reuse), PMO (demand), security reviewer (initial risk) | Summarize and propose a route |
| Initial tier | Platform admin | Requester or builder | Business owner, security reviewer | Propose, never set |
| Accept ownership | The named business owner | Business owner | | Nothing |

## Guidelines this chapter applies

- **G1 Keep one register of business applications.** The register entry starts the day an app is proposed, before build starts.
- **G2 Name the owners of every app.** Intake names the business owner, who accepts on a card.
- **G3 Set a risk tier for every app.** The initial tier comes from the plain answers at intake.
- **G16 Record AI use on every app and agent.** Intake asks whether the app uses AI or is an agent, and what it may do on its own.
- **G22 Let the AI builder ask before it builds.** The builder's AI client checks for duplicates and asks the tier questions before it builds, and drafts what it learns into the intake.

## Processes this chapter uses

- **P01 Intake and triage.** The front door itself.
- **P02 Tier assessment and required approvals.** Works out the initial tier from the answers.
- **P06 Gap check and claim.** Brings apps that are already running to the front door as claims.
- **P10 AI draft and human accept.** The AI client drafts; the person submits; the triage decision needs a person.

## How the product is designed to support it

- It is designed to offer the same intake from any MCP client, from its pages, and through approval cards, so most people never need a back office.
- It is designed to keep intake drafts written by AI as drafts until a person submits them, and to let an AI client submit an intake on the person's behalf where the configuration allows it.
- It is designed to search the register for duplicates and suggest a match, never decide it.
- It is designed to keep the intake decision apart from delivery status, and to show the decision and its reason to the requester.
- It is designed to align its agent intake questions with Microsoft's agent front door guidance, and to treat Microsoft's front door sample as a reference, not a dependency.

## What you can tailor

- The intake questions per kind of application, and which answers trigger extra steps.
- Who triages and who is consulted.
- Whether the lowest tier needs a triage card. Add an approval rule for intake if you want one.
- Who can see other people's intakes.

## Microsoft guidance it applies

- [Intake and prioritize agent ideas](https://learn.microsoft.com/agents/center-of-excellence/intake-prioritization), checked 2026-11-21: score value, feasibility and resources the same way for every request, and record the decision and its reasoning.
- [Design an agent front door for your Center of Excellence](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door), checked 2026-11-22: the front door agent recommends a route and does not approve the work. "Designated reviewers make approval decisions".
- [Agent CoE Front Door sample for Copilot Studio](https://learn.microsoft.com/agents/center-of-excellence/agent-front-door-sample), checked 2026-11-21: a sample build of the front door, which "isn't an official Microsoft product".
- [Establish a Center of Excellence](https://learn.microsoft.com/power-platform/guidance/adoption/common-vision/establish-coe), checked 2026-11-21: Microsoft asks for an idea intake process with required documentation and evaluation criteria.
- [Power Platform inventory](https://learn.microsoft.com/power-platform/admin/power-platform-inventory), checked 2026-11-22: the inventory finds apps, flows, agents and sites already built, which is where the gap check looks.

## How AI changes this

- The builder's AI client can draft the intake, check the register for duplicates and propose tier answers in minutes, or register an app it has already built.
- Under G22 it asks before it builds: the tier, what the tier needs, the owners and the dependants. The answers shape the build, instead of a review at the end.
- Speed is a reason to make intake fast, not to skip it. The intake still starts as a draft, and triage still needs a person.

## Old paradigm to avoid

[4. Governance checked only at the end](17-old-paradigms-and-how-not-to-slide-back.md#4-governance-checked-only-at-the-end). Here it shows up as a front door that people reach only at go-live, after the AI has already chosen the data, the identity and the audience. Register at the idea, and let the AI client ask the tier questions before it builds.

**Slide-back signal:** apps first registered at go-live, or found as gap check claims.

## Done when

- Every new app, agent, AI-built app and SaaS tool enters through the front door.
- Every request has a triage decision with a reason.
- Every accepted app has a register entry, an initial tier, an AI use answer and a business owner who said yes.
