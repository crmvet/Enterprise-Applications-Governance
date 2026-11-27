# G2. Name the owners of every app, each with a deputy

- **Number:** G2
- **Version:** 1.1, 2026-11-25 (the data owner sits on each business data domain)
- **Status:** active
- **Home chapter:** 2 (Parties, roles and decision rights)
- **Cited in chapters:** 1, 2, 3, 5, 12, 18, 19
- **Processes:** P01, P08

**Rule.** Every application names a business owner and a technical owner, each with a deputy, and declares its data uses. Every data domain has a data owner and a deputy, who decides about the uses of its data. Each decision about the app has exactly one accountable role.

**Why it matters.** Inventory owner fields are weak. Model-driven apps have no owner there, and a flow's owner shows its creator. An app with no named owner cannot be reviewed, changed safely or retired.

**How the product is designed to support it.** It is designed to record owners and deputies, with start and end dates, on the application record itself, never trusting inventory owner fields, and to flag gaps.

## Microsoft anchors

- [Power Platform inventory, known limitations](https://learn.microsoft.com/power-platform/admin/power-platform-inventory#known-limitations), checked 2026-11-22: "Model-driven apps don't have an owner." For cloud flows and agent flows, the Owner column "shows the user who created the flow. It doesn't update when the owner changes."
- [Define roles, responsibilities, and decision rights](https://learn.microsoft.com/agents/center-of-excellence/roles-responsibilities) (agentic Center of Excellence), checked 2026-11-22: "Assign exactly one accountable role per task." It also says a role owned by "IT" is a role no one owns.
- [Define roles and responsibilities](https://learn.microsoft.com/power-platform/guidance/adoption/roles) (Power Platform adoption), checked 2026-11-22: "Ensure each task has one accountable person".

## Maps to

NIST CSF 2.0 ID.AM (Asset Management).

## Working shape

- **When:** at registration, at every stage change, and when an owner changes role or leaves.
- **Who:** the business owner is accountable; the technical owner holds its part, and the data owner of each domain decides about its data; the platform team checks completeness.
- **Steps:**
  1. Name a person, not a team, as business, technical and data owner, and name a deputy for each.
  2. Agree who is accountable for each kind of decision about the app, one role per decision.
  3. When an owner leaves or moves, hand over to the deputy within an agreed time and name a new deputy.
- **Done when:** no live app goes without an owner or a deputy for longer than the agreed handover time.
- **Evidence:** the owner history on the application record, with start and end dates.
