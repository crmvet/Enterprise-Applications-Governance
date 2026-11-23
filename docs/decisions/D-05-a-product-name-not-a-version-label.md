# D-05 A product name, not a version label

- **Status:** Accepted. Answered by D-31 on 2026-11-23: the name is Enterprise Applications Governance.
- **Date:** 2026-11-21
- **Decided by:** the product owner

## Context

Early design work called the next version by a version label. A version label says nothing about what the product does, and it ties the new product to the 2021 release, which it does not upgrade in place.

## Options considered

- **Keep the version label** as the working name. Easy, but it means nothing to a reader.
- **Choose a product name.** It takes work, but the name can say what the product is.

## Decision

- **The product needs a real name.** A version label is only a version label. Product-facing text says "the product" or "the next version" until a name is chosen.
- **A name should say what the product is.** It must not use Microsoft's trademarks as part of it.
- **The product owner picks the name.**

## Why

A name should tell a reader what the product is before they read further. The product works next to Microsoft's tools, so its name must not borrow their marks.

## Consequences

- Until a name is chosen, the interim name is "Enterprise Applications Governance". The 2021 release and its name are the author's own work, published under the MIT License in 2021 (D-23). D-28 kept the interim name.
- The public docs say "the next version" for the new design and "the 2021 release" for the old one.
- The code uses placeholder names, such as `Product` in project names and a table prefix parameter, until the name is picked (D-27). Choices that are still open are marked in the code as open design questions.

## Amendment, 2026-11-23

[D-31](D-31-the-name-is-enterprise-applications-governance.md) answers this decision: the final public name is Enterprise Applications Governance. "EAGov" is used only for technical keys. The code placeholders can now be replaced, as a change of their own.

## Related

- [D-23](D-23-2021-release-and-name-are-the-authors-own.md), [D-27](D-27-technical-work-is-public-under-mit.md), [D-28](D-28-govern-ai-development-of-the-apps.md), [D-31](D-31-the-name-is-enterprise-applications-governance.md)
- [Overview](../overview.md)
