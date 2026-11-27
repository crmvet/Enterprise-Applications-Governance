# Power Platform governance guidelines

- **Version:** 1.1, 2026-11-22.
- **Facts:** every Microsoft fact in these guidelines was checked on Microsoft Learn (or GitHub Docs for GitHub) on 2026-11-22. Anything not confirmed is left out and listed under "Held back, unverified" below.

These guidelines describe how an organization can govern the business applications it runs on Microsoft Power Platform, Dataverse and the apps around them. They lean on Microsoft's own guidance and link to it.

They come with the next version of an open-source product, designed to govern AI development of the apps, so people can build with AI while each app stays owned, tiered and decided by a person. Underneath sits application portfolio management for Microsoft Power Platform, Dataverse and the apps around them. Built on Microsoft, never instead of it. G21 to G24 are the guidelines written for AI development itself. The next version is in design, so each guideline says what the product **is designed to** do, not what it does today. The guidelines work on their own, with or without the product.

The [playbook](../playbook/README.md) says how the parties work together. Each of its chapters names the guidelines it applies, and each guideline names its home chapter.

## How to read a guideline

- Each guideline has a permanent number. A retired number is never reused.
- Each one gives the rule, why it matters, how the product is designed to support it, and its Microsoft anchors, each with the date it was checked.
- Each one has the same working shape: **when**, **who**, **steps**, **done when** and **evidence**.
- Tiers, stages, roles and review cadences are yours to set. Where a guideline says "your tier", it means your organization's own tier model.
- Outside frameworks appear only as "maps to". That means a related idea, never a claim of compliance.

## The list

