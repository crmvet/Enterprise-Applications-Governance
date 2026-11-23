# Guide S1. Pick a preset and overlays

- **Status:** skeleton, 0.1 draft, 2026-11-22. Nothing here was run against a tenant.
- **Guide set:** setup guides S1 to S3 and admin guides A1 and A2. Index: [README](../README.md).
- **Design sources:** document 21, sections 4.8, 4.9, 6.1 to 6.8 and 7.6; document 26, sections 3.5 and 7.7; document 28, Q2, Q3, Q5, Q6 and Q10; `config/presets/README.md`.
- **Open design questions that touch this guide:** 1, 4, 7, 8 and 12 (markers below).

## Purpose

Choose the configuration an adopter starts from: one full preset, plus any overlays it needs, validated as one set (working default, 28, Q3). The choice becomes version 1 of the adopter's working set at install. After that the working set is the adopter's own, and every change is a configuration change accepted on a card.

## Who

| Who | Part |
|---|---|
| The adopter's platform owner | Decides. Accountable for the choice |
| The adopter's configuration admin | Will own the working set after install. Runs the workshop and the dry run, or asks an external admin to (guide [A1](../admin/A1-external-admin-access-runbook.md)) |
| The change manager and a security reviewer | Consulted on gate levels and separation of duties |
| A person with platform admin rights in the adopter's tenant | Runs the apply at install (step 7). See open point 2 |

## Prerequisites

- The shipped presets of the release, as built bundles (`config/build/<preset-key>.bundle.json`) or, once installed, through the `list_presets` tool.
- For the dry run: the engine command line from the release (no sign-in, no tenant). It reads files only.
- The adopter knows roughly: how many people can approve, whether an auditor or regulator reviews change records, whether it follows Microsoft's agent CoE guidance, and whether an ITSM tool is the system of record for changes.
- Which full preset installs first by default is still open under D-03 (the change gate page suggests Starter). Until D-03 is decided, pick it every time.

## What ships

| Preset key | Kind | In one line |
|---|---|---|
| `balanced` | full | The default three-tier preset. Flag by default, advise for tier 2 in production, enforce for tier 3 in production |
| `starter` | full, extends `balanced` | Record in test, flag in production at every tier. Self-approval at tier 3 only through a recorded exception decision |
| `regulated` | full, extends `balanced` | Enforce in production from tier 2 up, an audited emergency path, a quarterly configuration review, no self-approval at any tier, a second configuration admin |
| `microsoft-coe-baseline` | full, extends `balanced` | Tiers, roles and controls mapped to Microsoft's agent risk tiers and sample RACI. Re-checked against Microsoft's guidance each release (working default, 28, Q6) |
| `small-team` | overlay (people) | One group may carry several roles, no change board, self-approval at the top tier needs a recorded exception |
| `strict-separation` | overlay (people) | Different people for proposer and approver, instructing and accepting admin, AI lead and platform admin; two board members for a change exception |
| `linked-change-management` | overlay (change management) | One setting: the change management mode is linked, so the ITSM tool stays the system of record |

Gate levels in production (document 21, section 7.6; development is off in every preset):

| Tier | Starter | Balanced | Regulated | Microsoft CoE baseline |
|---|---|---|---|---|
| Tier 1, normal and standard | flag | flag | flag | record |
| Tier 2, normal and standard | flag | advise | enforce, fail open | advise |
| Tier 3, normal and standard | flag | enforce, fail closed | enforce, fail closed | enforce, fail closed |
| Any tier, emergency | flag | flag, emergency rule | enforce, emergency rule | flag, emergency rule |

Conflicts the validator refuses (V43):

- `small-team` with `strict-separation` (document 21).
- `regulated` with `small-team`. This one is a proposal in `regulated/preset.json`, not in document 21.

That leaves 22 selections the build checks: each of the 4 full presets alone, 5 overlay sets each for Balanced, Starter and the CoE baseline, and 3 for Regulated.

## Steps

1. **Run the workshop.** Walk the questions below with the people above and note each answer.

   | Question | If yes | If no |
   |---|---|---|
   | Do fewer than two people hold each approving role? | Add `small-team`. Start from `starter` or `balanced` (not `regulated`) | Leave it out |
   | Does an auditor or a regulator rely on change records? | Start from `regulated` | Start from `balanced`, or `starter` for a first, light rollout |
   | Must the AI lead and the platform admin be different people, or every configuration change be accepted by a second admin? | Add `strict-separation` (Regulated already has most of it) | Leave it out |
   | Does the CoE already follow Microsoft's agent CoE guidance and want its words? | Start from `microsoft-coe-baseline` | Leave it out |
   | Is an ITSM tool the system of record for change requests? | Add `linked-change-management` | Leave it out (built-in change management) |

2. **Read the candidates.** Read each candidate's `preset.json` (description, `extends`, `conflictsWith`, `setupNotes`) and its patch `$comment`. Once the product is installed, `list_presets` returns the same facts with a summary of tiers, gate levels and approvals.

3. **Dry run the selection, offline.** From the release's `src/` folder:

   ```
   dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll validate <preset-key> --overlay <overlay-key> --mode activation
   ```

   Repeat `--overlay` for each overlay. Exit code 0 means no errors. The CoE baseline shows two V29 warnings by design (quality review cadence only at tier 2, maturity review only at tier 3); a warning does not block.

