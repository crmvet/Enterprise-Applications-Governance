# G12. Approve every production change on record, never by the person who started it

- **Number:** G12
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 9 (Change control and release)
- **Cited in chapters:** 1, 4, 9, 10, 17
- **Processes:** P03, P05

**Rule.** Every production change has a change record, an approval that fits its type and tier, and a planned window. Whoever starts a production release does not approve it. A small team that cannot separate the two records the exception as a decision.

**Why it matters.** Self-approval removes the second pair of eyes that catches mistakes. A recorded approval and window show later who agreed to what, and when.

**How the product is designed to support it.** It is designed to run change requests, change types, approvals and windows by default, or to link your existing change tool, and to flag deployments that lack their approval or fall outside their window.

## Microsoft anchors

- [Define approvals and checks](https://learn.microsoft.com/azure/devops/pipelines/process/approvals) (Azure Pipelines), checked 2026-11-22: approvals and checks are set on resources such as environments, not in the YAML file. When you add an approval, you "Specify if you want to permit or restrict approvers from approving their own runs". A Business hours check holds deployments to a set time window.
- [Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments) (GitHub Docs), checked 2026-11-22: with the prevent self-review setting, users who start a deployment cannot approve that deployment job, even as a required reviewer. On GitHub Free, Pro and Team, required reviewers are "only available for public repositories".
- [Extend pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/extend-pipelines), checked 2026-11-22: a delegated deployment runs as a service principal or the pipeline stage owner instead of the requesting maker, and "requires approval from an authorized identity."

## Maps to

AICPA Trust Services Criteria CC8.1 (change management). ISO/IEC 27001:2022 control 8.32, Change management.

## Working shape

- **When:** before every production deployment.
- **Who:** the requester raises the change; the approvers that the type and tier require approve it; the change manager owns the windows.
- **Steps:**
  1. Raise a change record, or a change in your existing change tool, with its type, tier and planned window.
  2. Get the approvals the type and tier require, from people other than the requester.
  3. In your pipeline, turn on self-approval prevention and window checks where the tool offers them.
  4. If a small team must self-approve, record the exception (G4) and review it afterwards.
- **Done when:** each production deployment links to an approved change, approved by someone other than the requester or covered by a recorded exception.
- **Evidence:** the change record, its approval history, and the pipeline's approval log.
