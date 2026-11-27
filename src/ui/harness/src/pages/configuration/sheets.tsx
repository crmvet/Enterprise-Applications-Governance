// The configuration change sheet: the draft builder of process P11 in the shared ActionSheet (components/actions.tsx).
// A configuration admin picks what to change (any configuration entity), adds, changes or retires rows with typed
// fields, and checks the draft first: propose_configuration_change with validateOnly, which shows the engine's diff, the
// rule check, the applications and gate levels it touches, the data uses on the domains it touches and who accepts it.
// Saving stores the draft (still nothing in force); the draft's page submits it (submit_record, subjectKind
// configuration-draft), and a second person accepts it on the decision page (D-38).
//
// The Data page (Domains tab: propose a change to the tree) and Roles and access (propose a binding change) import
// ConfigurationChangeSheet. Its props are the contract the wave 3 scaffold set; DraftBuilderSheet is the same sheet with the Configuration page's own extras (open it from
// code, start the editor on one row, edit a stored draft's rationale).
//
// Choices (D-41):
//   - The check is the sheet's own button, not ActionSheet's preview: the check of a draft with rule errors still
//     answers ok (valid false), and the page shows the diff, the touches and the approvals, which ActionSheet's engine
//     answer has no room for.
//   - A new row's key is made from its label (lowercase words and hyphens, never one the entity used before), so no
//     person types or reads an internal key.
//   - A structured field (an emergency rule, triggers, a group's object ID) is kept as it is: an AI client or a
//     configuration file changes it. A row the page opened with such a value keeps it.
import { ShieldCheck } from 'lucide-react';
import { useEffect, useMemo, useRef, useState, type ReactElement } from 'react';
import { ActionSheet, Field, useWriteAction, type FormProblem } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Textarea } from '../../components/ui/textarea';
import type { ProposeConfigurationChangeInput } from '../../data/contracts';
import { humanizeKey, plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import type { WriteInput } from '../../data/writes';
import { AddButton, ChangeEditor, CheckResult, OperationCard, UndoNote, type EditorStart } from './builder';
import { entityName, type DraftOperation } from './logic';
import { useConfigRowsInForce } from './rows';

type ProposeInput = WriteInput<'propose_configuration_change'>;

export function ConfigurationChangeSheet(props: {
  // The sheet's title, such as "Propose a change to the data domain tree".
  title: string;
  trigger?: ReactElement;
  // The rows to change, prefilled by the page that opens it: a data domain row, a role binding. The person can change
  // them before the preview.
  operations?: ProposeConfigurationChangeInput['operations'];
  // A preset or overlays to move to (the Presets view).
  preset?: ProposeConfigurationChangeInput['preset'];
  // Words for the rationale field.
  rationaleHint?: string;
  // An open draft to change instead of a new one.
  configurationDraftId?: string;
  // After the draft is saved (not after the preview): its ID.
  onSaved?: (configurationDraftId: string) => void;
}): ReactElement | null {
  return <DraftBuilderSheet {...props} />;
}

export interface DraftBuilderProps {
  title: string;
  trigger?: ReactElement;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
  operations?: DraftOperation[];
  preset?: { presetKey: string; overlayKeys?: string[] };
  rationaleHint?: string;
  // The rationale to start from, when a stored draft is changed.
  rationale?: string;
  configurationDraftId?: string;
  // Open the editor at once: add a row of one entity, or change one row.
  start?: EditorStart;
  onSaved?: (configurationDraftId: string) => void;
}

export function DraftBuilderSheet(props: DraftBuilderProps) {
  // The body holds the form and is mounted fresh each time the sheet opens; it hands the sheet the input to send.
  const build = useRef<(() => ProposeInput | FormProblem) | null>(null);
  return (
    <ActionSheet
      tool="propose_configuration_change" site="pages/configuration/sheets#DraftBuilderSheet"
      title={props.title}
      description="You propose the change; another configuration admin accepts the exact change on the decision page. Nothing changes until then."
      trigger={props.trigger}
      open={props.open}
      onOpenChange={props.onOpenChange}
      size="wide"
      input={() => build.current?.() ?? { problem: 'The sheet is still opening. Try again.' }}
      submitLabel={props.configurationDraftId ? 'Save the draft again' : 'Save the draft'}
      onDone={data => { if (data.configurationDraftId) props.onSaved?.(data.configurationDraftId); }}
    >
      <BuilderBody {...props} build={build} />
    </ActionSheet>
  );
}

function BuilderBody(props: DraftBuilderProps & { build: { current: (() => ProposeInput | FormProblem) | null } }) {
  const config = useConfigRowsInForce();
  const [rationale, setRationale] = useState(props.rationale ?? '');
  const [ops, setOps] = useState<DraftOperation[]>(() => (props.operations ?? []).map(o => ({ ...o })));
  const [editor, setEditor] = useState<{ start: EditorStart; index: number | null; n: number } | null>(
    props.start && !props.preset ? { start: props.start, index: null, n: 0 } : null,
  );
  const [removed, setRemoved] = useState<{ op: DraftOperation; index: number } | null>(null);
  const check = useWriteAction('propose_configuration_change', { refresh: false });
  const checkRef = useRef<HTMLDivElement>(null);

  const buildInput = (): ProposeInput | FormProblem => {
    if (config.version === null) {
      return { problem: config.failure ? `The rules in force could not be read: ${config.failure.error.message}` : 'The rules in force are still being read. Try again in a moment.' };
    }
    const text = rationale.trim();
    if (!text) return { problem: 'Say why the change is wanted, in your words.', field: 'proposalRationale' };
    const draft = props.configurationDraftId ? { configurationDraftId: props.configurationDraftId } : {};
    if (props.preset) return { baseVersionNumber: config.version, proposalRationale: text, preset: props.preset, ...draft };
    if (editor) return { problem: 'Finish the change you are working on first: add it to the draft, or cancel it.' };
    if (!ops.length) return { problem: 'Add at least one change.', field: 'operations' };
    return { baseVersionNumber: config.version, proposalRationale: text, operations: ops, ...draft };
  };
  props.build.current = buildInput;

  // A check answers the draft as it was: any change to the draft clears it.
  const payload = JSON.stringify([rationale.trim(), ops, props.preset ?? null]);
  const { reset } = check;
  useEffect(() => { reset(); }, [payload, reset]);
  useEffect(() => { if (check.envelope) checkRef.current?.focus({ preventScroll: false }); }, [check.envelope]);
  const [checkProblem, setCheckProblem] = useState<string | null>(null);
  const runCheck = () => {
    const input = buildInput();
    if ('problem' in input) {
      setCheckProblem(input.problem);
      return;
    }
    setCheckProblem(null);
    void check.run(input, { preview: true });
  };

  const keep = (op: DraftOperation, index: number | null) => {
    setOps(list => {
      const next = [...list];
      const same = next.findIndex((o, i) => i !== index && o.entity === op.entity && o.key === op.key);
      if (index !== null) next[index] = op;
      else if (same >= 0) next[same] = op;
      else next.push(op);
      return index !== null && same >= 0 ? next.filter((_, i) => i !== same) : next;
    });
    setEditor(null);
    setRemoved(null);
  };
  const remove = (index: number) => {
    setRemoved({ op: ops[index], index });
    setOps(list => list.filter((_, i) => i !== index));
  };

  return (
    <>
      <div className="flex flex-col gap-1 rounded-md border border-line bg-background px-3 py-2.5 text-sm text-navy-2">
        <span className="flex items-center gap-1.5 font-semibold text-navy">
          <ShieldCheck className="size-4 text-navy-3" aria-hidden />
          {config.version !== null ? `Starts from version ${config.version}, the version in force` : config.failure ? 'The rules in force could not be read' : 'Reading the rules in force'}
        </span>
        <span>
          Save the draft, then submit it from its page. The engine checks it again when it is accepted, and a draft whose
          base version is no longer in force has to be proposed again.
        </span>
      </div>

      <Field label="Why the change is wanted" name="proposalRationale" required hint={props.rationaleHint ?? 'In your words. The people asked to accept it read this; the engine writes the summary of what changes itself.'}>
        {p => <Textarea {...p} required={false} rows={3} maxLength={2000} value={rationale} onChange={e => setRationale(e.target.value)} />}
      </Field>

      {props.preset ? <PresetMove preset={props.preset} /> : (
        <div className="flex flex-col gap-2">
          <span className="text-sm font-medium text-navy" id="draft-changes-title">
            {ops.length ? `The changes in this draft (${ops.length})` : 'The changes in this draft'}
          </span>
          {ops.length > 0 && (
            <ul className="m-0 flex list-none flex-col gap-2 p-0" aria-labelledby="draft-changes-title">
              {ops.map((op, i) => (
                <OperationCard
                  key={`${op.entity}-${op.key}-${i}`}
                  op={op}
                  config={config}
                  onEdit={editor ? undefined : () => setEditor({ start: { op }, index: i, n: Date.now() })}
                  onRemove={editor ? undefined : () => remove(i)}
                />
              ))}
            </ul>
          )}
          {removed && <UndoNote what={`The change to ${entityName(removed.op.entity).toLowerCase()} "${config.lookup(removed.op.entity, removed.op.key) ?? (removed.op.values?.label as string | undefined) ?? humanizeKey(removed.op.key)}"`} onUndo={() => { setOps(list => [...list.slice(0, removed.index), removed.op, ...list.slice(removed.index)]); setRemoved(null); }} />}
          {!ops.length && !editor && <Muted>No change yet. Add one: a new row, a change to a row, or a row to retire.</Muted>}
          {editor ? (
            <ChangeEditor key={editor.n} config={config} start={editor.start} onKeep={op => keep(op, editor.index)} onCancel={() => setEditor(null)} />
          ) : (
            <AddButton onClick={() => setEditor({ start: {}, index: null, n: Date.now() })}>{ops.length ? 'Add another change' : 'Add a change'}</AddButton>
          )}
        </div>
      )}

      <div className="flex flex-col gap-2 border-t border-line pt-3">
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" onClick={runCheck} disabled={check.sending}>
            {check.sending ? 'Checking' : 'Check the change'}
          </Button>
          <Muted>Every rule runs and nothing is saved.</Muted>
        </div>
        {checkProblem && <p className="m-0 text-sm text-warning" role="alert">{checkProblem}</p>}
        {check.envelope && (
          <div ref={checkRef} tabIndex={-1} aria-live="polite" className="outline-none">
            <CheckResult envelope={check.envelope} answer={check.answer} config={config} />
          </div>
        )}
      </div>
    </>
  );
}

// A move to another preset: what it names. The engine works out the rows it changes.
function PresetMove({ preset }: { preset: { presetKey: string; overlayKeys?: string[] } }) {
  const state = useRead('list_presets', {});
  const presets = useMemo(() => dataOf(state)?.presets ?? [], [state]);
  const label = (key: string) => presets.find(p => p.presetKey === key)?.label ?? humanizeKey(key);
  const overlays = preset.overlayKeys ?? [];
  return (
    <div className="flex flex-col gap-1 rounded-md border border-line bg-card px-3 py-2.5">
      <span className="text-sm font-semibold text-navy">{`Move to ${label(preset.presetKey)}`}</span>
      <span className="text-xs text-navy-2">
        {overlays.length ? `With ${plural(overlays.length, 'overlay', 'overlays')}: ${overlays.map(label).join(', ')}. ` : ''}
        The engine works out every row this changes from the version in force. Check the change to see them.
      </span>
    </div>
  );
}
