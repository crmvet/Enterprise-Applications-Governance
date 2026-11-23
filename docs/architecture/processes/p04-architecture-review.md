# P04 Architecture review

**Status: in design.** Nothing here is built or running. Roles, steps and approvals are the default configuration, and each organization can change them. Back to the [processes index](README.md).

## In short

This process is designed to make a review request find the architect when a configured trigger fires. It is designed to keep every review and every architecture decision in the shared decision log, with the status words Microsoft uses for architecture decision records: Proposed, Accepted and Superseded.

The full text of an architecture decision record lives in the team's repository or wiki, written by the builder's own AI tool. The product records the link and the decision.

## Trigger

All triggers are configuration.

- A tier answer adds an architecture review. In the default configuration that happens when an app writes under its own identity, uses restricted data, reaches the whole organization or outside users, is an agent that takes actions, has a pro-code part, or is hosted outside Power Platform.
- A change to a shared component that other apps depend on ([P03](p03-change-management.md) finds the dependants).
- A request to depart from a standard (an exception).
- A Design gate requirement that asks for a review done or a design decision accepted.

## Who does what (default configuration)

| Step or decision | Does the work | Decides | Asked first | Told after | AI client |
|---|---|---|---|---|---|
| Review a design that crosses a trigger | Builder | Architect | Data owner, platform admin, security reviewer | Business owner, change manager | Drafts the decision record |
| Record an architecture decision | Architect | Architect | Builder, platform admin | | Drafts the decision record |
| Grant an exception to a standard | Architect | Architect | Builder, platform admin, security reviewer | Business owner | none |
| Impact review of a shared component | Platform admin | Architect | Builder, business owner, owners of dependent apps | Change manager | Drafts the impact list |

The architect role is the design authority by default, with one approval rule per tier for design decisions. The default role list has no architecture board. An organization that wants a board for tier 3 and exceptions adds that role and gives the tier 3 rule to it, as configuration.

## Steps

1. **Request.** The engine opens an architecture review, due now, and records which trigger caused it. A person, or their AI client, can also ask for a review directly.
2. **Prepare.** The builder's AI client drafts the architecture decision record from the discussion: the context, the options considered, the outcome and the confidence. The builder's own AI tool writes the full text to the repository or wiki. The product records the link, and a design decision with the status Proposed and the options considered.
3. **Review.** The architect reviews. Linked assessments, for example a Well-Architected assessment export, are recorded as evidence links with an evidence level.
4. **Decide.** The design decision goes on a card to the architect, or to a board role where the organization added one. A data-use decision goes to the data owner when the data use changed ([P02](p02-tier-assessment.md)).
5. **Close the review.** The review moves to done and links the decision that settled it. The engine checks the Design gate again.
6. **Exceptions.** An exception decision records the standard it departs from, the reason and the review date. It is a normal decision and can be superseded later.

## What is recorded

| Record | What happens |
|---|---|
| Review | An architecture review, from due to done, with its trigger and the settling decision |
| Decision | Design; data use; exception |
| Approval record | One per required answer |
| Document link | The link to the architecture decision record in the repository or wiki |
| Evidence link | Assessments and impact lists |

## Approvals (people only, on cards)

| Decision | Approver in the default configuration | Card mode |
|---|---|---|
| Design | Architect (one rule per tier) | First to respond |
| Data use | Data owner | First to respond |
| Exception | Architect | First to respond |

## What an organization can configure

- What triggers an architecture review.
- The review types. The design adds security and responsible AI reviews as proposed defaults.
- Whether a design decision must list the options considered (yes by default).
- Who the design authority is, per tier: the architect role, or a board the organization adds.
- What the Design gate requires.
- Whether an architecture decision record is a required document, per tier.

## Designed MCP tools

Every tool runs as the signed-in person.

| Tool | What it does |
|---|---|
| `request_architecture_review` | Asks for an architecture review |
| `get_my_queue` | Lists the reviews waiting for the architect |
| `save_decision_proposal` | Opens a design decision as Proposed. It cannot accept it |
| `get_dependency_impact` | Walks the dependencies of a shared component |
| `record_document_link` | Records the link to the architecture decision record |
| `save_link` | Records an evidence link, such as an assessment |

## Microsoft tools linked, never rebuilt

- Microsoft's [architecture decision record](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record) guidance, which shapes the decision log.
- Well-Architected assessments, linked as evidence.
- The Azure DevOps wiki and repositories where the decision record's text lives, written by the builder's own AI tool.

## Where it runs on Azure (in design)

| Concern | Designed to run on |
|---|---|
| Opening reviews from triggers | The rules engine in the one Azure Functions app, when a tier is accepted or a change is submitted |
| A person's request for a review | An API endpoint with the same name as the tool |
| Decision cards | The approval runner ([approval cards](README.md#approval-cards)) |
| Decisions | A ledger table in Azure SQL Database. A changed decision gets a new record that supersedes the old one |

## Open design questions

- Whether an AI client may mark a review done, and what "done" must carry (the date, the reviewer and an accepted decision), is still open.

## Microsoft sources

All checked on Microsoft Learn on 2026-11-22.

- "The ADR serves as an append-only log." "If a decision changes, write a new record that supersedes the original and link the two together." A record should include a problem statement with context, the options considered, the decision outcome with its confidence level, and a "Status, such as Proposed, Accepted, or Superseded." https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record

Previous: [P03 Change management](p03-change-management.md). Next: [P05 Deployment record](p05-deployment-record.md).
