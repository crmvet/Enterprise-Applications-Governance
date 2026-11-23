# D-12 Planning tools: Azure DevOps first, with links to Planner and GitHub

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

The PMO ranks demand, checks capacity and runs the portfolio review. Its plans, tasks and sprints stay in its own tools. The product never tracks work itself (D-02). Still, each application record should show which plans and work items changed it, so the record outlives every project.

Teams plan in different tools. Azure Boards in Azure DevOps has work items, portfolio backlogs and delivery plans, and the Azure DevOps MCP Server can create and link a work item as the signed-in person. Many PMOs use Planner plans. Some development teams plan in GitHub Projects.

The design had to choose which planning tools an application's record can link to.

## Options considered

- **A. Azure DevOps only.** Links only to Azure Boards work items and epics.
  - Pros: matches D-02. An AI tool can create and link a work item in one turn.
  - Cons: PMOs on Planner or GitHub get no link, so their apps look unplanned.
- **B. Azure DevOps first, plus Planner and GitHub links.** One generic link record holds any tool. Azure DevOps keeps the step that creates and links the work item. For Planner and GitHub, a person adds the link at first.
  - Pros: covers the common planning tools at almost no cost. The product still only links.
  - Cons: Planner and GitHub links are added by hand at first. More link kinds to document.
- **C. Links plus copied plan facts.** As B, and the product also copies dates or status from each plan.
  - Pros: the portfolio review shows plan dates without opening each tool.
  - Cons: copies go stale. It moves the product toward tracking work, which D-02 rules out.

## Decision

Option B, as recommended.

- Azure DevOps first, with a step that creates a work item and links it, acting as the signed-in person.
- The same generic link record also holds Planner plans and GitHub Projects: the tool, the kind, the ID or URL, a label and a date.
- Links only. The product never tracks work and never copies plan facts.
- The product never reads Planner data. It stores links only.

## Why

- Every planning tool the parties use gets a place, so no team's apps look unplanned.
- A generic link record costs almost nothing to extend to another tool.
- Copying plan facts would turn the product into a work tracker, and the copies would go stale. Work tracking is something existing tools already do (D-01).
- The first reason given for not reading Planner data was a licensing rule for other apps that read Planner premium plan data. Microsoft has since said it will no longer invest in Dataverse-based plans, so that rule now covers only older premium plans. Storing links only works the same way for every kind of plan.

## Consequences

- The link record's tool list includes Azure DevOps, Planner and GitHub. The list is configuration (D-06).
- D-16 dropped the product's own agent. The step that creates the work item is designed to run in the person's own AI tool: it creates the work item through the Azure DevOps MCP Server, as that person, and then records the link in the product.
- The product never reads the work item or the plan.
- Public wording says "Planner plans". That covers the unified plans in Planner's own service and the older premium plans still stored in Dataverse.

## Related

- [D-01](D-01-what-the-product-is.md), [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-06](D-06-risk-tiers-are-configuration.md), [D-16](D-16-no-copilot-or-teams-agent.md)
- [P01 Intake and triage](../architecture/processes/p01-intake-and-triage.md)
- [Data model](../architecture/data-model.md)
- [Microsoft fit](../microsoft-fit.md)
