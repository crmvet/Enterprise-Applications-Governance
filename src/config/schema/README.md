# config/schema

JSON Schemas, draft 2020-12. They check shape only. Meaning, such as orphan keys, coverage and safety floors, is the engine validator's job (OP-09, rules V01 to V44 of document 21, section 9).

## Files

| File | What it checks |
|---|---|
| `config-bundle.schema.json` | The seed format of document 21, sections 3 and 8: bundles, patches, preset source files, and the engine records C01, C02 and C46 |
| `client-binding-patch.schema.json` | The client binding patch (document 21, section 6.8): upsert and retire by stable key, for role group bindings, workload grants, portfolio-scoped rows and the portfolio narrowing of `change-management-mode` |
| `fixture.schema.json` | One engine test case, `fixtures/<operation>/<case>.json` (document 25, section 10). The operation is OP-01 to OP-14, or `rebase` (document 21, section 4.5), whose name, input and outcome words are proposals of this schema |
| `replay-record.schema.json` | One recorded engine call for the replay tool (document 25, section 9): the snapshot version, the context, the request and the recorded result. `fixtures/replay/` holds the records. A proposal of this schema: document 25 names the tool, not its format |
| `examples/` | The document 21 examples word for word (`doc21-*.json`), and four example fixtures |

`tools/schema-map.json` says which files validate against which schema. `node tools/json-check.mjs` runs the check, and `node tools/ci.mjs` runs it with the other checks.

## config-bundle.schema.json

The root validates a bundle or a patch. It picks the profile from `manifest.kind` and `manifest.presetKey`:

| manifest | Profile | Used for |
|---|---|---|
| kind `bundle`, with `presetKey` | `presetBundle` | A shipped preset, written whole or built (`config/build/`) |
| kind `bundle`, no `presetKey` | `snapshotBundle` | C02 Content Json, a draft preview, an export |
| kind `patch`, with `presetKey` | `presetPatch` | An overlay, or a full preset that extends another |
| kind `patch`, no `presetKey` | `draftPatch` | An AI draft, a configuration file, a revert, an upgrade draft, a client binding patch |

Other entry points in `$defs`: `presetFile` (a preset's `preset.json`), `entityFile` (one file per entity), `configurationSet` (C01), `configurationSetVersion` (C02), `configurationDraft` (C46), `validationReport` and `reason`.

Each configuration entity C04 to C45 has four profiles:

| Profile | Fields allowed | Where |
|---|---|---|
| `<entity>.seedRow` | Seed classes R and O. Wildcards in the scope fields that allow them | Shipped presets and preset source files |
| `<entity>.setRow` | All classes, including working set (W) and engine (S) fields. No wildcards | Snapshots and exports |
| `<entity>.presetValues` | R and O, none required, no key | Upsert values in a preset patch |
| `<entity>.draftValues` | R, O and W, none required, no key | Upsert values in a draft patch |

Child rows (`list-value`, `tier-answer-option`, `decision-outcome`) nest in their parent. In a patch they use a key path: the parent key, a slash, then the child key.

## Names

- Entity keys are the kebab-case form of the document 20 logical names, such as `approval-rule`.
- Field names follow the field tables of document 21, section 3. Where document 21 drops the Json suffix of a document 20 column (`value`, `triggers`, `conditions`, `emergencyRule`, `flagNotify`, `setupNotes`), the schema follows document 21.
- References to configuration rows are stable keys, in fields ending `Key` or `Keys`. The annotation `x-references` names the target entity, so the validator can read it.
- References to operational rows end in `Id`. Directory object IDs end in `ObjectId`. Neither may appear in a shipped preset (V44).

## Proposals

Entities and fields not adopted yet carry the annotation `x-proposal` with their source:
C41 Tool Policy (document 24), C43 Column Protection Rule, C44 Portfolio Access and C45 Known Client (document 26),
the columns `requiredRoleKey` (C28), `allowsGuests` and `requiresJustInTime` (C29) and `allowsGuests` (C32) (document 26),
and the shape of a standard change condition (this schema; document 21 leaves Conditions Json open).
In `fixture.schema.json`, the operation `rebase`, its input (`rebaseInput`) and its outcome words are proposals too.

## Working defaults

The places that rest on an open question carry a `$comment` that starts `WORKING DEFAULT (open design question N)`. Search for that text to find them. Questions 1, 4, 7, 8, 9 and 12 appear here.
