# Chapter 2. Parties, roles and decision rights

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Organization
- **Parties:** all seven: the business, IT, AI, citizen developers, the PMO, change control and architects; with security, data owners and auditors inside them
- **Guidelines:** [G2](../guidelines/G02-name-owners-and-deputies.md), [G4](../guidelines/G04-record-each-decision.md), [G14](../guidelines/G14-grant-access-through-groups.md)
- **Processes:** [P08](processes.md#p08-owner-leaves), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

Governance fails at the hand-offs. The business asks, IT builds or blocks, the PMO ranks, change control approves, architects review, makers ship, and AI now drafts most of the text in between. When nobody knows who decides, decisions wait, or the loudest voice wins. This chapter names the parties, the roles inside them, and one accountable role for each decision.

## The seven parties

| Party | What they want | What they own |
|---|---|---|
| Business (business owner, sponsor, data owner, end users) | Solutions that fit the work, soon, at a known cost | The application as a product: its future, its data use, its residual risk at the top tier |
| IT (platform team, admins, security, support) | A safe, supported estate that does not grow out of control | The platform and its Microsoft tools, triage, the confirmed tier, deployments, the product's configuration |
| AI (AI building tools, agents as products, AI helpers) | Speed without losing control | Nothing on their own. Every agent has a human owner. AI never holds an accountability |
| Citizen developers (makers, champions, fusion teams) | To build and ship, and not be left alone supporting an app that grew critical | The build and support of a low-tier app; a share of a fusion team's app |
| PMO (portfolio and demand) | Money and people on the highest value | Ranking demand, the portfolio review, the benefit check. Plans stay in their own tools |
| Change control (change manager, change board, release manager) | No surprise changes in production | Change types, standard change models, the change authority, the schedule and windows |
| Architects (enterprise, solution, design authority) | Reuse, sound integration, decisions that hold | Standards, design reviews, architecture decisions, exceptions to standards |

One person may hold several roles, especially early on. The playbook still keeps the roles apart, so duties stay clear when people change.

## What good looks like

- Every application names a business owner, a technical owner and a data owner, each a person, each with a deputy.
- Every kind of decision has exactly one accountable role.
- People get roles only through Microsoft Entra groups, never one by one.
- When an owner leaves, a deputy acts at once and a successor is named before access goes.
- Three things hold the parties together: one application record every party points to, one decision log every party writes to, and one value stream with one decider per stage.

## How it works

1. **Agree the roles.** Start from the default role list below. Rename, merge, split or drop roles to fit your organization.
2. **Bind each role to a group.** Create one Entra group per role. Give people the role by adding them to the group.
3. **Agree the decision rights.** Use the matrix below as the default. Keep one accountable role per row. Where a decision needs a second yes, make it an approval rule with two approvers, not a second accountable.
4. **Name owners per app.** At registration, name the owner roles for the application, each with a deputy and a start date.
5. **Plan for leavers.** Agree the handover time. When an owner leaves, the deputy acts and a successor accepts on a card.
6. **Record every decision.** Each decision is one line in the shared decision log: what, who, when, why.

## The default roles

| Kind | Roles |
|---|---|
| Owner roles, per application | Business owner, sponsor, data owner, technical owner, builder, process owner, support owner, security owner |
| Product roles, through Entra groups | Requester, maker, application owner, data owner, architect, change manager, change board, PMO, AI lead, security reviewer, platform admin, configuration admin, auditor, reader |
| Workload roles, for non-human workloads only | A deployment recorder for pipeline steps, and an approval writer for the flows that send approval cards |

Pipelines are not people, and Entra does not pass a group's app roles to a service principal. So in the default configuration a non-human workload holds its workload role directly, through a recorded grant with a reason, the decision behind it and an expiry date. People never hold a workload role, and agent identities never get a grant.

## Who decides (default decision rights)

A shortened view. R does the work, A answers for the outcome (exactly one), C gives input before, I is told after, S is an AI draft that a person accepts.

| Decision or task | Business owner | Data owner | Builder | Platform admin | Security | PMO | Change manager | Architect | AI |
|---|---|---|---|---|---|---|---|---|---|
| Submit an idea or register an app | A | | R | I | | I | | I | S |
| Triage: accept, park, redirect, decline | C | | I | A/R | C | C | | C | S |
| Confirm an app's tier | C | C | R | A | C | | I | C | S |
| Approve a data use | I | A | I | R | C | | | C | |
| Authorize a normal change, lower tiers | A | C | I | R | | | I | | S |
| Authorize a normal change, top tier | C | C | I | R | C | I | A | C | S |
| Approve an emergency change | C | | I | R | C | I | A | | |
| Decide an app's future | A | C | I | C | | R | I | C | |
| Name a successor when an owner leaves | A | | | R | | I | | | S |

The matrix is a hybrid, hub-and-spoke default. A more central organization moves some accountabilities from the business owner to the platform team; a more federated one moves them the other way.

## Guidelines this chapter applies

- **G2 Name the owners of every app, each with a deputy.** The owner roles, the deputies and the one accountable role per decision come from here.
- **G4 Record each decision in one line.** Every decision in the matrix ends as an entry in the shared decision log.
- **G14 Grant access through groups.** Roles reach people only through Entra groups, and a role assigned directly to a person is a finding.

## Processes this chapter uses

- **P08 Owner leaves.** The deputy acts, open approvals move, and a successor accepts on a card.
- **P11 Configuration change.** Roles, their group bindings and the decision rights are configuration. Changing them is a recorded configuration change, accepted by the configuration admin.

## How the product is designed to support it

- It is designed to hold roles, decision rights and approval rules as configuration records that each organization can rename and remap.
- It is designed to resolve each product role to the Entra group bound to it, to read who holds a role from Entra at the moment it matters, and to flag any role given to a person directly.
- It is designed to record owner roles on each application, with deputies, start dates and end dates, and to flag apps with no owner or no deputy.
- It is designed to send each decision to the holder of its accountable role on an approval card, and to keep the answer in the shared decision log.

## What you can tailor

- The role list: rename, merge, split or drop any role, including the AI lead.
- The decision rights: who is accountable, responsible, consulted and informed for each decision type, by tier, stage or kind of change.
- Which owner roles need a deputy, and how many holders each may have.

## Microsoft guidance it applies

- [Define roles and responsibilities](https://learn.microsoft.com/power-platform/guidance/adoption/roles) (Power Platform adoption), checked 2026-11-22: "Ensure each task has one accountable person". The adoption RACI names roles such as the PMO and enterprise architects.
- [Define roles, responsibilities, and decision rights](https://learn.microsoft.com/agents/center-of-excellence/roles-responsibilities) (agentic Center of Excellence), checked 2026-11-22: "Assign exactly one accountable role per task." A role owned by "IT" is a role no one owns.
- [Review the manage checklist](https://learn.microsoft.com/microsoft-copilot-studio/guidance/manage-checklist) (Copilot Studio), checked 2026-11-22: "Assign access through Microsoft Entra ID groups, not individuals".
- [Manage group teams](https://learn.microsoft.com/power-platform/admin/manage-group-teams), checked 2026-11-22: group memberships are cached at sign-in and refresh after 8 hours of continuous sign-in, so read role holders from Entra.

## How AI changes this

- AI now sits in every hand-off and drafts most of the text between the parties: the intake, the tier answers, the decision record and the change notes. Hand-offs get faster, but no accountability moves to the AI.
- Each decision right says what AI may do (the AI column in the matrix), and every agent has a human owner.
- Roles reach people only through groups, so an AI client acting for a person can do no more than that person may do. Giving builders AI clients does not widen anyone's rights.

## Old paradigm to avoid

[1. One board approves every change](17-old-paradigms-and-how-not-to-slide-back.md#1-one-board-approves-every-change). Here it shows up as a committee named as the accountable role, so every decision waits for its next meeting while AI builders produce changes every day. Keep one accountable role per decision, weighted by tier, and keep the board for exceptions.

**Slide-back signal:** a committee listed as accountable, or decisions that wait for a meeting.

## Done when

- The role list and the decision rights matrix are agreed, with one accountable role per row.
- Every role is bound to an Entra group, and no role is held directly.
- Every live app has its owners and deputies named.
- The handover time for leavers is agreed and written down.
