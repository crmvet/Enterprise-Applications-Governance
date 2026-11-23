# G15. Keep an evidence pack, not assurances

- **Number:** G15
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 15 (Evidence and audit)
- **Cited in chapters:** 1, 15, 22
- **Processes:** P05, P10, P11

**Rule.** For each app, keep the evidence that its rules were followed: decisions, approvals, deployments, reviews, and links to Microsoft's own logs. An auditor should be able to follow it without asking anyone.

**Why it matters.** "We always do that" is not evidence. Evidence that sits with the record makes audits and incident reviews short.

**How the product is designed to support it.** It is designed to stamp every record with who performed it, the guideline version and the configuration version in force at the time, so evidence is a query, not a hunt.

## Microsoft anchors

- [Power Platform activity logs and auditing in Microsoft Purview](https://learn.microsoft.com/power-platform/admin/activity-logging-auditing/activity-logs-overview), checked 2026-11-22: Purview can capture activity for connectors, Copilot Studio, Dataverse and model-driven apps, Power Apps, Power Automate, Power Pages and Power Platform admin actions. To see a production environment's logs in Purview, turn on its "Enable SAS Logging in Purview" setting.
- [Recommendations for designing a reliable monitoring and alerting strategy](https://learn.microsoft.com/power-platform/well-architected/reliability/monitoring-alerting-strategy) (Power Platform Well-Architected), checked 2026-11-22: "Most events are available within 24 hours of the activity. Don't use this information for real-time monitoring."
- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: "Both managed and unmanaged solutions are automatically exported and stored in the pipelines host for every deployment."
- [Govern agents by risk](https://learn.microsoft.com/agents/center-of-excellence/govern-agents-risk), checked 2026-11-22: release gates and audit logs keep a record of accountability. "Microsoft Purview provides audit of agent actions to support this record."

## Working shape

- **When:** all the time, and before each audit or periodic review.
- **Who:** the app owner keeps the pack complete; the change manager and auditors use it.
- **Steps:**
  1. Decide which evidence each tier needs, and where each item lives.
  2. Turn on Microsoft's audit logging for production environments, and keep pipeline run history.
  3. Link each item to the app record instead of copying it.
- **Done when:** an auditor can answer "who approved this change, when, and under which rule" from the record alone.
- **Evidence:** the evidence index for the app, with its links.
