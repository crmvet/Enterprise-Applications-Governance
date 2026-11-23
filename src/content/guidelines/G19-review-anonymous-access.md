# G19. Review anonymous access before any site goes public

- **Number:** G19
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 8 (Build paths: citizen, fusion, pro and AI-built, and graduation)
- **Cited in chapters:** 1, 8, 13, 20
- **Processes:** P02, P04

**Rule.** Before a Power Pages site goes public, including a site built with AI, check every table permission given to the Anonymous Users web role. Treat each one as data published to the internet.

**Why it matters.** When the Anonymous Users role has a table permission, anyone who visits the site can read that table's data. Sites start private, so going public is the moment to check.

**How the product is designed to support it.** It is designed to treat public, anonymous access as a tier question, so a public site gets its tier's approvals before go-live.

## Microsoft anchors

- [Assign table permissions](https://learn.microsoft.com/power-pages/security/assign-table-permissions), checked 2026-11-22: "When the Anonymous users web role is granted access to a table, any user who visits the site can access the data in the table."
- [Site visibility in Power Pages](https://learn.microsoft.com/power-pages/security/site-visibility), checked 2026-11-22: "All sites you create in Power Pages are private by default."
- [Apply security best practices to your Power Pages site](https://learn.microsoft.com/power-pages/security/security-best-practices), checked 2026-11-22: "Treat anonymous table permissions as public data access."
- [Use governance controls to disable anonymous access](https://learn.microsoft.com/power-pages/security/disable-anonymous-access), checked 2026-11-22: admins can block anonymous access to Dataverse data for chosen sites or for all sites. Unauthenticated users can still write data to Dataverse when it is blocked.

## Working shape

- **When:** before a site's visibility changes to public, and after any change to its table permissions.
- **Who:** the site owner, the data owner, a security reviewer and the Power Platform admin.
- **Steps:**
  1. List every table permission that includes the Anonymous Users role. Remove any that is not meant to be public.
  2. Have the data owner approve each remaining anonymous permission as a recorded decision (G4).
  3. Where the site needs no public Dataverse data, ask the admin to block anonymous access for it.
- **Done when:** every anonymous permission on the site has an approving decision, or anonymous access is blocked.
- **Evidence:** the list of permissions at go-live, and the approving decisions.
