// The change request draft form in the shared side sheet (save_change_request_draft): a new change from the New change
// page, or an edit of a draft from its record. "Check first" runs every rule with validateOnly and shows what the engine
// would ask for once the change is submitted (its requirements preview). Only the creator may edit, and only while the
// change is a draft; the engine refuses anyone else and names the field it refuses.
import { useEffect, useMemo, useState, type ReactElement } from 'react';
import type { ChangeDraftResult, ChangeRequestDetail } from '../../data/contracts';
import { fmtWindow, zoneLabel } from '../../data/format';
import { useLabels, useListValues } from '../../data/labels';
import { dataOf, useTool } from '../../data/toolClient';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Checkbox } from '../../components/ui/checkbox';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import { buildChangeDraft, durationText, emptyChangeForm, localInputToUtc, utcToLocalInput, windowMinutes, type ChangeFormValues } from './logic';
import { choicesOf, SelectField, subhead } from './parts';

export interface ExistingDraft {
  change: ChangeRequestDetail['change'];
  affected: ChangeRequestDetail['affected'];
}

const DEFAULT_ROLE = 'depends-on-change';

function formOf(existing: ExistingDraft | undefined): ChangeFormValues {
  if (!existing) return emptyChangeForm();
  const c = existing.change;
  return {
    title: c.title,
    changeTypeKey: c.changeType.key,
    standardChangeModelKey: c.standardChangeModel?.key ?? '',
    description: c.description,
    windowStart: utcToLocalInput(c.windowStart),
    windowEnd: utcToLocalInput(c.windowEnd),
    plannedTargetId: c.plannedTargetId ?? '',
    affected: existing.affected
      .filter(a => a.businessApplicationId.toLowerCase() !== c.businessApplicationId.toLowerCase())
      .map(a => ({ businessApplicationId: a.businessApplicationId, impactRoleKey: a.impactRoleKey ?? DEFAULT_ROLE })),
    externalChangeNumber: c.externalChangeNumber ?? '',
    externalChangeUrl: c.externalChangeUrl ?? '',
  };
}

