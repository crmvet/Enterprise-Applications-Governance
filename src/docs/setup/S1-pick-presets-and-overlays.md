# Guide S1. Pick a preset and overlays

- **Status:** 0.2 draft, 2026-11-23. Nothing here was run against a tenant. Steps 1 to 6 and the offline dry runs work today; steps 7 and 8 are designed but not built (see each step).
- **Guide set:** setup guides S1 to S7 and admin guides A1 to A4. Index: [README](../README.md).
- **See also:** [concepts.md](../concepts.md#configuration-versions), [admin.md](../admin.md#configuration-and-presets); [`config/presets/README.md`](../../config/presets/README.md).
- **Open design questions that touch this guide:** 1, 4, 7, 8 and 12 (markers below).

## Purpose

Choose the configuration an adopter starts from: one full preset, plus any overlays it needs, validated as one set (a working default). The choice becomes version 1 of the adopter's working set at install. After that the working set is the adopter's own, and every change is a configuration change accepted on the decision page.

## Who

| Who | Part |
|---|---|
| The adopter's platform owner | Decides. Accountable for the choice |
| The adopter's configuration admin | Will own the working set after install. Runs the workshop and the dry run, or asks an external admin to (guide [A1](../admin/A1-external-admin-access-runbook.md)) |
| The change manager and a security reviewer | Consulted on gate levels and separation of duties |
| A person with platform admin rights in the adopter's tenant | Runs the apply at install (step 7), as themselves, never through a pipeline. See open point 2 |

## Prerequisites

- The shipped presets of the release, as built bundles (`config/build/<preset-key>.bundle.json`) or, once a version is in force, through the `list_presets` tool.
- For steps 7 and 8: the product deployed with its role groups and app registrations (guide [S4](S4-prepare-entra-and-deploy.md)).
- For the dry run: the engine command line from the release (no sign-in, no tenant), and the .NET 10 SDK to run it (`src/global.json` pins the version). It reads files only. Until a release ships it built, build it once from `src/` with `dotnet build engine/Product.Engine.Cli/Product.Engine.Cli.csproj -c Release` (or run `node tools/ci.mjs --no-scrub`, which also needs Node 20 or later), which puts it where steps 3 and 8 look.
- The adopter knows roughly: how many people can approve, whether an auditor or regulator reviews change records, whether it follows Microsoft's agent CoE guidance, and whether an ITSM tool is the system of record for changes.
- Which full preset installs first by default is still open. It waits on the change gate decision, which is not made yet: whether a change approval the product records should stop a deployment that lacks it, or only record and flag it. Until then, pick the full preset every time.

## What ships

| Preset key | Kind | In one line |
|---|---|---|
| `balanced` | full | The default three-tier preset. Flag by default, advise for tier 2 in production, enforce for tier 3 in production |
| `starter` | full, extends `balanced` | Record in test, flag in production at every tier. Self-approval at tier 3 only through a recorded exception decision |
| `regulated` | full, extends `balanced` | Enforce in production from tier 2 up, an audited emergency path, a quarterly configuration review, no self-approval at any tier, a second configuration admin |
| `microsoft-coe-baseline` | full, extends `balanced` | Tiers, roles and controls mapped to Microsoft's agent risk tiers and sample RACI. Re-checked against Microsoft's guidance each release (a working default) |
| `small-team` | overlay (people) | One group may carry several roles, no change board, self-approval at the top tier needs a recorded exception |
| `strict-separation` | overlay (people) | Different people for proposer and approver, instructing and accepting admin, AI lead and platform admin; two board members for a change exception |
| `linked-change-management` | overlay (change management) | One setting: the change management mode is linked, so the ITSM tool stays the system of record |
| `data-domains-starter` | overlay (data) | A generic data domain tree to rename: Customers, Commercial, People, Operations, Finance and Workplace with fifteen subdomains, no owners (the data taxonomy: data places are business data domains the adopter configures, mapped to technical locations) |

Gate levels in production (development is off in every preset):

| Tier | Starter | Balanced | Regulated | Microsoft CoE baseline |
|---|---|---|---|---|
| Tier 1, normal and standard | flag | flag | flag | record |
| Tier 2, normal and standard | flag | advise | enforce, fail open | advise |
| Tier 3, normal and standard | flag | enforce, fail closed | enforce, fail closed | enforce, fail closed |
| Any tier, emergency | flag | flag, emergency rule | enforce, emergency rule | flag, emergency rule |

Conflicts the validator refuses (V43):

- `small-team` with `strict-separation`: each lists the other in `conflictsWith`.
- `regulated` with `small-team`: Regulated refuses self-approval at every tier, and the small team overlay allows it with an exception. This one is a proposal in `regulated/preset.json` that the configuration design does not have yet.

That leaves 44 selections the build checks: each of the 4 full presets alone, 11 overlay sets each for Balanced, Starter and the CoE baseline, and 7 for Regulated. The data domains starter overlay (a generic data domain tree to rename) conflicts with nothing, so it doubles the 22 selections there were before it.

## Steps

1. **Run the workshop.** Walk the questions below with the people above and note each answer.

   | Question | If yes | If no |
   |---|---|---|
   | Do fewer than two people hold each approving role? | Add `small-team`. Start from `starter` or `balanced` (not `regulated`) | Leave it out |
   | Does an auditor or a regulator rely on change records? | Start from `regulated` | Start from `balanced`, or `starter` for a first, light rollout |
   | Must the AI lead and the platform admin be different people, or every configuration change be accepted by a second admin? | Add `strict-separation` (Regulated already has most of it) | Leave it out |
   | Does the CoE already follow Microsoft's agent CoE guidance and want its words? | Start from `microsoft-coe-baseline` | Leave it out |
   | Is an ITSM tool the system of record for change requests? | Add `linked-change-management` | Leave it out (built-in change management) |

2. **Read the candidates.** Read each candidate's `preset.json` (description, `extends`, `conflictsWith`, `setupNotes`) and its patch `$comment`. Once a version is in force, `list_presets` returns the same facts with a summary of tiers, gate levels and approvals.

3. **Dry run the selection, offline.** From the release's `src/` folder:

   ```
   dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll validate <preset-key> --overlay <overlay-key> --mode activation
   ```

   Repeat `--overlay` for each overlay. Exit code 0 means no errors. The CoE baseline shows two V29 warnings by design (quality review cadence only at tier 2, maturity review only at tier 3); a warning does not block.

4. **Check the conflicts and the order.** The engine applies the full preset, then each overlay in the order given; a later operation on the same entity and key wins. The four shipped overlays touch different rows, and the two that overlap conflict, so their order does not change the result. The build checks them in key order.

5. **Know what the preset cannot change.** These are engine contract or locked settings, the same in every preset:
   - `ai-approval-lock`: AI never approves. Locked.
   - `engine-acceptable-decision-types`: the only decisions the engine may accept without a person's answer are lowest-tier intake and a change authorization under an active standard change model. Locked. WORKING DEFAULT (open design question 8)
   - `default-ai-only-code-approval-handling` ships as `flag`: an AI-only code approval in a client tool never counts as authorization. WORKING DEFAULT (open design question 7)
   - A short list of fixed engine meanings, for example that `configuration-admin` is the one role with Can Change Configuration (engine check `role.config-admin`). Labels are the adopter's to rename; the keys of fixed meanings stay. WORKING DEFAULT (open design question 1)
   - The two workload roles `deployment-recorder` and `approval-writer`, plus the proposed `authorization-reporter` and `consistency-checker`, are held only through Workload Grants, never through a group. WORKING DEFAULT (open design question 12)

6. **Record the decision.** Write down the full preset key, the overlay keys in order, and the content versions. At install they go into the version 1 row (`appliedPresetsJson`), so the record shows what the adopter started from.

7. **Apply at install (bootstrap of version 1). Not built yet as a route.** A person with platform admin rights runs the apply operation with the chosen preset and overlays, as themselves, usually through Claude Code and the product's MCP server. No rules exist yet, so version 1 is accepted directly by that person and recorded as a configuration change decision with that person as decider. This is engine contract, not a setting, and it is the only case outside the locked list with no approval request.
   - What is built: the engine's bootstrap operation. It accepts a person, or an AI client acting for a person (the AI client is the performer and the person the decider), who holds the platform's install rights while no version exists; it refuses a pipeline, an agent user and a person without install rights (fixtures under `fixtures/bootstrap/`).
   - What is not built: a route or MCP tool that calls it, and the platform fact that says who holds install rights. So a deployed install cannot reach version 1 yet (open points 1 and 2).
   - The local server's `seed` command is not a bootstrap. It runs only in the Development environment, and it writes version 1 and demo rows past the engine for local runs and tests.
   - [branch: Azure] The deployment identity installs the stack (guide S4). The design has a post-deployment step store each preset bundle in `config.SeedPackage`, and the first admin then runs the apply through the MCP server. Neither the seed package table nor the apply entry point exists in the template yet.
   - [branch: Dataverse] The installer holds the environment's system administrator role. Each preset bundle arrives as one read-only JSON web resource in the managed solution. Package Deployer custom code, or the setup service through the MCP server, calls the engine's apply operation after import.

8. **Apply the client binding patch next. Not built yet as a route.** The binding patch binds each human product role to the adopter's Entra group (one Role Group Binding per role, guide S4, part A), adds a Workload Grant for each pipeline and for the approval runner (guides [S5](S5-connect-the-approval-runner.md), [S6](S6-connect-pipeline-record-steps.md) and [A3](../admin/A3-workload-grants.md)), and puts the registered Claude Code client on the Known Client list (guide [S2](S2-connect-claude-code.md)). WORKING DEFAULT (open design question 12). The binding patch holds the adopter's facts, so it lives in the adopter's own private repository or nowhere, never in a public preset (V44). Guide [A1](../admin/A1-external-admin-access-runbook.md) covers how an external admin gets the access to do it.
   - Its format is `config/schema/client-binding-patch.schema.json`: upsert and retire by stable key, for `role-group-binding`, `workload-grant`, `known-client` and the portfolio-scoped rows.
   - Dry run it offline before anything else, from the release's `src/` folder, with the same preset and overlays as step 3:

     ```
     dotnet engine/Product.Engine.Cli/bin/Release/net10.0/Product.Engine.Cli.dll validate <preset-key> --overlay <overlay-key> --patch <binding-patch-file>
     ```

     With a patch the mode is draft, so the result is the working set as it would be after the patch. Exit code 0 means no errors. The output lists each row the patch adds, and V37 warns for every role that approves under a rule but has no Role Group Binding ("nobody can answer its cards"); a complete patch shows no such warning. The file stays outside the product's repository.
   - After version 1 no product role is bound yet, so nobody holds Can Change Configuration through a group, and the engine cannot take the binding patch as an ordinary change. One way is to let the bootstrap take the binding patch with the presets, so version 1 already binds the roles. That is open (open point 5).

9. **Changing preset later.** A later switch is a large configuration change: `propose_configuration_change` with the `preset` input builds a draft whose diff shows every row that would change; rows with origin local stay unless the patch replaces them. A person accepts the engine's diff on the decision page. The instructing admin may accept their own AI's draft; under Regulated or `strict-separation` a second configuration admin must accept. WORKING DEFAULT (open design question 4)

## Checks

- The dry run of step 3 exits 0 for the chosen selection, with only the expected warnings.
- The offline dry run of the binding patch (step 8) exits 0, and the file is not in the product's repository.
- After step 7: `get_configuration_health` shows version 1 active, with its content hash and the decision behind it.
- `get_configuration` for `gate-policy` matches the matrix above for the chosen preset.
- `explain_rules` on one sample record names the expected winning rule group.
- After step 8: `check_role_bindings` reports every bound group as existing, every Workload Grant with its app role assignment found and not expired, and no direct assignment without a grant (guide A3, part B). Validation already refused any grant whose Expires On is more than `workload-grant-max-days` (default 365) ahead (V28).
- After step 8: `whoami` through Claude Code answers the person's roles from their groups, and the Client App reads "Claude Code" from the Known Client list.
- After any product upgrade: the upgrade arrives as one upgrade draft, and rows the adopter changed are listed as conflicts, never overwritten (a working default).

## Open points

1. **The entry point for the bootstrap apply.** The MCP server has no tool for it, and the Azure binding has no route. `propose_configuration_change` takes a `preset` input, but it creates a draft that needs an accepted decision, and at bootstrap no approval rule exists yet. The engine operation is built; the entry point is not. Proposal: a bootstrap tool (or route) that answers only while no version exists, takes the preset, the overlays and the binding patch, and runs as the signed-in person.
2. **Who the decider of version 1 is, and what "install rights" are on Azure.** The configuration design says a person with platform admin rights accepts version 1 directly, and its Azure install step lets the deployment pipeline run the apply with its workload identity. As built, the engine refuses a pipeline and accepts only a person (directly, or through an AI client acting for them) with the platform's install rights, so the design's Azure install step should say that a pipeline only stages the seed. What proves install rights on Azure is not designed: before version 1 nobody holds a product role. Proposal: with open point 5, the caller must be a member of the group the binding patch binds to `platform-admin`, read from the token's groups claim, for the bootstrap call only.
3. The security design says Regulated sets Requires Just In Time = Yes for the configuration admin and platform admin roles. The schema has the column (`requiresJustInTime`, a proposal), but no shipped preset sets it, so the designed just-in-time check (a role that requires just-in-time membership whose group has permanent members) has nothing to flag on Regulated.
4. Which full preset installs first by default waits on the change gate decision (see Prerequisites).
5. **The binding patch after version 1:** with no role bound, nobody can instruct or accept the patch as an ordinary change. Taking the patch with the bootstrap would close the gap.

## Microsoft facts used

This guide adds no new Microsoft fact. The CoE baseline's mapping to Microsoft's agent risk tiers and sample RACI was last checked against Microsoft's Learn pages on 2026-11-22, the date the preset carries as `checkedOn`.