4. **Check the conflicts and the order.** The engine applies the full preset, then each overlay in the order given; a later operation on the same entity and key wins (document 21, section 6.7). The three shipped overlays touch different rows, and the two that overlap conflict, so their order does not change the result. The build checks them in key order.

5. **Know what the preset cannot change.** These are engine contract or locked settings, the same in every preset:
   - `ai-approval-lock`: AI never approves. Locked.
   - `engine-acceptable-decision-types`: the only decisions the engine may accept without a person's card are lowest-tier intake and a change authorization under an active standard change model. Locked. WORKING DEFAULT (open design question 8)
   - `default-ai-only-code-approval-handling` ships as `flag`: an AI-only code approval in a client tool never counts as authorization. WORKING DEFAULT (open design question 7)
   - A short list of fixed engine meanings, for example that `configuration-admin` is the one role with Can Change Configuration (engine check `role.config-admin`). Labels are the adopter's to rename; the keys of fixed meanings stay. WORKING DEFAULT (open design question 1)
   - The two workload roles `deployment-recorder` and `approval-writer`, plus the proposed `authorization-reporter` and `consistency-checker`, are held only through Workload Grants, never through a group. WORKING DEFAULT (open design question 12)

6. **Record the decision.** Write down the full preset key, the overlay keys in order, and the content versions. At install they go into the version 1 row (`appliedPresetsJson`), so the record shows what the adopter started from.

7. **Apply at install (bootstrap of version 1).** A person with platform admin rights runs the apply operation with the chosen preset and overlays, as themselves, usually through Claude Code and the product's MCP server (document 21, section 4.8). No rules exist yet, so version 1 is accepted directly by that person and recorded as a configuration change decision with that person as decider. This is engine contract, not a setting, and it is the only no-card case outside the locked list.
   - [branch: Dataverse] The installer holds the environment's system administrator role. Each preset bundle arrives as one read-only JSON web resource in the managed solution. Package Deployer custom code, or the setup service through the MCP server, calls the engine's apply operation after import (document 21, section 11.1). See open point 1.
   - [branch: Azure] The deployment identity installs the stack. A post-deployment script stores each preset bundle in `config.SeedPackage`. The deployment pipeline then runs the engine's apply command, or the first admin runs it through the MCP server (document 21, section 11.2). See open points 1 and 2.

8. **Apply the client binding patch next.** Version 2 binds each human product role to the adopter's Entra group (one Role Group Binding per role) and adds the Workload Grants for pipelines and the approval runner. WORKING DEFAULT (open design question 12). The binding patch holds the adopter's facts, so it lives in the adopter's own private repository or nowhere, never in a public preset (V44). Guide [A1](../admin/A1-external-admin-access-runbook.md) covers how an external admin gets the access to do it.

9. **Changing preset later.** A later switch is a large configuration change: `propose_configuration_change` with the `preset` input builds a draft whose diff shows every row that would change; rows with origin local stay unless the patch replaces them. A person accepts the engine's diff on one card. The instructing admin may accept their own AI's draft; under Regulated or `strict-separation` a second configuration admin must accept. WORKING DEFAULT (open design question 4)

## Checks

- The dry run of step 3 exits 0 for the chosen selection, with only the expected warnings.
- After step 7: `get_configuration_health` shows version 1 active, with its content hash and the decision behind it.
- `get_configuration` for `gate-policy` matches the matrix above for the chosen preset.
- `explain_rules` on one sample record names the expected winning rule group.
- After step 8: `check_role_bindings` shows every human role bound to an existing group, every workload grant with an Expires On no more than `workload-grant-max-days` (default 365) ahead, and no direct assignment without a grant.
- After any product upgrade: the upgrade arrives as one upgrade draft, and rows the adopter changed are listed as conflicts, never overwritten (document 21, section 4.9; 28, Q5).

## Open points

1. The MCP route for the bootstrap apply has no tool in document 24. `propose_configuration_change` takes a `preset` input, but it creates a draft that needs a card, and at bootstrap no approval rule exists yet. Document 21 names the other routes (Package Deployer custom code on Dataverse, the engine's apply command on Azure). Document 24 should either add the MCP entry point or say the bootstrap never goes through MCP.
2. Who the decider of version 1 is. Document 21, section 4.8 says a person with platform admin rights accepts version 1 directly. Section 11.2 lets the Azure deployment pipeline run the apply with its workload identity, which is not a person, and before any role is bound nobody holds "platform admin rights" in the product. The documents should say which person is recorded as the decider on each branch, and that a pipeline only stages the seed.
3. RR-06 (document 26, section 3.5) says Regulated sets Requires Just In Time = Yes for the configuration admin and platform admin roles. The schema has the column (`requiresJustInTime`, a proposal), but no shipped preset sets it, so CHK-08 has nothing to flag on Regulated.
4. Which full preset installs first by default is part of D-03.

## Microsoft facts used

This guide adds no new Microsoft fact. The CoE baseline's mapping to Microsoft's agent risk tiers and sample RACI comes from document 21, section 6.4 (sources S2 and T1 there, checked 2026-11-22), and the preset carries `checkedOn` 2026-11-22.
