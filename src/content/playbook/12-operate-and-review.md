# Chapter 12. Operate and review

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Shared
- **Parties:** the business owner, IT (the platform team), the PMO, document owners
- **Guidelines:** [G2](../guidelines/G02-name-owners-and-deputies.md), [G3](../guidelines/G03-set-a-risk-tier.md), [G14](../guidelines/G14-grant-access-through-groups.md), [G20](../guidelines/G20-keep-required-documents-current.md)
- **Processes:** [P02](processes.md#p02-tier-assessment-and-required-approvals), [P07](processes.md#p07-documentation-tracking), [P08](processes.md#p08-owner-leaves)

## Why this chapter exists

Most of an app's life is after go-live. Owners move on, audiences grow, documents go stale, and access piles up. "Nobody knows if this app has a runbook, or whether its wiki page is still true." Periodic reviews, set by tier and backed by reminders, keep the register true without a weekly meeting.

## What good looks like

- Every live app is supported and monitored at the level its tier needs.
- Reviews come due on a cadence set by tier: owner checks, access checks, tier checks and benefit reviews.
- Reminders go to the owner, then to the deputy, without anyone chasing by hand.
- Each required document is current, or its owner knows it is stale.
- Monitoring, usage and recommendations stay in Microsoft's admin centers, and are linked.

## The default reviews

| Review | What it checks | Cadence (default) |
|---|---|---|
| Owner check | Owners and deputies are still right and still here | Yearly at the lowest tier, twice a year in the middle, quarterly at the top |
| Access check | Who holds each role, read from Entra | The same cadence as the owner check |
| Tier check | The answers behind the tier still hold | Yearly |
| Benefit review | The expected benefit against the result | Yearly |

## How it works

1. **Run at the tier's level.** The builder supports a lowest-tier app; IT supports the higher tiers. Health, usage and recommendations are read in Microsoft's admin centers and linked from the register.
2. **Reviews come due.** The next review date follows from the tier's cadence. When it comes, the review opens and the owner is reminded.
3. **Escalate.** If the owner does not act within the set days, the reminder goes to the deputy, then to the accountable role.
4. **Check owners.** Confirm each owner and deputy. An admin can also run an on-demand owner check that reads each owner's account state from Entra. A leaver starts the handover (chapter 2).
5. **Check access.** Review who holds each role through its Entra group, with Entra access reviews where you are licensed for them. Remove what is no longer needed.
6. **Check the tier.** If the data, audience or autonomy changed, re-answer the tier questions.
7. **Check documents.** Each required document is current or stale. A document goes stale when its freshness time passes since the last check. The owner's own tool updates it, and the owner records the check.
8. **Close with a decision.** Each review ends with its outcome on record. The portfolio review decides the app's future (chapter 6).

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| The app is supported and monitored at its tier | Business owner | Platform admin (builder at the lowest tier) | Security reviewer | Summarize health from Microsoft's pages |
| Review who has access | Business owner | Platform admin | Data owner, security reviewer | Prepare the list from Entra |
| Confirm a gate-relevant document is current | Technical owner | Technical owner | Business owner | Mark it current as a claim, until the owner confirms |
| Move a leaver's open approvals | Platform admin | Platform admin | | Nothing |

## Guidelines this chapter applies

- **G20 Keep each app's required documents current.** Required documents, their status and freshness, written in the team's own tools.
- **G2 Name the owners of every app.** The owner check keeps owners and deputies true.
- **G14 Grant access through groups, review it.** The access check, on a cadence set by tier.
- **G3 Set a risk tier for every app.** The tier check moves an app up when its scope grows.

## Processes this chapter uses

- **P07 Documentation tracking.** Missing and stale documents, reminders, and confirmation for gate-relevant documents.
- **P08 Owner leaves.** The owner check and the handover.
- **P02 Tier assessment and required approvals.** The tier check, and the new cadence when a tier changes.

## How the product is designed to support it

- It is designed to set each app's next review date from its tier's cadence, open reviews when they come due, remind the owner and escalate to the deputy.
- It is designed to record each reminder, so "who was told, and when" can be shown.
- It is designed to work out missing documents from configuration and to move documents from current to stale when their freshness time passes.
- It is designed to keep "current" set by an AI client as a claim, for documents a gate depends on, until the document owner confirms it.
- It is designed to link to Microsoft's monitoring, usage and recommendations, and to copy none of them.

## What you can tailor

- The review types and their cadence per tier.
- Reminder lead times, repeats and when to escalate.
- Required documents per kind and tier, and how long each stays fresh.

## Microsoft guidance it applies

- [Monitoring in the Power Platform admin center](https://learn.microsoft.com/power-platform/admin/monitoring/monitoring-overview), checked 2026-11-21: health metrics, logs and recommendations for apps, flows and agents.
- [Actions and recommendations in the Power Platform admin center](https://learn.microsoft.com/power-platform/admin/power-platform-advisor), checked 2026-11-21: weekly recommendations such as apps without a valid owner, apps shared with everyone and unused apps.
- [What are access reviews?](https://learn.microsoft.com/entra/id-governance/access-reviews-overview) (Microsoft Entra ID Governance), checked 2026-11-22: access reviews need Microsoft Entra ID Governance or Microsoft Entra Suite licenses.
- [About wikis, READMEs, and Markdown](https://learn.microsoft.com/azure/devops/project/wiki/about-readme-wiki) (Azure DevOps), checked 2026-11-22: a wiki published from code keeps documentation in step with the code base.

## How AI changes this

- The builder's AI tool writes and updates the app's documents as part of each change, in the team's own repository, wiki or SharePoint. A document no longer waits for a later phase.
- The AI can prepare the access list from Entra and the owner check, so each review starts with the facts gathered. The owner still decides.
- An AI client may mark a document current. For a document a gate depends on, that stays a claim until its owner confirms it.
- Reviews come due by tier and reach the owner, then the deputy, without anyone chasing by hand.

## Old paradigm to avoid

[9. Documentation as a phase after the build](17-old-paradigms-and-how-not-to-slide-back.md#9-documentation-as-a-phase-after-the-build). Here it shows up as documents written just before an audit, often by someone who did not make the change, and drifting from the app the rest of the time.

**Slide-back signal:** missing or stale documents rise, or AI "current" claims stay unconfirmed.

## Done when

- Every review is complete by its due date, or escalated.
- No live app goes without a current owner and deputy.
- No live app has a required document that is missing, or stale beyond the agreed time.
