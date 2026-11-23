# G20. Keep each app's required documents current

- **Number:** G20
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 12 (Operate and review)
- **Cited in chapters:** 1, 4, 5, 7, 12, 17, 21
- **Processes:** P07

**Rule.** Decide which documents each kind of app or tier needs, for example a wiki page, a runbook or an architecture decision. The owner writes them in the team's own tools and keeps them current. Track each document's link and status.

**Why it matters.** Documents nobody asked for are not written, and documents nobody checks go stale. Knowing what is missing or stale is what makes support, audits and handovers work.

**How the product is designed to support it.** It is designed to hold the required documents as configuration, record each document's link, status (missing, draft, current or stale), owner and last check, and remind the owner. By design, it never writes the documents itself.

## Microsoft anchors

- [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record#workload-documentation-repository), checked 2026-11-22: the log "should be readily available and stored openly with the workload's documentation. A decision that's made but never recorded will likely be forgotten".
- [About wikis, READMEs, and Markdown](https://learn.microsoft.com/azure/devops/project/wiki/about-readme-wiki) (Azure DevOps), checked 2026-11-22: publishing code as a wiki "provides a way to maintain your code documentation synchronously with your code base".

## Working shape

- **When:** at registration, at each gate, and at each periodic review.
- **Who:** the app owner writes the documents or delegates them; the platform team sets what is required; reviewers check status.
- **Steps:**
  1. Set the required documents for each kind of app or tier.
  2. The owner writes each document where the team already works, such as a wiki, a repository or SharePoint, with whatever AI tool they use, and records the link.
  3. At each review, mark each document current or stale, and fix the stale ones.
- **Done when:** no live app has a required document that is missing, or stale beyond the agreed time.
- **Evidence:** the document list on the app record, with links, status and last check dates.
