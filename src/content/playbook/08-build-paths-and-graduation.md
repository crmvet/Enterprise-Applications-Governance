# Chapter 8. Build paths: citizen, fusion, pro and AI-built, and graduation

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** citizen developers, IT, AI builders, architects, the business owner
- **Guidelines:** [G3](../guidelines/G03-set-a-risk-tier.md), [G7](../guidelines/G07-narrow-app-identities.md), [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G19](../guidelines/G19-review-anonymous-access.md), [G21](../guidelines/G21-give-ai-builders-a-paved-road.md), [G23](../guidelines/G23-say-who-may-build-with-ai.md)
- **Processes:** [P01](processes.md#p01-intake-and-triage), [P02](processes.md#p02-tier-assessment-and-required-approvals), [P08](processes.md#p08-owner-leaves), [P10](processes.md#p10-ai-draft-and-human-accept)

## Why this chapter exists

"I built a working app with AI in days. Governance was built for slow makers." "My small app grew until a team depends on it, and I'm stuck supporting it." The way an app is built should not decide how much governance it gets. The tier does. This chapter sets out the lanes, the guardrails in each lane, and the path from a personal app to a supported business app.

## The build lanes

| Lane | Who builds | Typical tier | Guardrails |
|---|---|---|---|
| Citizen | A maker, in a personal or developer environment, from a template | Lowest | Register as soon as anyone else uses it; made-up data; sharing limits |
| Fusion | Makers with pro developers and IT | Middle or top | Dedicated environments, a solution, source control, pipelines, team-assisted support |
| Pro | A professional team | Middle or top | Full ALM, code review, pipelines, formal support |
| AI-built | Anyone, with AI coding tools | By tier, like any app | The same path as any app; speed is a reason to make intake fast, not to skip it |

## What good looks like

- Every app is registered as soon as anyone besides its builder uses it.
- Each app that runs as itself has its own narrow identity.
- Each connection to Dataverse has a recorded approach: as the person, as the app, or read-only for the person.
- AI coding agents follow a rules file in the repository, and AI never approves its own work.
- No site goes public before its anonymous access is reviewed.
- When a graduation trigger fires, the business owner decides the next step on record.

## Graduation triggers (default)

Any one of these prompts the business owner to look again at the app's tier and support:

1. The audience grows beyond the builder's team, or reaches people outside the organization.
2. The app starts to change shared business data, or data under its own identity.
3. It starts to use restricted data, or sends data to an AI model.
4. People depend on it to do their job: a day's outage would stop a team.
5. The builder is leaving, moving, or asks for help supporting it.

## How it works

1. **Learn and build.** The maker builds in an approved environment, from a template, with made-up data.
2. **Register.** As soon as anyone else will use it, the maker registers it at the front door (chapter 3). An AI client can draft the entry; registration of an app the AI created needs the business owner's confirmation.
3. **Pick the identity and the access approach.** If the app runs unattended, it gets its own identity with the smallest role that works. For each connection, record how it reaches Dataverse. For agents, require end-user credentials unless a recorded decision allows otherwise.
4. **Keep AI in its place.** Keep a rules file in each repository that AI coding agents read. Mark what AI produced. People approve.
5. **Before a site goes public.** List every table permission that includes the Anonymous Users role. The data owner approves each one that stays, as a recorded decision.
6. **Graduate.** When a trigger fires, a new tier assessment opens. Graduation is a tier decision plus an ownership decision that names the new technical owner and deputy.
7. **Hand over or stay.** The maker either stays as a co-owner in a fusion team or hands the app to IT support, through an ownership handover.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Choose the build path | Architect | Platform admin | Builder, business owner, PMO | Propose a lane |
| Confirm the new tier at graduation | Platform admin | Builder | Business owner, security reviewer | Propose answers |
| Hand over to a new technical owner | Business owner | Platform admin | The builder, the successor (accepts) | Draft the handover |
| Approve an anonymous table permission | Data owner | Site owner | Security reviewer, platform admin | List the permissions |

## Guidelines this chapter applies

- **G3 Set a risk tier for every app.** The tier, not the tool, decides the reviews. Graduation moves the app up a tier.
- **G7 Give each app identity its own narrow role.** Unattended apps get their own identity, never a shared powerful account.
- **G8 Decide how each app reaches Dataverse.** Each connection's approach is chosen and recorded.
- **G17 Let AI draft and recommend; people approve.** Rules files for AI coding agents, AI work marked, people approve.
- **G19 Review anonymous access before any site goes public.** Including sites built with AI.
- **G21 Give AI builders a paved road.** Each lane starts from governed environments, templates and pipelines the organization owns, so the safe way is the fast way (chapter 20).
- **G23 Say who may build with AI, and on which path.** The lanes say who may build with AI, with which AI clients and in which environments (chapter 18).

## Processes this chapter uses

- **P01 Intake and triage.** Registration of a citizen or AI-built app.
- **P02 Tier assessment and required approvals.** The re-tier at graduation.
- **P08 Owner leaves.** The handover shape: a successor accepts, a new deputy is named.
- **P10 AI draft and human accept.** What AI may draft, and what needs a person's acceptance.

## How the product is designed to support it

- It is designed to tell a maker, for the app in front of them, its tier, what the tier asks and who must say yes.
- It is designed to let a maker register an app in minutes from Claude Code or another MCP client, or from its pages.
- It is designed to record the build type, the identities and the access approach on the application record.
- It is designed to record graduation as a tier decision plus an ownership decision, so the path from personal app to supported app is on record.
- It is designed to treat public, anonymous access as a tier question, so a public site gets its tier's approvals before go-live.

## What you can tailor

- The lanes and the guardrails in each.
- The graduation triggers.
- Which environments count as approved for each lane.

## Microsoft guidance it applies

- [User support: ongoing production solution support](https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions#define-tiers-of-applications), checked 2026-11-22: "consider a graduation path", and for Critical applications "solutions are owned by a service principal in test and production."
- [Implement a zoned governance strategy](https://learn.microsoft.com/microsoft-copilot-studio/guidance/sec-gov-phase2) (Copilot Studio), checked 2026-11-22: citizen, partnered and professional development zones.
- [Recommendations for identity and access management](https://learn.microsoft.com/power-platform/well-architected/security/identity-access) (Power Platform Well-Architected, SE:05), checked 2026-11-22: "Grant roles that start with least privilege".
- [Control maker-provided credentials for authentication](https://learn.microsoft.com/microsoft-copilot-studio/configure-no-maker-authentication) (Copilot Studio), checked 2026-11-22: "By default, both End-user and Maker-provided credentials are enabled."
- [Assign table permissions](https://learn.microsoft.com/power-pages/security/assign-table-permissions) (Power Pages), checked 2026-11-22: when the Anonymous users web role has access to a table, any visitor can access its data.

## How AI changes this

- The AI-built lane is now the busiest one, and anyone can arrive with a working app instead of a request.
- Under G21 each lane gets its piece of the paved road: an environment without a ticket, templates, approved connectors and MCP servers, a pipeline the organization owns, and identities ready to use.
- Under G23 the organization says who may build with AI, with which AI clients and on which path (chapter 18).
- A personal AI-built app that spreads beyond its builder fires a graduation trigger, like any other app.

## Old paradigm to avoid

[2. IT as a ticket queue](17-old-paradigms-and-how-not-to-slide-back.md#2-it-as-a-ticket-queue). Here it shows up as a ticket for every environment, connector or pipeline, so the work moves to wherever nobody is watching: the default environment or a personal account. Build the road once and let builders serve themselves on it.

**Slide-back signal:** apps built in the default environment, or environment requests by ticket.

## Done when

- Every app used by more than its builder is registered.
- Every unattended app has its own identity, and every connection has a recorded access approach.
- Every public site has its anonymous permissions approved, or anonymous access is blocked.
- Every graduation has a tier decision and an ownership decision on record.
