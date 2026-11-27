# D-41 The data taxonomy details, and a "never" rule cannot be broken

- **Status:** Accepted. Answers the details D-37 left open.
- **Date:** 2026-11-24
- **Decided by:** the product owner

## Context

D-37 set the shape of the data taxonomy and left its details to the design. The design put nine detail questions, numbered 1 to 9, each with lettered options and a recommendation. Other pages and the code cite them in that form, for example "D-41, question 6, option A".

Question 7 asked what happens to a data use that a "never" rule forbids. Examples are restricted data copied to a test environment, or bank details sent to an AI model where the domain's rule for AI use is never. The design recommended recording such a use and flagging it, the way the product flags a deployment that arrives another way (D-19).

## Options considered

The nine questions, with their options. The design recommended the first option, A, for every question except question 4, where it recommended B in Balanced and Regulated and A in Starter.

**Question 1. Are domain owners records or configuration?**

- **A. Records.** An owner assignment with a domain as its subject.
  - Pros: one owner check, deputies with dates, and no queue at the configuration admin.
  - Cons: who owned a domain under an earlier configuration version is answered from the owner history, not from the configuration snapshot.
- **B. Configuration.** Owner fields on the domain row, versioned with the tree.
  - Pros: one versioned picture.
  - Cons: every change of person waits for a configuration change, deputies have no dates, and the owner check and the leaver process miss them.

**Question 2. Are locations and mappings records or configuration?**

- **A. Records,** kept by the people who run the stores. Only the domain tree, its levels, the classifications and the vendor platforms are configuration.
  - Pros: the people who run a store keep it current, with a "last seen" date, and tenant and environment IDs stay out of configuration.
  - Cons: their history sits in the record history, not in configuration versions.
- **B. Configuration.**
  - Pros: one versioned picture.
  - Cons: every move of a store waits for a configuration admin, the snapshot grows with the estate, and environment IDs move into configuration.

**Question 3. What happens to the one data owner per application?**

- **A. Derive, with a fallback.** An application's data owners are derived from the domains it uses. The application-level data owner stays as a fallback where domains are optional.
  - Pros: a request goes to the person who owns the data, and existing installs keep working.
  - Cons: an application must declare its uses to get the right route.
- **B. Keep both, independent.**
  - Pros: no change.
  - Cons: two answers to "who decides".
- **C. Retire the application-level data owner now.**
  - Pros: one clean model.
  - Cons: existing data-use decisions break until every application declares its uses.

**Question 4. Who appoints the owner of a top-level domain?**

- **A. The platform admin.**
  - Pros: no new role.
  - Cons: IT appoints the business's data owners.
- **B. A new product role, the data governance lead,** for the data office or its equivalent.
  - Pros: the business appoints the owners of business data.
  - Cons: one more group to bind. The small team overlay binds it to the platform admin's group.
- **C. A single root domain, "All company data",** owned by a named person.
  - Pros: no new role.
  - Cons: a person must be named at install, and that person owns all data by default.

**Question 5. Who may draft changes to the domain tree?**

- **A. The configuration admin only.** The owners of the touched domains are consulted, and a change that loosens a rule needs their approval.
  - Pros: no change to the safety floor, and the smallest build.
  - Cons: the data office waits on the configuration admin for every rename.
- **B. The data governance lead and domain owners may also draft,** limited to their own part of the tree. The configuration admin still accepts.
  - Pros: the data office keeps its own words.
  - Cons: a scoped drafting right, and a check of the part of the tree each draft touches.
- **C. Domain owners accept changes in their own part of the tree.**
  - Pros: the fastest.
  - Cons: a second role that accepts configuration, beside the configuration admin.

**Question 6. Which uses need a data owner's decision by default?**

- **A. They follow sensitivity.** Public data needs none. Internal data needs one only when it reaches AI. Confidential and restricted data need one for every use.
  - Pros: owners see AI use and sensitive use, not every read of internal data.
  - Cons: a read of internal data without AI reaches its owner only as a record.
- **B. Every use above public.**
  - Pros: owners see everything.
  - Cons: many requests, and routine reads stall.
- **C. AI use only.**
  - Pros: the lightest.
  - Cons: nobody asks the owner before an application without AI uses the data.

**Question 7. A use that breaks a never rule: record and flag, or refuse the save?**

- **A. Record and flag.** The use is saved, no decision can allow it, the gate blocks, and the data owner and the security reviewer see it.
  - Pros: nothing is hidden. The register shows what really happens.
  - Cons: the register holds a use that can never be allowed.
- **B. Refuse the save.** Nobody can save a use that breaks a never rule.
  - Pros: no one holds the privilege to break the rule. The register holds only uses that can be allowed.
  - Cons: a use that already happens outside the product is not on the register until the data is left out or the rule is changed.

**Question 8. Who plays the data roles in the demo?**

- **A. Fictional people already in the demo estate.**
  - Pros: no new names, and the demo story keeps its applications.
  - Cons: one fictional person plays more than one part.
- **B. A new fictional data office persona.**
  - Pros: clearer role play.
  - Cons: one more name in the story.

**Question 9. How fine are locations?**

- **A. Two levels:** an instance, then a store inside it. Table names stay as text on the mapping.
  - Pros: far fewer rows, and no drift toward being a data catalog. The build loop still resolves a table to its domain.
  - Cons: a table cannot have its own technical owner.
