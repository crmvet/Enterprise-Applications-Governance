# Chapter 7. Architecture review and decisions

- **Version:** 0.2 draft, 2026-11-22
- **Track:** IT
- **Parties:** architects (enterprise, solution, design authority), IT, builders and pro developers, data owners
- **Guidelines:** [G4](../guidelines/G04-record-each-decision.md), [G6](../guidelines/G06-map-dependencies.md), [G20](../guidelines/G20-keep-required-documents-current.md)
- **Processes:** [P03](processes.md#p03-change-management), [P04](processes.md#p04-architecture-review), [P07](processes.md#p07-documentation-tracking)

## Why this chapter exists

"Decisions live in heads and chat. Nobody can find why we chose this." "We review designs too late, or never." Architects need two things: a review request that finds them when a design crosses a line, and one shared log where every decision, theirs and everyone else's, is kept with Microsoft's status words.

## What good looks like

- A design review happens when a trigger fires, not for every form change and not too late.
- Each significant, hard-to-reverse choice has a decision record: context, options considered, outcome, trade-offs and confidence.
- Decisions are append-only. A changed decision is a new entry that supersedes the old one.
- The full decision record lives with the team's documentation, in a repository or wiki. The register keeps one line and the link.
- Before a shared component changes, its dependants and their owners are known.

## Review triggers (default)

| Trigger | Review | Who decides |
|---|---|---|
| A low-tier app on an approved template with approved connectors | None: a self-check against the golden path | Builder |
| A new table in a shared data model, a new connector, or a new integration with a system of record | Light design review | Design authority |
| Writes under its own identity, restricted data, an organization-wide or outside audience | Full design review, plus the security review | Architect, with security |
| An agent that executes actions in systems | Design review plus a responsible AI review | Architect, with security and the AI lead |
| A pro-code part, or hosting outside Power Platform | Design review of the hosting and the identity | Architect |
| A change to a shared component others depend on | Impact review from the dependency map | Architect, with the owners of dependants consulted |
| A request to depart from a standard | Exception decision | Architect, or a board where you have one |

## How it works

1. **Request.** A review opens when a trigger fires, with the trigger recorded. Anyone, or their AI client, can also ask for one.
2. **Prepare.** The builder's AI client may draft the architecture decision record from the discussion. The builder's own tool writes the full text into the team's repository or wiki.
3. **Review.** The architect reviews the design, the dependency map and any linked assessment, such as a Well-Architected assessment export.
4. **Decide.** The design decision goes to the architect, who answers on the decision page. A data use decision goes to the data owner when data use changed.
5. **Close.** The review closes with its decision, and the Design gate is checked again.
6. **Supersede, never edit.** If the decision changes later, record a new one that supersedes it, and link the two.
7. **Exceptions.** An exception records the standard it departs from, the reason and a review date.

## Who decides (default decision rights)

| Decision | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Review a design that crosses a trigger | Architect | Builder | Data owner, platform admin, security reviewer | Draft the decision record and list dependants |
| Record an architecture decision | Architect | Architect | Builder, platform admin | Draft it |
| Grant an exception to a standard | Architect | Architect | Builder, platform admin, security reviewer | Nothing |
| Impact review of a shared component | Architect | Platform admin | Owners of dependants, builder | List dependants |

## Guidelines this chapter applies

- **G4 Record each decision in one line.** The decision log is append-only, with the status words Proposed, Accepted and Superseded.
- **G6 Map what each app depends on.** The dependency map drives impact reviews and tells architects who depends on a shared component.
- **G20 Keep each app's required documents current.** An architecture decision record can be a required document for a tier, with its link and status tracked.

## Processes this chapter uses

- **P04 Architecture review.** The review request, the design decision and exceptions.
- **P03 Change management.** A change to a shared component lists the affected applications and can trigger an impact review.
- **P07 Documentation tracking.** Tracks the decision record's link and status.

## How the product is designed to support it

- It is designed to open a review when a configured trigger fires, from the tier answers, the change kind or the dependency map, so the request finds the architect.
- It is designed to keep one append-only decision log for every party, with Proposed, Accepted and Superseded, and a link to the full record.
- It is designed to answer "what breaks if this changes?" in both directions, to a set depth.
- It is designed never to be an enterprise architecture suite: no capability models, diagram editor or technology radar. An architecture tool you already run stays, and is linked.

## What you can tailor

- The triggers, and which review each one adds.
- The design authority: the architect role by default, or a board for the top tier and exceptions.
- Whether an architecture decision record is required, and from which tier and stage.

## Microsoft guidance it applies

- [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record), checked 2026-11-22: "The ADR serves as an append-only log." "Don't go back and edit accepted records." Status words "such as Proposed, Accepted, or Superseded."
- [Recommendations for performing failure mode analysis](https://learn.microsoft.com/power-platform/well-architected/reliability/failure-mode-analysis) (Power Platform Well-Architected, RE:03), checked 2026-11-22: "Identify and document the dependencies in your workload".
- [Power Platform and Copilot Studio Architecture Center](https://learn.microsoft.com/power-platform/architecture/architecture-center-overview), checked 2026-11-21: reference architectures and solution ideas to point standards at.
- [Power Platform Well-Architected assessment](https://learn.microsoft.com/power-platform/well-architected/implementing-recommendations), checked 2026-11-21: the assessment is free and exports to CSV, which can be linked as review evidence.

## How AI changes this

- The AI client can draft the decision record and list the dependants of a shared component in seconds, so preparing a review is no longer the slow part.
- Review triggers surface during the build, because the AI client asks what the tier needs before it starts, and the answer names the review.
- Templates on the paved road (chapter 20) carry decisions already made, so the lowest tier needs no review beyond a self-check. Where a review is needed, the architect still decides on the decision page.

## Old paradigm to avoid

[5. The standards document nobody reads](17-old-paradigms-and-how-not-to-slide-back.md#5-the-standards-document-nobody-reads). Here it shows up as standards kept only in a long document, out of reach of builders and their AI clients while they work, so reviews find the gaps after the build. Turn standards into templates and review triggers that the product is designed to answer.

**Slide-back signal:** reviews opened after the build, or exceptions with no review date.

## Done when

- Every design that crossed a trigger has a review with an accepted decision.
- Every significant choice has a decision record, linked from the register.
- No accepted decision was edited; changes are superseding entries.
