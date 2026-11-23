# G9. Release the tested artifact through your own pipeline

- **Number:** G9
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 9 (Change control and release)
- **Cited in chapters:** 1, 9, 15, 17, 20, 22
- **Processes:** P03, P05

**Rule.** Promote to production the same artifact that passed test, through your organization's own pipeline. Before each import, know what the upgrade will change and delete. Versions only go up: fix forward, unless a recorded decision allows a rollback.

**Why it matters.** A rebuilt or re-exported package is not the one you tested. An upgrade deletes components that are missing from the new version, and going back to an older version can overwrite data.

**How the product is designed to support it.** It is designed to map which of your pipelines deploys each app, take a deployment record from a step at the end of that pipeline, and flag deployments that arrive another way. By design, it never builds, hosts or runs pipelines.

## Microsoft anchors

- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: "the same solution artifact will be deployed" to each stage. "This ensures customization can't bypass QA environments or your approval processes." "Pipelines default import behavior is Upgrade without Overwrite customizations." If the redeployment setting is off, "only higher solution versions can be deployed or imported."
- [Upgrade or update a solution](https://learn.microsoft.com/power-apps/maker/data-platform/update-solutions), checked 2026-11-22: with Upgrade, "Any components associated to the previous solution version that aren't in the newer solution version will be deleted."
- [Redeploy past solution versions](https://learn.microsoft.com/power-platform/alm/redeploy-past-solution-versions), checked 2026-11-22: "Redeploying overwrites the most recent version and any data within that version. This is an irreversible operation".
- [Recommendations for safe deployment practices](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/safe-deployments) (Power Platform Well-Architected, OE:10), checked 2026-11-22: small, incremental, quality-gated releases, with paths for routine and emergency deployments.

## Maps to

AICPA Trust Services Criteria CC8.1 (change management). ISO/IEC 27001:2022 control 8.32, Change management.

## Working shape

- **When:** every deployment to test or production.
- **Who:** the technical owner prepares the release; the release manager runs the pipeline; the change manager confirms the approval (G12).
- **Steps:**
  1. Build once, and promote that one artifact through each stage of your pipeline: Power Platform Pipelines, Azure Pipelines or GitHub Actions.
  2. Before the import, list what changes and which components the upgrade deletes, and attach the list to the change.
  3. Raise the version on every release. Treat a rollback as an exception with its own decision (G4).
  4. Add a last step to the pipeline that records the deployment: what, where, when, which pipeline and who approved.
- **Done when:** each production deployment matches an artifact that passed test, carries a higher version, and is recorded.
- **Evidence:** the pipeline run history, the deployment record, and the change it belongs to.
