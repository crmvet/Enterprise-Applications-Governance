# Chapter 9. Change control and release

- **Version:** 0.2 draft, 2026-11-22
- **Track:** Shared
- **Parties:** change control (change manager, change board, release manager), IT, the business owner, testers
- **Guidelines:** [G6](../guidelines/G06-map-dependencies.md), [G9](../guidelines/G09-release-the-tested-artifact.md), [G12](../guidelines/G12-approve-production-changes-on-record.md), [G13](../guidelines/G13-keep-an-emergency-path.md), [G17](../guidelines/G17-ai-drafts-people-approve.md)
- **Processes:** [P03](processes.md#p03-change-management), [P05](processes.md#p05-deployment-record)

## Why this chapter exists

"Every tool approves its own releases. I have no change history per app across them." "Some changes go around the pipeline, and nobody records them." "Our change board was built for servers. A form change waits a week, or skips the board." Change control for low-code and AI-built apps needs types that fit the risk, an authority that is a named role rather than a weekly meeting, and one change history per app across every tool that deploys it.

## What good looks like

- Every production change has a change record, an approval that fits its type and tier, and a planned window.
- Whoever starts a production release does not approve it. A small team that cannot separate the two records the exception.
- The same artifact that passed test goes to production, through your own pipeline. Versions only go up.
- Every deployment is recorded, whatever tool ran it. Deployments that came another way are flagged.
- Before a change, the owners of everything that depends on it are told.

## Change types (default)

| Type | When | Who authorizes |
|---|---|---|
| Standard | It follows a model the change manager listed for this tier, moves the same tested artifact through the pipeline, and adds no new data use, connector, permission or identity | Pre-authorized by the model |
| Normal | Anything else | Business owner at the lower tiers; change manager at the top tier, with security and architecture consulted |
| Emergency | Service down or a security exposure that cannot wait | Change manager, at once (chapter 10) |

Some changes are always normal: a new restricted data use, a role change for the app's own identity, an agent model or instruction change, a change to a shared component that others depend on, and anything that could raise the tier. The change board handles exceptions and windows that cover several applications.

## How it works

1. **Draft the change.** The change owner, or their AI client, drafts it: application, proposed type, window and target. The affected applications come from the dependency map, and their owners are informed.
2. **Classify.** Check whether it fits a standard change model. A person sets the type.
3. **Submit and authorize.** Standard changes are pre-authorized. In the default configuration they need no new approval, because a person approved the model once. Normal changes go for approval per the tier and type, and people answer on the decision page.
4. **Plan the import.** Before the import, list what changes and which components the upgrade deletes, and attach the list to the change.
5. **Deploy through your pipeline.** Build once, then promote that one artifact through each stage: Power Platform Pipelines, Azure Pipelines or GitHub Actions. Turn on self-approval prevention and window checks where the tool offers them.
6. **Record the deployment.** A last step in the pipeline records what, where, when, which pipeline and who approved. The owner's AI tool or a person can record it too.
7. **Follow up flags.** A deployment that came another way, lacked its approval or landed outside its window is flagged. The change manager opens a change after the fact or records an exception.
8. **Go live and close.** A first production deployment prompts the go-live decision. The change owner confirms the result. A failed change opens a review.

**AI-only code approvals.** Some code review tools let an AI review count toward the required approvals of a pull request. In the default configuration such an approval never counts as authorization for a change, and a deployment whose only code approval came from an AI is flagged.

**Linked mode.** If you already run a change tool, the change record is designed to hold that tool's number and link instead, and never to copy its workflow. A recorded number alone never counts as authorization: your tool reports it through a step you add, or the change manager confirms it on the decision page.

## Gate levels

Each organization sets a gate level per tier, change type and environment: Off, Record, Flag, Advise or Enforce. At every level the deployment tool's own approvals keep running. Off, Record and Flag need nothing in your pipeline. Advise and Enforce need a check step in your own pipeline that asks the product first. No gate level can stop a manual import; it can only be flagged.

## Who decides (default decision rights)

| Decision or task | Accountable | Does the work | Consulted | AI may |
|---|---|---|---|---|
| Keep the standard change models per tier | Change manager | Platform admin | Security reviewer, architect | Draft a model |
| Authorize a normal change, lower tiers | Business owner | Platform admin | Data owner | Check the standard change fit, draft release notes |
| Authorize a normal change, top tier | Change manager | Platform admin | Business owner, data owner, security reviewer, architect; the change board for exceptions | Same |
| Keep the change schedule and windows | Change manager | Platform admin | Business owner | Nothing |
| Deploy, in your own tool | Platform admin | Platform admin | | Start a test-stage run when a person asks |
| Declare the app live | Business owner | Platform admin | | Nothing |

## Guidelines this chapter applies

- **G6 Map what each app depends on.** Before a release, find who depends on what is changing, and tell their owners.
- **G9 Release the tested artifact through your own pipeline.** Same artifact, a known upgrade effect, versions only up, and a record step at the end.
- **G12 Approve every production change on record.** Change record, fitting approval, planned window, and no self-approval without a recorded exception.
- **G13 Keep an emergency path that still tests and still records.** The emergency type, detailed in chapter 10.
- **G17 Let AI draft and recommend; people approve.** AI checks and drafts; a code approval from an AI alone never authorizes a change.

## Processes this chapter uses

- **P03 Change management.** The change request, its type, authorization, schedule, go-live and result.
- **P05 Deployment record.** The record from your pipeline, an AI tool or a person, and the flags.

## How the product is designed to support it

- It is designed to run change requests, change types, approvals and windows by default, or to link your existing change tool.
- It is designed to map which of your pipelines deploys each app, to take a deployment record from a step at the end of that pipeline, and to flag deployments that arrive another way, lack their approval or fall outside their window.
- It is designed so that a pipeline signs in with its own workload identity, which in the default configuration holds the deployment recorder role through a recorded, expiring grant.
- It is designed to publish the pipeline record steps as copy-paste samples. By design, it never builds, hosts or runs pipelines.
- It is designed to keep one change and deployment history per app across Power Platform Pipelines, Azure DevOps, GitHub and manual entries.

## What you can tailor

- The change types, the standard change models and the always-normal list.
- Who authorizes each type at each tier, and the separation of duties rules.
- The gate level per tier, change type and environment, and the expected path into each environment.
- Built-in or linked change management.

## Microsoft guidance it applies

- [Overview of pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/pipelines), checked 2026-11-22: "the same solution artifact will be deployed" to each stage.
- [Upgrade or update a solution](https://learn.microsoft.com/power-apps/maker/data-platform/update-solutions), checked 2026-11-22: an upgrade deletes components of the previous version that are not in the new one.
- [Recommendations for safe deployment practices](https://learn.microsoft.com/power-platform/well-architected/operational-excellence/safe-deployments) (Power Platform Well-Architected, OE:10), checked 2026-11-22: small, quality-gated releases, with paths for routine and emergency deployments.
- [Define approvals and checks](https://learn.microsoft.com/azure/devops/pipelines/process/approvals) (Azure Pipelines), checked 2026-11-22: approvals can restrict approvers from approving their own runs, and a Business hours check holds deployments to a window.
- [Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments) (GitHub Docs), checked 2026-11-22: prevent self-review stops the person who started a deployment from approving it.
- [Extend pipelines in Power Platform](https://learn.microsoft.com/power-platform/alm/extend-pipelines), checked 2026-11-22: a delegated deployment "requires approval from an authorized identity."
- [Copilot code review approvals](https://github.blog/changelog/2026-10-28-copilot-code-review-merge-with-confidence-with-max-and-approvals) (GitHub changelog), checked 2026-11-22: Copilot approvals can count toward required approvals, and this is off by default.

## How AI changes this

- AI produces more changes, and smaller ones. A weekly meeting cannot keep up, so the standard change models and the tier carry the load: a routine change moves on a model a person approved once, and only a normal change waits for its named approver.
- The change owner's AI client drafts the change and the release notes, lists the affected applications from the dependency map, and checks whether the change fits a standard model. A person sets the type.
- A code approval from an AI never authorizes a change. In the default configuration, a deployment whose only code approval came from an AI is flagged.
- Your own pipeline deploys the tested artifact, and its last step records the deployment. An AI client may start a test-stage run when a person asks. It never approves a release.

## Old paradigm to avoid

[1. One board approves every change](17-old-paradigms-and-how-not-to-slide-back.md#1-one-board-approves-every-change). In this chapter it shows up as every production change waiting for the change board, whatever its type and tier. Keep the board for exceptions and for windows that cover several applications.

**Slide-back signal:** the share of standard changes falls, or lower-tier normal changes wait for a meeting.

## Done when

- Every production deployment links to an approved change, approved by someone other than the requester or covered by a recorded exception.
- Every production deployment matches an artifact that passed test, carries a higher version, and is recorded.
- Every flagged deployment has a follow-up decision.