export function ChangeDraftSheet(props: {
  applicationId: string;
  applicationName: string | null;
  existing?: ExistingDraft;
  trigger: ReactElement;
  // False hides the trigger once the draft is no longer editable; an open sheet stays until the person closes it.
  show?: boolean;
  onSaved?: (data: ChangeDraftResult) => void;
}) {
  const { applicationId, existing } = props;
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<ChangeFormValues>(() => formOf(existing));
  // The draft this sheet created. A later save from the same sheet edits it, so a second click never makes a second change.
  const [createdId, setCreatedId] = useState<string | null>(null);
  const draftId = existing?.change.changeRequestId ?? createdId ?? undefined;
  // The person has changed the affected list: the dependency walk no longer fills it.
  const [affectedTouched, setAffectedTouched] = useState(!!existing);
  const set = <K extends keyof ChangeFormValues>(key: K, value: ChangeFormValues[K]) => setForm(f => ({ ...f, [key]: value }));

  const types = useLabels('change-type');
  const models = useLabels('standard-change-model');
  const stages = useLabels('target-stage');
  const impactRoles = useListValues('impact-role');
  // The sheet's reads wait until it opens.
  const targets = useTool('get_deployments', open ? { businessApplicationId: applicationId, latestOnly: true, top: 100 } : null);
  const walk = useTool('get_dependency_impact', open ? { businessApplicationId: applicationId, direction: 'dependants', depth: 2 } : null);
  const walkNodes = dataOf(walk)?.nodes ?? [];

  // An edit starts from the record as it is now, each time the sheet opens.
  const onOpenChange = (next: boolean) => {
    if (next && existing) {
      setForm(formOf(existing));
      setAffectedTouched(true);
    }
    setOpen(next);
  };

  // A new change starts with the direct dependants marked as affected: the engine does not walk the dependencies itself,
  // so the page passes the walk's applications. The person unticks what the change does not touch.
  useEffect(() => {
    if (affectedTouched || !walkNodes.length) return;
    setForm(f => ({
      ...f,
      affected: walkNodes.filter(n => n.depth === 1).map(n => ({ businessApplicationId: n.businessApplicationId, impactRoleKey: DEFAULT_ROLE })),
    }));
  }, [walkNodes, affectedTouched]);

  const targetChoices = useMemo(() => {
    const seen = new Map<string, string>();
    for (const d of dataOf(targets)?.items ?? []) {
      if (d.deploymentTargetId && !seen.has(d.deploymentTargetId)) {
        seen.set(d.deploymentTargetId, `${d.targetLabel ?? 'Unnamed target'}${d.targetStageKey ? `, ${stages.label(d.targetStageKey).toLowerCase()}` : ''}`);
      }
    }
    if (form.plannedTargetId && !seen.has(form.plannedTargetId)) seen.set(form.plannedTargetId, 'The planned target (no deployment recorded there yet)');
    return [...seen.entries()].map(([value, label]) => ({ value, label }));
  }, [targets, stages, form.plannedTargetId]);

  // Candidates for the affected list: the walk's dependants, then any application the draft already lists.
  const candidates = useMemo(() => {
    const list = walkNodes.map(n => ({ id: n.businessApplicationId, name: n.name, note: n.depth === 1 ? 'Depends on it directly' : `Depends on it through ${n.depth - 1} other ${n.depth === 2 ? 'application' : 'applications'}` }));
    for (const a of existing?.affected ?? []) {
      if (!list.some(c => c.id.toLowerCase() === a.businessApplicationId.toLowerCase()) && a.businessApplicationId.toLowerCase() !== applicationId.toLowerCase()) {
        list.push({ id: a.businessApplicationId, name: a.name, note: 'Listed on the draft' });
      }
    }
    return list;
  }, [walkNodes, existing, applicationId]);

  const typeKey = form.changeTypeKey;
  const typeHint = typeKey ? typeText(typeKey, types.field<boolean>(typeKey, 'requiresAuthorization'), types.field<boolean>(typeKey, 'allowsAuthorizationAfter'), types.field<boolean>(typeKey, 'requiresReviewAfter'), types.field<number>(typeKey, 'reviewDueDays')) : undefined;
  const needsModel = !!typeKey && types.field<boolean>(typeKey, 'requiresStandardModel') === true;
  const modelChoices = choicesOf(models);
  const minutes = windowMinutes(localInputToUtc(form.windowStart), localInputToUtc(form.windowEnd));

  const toggleAffected = (id: string, on: boolean) => {
    setAffectedTouched(true);
    setForm(f => ({
      ...f,
      affected: on
        ? [...f.affected.filter(a => a.businessApplicationId !== id), { businessApplicationId: id, impactRoleKey: DEFAULT_ROLE }]
        : f.affected.filter(a => a.businessApplicationId !== id),
    }));
  };
  const setRole = (id: string, role: string) => {
    setAffectedTouched(true);
    setForm(f => ({ ...f, affected: f.affected.map(a => (a.businessApplicationId === id ? { ...a, impactRoleKey: role } : a)) }));
  };

  if (props.show === false && !open) return null;
  return (
    <ActionSheet
      tool="save_change_request_draft" site="pages/changes-records/ChangeDraftSheet#ChangeDraftSheet"
      title={draftId ? 'Edit the draft' : `Plan a change${props.applicationName ? ` to ${props.applicationName}` : ''}`}
      description={draftId
        ? 'Change what the draft says. It stays a draft until you submit it.'
        : 'Saved as a draft. Check first shows who must authorize it at this application\'s tier; nothing is asked until you submit.'}
      trigger={props.show === false ? undefined : props.trigger}
      open={open}
      onOpenChange={onOpenChange}
      input={() => buildChangeDraft(form, applicationId, draftId)}
      submitLabel="Save draft"
      preview
      size="wide"
      onDone={data => {
        if (!existing && data.recordId) setCreatedId(data.recordId);
        props.onSaved?.(data);
      }}
    >
      <Field label="Title" name="title" required>
        {p => <Input {...p} value={form.title} onChange={e => set('title', e.target.value)} maxLength={200} placeholder="Release 3.5: shift export fix" />}
      </Field>
      <SelectField
        label="Change type"
        name="changeTypeKey"
        value={form.changeTypeKey}
        onChange={v => set('changeTypeKey', v)}
        choices={choicesOf(types)}
        required
        placeholder="Choose the type"
        hint={typeHint ?? 'You set the type. An AI client never decides that a change is standard.'}
      />
      {needsModel && (modelChoices.length ? (
        <SelectField
          label="Standard change model"
          name="standardChangeModelKey"
          value={form.standardChangeModelKey}
          onChange={v => set('standardChangeModelKey', v)}
          choices={modelChoices}
          noneLabel="Let the standard check find one"
          hint="The model you think fits. The standard check on the record says whether it does."
        />
      ) : (
        <p className="m-0 rounded-md border border-warning-line bg-warning-soft px-3 py-2 text-sm text-warning">
          No standard change model is configured, so a standard change cannot fit one. Choose another type, or ask a configuration admin to propose a model.
        </p>
      ))}
      <Field label="What changes, and why" name="description" required>
        {p => <Textarea {...p} rows={5} maxLength={4000} value={form.description} onChange={e => set('description', e.target.value)} />}
      </Field>

      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <Field label="Window starts" name="windowStart" hint={`In your time zone, ${zoneLabel()}.`}>
          {p => <Input {...p} type="datetime-local" value={form.windowStart} onChange={e => set('windowStart', e.target.value)} />}
        </Field>
        <Field label="Window ends" name="windowEnd" hint={minutes !== null ? `${durationText(minutes)}.` : 'Leave both empty if the window is not known yet.'}>
          {p => <Input {...p} type="datetime-local" value={form.windowEnd} onChange={e => set('windowEnd', e.target.value)} />}
        </Field>
      </div>
      {minutes !== null && minutes > 0 && (
        <Muted>{`Window: ${fmtWindow(localInputToUtc(form.windowStart), localInputToUtc(form.windowEnd))}.`}</Muted>
      )}

      <SelectField
        label="Planned target"
        name="plannedTargetId"
        value={form.plannedTargetId}
        onChange={v => set('plannedTargetId', v)}
        choices={targetChoices}
        noneLabel="No target yet"
        hint={targets.status === 'loading' && open ? 'Reading the targets this application deploys to.' : 'The targets this application has deployed to. The target\'s stage sets the gate level.'}
      />

      <Field
        label="Other applications affected"
        name="affected"
        hint={walk.status === 'loading' && open ? 'Walking the dependencies.' : 'From the dependency walk: the applications that depend on this one. Untick what the change does not touch.'}
      >
        {p => (
          <div id={p.id} role="group" aria-label="Other applications affected" aria-describedby={p['aria-describedby']} className="flex flex-col gap-2">
            {walk.status === 'done' && !walk.envelope.ok && <Muted>{`The dependency walk could not be read: ${walk.envelope.error.message}`}</Muted>}
            {!candidates.length && walk.status === 'done' && walk.envelope.ok && <Muted>No application depends on this one.</Muted>}
            {candidates.map(c => {
              const chosen = form.affected.find(a => a.businessApplicationId === c.id);
              const boxId = `affected-${c.id}`;
              return (
                <div key={c.id} className="grid grid-cols-[auto_minmax(0,1fr)_200px] items-center gap-x-2.5 gap-y-1.5 rounded-md border border-line px-3 py-2 narrow:grid-cols-[auto_minmax(0,1fr)]">
                  <Checkbox id={boxId} checked={!!chosen} onCheckedChange={v => toggleAffected(c.id, v === true)} />
                  <label htmlFor={boxId} className="flex min-w-0 cursor-pointer flex-col">
                    <span className="text-sm font-semibold [overflow-wrap:anywhere] text-navy">{c.name}</span>
                    <span className="text-xs text-navy-3">{c.note}</span>
                  </label>
                  {chosen ? (
                    <Select value={chosen.impactRoleKey} onValueChange={v => setRole(c.id, v)}>
                      <SelectTrigger className="w-full narrow:col-span-2" aria-label={`How the change touches ${c.name}`}>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {(impactRoles.choices.length ? impactRoles.choices : [{ value: DEFAULT_ROLE, label: 'Depends on the change' }]).map(r => (
                          <SelectItem key={r.value} value={r.value}>{r.label}</SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  ) : <span className="text-xs text-navy-3 narrow:hidden">Not affected</span>}
                </div>
              );
            })}
          </div>
        )}
      </Field>

      <div className="flex flex-col gap-3 border-t border-line pt-3">
        <h3 className={subhead}>Linked change management</h3>
        <p className="m-0 text-xs leading-relaxed text-navy-3">
          Only where this application's portfolio uses linked change management: your ITSM tool authorizes the change, and the
          engine asks for its number and link. A recorded number alone never counts as authorization.
        </p>
        <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
          <Field label="ITSM change number" name="externalChangeNumber">
            {p => <Input {...p} value={form.externalChangeNumber} onChange={e => set('externalChangeNumber', e.target.value)} maxLength={100} />}
          </Field>
          <Field label="ITSM change link" name="externalChangeUrl">
            {p => <Input {...p} type="url" inputMode="url" value={form.externalChangeUrl} onChange={e => set('externalChangeUrl', e.target.value)} placeholder="https://" maxLength={2000} />}
          </Field>
        </div>
      </div>
    </ActionSheet>
  );
}

// What the chosen change type means, from its configured fields.
function typeText(key: string, requiresAuthorization: boolean | undefined, after: boolean | undefined, reviewAfter: boolean | undefined, reviewDays: number | undefined): string {
  if (requiresAuthorization === false) return 'Needs no authorization when an approved standard change model fits it.';
  const parts = ['Needs authorization before it is deployed.'];
  if (after) parts[0] = 'Needs authorization, which may come after the change in an emergency.';
  if (reviewAfter) parts.push(`A post-change review follows${reviewDays ? ` within ${reviewDays} days` : ''}.`);
  return requiresAuthorization === undefined && !after && !reviewAfter ? `The ${key.replace(/-/g, ' ')} type.` : parts.join(' ');
}
