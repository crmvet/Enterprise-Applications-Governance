# G7. Give each app identity its own narrow role

- **Number:** G7
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 13 (Data owners and access)
- **Cited in chapters:** 1, 8, 13, 17, 20
- **Processes:** P04, P05, P09

**Rule.** Each app, flow, agent or integration that runs as itself gets its own identity, with only the permissions it needs. Never share one powerful account across apps.

**Why it matters.** A shared, over-privileged identity turns one mistake or one breach into many. Narrow identities limit the damage and make audits readable.

**How the product is designed to support it.** It is designed to link each app's identities, such as an application user or an app registration, to the application record, so owners and reviewers can see them.

## Microsoft anchors

- [Recommendations for identity and access management](https://learn.microsoft.com/power-platform/well-architected/security/identity-access) (Power Platform Well-Architected, SE:05), checked 2026-11-22: "Avoid permissions that target individual users." "Grant roles that start with least privilege". The identities it covers include workload identities, managed identities and service principals.
- [User support: ongoing production solution support](https://learn.microsoft.com/power-platform/guidance/adoption/support-strategy-solutions#define-tiers-of-applications), checked 2026-11-22: for Critical applications, "solutions are owned by a service principal in test and production."

## Working shape

- **When:** when an app first needs to run as itself, and at each access review (G14).
- **Who:** the technical owner requests the identity; the platform or identity team creates it and grants its role.
- **Steps:**
  1. Create a dedicated identity for each app or integration that runs unattended.
  2. Grant the smallest role that works, only in the environments it needs.
  3. Link the identity on the app's record, and review it with the app's access review.
- **Done when:** no unattended app shares an identity, and each identity's role matches what its app does.
- **Evidence:** the identity link on the app record, and the date of its last review.
