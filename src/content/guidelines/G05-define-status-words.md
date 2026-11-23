# G5. Define your status words once, and use them everywhere

- **Number:** G5
- **Version:** 1.0, 2026-11-22
- **Status:** active
- **Home chapter:** 5 (One register of business applications)
- **Cited in chapters:** 1, 5, 6
- **Processes:** P01, P09, P11

**Rule.** Agree one short list of lifecycle stages and status words, each with one meaning. Use the same words in the register, in reports and in conversation.

**Why it matters.** "Live" can mean a pilot for one team or a service for the whole company. Shared words make reviews, reports and audits comparable.

**How the product is designed to support it.** It is designed to hold stages and statuses as configuration records, so every record, page and report shows the same words.

## Microsoft anchors

- [Azure API Center key concepts](https://learn.microsoft.com/azure/api-center/key-concepts), checked 2026-11-22: an API version can be at any lifecycle stage "from design, to preview, production, or deprecated".
- [LifecycleStage enum](https://learn.microsoft.com/python/api/azure-mgmt-apicenter/azure.mgmt.apicenter.models.lifecyclestage?view=azure-python) (Azure API Center SDK reference), checked 2026-11-22: the stage values are design, development, testing, preview, production, deprecated and retired.
- [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/azure/well-architected/architect-role/architecture-decision-record), checked 2026-11-22: decision status words such as Proposed, Accepted and Superseded.

## Working shape

- **When:** when you set up governance, and when you review the vocabulary on a set cadence.
- **Who:** the platform team owns the list; the PMO and architects agree it.
- **Steps:**
  1. Choose the stages an app moves through, from idea to retired, and define each in one sentence.
  2. Say who may move an app into each stage.
  3. Use only these words in the register, reports and dashboards.
- **Done when:** every app shows one stage from the agreed list, and no report uses a word outside it.
- **Evidence:** the published list with its definitions, and each app's stage history.
