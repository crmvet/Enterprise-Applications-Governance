# Product.Engine.Configuration

Engine configuration (document 25, section 3.1) and the validator OP-09 with every rule of document 21, section 9.
Targets net10.0, net48 and netstandard2.0. It uses the core's own JSON model (`JsonTreeNode`), so it needs no JSON package today.

## What it does

| Folder | What |
|---|---|
| `Schema/` | A JSON Schema checker for draft 2020-12, with the keywords the product's schemas use. The configuration schemas are embedded at build time. An unknown keyword or format fails loudly |
| `Presets/` | Reads preset folders: `preset.json`, per-entity files, `patch.json`. Each file is checked against its schema profile (V01) |
| `Patches/` | Reads patches and applies them by stable key: upsert merges, retire sets `isActive` false, child rows use `parent/child` |
| `Wildcards/` | Expands `"*"` in review cadences and gate policies. Explicit beats wildcard |
| `Composition/` | Builds a configuration: preset with `extends`, then overlays, then patches |
| `Snapshots/` | The snapshot compiler (core snapshot model, canonical hash, version number) and the diff that writes the change summary |
| `Validation/` | OP-09: the rules V01 to V44, one class each in `Validation/Rules/`. `RuleSupport.cs` holds what several rules share: rule groups, roles, decision types, the gate policy index. `ValidationFacts` holds what the configuration cannot say: the validation date, version numbers, principal kinds, the binding's size limit |
| `Validation/Checks/` | The engine checks of document 25, section 5.11, which OP-09 runs after V01 to V44 |
| `Drafts/` | Draft rebase, run inside activation: re-applies a draft to the active version when its base moved, or refuses with V42 when an operation touches a row that changed between the two versions (document 21, section 4.5) |
| `Activation/` | Activation of an accepted configuration change (document 25, section 6.4, step 5) and the bootstrap of version 1 (document 21, section 4.8) |

## Order of composition (document 21, sections 6.7 and 8.4)

1. A full preset resolves its `extends` chain on source rows, before wildcards expand. So a preset that extends Balanced can change a wildcard row by its own key.
2. Wildcards expand. The key of an expanded row is `<row key>--<target key>`.
3. Overlays apply in order to the flat, expanded bundle.
4. Patches apply in order: draft patches, client binding patches, inline operations.
5. After each step any wildcard row that is left expands, so a result never holds a wildcard.

In a working set every row gets an origin. A row a client change touched is `local`. Any other row is `shipped`.
The version number stays out of Content Json, so a revert gets the hash of the version it returns to.

## Choices the validator makes

These follow the design where it is silent. Each is stated in the rule's code comment.

| Rule | Choice |
|---|---|
| V04 | An upsert on a retired row is a reused key, unless it sets `isActive` true or only edits the label, description or sort order |
| V08 | A removed row is in use when an active row of the active or the candidate version points at it, or the facts say a record uses it. Patches cannot remove rows, so V08 fires on whole-bundle comparisons |
| V09 | A wildcard row that wins no combination is reported, because it would have no effect. In a working set, retire the expanded rows first |
| V12 | Ranks must differ among active tiers. A retired tier keeps its rank |
| Upsert of a child array | Merges by child key. It never drops a child row |
| Folder problems | A preset folder with the wrong files, or a manifest that does not match, reports as V01 |
| V14 | Stage numbers and ranks must differ among active stages, as tier ranks do (V12) |
| V16 | Actor kinds are checked too: they carry an engine meaning and records stamp them |
| V19, V20, V22 | Subjects that carry a tier: business application, intake request (through its tier assessment) and change request. A configuration draft carries none |
| V19, V21, V32 | The decision type keys `change-authorization`, `configuration-change` and `gate-policy-change` are fixed, as the locked list and document 21, section 4.2 name them (open design question 1) |
| V21 | Only groups scoped by tier, change type or target stage count, because a group narrowed to one application kind or portfolio leaves the others without a group |
| V22 | Every group must hold exactly one accountable, because the most specific group replaces broader ones. Each decision type also needs a group that applies whatever the other scope fields are |
| V23 | Stays quiet where V09 already reports two equally explicit rows for one combination |
| V26 | A combination that names a missing or retired row, or a tier for a type that carries no tier, is not reachable. Separation rules are grouped by subject, one rule per group |
| V28 | The validation date is a fact. Without it the grant window is not checked; a grant with no expiresOn still fails |
| V30 | Emergency rule approvers (`approverRoleKeys`) are approvers too |
| V31 | The non-human holders in configuration are the workload roles |
| V32 | Holds the gate policy change type to the same rule as the configuration change type (document 21, section 5.4, floor 3), group by group |
| V33 | The two locked settings are compared with the values this engine ships, so a preset cannot change them. Other settings locked in the active version must stay as they were |
| V34 | Runs at activation only. It needs the version the approvals came from; without that fact it fails loudly. Label, description and sort order edits are not protected |
| V35 | Principal kinds come from the facts. A grant gives only a workload role, and only to a service principal or a managed identity |
| V37 | Waits for the first binding: a working set with no binding is still at bootstrap, and the client binding patch comes next |
| V38 | Only `guideline-hub-base-url` and `audit-retention-days` may be null. The warning document 21 asks for while the hub URL is null is not built yet, because it would fire on every working set |
| V39 | Each JSON field is checked alone against its definition, so V01 reports the same problem for the whole document as well |
| V41 | Measures the canonical snapshot text. The limit defaults to the Dataverse one and is a fact a host can change |
| V42 | Runs at activation only. A draft whose base moved may still be submitted |

## Where the rules are listed

The validator lists its rules in `ConfigurationValidator.Rules`. The engine command line prints them with `rules`.
