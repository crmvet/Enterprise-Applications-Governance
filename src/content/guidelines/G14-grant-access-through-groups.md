# G14. Grant access through groups, review it, and keep a kill switch

- **Number:** G14
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 13 (Data owners and access)
- **Cited in chapters:** 1, 2, 12, 13, 17, 20
- **Processes:** P08, P11

**Rule.** Give people roles only through Microsoft Entra groups, never one by one. Review group membership on a cadence set by tier. Keep a tested way to cut access fast.

**Why it matters.** Roles given to individuals pile up and outlive the need. A change to group membership can take hours to reach Dataverse, so an urgent revoke needs a faster lever.

**How the product is designed to support it.** It is designed to map each of its own roles to a Microsoft Entra group, show which group carries which role, flag any role assigned directly to a user, and remind owners when a review is due.

## Microsoft anchors

- [Manage group teams](https://learn.microsoft.com/power-platform/admin/manage-group-teams), checked 2026-11-22: a member's Entra group memberships "are cached upon the team member's sign in", so a change shows at the next sign-in or when the cache refreshes "after 8 hours of continuous sign in". A group team lists only members who have accessed the environment. Users deleted or disabled in Microsoft Entra ID "can't access the environment when they try to sign in."
- [Review the manage checklist](https://learn.microsoft.com/microsoft-copilot-studio/guidance/manage-checklist) (Copilot Studio), checked 2026-11-22: "Assign access through Microsoft Entra ID groups, not individuals".
- [What are access reviews?](https://learn.microsoft.com/entra/id-governance/access-reviews-overview) (Microsoft Entra ID Governance), checked 2026-11-22: access reviews need Microsoft Entra ID Governance or Microsoft Entra Suite licenses for your users. Some capabilities work with Microsoft Entra ID P2.

## Working shape

- **When:** at setup, on each review date, and at once when someone must lose access.
- **Who:** the platform team keeps the groups and teams; app owners review membership; the identity team runs access reviews and urgent revokes.
- **Steps:**
  1. Create one Entra group per role, and map it to a Dataverse group team that holds the role.
  2. Review membership on the cadence your tier model sets, with Entra access reviews where you are licensed for them.
  3. For an urgent revoke, disable the user or the application user, not only the group membership.
  4. Read who holds a role from Entra, not from the team's member list.
- **Done when:** no role is assigned directly to a user, and every review is complete by its due date.
- **Evidence:** the group-to-role map, access review results, and revoke records.