- **B. A full tree down to every table.**
  - Pros: exact, with an owner for each table.
  - Cons: heavy upkeep, and a drift toward being a data catalog.

## Decision

**A never rule cannot be broken (question 7, option B).** A data use, or a copy of data outside production, whose governing rule is never is refused when it is saved, for people and AI clients alike. The refusal names the rule, the domain and where the rule is set. The product owner chose this over the design's recommendation to record and flag (option A). Other pages cite it as "D-41, question 7, as the product owner changed it".

**The other details follow the design's recommendations:**

| # | Question | Option chosen | Decision |
|---|---|---|---|
| 1 | Are domain owners records or configuration? | A | Records: an owner assignment with a domain as its subject, with deputies and dates, covered by the owner check and the leaver process |
| 2 | Are locations and mappings records or configuration? | A | Records, kept by the people who run the stores. The domain tree, its levels, the classifications and the vendor platforms are configuration |
| 3 | What happens to the one data owner per application? | A | An application's data owners are derived from the domains it uses. The application-level data owner stays as a fallback where domains are optional. Where domains are required, as in Regulated, it no longer counts |
| 4 | Who appoints the owner of a top-level domain? | B, and A in Starter | A new product role, the data governance lead, in Balanced and Regulated. The platform admin in Starter |
| 5 | Who may draft changes to the domain tree? | A | The configuration admin. The owners of the touched domains are consulted, and a change that loosens a rule needs their approval. Option B is not built |
| 6 | Which uses need a data owner's decision by default? | A | They follow sensitivity. In Balanced, public data needs none. Internal data needs one only when it reaches AI. Confidential and restricted data need one for every use, and restricted data reaching AI also needs the security reviewer. Masked copies of confidential data may go to test with a decision. Restricted data never goes to test |
| 7 | A use that breaks a never rule: record and flag, or refuse the save? | B, changed by the product owner | Refused when it is saved, as above |
| 8 | Who plays the data roles in the demo? | A | Fictional people already in the demo estate |
| 9 | How fine are locations? | A | Two levels: an instance, then a store inside it. Table names stay as text on the mapping |

The [data taxonomy](../architecture/data-taxonomy.md) page describes the design as built.

## Why

- **The never rule.** A rule that says never must mean never. Nobody holds the privilege to save such a use, so the check is about the data, not about who asks, a person or an AI client. An AI builder hears the answer before it builds: the product's `check_data_use` tool says that the rule is never and that a save would be refused.
- **The other details.** Each follows the design's principles. Facts about people and the estate are records with history, and words are configuration with versions (D-06, D-10). A decision goes to the person who owns the data. The business appoints the owners of business data, not IT. The product stays out of the catalog's job (D-15).

## Consequences

- **What is still recorded and flagged.** A use saved before a rule tightened stays on the register, shows that it breaks the rule, and can always be ended. Ending or archiving a use is never refused. A break that is not a never rule, such as an unmasked copy where only masked copies are allowed, is recorded and flagged, not refused.
- **The engine enforces the never rule** as a guard rule on every save. Engine fixtures cover the refused saves (a person's, an AI client's, a copy to test, and a use that names a future end date) and the allowed end of a never use.
- **A new product role, the data governance lead,** appoints top-level domain owners and sees every data gap. The small team overlay's setup notes say to bind it to the platform admin's group.
- **How later detail questions are settled.** From this decision on, the product owner let the AI sessions settle the other detail questions of a design with the design's recommendation. Each choice is recorded with its reason, then built. Questions that only the product owner can answer, such as a release, still wait for the product owner.
- **Status.** The data taxonomy is built and tested with these choices. It runs as a demo install with fictional data. There is no release yet. The demo shows the refusal: an AI client tries to save a use that a never rule forbids, and the refusal names the rule and where it is set.

## Related

- [D-06 Risk tiers are configuration, never hardcoded](D-06-risk-tiers-are-configuration.md)
- [D-10 Rules are configuration, the engine is code](D-10-rules-are-configuration-and-the-engine-is-code.md)
- [D-15 An on-demand gap check instead of importing Microsoft's inventory](D-15-gap-check-instead-of-inventory-import.md)
- [D-19 The product never builds pipelines](D-19-never-build-pipelines.md)
- [D-28 Govern AI development of the apps](D-28-govern-ai-development-of-the-apps.md)
- [D-37 Business data domains, configured per install and mapped to technical locations](D-37-business-data-domains-mapped-to-technical-locations.md)
- [Data taxonomy](../architecture/data-taxonomy.md)
- [MCP tools](../architecture/mcp-tools.md), the `data` toolset
- The guard rule for data writes: [src/engine/Product.Engine.Core/Operations/DataGuard.cs](../../src/engine/Product.Engine.Core/Operations/DataGuard.cs), with its fixtures in [src/fixtures/op-08/](../../src/fixtures/op-08/) (the `g25-` files)
- The validation rules for the taxonomy: [src/engine/Product.Engine.Configuration/Validation/Rules/DataTaxonomyRules.cs](../../src/engine/Product.Engine.Configuration/Validation/Rules/DataTaxonomyRules.cs)
- The Balanced classifications and their default rules: [src/config/presets/balanced/data-classification.json](../../src/config/presets/balanced/data-classification.json)
- The data governance lead's guide: [src/docs/guide/data-governance-lead.md](../../src/docs/guide/data-governance-lead.md)
