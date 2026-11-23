# config/presets

One folder per preset key (document 21, section 8.1):

| File | What it holds | Schema |
|---|---|---|
| `preset.json` | The manifest: C03 fields plus `schemaVersion` | `config-bundle.schema.json#/$defs/presetFile` |
| `<entity-key>.json` | One file per entity: `{ "entity": "<entity-key>", "rows": [ ... ] }` | `config-bundle.schema.json#/$defs/entityFile` |
| `patch.json` | For an overlay, or a full preset with `extends`: the operations | `config-bundle.schema.json#/$defs/presetPatch` |

Built bundles go to `config/build/`, which git ignores. The build resolves `extends`, expands wildcards, runs the validator and writes one bundle per preset. Only bundles ship.

## Rules for preset files

- Seed fields only (classes R and O of document 21, section 3). No role group bindings, workload grants, portfolio IDs, directory object IDs or secrets (V44).
- References are stable keys, never IDs.
- Wildcards (`"*"`) only in `review-cadence.tierKey` and in the three scope fields of `gate-policy`.
- A file may start with `$comment`: notes for authors, as a string or a list of strings. The build drops it. Rows themselves carry no comments.
- A row that rests on an open question is named in its file's `$comment` with `WORKING DEFAULT (open design question N)`.

## balanced

The canonical full preset. It follows document 21, section 12, with these additions:

| File | Addition | Source |
|---|---|---|
| `role.json` | Two more workload roles, `authorization-reporter` and `consistency-checker`, marked as proposals | A proposal, not adopted yet (open design question 12) |
| `guideline.json` | G1 to G24 from the public guideline list, version 1.1. G10 and G11 are retired rows, so their keys are never reused | Document 21 carries only four example rows |
| `setting.json` | Two row descriptions say "(question 8)" and "(question 9)" instead of naming a person | House rule: no names in shipped data |

Everything else is document 21, section 12, word for word: 24 settings, 14 human roles and the two adopted workload roles, the change manager for tier 3 and emergency changes, the change board for exceptions about a change, the two separation rules, the locked engine acceptance list and the read visibility settings.

`role-group-binding.json` and `standard-change-model.json` are empty on purpose: bindings are client facts, and clients list their own standard change models.

## The other presets

Starter, Regulated and the Microsoft CoE baseline are full presets written as changes to Balanced (`extends`). Small team,
strict separation and linked change management are overlays. Each `patch.json` says in its `$comment` what it changes and why.

| Preset | Kind | Notes |
|---|---|---|
| `starter` | full, extends `balanced` | `gp-prod-tier-3` is retired and `gp-prod-tier-3-flag` replaces it, because an upsert cannot clear the fail mode (V24) |
| `regulated` | full, extends `balanced` | Lists `small-team` in `conflictsWith`. A proposal, not in document 21: Regulated refuses self-approval at every tier and small team allows it with an exception |
| `microsoft-coe-baseline` | full, extends `balanced` | V29 warns twice by design: the quality review has a cadence only at tier 2 and the maturity review only at tier 3 (document 21, section 6.4). A warning does not block the build |
| `small-team` | overlay | Conflicts with `strict-separation` (V43) |
| `strict-separation` | overlay | Same separation rule keys as Regulated, so on Regulated it changes only the AI lead rule, the board count and the deployer rule |
| `linked-change-management` | overlay | One setting: `change-management-mode` is `linked` |

The build checks 22 selections: the 4 full presets alone, 5 overlay sets for Balanced, Starter and the CoE baseline,
and 3 for Regulated. `PresetCatalog.AllowedOverlayCombinations` makes the list, and `build-presets` and the test
`ShippedPresetTests` both use it. Every selection has zero errors in preset build, draft and activation mode.
