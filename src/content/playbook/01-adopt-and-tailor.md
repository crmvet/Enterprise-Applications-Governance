# Chapter 1. How to adopt and tailor this playbook

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Start here
- **Parties:** IT (the platform owner and the configuration admin), the PMO, and one lead from each of the other parties
- **Guidelines:** [G1](../guidelines/G01-keep-one-register.md), [G2](../guidelines/G02-name-owners-and-deputies.md), [G3](../guidelines/G03-set-a-risk-tier.md), [G4](../guidelines/G04-record-each-decision.md), [G5](../guidelines/G05-define-status-words.md), [G6](../guidelines/G06-map-dependencies.md), [G7](../guidelines/G07-narrow-app-identities.md), [G8](../guidelines/G08-decide-how-apps-reach-dataverse.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G12](../guidelines/G12-approve-production-changes-on-record.md), [G13](../guidelines/G13-keep-an-emergency-path.md), [G14](../guidelines/G14-grant-access-through-groups.md), [G15](../guidelines/G15-keep-an-evidence-pack.md), [G16](../guidelines/G16-record-ai-use.md), [G17](../guidelines/G17-ai-drafts-people-approve.md), [G18](../guidelines/G18-retire-apps-on-purpose.md), [G19](../guidelines/G19-review-anonymous-access.md), [G20](../guidelines/G20-keep-required-documents-current.md), [G21](../guidelines/G21-give-ai-builders-a-paved-road.md), [G22](../guidelines/G22-let-the-ai-builder-ask-before-it-builds.md), [G23](../guidelines/G23-say-who-may-build-with-ai.md), [G24](../guidelines/G24-measure-flow-and-coverage.md)
- **Processes:** [P10](processes.md#p10-ai-draft-and-human-accept), [P11](processes.md#p11-configuration-change)

## Why this chapter exists

A playbook that nobody tailors sits on a shelf. A playbook that every team rewrites loses its shared words and numbers. This chapter shows how to start from the shipped defaults, change what your organization needs, and keep one numbering that people, records and AI tools all cite.

## The four layers

| Layer | What it is | Who keeps it |
|---|---|---|
| Playbook | These chapters: the parties, who decides what, and how work moves from idea to retirement | Your playbook owner, in your own wiki or site |
| Guidelines | Short numbered rules, each shaped "when, who, steps, done when, evidence" | The public list, plus your local text and links |
| Processes | Eleven [processes](processes.md): what starts the work, who decides, what is recorded | Your configuration |
| Engine | The product that is designed to run the processes and keep their records | Product releases; your configuration admin changes the configuration |

## What good looks like

- One named playbook owner, with a deputy, and one configuration admin.
- Every guideline is either adopted as is, adopted with your own text or link, or marked "not adopted" with a reason. Its number never changes.
- Tiers, stages, roles, approvals and required documents are set once, as configuration, and changed only through a recorded configuration change.
- Each party knows which chapter is theirs and where the playbook lives.

## How to adopt it

1. **Name the people.** The platform owner names the playbook owner and the configuration admin, each with a deputy (chapter 2).
2. **Pick a starting preset.** Start from one full preset and then adjust it. The product is designed to ship four: Starter (lighter gates), Balanced (the default, three tiers), Regulated (stricter gates and separation) and a Microsoft CoE baseline (Balanced mapped to Microsoft's agent risk tiers). Overlays add one concern each: small team, strict separation, and linked change management for organizations that keep an existing change tool.
3. **Walk the chapters with the parties.** Chapter 2 first, because every later chapter names roles. Then the front door (chapter 3), the tier (chapter 4) and change (chapter 9).
4. **Decide each guideline.** For each one, choose: adopt as is, adopt with local text or a local link, or not adopted with a reason. Keep the number.
5. **Set the rules.** Tiers and what each needs (chapter 4), stages and status words (chapter 5), approvals and change types (chapter 9), required documents and review cadences (chapter 12).
6. **Publish your copy where people work.** A wiki or a SharePoint communication site. Link each guideline number to your page.
7. **Use Microsoft's own front doors.** Put a short message and a link to your intake in maker welcome content and in Copilot Studio maker guidelines. Do not rebuild them.
8. **Review on a cadence.** The playbook owner reviews the playbook and the configuration at least yearly, and after any audit finding.

## Guidelines this chapter applies

Adopting the playbook means deciding every guideline, so this chapter touches all of them. Every guideline has one home chapter, where it is explained in context. Other chapters cite it where it applies.

| Guideline | Home chapter |
|---|---|
| G1 Keep one register of business applications | 5 |
| G2 Name the owners of every app, each with a deputy | 2 |
| G3 Set a risk tier for every app, on tiers you define | 4 |
| G4 Record each decision in one line: what, who, when, why | 7 |
| G5 Define your status words once, and use them everywhere | 5 |
| G6 Map what each app depends on, before you release or retire it | 7 |
| G7 Give each app identity its own narrow role | 13 |
| G8 Decide how each app reaches Dataverse | 13 |
| G9 Release the tested artifact through your own pipeline | 9 |
| G12 Approve every production change on record, never by the person who started it | 9 |
| G13 Keep an emergency path that still tests and still records | 10 |
| G14 Grant access through groups, review it, and keep a kill switch | 13 |
| G15 Keep an evidence pack, not assurances | 15 |
| G16 Record AI use on every app and agent | 11 |
| G17 Let AI draft and recommend; people approve | 11 |
| G18 Retire apps on purpose | 14 |
| G19 Review anonymous access before any site goes public | 8 |
| G20 Keep each app's required documents current | 12 |
| G21 Give AI builders a paved road | 20 |
| G22 Let the AI builder ask before it builds | 21 |
| G23 Say who may build with AI, and on which path | 18 |
| G24 Measure flow and coverage, not apps shipped | 19 |

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Keep the playbook and the operating model current | Platform owner | Playbook owner | Every party lead | Draft changes to the text |
| Change any configuration rule | Configuration admin | Configuration admin, with an AI performing the change | The role that owns the area | Prepare and propose the change, never accept it |
| Mark a guideline "not adopted" | Configuration admin | Playbook owner | Security reviewer, auditor | Draft the reason |

## Processes this chapter uses

- **P11 Configuration change.** Every tailoring step above is a configuration change: the admin says what to change, an AI client prepares it, the product is designed to validate it and show the difference, and the admin accepts it on the decision page. In the default configuration the instructing admin may accept their own AI's draft; with the strict separation overlay a second configuration admin must accept.
- **P10 AI draft and human accept.** The AI that prepares a configuration change only drafts it. The record shows "performed by" the AI, on behalf of the admin.

## How the product is designed to support it

- It is designed to hold every rule as a configuration record: tiers, stages, gates, change types, decision types, roles, approvals, required documents and review cadences. Nothing an adopter needs to change is designed to need code, a rebuild or a redeploy.
- It is designed to keep the guideline list as data: number, version, the one-line rule and a link, with your own local text and link, or a "not adopted" reason.
- It is designed to stamp every record with the guideline version and the configuration version in force when it was made, so an old record still shows the rule it followed.
- It is designed so that every label is yours to rename. A short list of fixed meanings stays in the engine, for example that a status is final, that an approver is a person, or that a route is a pipeline.
- It is designed so that an upgrade proposes new default rows as a draft for your configuration admin, and never overwrites your own changes.

## What you can tailor

- The number of tiers, their names, their questions and what each tier requires.
- The stages, gates and status words.
- Role names, and which Microsoft Entra group carries each role. Roles can be merged, split or dropped.
- Approval rules, change types and the gate level per tier, change type and environment.
- Which documents each kind of app needs, and how fresh they must stay.
- Guideline text and links, kept under the same numbers.

## Microsoft guidance it applies

- [Enable maker welcome content](https://learn.microsoft.com/power-platform/admin/welcome-content), checked 2026-11-21: in managed environments, admins can show makers their own message with a "Learn more" link, and an optional consent that is logged for audit.
- [Configure maker guidelines for Copilot Studio](https://learn.microsoft.com/power-platform/admin/maker-guidelines) (preview), checked 2026-11-21: a short message, a button link and a contact email shown to agent makers, set per environment or environment group.
- [Set up tools to support your community](https://learn.microsoft.com/power-platform/guidance/adoption/wiki-community), checked 2026-11-21: Microsoft suggests a SharePoint communication site as the home for policies and guidelines.
- [Get started with a SharePoint site template](https://learn.microsoft.com/power-platform/guidance/adoption/sharepoint-site-template), checked 2026-11-21: a ready template for that internal site.

## How AI changes this

- Tailoring no longer means editing a long document by hand. The configuration admin tells an AI client what to change, for example "add a fourth tier" or "rename the change board". The AI client prepares the change, the product is designed to show the exact difference, and the admin accepts it on the decision page.
- Adoption starts from the keynote in chapter 0 and from the paved road in chapter 20, not from a policy document that people must read before they may build.
- The four guidelines for AI development (G21 to G24) are decided like every other guideline: adopted as is, adopted with local text, or not adopted with a reason.

## Old paradigm to avoid

[5. The standards document nobody reads](17-old-paradigms-and-how-not-to-slide-back.md#5-the-standards-document-nobody-reads). Here it shows up as a tailored playbook that lives only as text on a wiki, while the rules people and AI clients actually meet are kept somewhere else, or nowhere. Keep the rules as configuration that an AI client can answer, and publish your copy to match it.

**Slide-back signal:** your published copy and your configuration disagree, or guidelines have no adoption state.

## Done when

- The playbook owner, the configuration admin and their deputies are named.
- A preset is picked, and every change from it is a recorded configuration change.
- Every guideline has an adoption state, and your copy of the playbook is published where people work.
- Maker welcome content and Copilot Studio maker guidelines point makers to your intake.
