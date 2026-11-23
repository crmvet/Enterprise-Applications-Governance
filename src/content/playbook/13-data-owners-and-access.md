# Chapter 13. Data owners and access

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** data owners, security, IT (the platform and identity teams), technical owners
- **Guidelines:** [G7](../guidelines/G07-narrow-app-identities.md), [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G14](../guidelines/G14-grant-access-through-groups.md), [G19](../guidelines/G19-review-anonymous-access.md)
- **Processes:** [P02](processes.md#p02-tier-assessment-and-required-approvals), [P04](processes.md#p04-architecture-review), [P09](processes.md#p09-retirement)

## Why this chapter exists

"I own this data, but nobody asks me before an app or an AI tool uses it." Data owners are often named on paper and skipped in practice. Meanwhile apps run as powerful shared accounts, roles pile up on individuals, and a public site can publish a table to the internet with one permission. This chapter gives the data owner a say at the right moments, and keeps access narrow, grouped and quick to cut.

## What good looks like

- Every application names a data owner with a deputy.
- A new data use is a decision the data owner makes, at design and whenever the use changes.
- Restricted data reaching an AI model needs two yeses: the data owner's and the security reviewer's.
- Every app that runs as itself has its own narrow identity.
- Every connection to Dataverse has a recorded approach that the data owner accepted.
- People get roles only through Entra groups, access is reviewed by tier, and an urgent revoke works in minutes.
- Every anonymous permission on a public site is a decision the data owner made.

## How it works

1. **Name the data owner.** At registration, with a deputy (chapter 2).
2. **Decide data use at design.** When a design review or a tier answer shows a new data use, the data owner decides on an approval card. Restricted data to an AI model also needs the security reviewer's agreement.
3. **Choose the access approach.** For each connection, the technical owner chooses: as the signed-in person, as the app's own identity, or read-only for the person. The data owner agrees. Prefer the person's identity, so their security roles apply.
4. **Narrow the identity.** An app or integration that runs unattended gets its own identity, with the smallest role that works, only in the environments it needs.
5. **Group the roles.** One Entra group per role, mapped to the role in each environment. Review membership on the cadence your tier sets.
6. **Keep a kill switch.** For an urgent revoke, disable the user or the application user, not only the group membership: membership changes can take hours to reach an environment.
7. **Review anonymous access.** Before a site goes public, list every table permission given to the Anonymous Users role. The data owner approves each one that stays, or the admin blocks anonymous access.
8. **Settle data at retirement.** The data owner decides what is kept, where, and for how long (chapter 14).

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Approve a data use | Data owner | Platform admin | Security reviewer, architect | Nothing |
| Approve restricted data reaching an AI model | Data owner, with the security reviewer's agreement | Platform admin | Architect, AI lead | Nothing |
| Choose an access approach | Technical owner | Technical owner | Data owner, security reviewer | Propose one |
| Grant an app identity its role | Platform admin | Identity team | Technical owner, security reviewer | Nothing |
| Approve an anonymous table permission | Data owner | Site owner | Security reviewer, platform admin | List the permissions |
| Decide data retention at retirement | Data owner | Platform admin | Business owner | Draft the plan |

## Guidelines this chapter applies

- **G7 Give each app identity its own narrow role.** One identity per unattended app, least privilege.
- **G8 Decide how each app reaches Dataverse.** The access approach per connection, accepted by the data owner.
- **G14 Grant access through groups, review it, and keep a kill switch.** Groups only, reviews by tier, disable for an urgent revoke.
- **G19 Review anonymous access before any site goes public.** Each anonymous permission is data published to the internet.

## Processes this chapter uses

- **P02 Tier assessment and required approvals.** Data use and restricted data decisions follow from the tier answers.
- **P04 Architecture review.** A design review sends a data use decision to the data owner when data use changed.
- **P09 Retirement.** The data owner settles the data before anything is deleted.

## How the product is designed to support it

- It is designed to name a data owner on every application and to send data use decisions to that person on approval cards.
- It is designed to express a second yes, such as the security reviewer's agreement, as an approval rule with two approvers, so each decision keeps one accountable role.
- It is designed to record each app's identities and access approach on the application record.
- It is designed to map each of its own roles to an Entra group, flag any role given to a person directly, and remind owners when an access review is due.
- It is designed never to manage group membership, run access reviews or remove access itself. Those stay in Entra and the admin centers.

## What you can tailor

- Which data counts as restricted, through the tier questions.
- Who must agree to which data use, per tier.
- The access review cadence per tier.

## Microsoft guidance it applies

- [Recommendations for identity and access management](https://learn.microsoft.com/power-platform/well-architected/security/identity-access) (Power Platform Well-Architected, SE:05), checked 2026-11-22: "Avoid permissions that target individual users." "Grant roles that start with least privilege".
- [Impersonate another user](https://learn.microsoft.com/power-apps/developer/data-platform/impersonate-another-user) (Dataverse), checked 2026-11-22: when code acts for another user, the privileges used are the intersection of both users' privileges.
- [Manage group teams](https://learn.microsoft.com/power-platform/admin/manage-group-teams), checked 2026-11-22: memberships are cached at sign-in; users disabled in Microsoft Entra ID "can't access the environment when they try to sign in."
- [Apply security best practices to your Power Pages site](https://learn.microsoft.com/power-pages/security/security-best-practices), checked 2026-11-22: "Treat anonymous table permissions as public data access."
- [Use governance controls to disable anonymous access](https://learn.microsoft.com/power-pages/security/disable-anonymous-access) (Power Pages), checked 2026-11-22: admins can block anonymous access to Dataverse data for chosen sites or for all sites.
- [Best practices for Microsoft Entra Agent ID](https://learn.microsoft.com/entra/agent-id/best-practices-agent-id), checked 2026-11-22: "Don't create AI agents as plain app registrations or service principals outside the Agent ID framework." Every agent identity must have a sponsor, the person or group accountable for the agent's purpose.
- [Workload identity federation concepts](https://learn.microsoft.com/entra/workload-id/workload-identity-federation) (Microsoft Entra), checked 2026-11-22: of secrets and certificates, "These credentials pose a security risk and have to be stored securely and rotated regularly." GitHub Actions and Azure Pipelines service connections are supported scenarios.
- [Development security implementation patterns](https://learn.microsoft.com/security/saf/disciplines/development/implementation-patterns) (Microsoft security guidance), checked 2026-11-22: "Separate identities for build, test, and production deployment when their privileges differ."

## How AI changes this

- AI clients act as the person who signed in, so that person's own access is the boundary. An AI client never gets more than the person it acts for.
- Restricted data reaching an AI model still needs two yeses: the data owner's and the security reviewer's. Building fast is no reason to skip either.
- Each agent gets its own Microsoft Entra agent identity with a human sponsor, not its maker's credentials or a plain app registration. An agent that runs on maker credentials needs a recorded decision.
- Each pipeline runs as its own workload identity, separate per stage where the privileges differ. Use workload identity federation where the pipeline tool supports it, so no secret is stored.

## Old paradigm to avoid

[10. Shared accounts and stored secrets](17-old-paradigms-and-how-not-to-slide-back.md#10-shared-accounts-and-stored-secrets). Here it shows up as an app, agent or pipeline running as one shared powerful account, a long-lived secret in pipeline settings, or roles given to people one by one.

**Slide-back signal:** direct role assignments are found, or agents run on maker credentials with no recorded decision.

## Done when

- Every live app has a data owner and deputy, and every data use has an accepted decision.
- Every unattended app has its own identity, and every connection has an accepted access approach.
- No role is held directly, and every access review is complete by its due date.
- Every public site's anonymous permissions are approved, or anonymous access is blocked.