| Number | Guideline | Home chapter | Status |
|---|---|---|---|
| G1 | [Keep one register of business applications](G01-keep-one-register.md) | 5 | active |
| G2 | [Name the owners of every app, each with a deputy](G02-name-owners-and-deputies.md) | 2 | active |
| G3 | [Set a risk tier for every app, on tiers you define](G03-set-a-risk-tier.md) | 4 | active |
| G4 | [Record each decision in one line: what, who, when, why](G04-record-each-decision.md) | 7 | active |
| G5 | [Define your status words once, and use them everywhere](G05-define-status-words.md) | 5 | active |
| G6 | [Map what each app depends on, before you release or retire it](G06-map-dependencies.md) | 7 | active |
| G7 | [Give each app identity its own narrow role](G07-narrow-app-identities.md) | 13 | active |
| G8 | [Decide how each app reaches Dataverse](G08-decide-how-apps-reach-dataverse.md) | 13 | active |
| G9 | [Release the tested artifact through your own pipeline](G09-release-the-tested-artifact.md) | 9 | active |
| G10 | [Retired, merged into G9](G10-retired.md) | | retired |
| G11 | [Retired, merged into G9](G11-retired.md) | | retired |
| G12 | [Approve every production change on record, never by the person who started it](G12-approve-production-changes-on-record.md) | 9 | active |
| G13 | [Keep an emergency path that still tests and still records](G13-keep-an-emergency-path.md) | 10 | active |
| G14 | [Grant access through groups, review it, and keep a kill switch](G14-grant-access-through-groups.md) | 13 | active |
| G15 | [Keep an evidence pack, not assurances](G15-keep-an-evidence-pack.md) | 15 | active |
| G16 | [Record AI use on every app and agent](G16-record-ai-use.md) | 11 | active |
| G17 | [Let AI draft and recommend; people approve](G17-ai-drafts-people-approve.md) | 11 | active |
| G18 | [Retire apps on purpose](G18-retire-apps-on-purpose.md) | 14 | active |
| G19 | [Review anonymous access before any site goes public](G19-review-anonymous-access.md) | 8 | active |
| G20 | [Keep each app's required documents current](G20-keep-required-documents-current.md) | 12 | active |
| G21 | [Give AI builders a paved road](G21-give-ai-builders-a-paved-road.md) | 20 | active |
| G22 | [Let the AI builder ask before it builds](G22-let-the-ai-builder-ask-before-it-builds.md) | 21 | active |
| G23 | [Say who may build with AI, and on which path](G23-say-who-may-build-with-ai.md) | 18 | active |
| G24 | [Measure flow and coverage, not apps shipped](G24-measure-flow-and-coverage.md) | 19 | active |

## Change log

| Version | Date | Change |
|---|---|---|
| 1.0 | 2026-11-22 | First public list. G1 to G9 and G12 to G19 kept or reworded. G10 and G11 retired and merged into G9. G20 added |
| 1.1 | 2026-11-22 | Four guidelines added for governing AI development of the apps: G21 (a paved road for AI builders), G22 (the AI builder asks before it builds), G23 (who may build with AI, and on which path) and G24 (measure flow and coverage, not apps shipped) |

## Retired and merged numbers

| Number | Status | Was | Why |
|---|---|---|---|
| G10 | Retired. Merged into G9 (step 2) | "Plan before every import: what changes, and what an upgrade deletes" | By design, the product never builds, hosts or runs pipelines. The pre-import check belongs in the organization's own release, so it now sits inside the release guideline |
| G11 | Retired. Merged into G9 (step 3) | "Versions only go up; fix forward by default" | Same reason. The product is designed to record deployments, not to run them, so the version rule sits with the release rule |

Retired numbers are never reused. A new guideline takes the next free number (G20 was the first; G21 to G24 followed).

## Held back, unverified

None of these appear in the guidelines.

1. **A Microsoft page that joins the three tier vocabularies** (support tiers, Copilot Studio zones, agent risk tiers). None was found; that is an absence, so it is not claimed. The mapping in G3 is the product's own default configuration.
2. **NIST CSF 2.0 ID.AM-03 for G6.** Its subject (network communication and data flows) fits app dependencies weakly, and it was not re-checked. G6 maps to ID.AM only.
3. **An EU AI Act article for G16.** No article was verified, so none is cited.
4. **A Microsoft Learn page on repository rules files for AI coding agents (G17, G21, G22).** Not checked. The step stands as practice, with no Microsoft anchor.
5. **"Personal and platform-host pipelines can't be extended" (Power Platform Pipelines).** Not re-checked for this version. Left out of G9 and G12.
6. **Agent 365 owner reassignment features and their licensing (G16).** Not re-checked. G16 states only the registry and the subscription facts.
7. **Which access review capabilities work with Microsoft Entra ID P2 alone (G14).** Learn says "some capabilities"; the list per capability was not checked.
8. **Plan limits for GitHub's prevent self-review setting (G12).** GitHub Docs states plan limits for required reviewers, not for this setting, so none is claimed.
9. **AICPA TSC CC8.1 and ISO/IEC 27001:2022 control 8.32 wording.** Not read. Only the identifier and name are used, as "maps to".
10. **Defaults for AI-built Power Pages sites (G19).** Whether they differ from other sites was not checked. G19 applies to every site.
11. **"Paved road" as a Microsoft term (G21).** Not found on the pages read. Microsoft's word is "golden path", and G21 says so.
12. **A Microsoft page that joins the builder paths to the Copilot Studio zones (G23).** None was found. The mapping is the playbook's own default, written as "maps to".
13. **Every product capability.** Nothing is released or deployed. Each product line is design intent, and no release date is stated.

Microsoft, Azure, Azure DevOps, Copilot, Copilot Studio, Dataverse, Entra, Microsoft 365, Microsoft Agent 365, Microsoft Entra Agent ID, Power Apps, Power Automate, Power Pages, Power Platform, Purview and Visual Studio are trademarks of the Microsoft group of companies. GitHub and GitHub Copilot are trademarks of GitHub, Inc. Claude and Claude Code are trademarks of Anthropic. Framework names belong to their publishers.
