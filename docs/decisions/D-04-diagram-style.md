# D-04 Diagram style

- **Status:** Accepted
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

The design needs diagrams: how the product sits among Microsoft's tools, the data model, and the processes. Three sample styles were drawn for the ecosystem view before the rest of the set was made.

## Options considered

- **A, the hub.** The product in the center, with Microsoft's tools around it in four groups: admin and inventory; ALM and source control; identity, security and compliance; AI and agents. Each tool shows how it relates to the product. The complete reference on one page.
- **B, the layers.** Three bands: the business view (the product), Microsoft's control planes, and the places where apps are built and run. The simplest picture, with the least detail.
- **C, who asks what.** One lane for each kind of person who asks, such as a business owner, a platform admin, a security reviewer and a maker using AI tools. Each lane shows the question, the Microsoft tools that answer part of it, and what only the product answers.

Samples A and C used a neutral palette with an orange accent. Sample B used navy, blue and gold.

## Decision

- **Use all three.** Each style becomes a page of one Visio file.
- **One visual language.** The later diagrams, such as the data model and the processes, use the same language.
- **One palette** on every page of the set: navy `#0A2A4A`, blue `#0071DC`, and gold `#FFC221` as the accent.
- **The content follows the decisions.** The diagrams show the operating model of D-01 to D-03, not a business register on its own.

## Why

Each style answers a different question. The hub shows what the product connects to. The layers show what sits on what. "Who asks what" shows why each party would use the product. One palette and one language make the whole set read as one piece.

## Consequences

- Every published diagram has an image and its editable Visio source.
- The diagrams are drawn on light backgrounds, with navy text.
- Microsoft's product icons in the diagrams stay under Microsoft's terms, not the MIT License (D-27).

## Related

- [D-01](D-01-what-the-product-is.md), [D-02](D-02-how-people-and-ai-clients-use-the-product.md), [D-03](D-03-change-gate-record-or-stop.md), [D-27](D-27-technical-work-is-public-under-mit.md)
- The diagrams and their Visio source: [Diagrams](../diagrams/README.md)
- The architecture pages, which show the diagrams in context: [Architecture](../architecture/README.md)
