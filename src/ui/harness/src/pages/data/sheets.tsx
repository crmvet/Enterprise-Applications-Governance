// The data write sheets (docs/architecture/data-taxonomy.md, In the web app and Screens), one per action, each the shared ActionSheet
// (components/actions.tsx) on one write tool. The Data page's tabs and the data record pages, Application 360's Data
// panel and My work all open them, so an action reads the same wherever it starts.
//
// The data record pages import them and never copy them. The props below are the contract they code against.
//
//   ProposeDataOwnerSheet     propose_owner_change with dataDomainKey or dataLocationId: name or end a data owner, its
//                             deputy or a data steward on a domain, or a technical owner on a location
//   DataUseSheet              save_data_use: declare a use (two ways in: pick a domain, or name a location and a table
//                             and let resolve_data find the domain), change it, confirm an AI draft, or end it.
//                             check_data_use is the preview: the state, the governing rule and who would be asked. A
//                             use whose rule is never is refused when it is saved, and the refusal names the rule
//   DataLocationSheet         save_data_location: add, change, confirm seen, retire
//   DataMappingSheet          save_data_mapping: map a domain to a location (containers, origin), change or end it
//   DataDecisionSheet         save_decision_proposal on a data-use or data-mapping subject (data-use, copy or
//                             data-retention decision), then submit_record. Approve and reject stay on the decision page
//   MoveDataReferencesSheet   move_data_references: platform admin and data governance lead, previewed first
//
// How they behave (choices under D-41, also in index.tsx):
// - Each sheet shows only when whoami's tools hold its write tool; the engine still decides every write.
// - A write that opens a decision and does not send it (an owner change, a decision proposal, a copy decision) closes
//   its sheet and opens "Send it for approval now?" with the engine's answer on top (SubmitFollowUp). A person's
//   confirmed use or copy sends its decision itself, so no follow-up shows.
// - DataUseSheet with an empty applicationId asks for the application first (the Uses tab and a domain record have
//   none to give).
// - A sheet opened from a tab tells the tab (SheetHoldContext), which keeps its rows as they were until the sheet closes.
import { Search } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted, ToneBadge } from '../../components/blocks';
import { DataUseStateBadge } from '../../components/dataChips';
import { PersonSearchField, useSettled } from '../../components/people';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Input } from '../../components/ui/input';
import { Textarea } from '../../components/ui/textarea';
import type { DataLocationItem, DataMappingItem, DataOrigin, DataUseItem, PrincipalRef } from '../../data/contracts';
import { DATA_ACCESS_TEXT, DATA_ORIGIN_TEXT, DATA_RULE_TEXT, governingRuleText, ruleSourceText } from '../../data/dataTaxonomy';
import { fmtDate, todayInZone } from '../../data/format';
import { usePagedRead, useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { readEngineAnswer } from '../../data/writes';
import { SelectField, type Choice } from '../changes-records/parts';
import { AppField, targetChoices, type AppChoice } from '../deployments-places/parts';
import {
  buildDecisionInput,
  buildLocationInput,
  buildMappingInput,
  buildMoveInput,
  buildOwnerInput,
  buildUseInput,
  COPY_ORIGINS,
  isProblem,
  locationFormOf,
  mappingFormOf,
  sameId,
  unsentDecision,
  useFormOf,
  type DecisionForm,
  type LocationForm,
  type MappingForm,
  type MoveForm,
  type OwnerForm,
  type UseForm,
} from './logic';
import { ApproverList, CheckField, RadioField, SubmitFollowUp, useConfigLabels, useDomainChoices, useFollowUp, useListChoices, useLocationChoices, useOutcomeChoices, useSheetOpen, type DomainChoice } from './sheetParts';

// A domain or a location as an owner subject.
export type DataOwnerSubjectRef =
  | { kind: 'data-domain'; domainKey: string; label: string }
  | { kind: 'data-location'; dataLocationId: string; label: string };

const ORIGINS: DataOrigin[] = ['system-of-record', 'replica', 'production-copy', 'masked-copy', 'synthetic'];

const ORIGIN_HELP: Record<DataOrigin, string> = {
  'system-of-record': 'The data is made and kept here.',
  replica: 'A synced copy for reading, such as a reporting copy.',
  'production-copy': 'Real data copied here, such as into a test environment.',
  'masked-copy': 'Production data with names and numbers masked.',
  synthetic: 'Made-up data that looks real.',
};


const holdsOn = (subjectKinds: unknown, kind: string) => Array.isArray(subjectKinds) && subjectKinds.includes(kind);

// The domains a use or a mapping may point at: active, at a declarable level.
const declarable = (c: DomainChoice) => c.isActive && c.declarable;

function domainChoiceList(choices: DomainChoice[], filter: (c: DomainChoice) => boolean = declarable): Choice[] {
  return choices.filter(filter).map(c => ({ value: c.key, label: c.path }));
}

// ---------------------------------------------------------------------------------------------------------------------
// ProposeDataOwnerSheet
// ---------------------------------------------------------------------------------------------------------------------

export function ProposeDataOwnerSheet(props: {
  subject: DataOwnerSubjectRef;
  // The owner role to name: data-owner or data-steward on a domain (a deputy is a data-owner assignment with isDeputy),
  // technical-owner on a location. Left out, the person picks one.
  ownerRoleKey?: string;
  // The assignment to end, for a successor or an end.
  endAssignmentId?: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const ownerRoles = useConfigLabels('owner-role', open);
  const follow = useFollowUp();
  const initial = (): OwnerForm => ({ ownerRoleKey: props.ownerRoleKey ?? '', isDeputy: false, person: null, endAssignmentId: props.endAssignmentId ?? '', startOn: '', reason: '' });
  const [form, setForm] = useState<OwnerForm>(initial);
  const set = (patch: Partial<OwnerForm>) => setForm(f => ({ ...f, ...patch }));
  const subject = props.subject;
  const domainOwners = useRead('get_data_domain', open && subject.kind === 'data-domain' ? { domainKey: subject.domainKey, sections: ['owners'] } : null);
  const location = useRead('find_data_locations', open && subject.kind === 'data-location' ? { locationId: subject.dataLocationId } : null);
  if (!session.can('propose_owner_change')) return null;

  const roleKeys = ownerRoles.keys().filter(k => holdsOn(ownerRoles.field(k, 'subjectKinds'), subject.kind));
  const roleFixed = !!props.ownerRoleKey;
  const requiresDeputy = !!form.ownerRoleKey && ownerRoles.field<boolean>(form.ownerRoleKey, 'requiresDeputy') === true;

  // Who holds the chosen role now, and the assignment a successor would end.
  const owners = dataOf(domainOwners)?.owners ?? [];
  const chain = owners.find(o => o.ownerRole.key === form.ownerRoleKey) ?? null;
  const technical = dataOf(location)?.items[0]?.technicalOwner ?? null;
  const now: { person: PrincipalRef; assignmentId: string; inherited: string | null } | null = subject.kind === 'data-domain'
    ? (chain?.holder ? { person: chain.holder.principal, assignmentId: chain.holder.ownerAssignmentId, inherited: chain.inheritedFrom?.label ?? null } : null)
    : (technical && (!form.ownerRoleKey || form.ownerRoleKey === 'technical-owner') ? { person: technical.principal, assignmentId: technical.ownerAssignmentId, inherited: technical.fromParent ? 'the instance it sits in' : null } : null);
  const ending = form.endAssignmentId
    ? [chain?.holder, ...(chain?.otherHolders ?? []), ...(chain?.deputies ?? [])].find(h => h && sameId(h.ownerAssignmentId, form.endAssignmentId))?.principal
      ?? (now && sameId(now.assignmentId, form.endAssignmentId) ? now.person : null)
    : null;
  const canReplace = !!now && !now.inherited && !form.isDeputy && !props.endAssignmentId;
  // Only the read of this subject's kind runs; the other one never leaves its loading state.
  const reading = open && (subject.kind === 'data-domain' ? domainOwners.status === 'loading' : location.status === 'loading');

  const close = () => { setOpen(false); setForm(initial()); };
  const kindWord = subject.kind === 'data-domain' ? 'domain' : 'location';
  return (
    <>
      <ActionSheet
        tool="propose_owner_change" site="pages/data/sheets#ProposeDataOwnerSheet"
        title={subject.kind === 'data-domain' ? `Propose an owner: ${subject.label}` : `Propose a technical owner: ${subject.label}`}
        description={`This records an ownership decision about the ${kindWord}. Nothing changes until the people the rules ask accept it. Check first to see who.`}
        trigger={props.trigger}
        open={open}
        onOpenChange={next => { if (next) setOpen(true); else close(); }}
        input={() => {
          const built = buildOwnerInput(form, subject.kind === 'data-domain' ? { kind: 'data-domain', domainKey: subject.domainKey } : { kind: 'data-location', dataLocationId: subject.dataLocationId });
          return isProblem(built) ? built : built;
        }}
        submitLabel="Propose"
        preview
        onDone={(data, envelope) => {
          if (data.decisionId) {
            follow.start({ decisionId: data.decisionId, answer: readEngineAnswer('propose_owner_change', envelope) });
            close();
          }
        }}
      >
        {roleFixed ? (
          <div className="flex flex-col gap-1">
            <span className="text-sm font-medium text-navy">Owner role</span>
            <span className="text-sm text-navy-2">{ownerRoles.label(form.ownerRoleKey)}</span>
          </div>
        ) : (
          <SelectField
            label="Owner role"
            name="changes.ownerRoleKey"
            value={form.ownerRoleKey}
            onChange={v => set({ ownerRoleKey: v, isDeputy: false, endAssignmentId: props.endAssignmentId ?? '' })}
            choices={roleKeys.map(k => ({ value: k, label: ownerRoles.label(k) }))}
            placeholder={roleKeys.length ? 'Choose the owner role' : 'Reading the owner roles'}
            required
          />
        )}
        {form.ownerRoleKey && (
          <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2 text-sm text-navy">
            <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Holds it now</span>
            {reading ? <Muted>Reading who holds it.</Muted>
              : now ? <span>{now.person.displayLabel || 'A person the product knows'}{now.inherited ? <span className="text-navy-3">{`, from ${now.inherited}`}</span> : null}</span>
                : <span>Nobody{subject.kind === 'data-domain' ? ', here or above' : ''}.</span>}
            {ending && <span className="text-navy-2">{`Ends: ${ending.displayLabel || 'the current assignment'}, once the decision is accepted.`}</span>}
          </div>
        )}
        {requiresDeputy && !props.endAssignmentId && (
          <RadioField<'holder' | 'deputy'>
            legend="Name"
            name="owner-change-kind"
            value={form.isDeputy ? 'deputy' : 'holder'}
            onChange={v => set({ isDeputy: v === 'deputy', endAssignmentId: '' })}
            options={[
              { value: 'holder', label: 'A holder', help: 'The person who answers for the role.' },
              { value: 'deputy', label: 'A deputy', help: 'Acts when the holder is away. The role needs one.' },
            ]}
          />
        )}
        <PersonSearchField
          label={props.endAssignmentId ? 'Successor (optional)' : form.isDeputy ? 'New deputy' : 'New holder'}
          name="changes.addObjectId"
          value={form.person}
          onChange={person => set({ person })}
          required={!props.endAssignmentId}
          activeOnly
          exclude={now ? [now.person.objectId] : undefined}
          hint={props.endAssignmentId ? 'Leave it empty to end the assignment and name no one.' : undefined}
        />
        {canReplace && (
          <CheckField
            id="owner-change-replace"
            label={`Replace ${now!.person.displayLabel || 'the current holder'}`}
            checked={!!form.endAssignmentId}
            onChange={v => set({ endAssignmentId: v ? now!.assignmentId : '' })}
            hint="Their assignment ends when the decision is accepted. Leave it unchecked to add a second holder."
          />
        )}
        {form.person && (
          <Field label="Starts on" name="changes.startOn" hint="Once the decision is accepted. Leave it empty to start on acceptance.">
            {p => <Input {...p} required={false} type="date" value={form.startOn} onChange={e => set({ startOn: e.target.value })} className="w-[200px]" />}
          </Field>
        )}
        <Field label="Reason" name="reason" required hint="The approvers read this. Stored as you type it.">
          {p => <Textarea {...p} rows={4} maxLength={2000} value={form.reason} onChange={e => set({ reason: e.target.value })} />}
        </Field>
      </ActionSheet>
      {follow.data && (
        <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`Owner change for ${subject.label}`} open={follow.open} onOpenChange={follow.onOpenChange} />
      )}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// DataUseSheet
// ---------------------------------------------------------------------------------------------------------------------

const USE_TITLE: Record<'declare' | 'edit' | 'confirm' | 'end', string> = {
  declare: 'Declare a data use',
  edit: 'Change the data use',
  confirm: 'Confirm the data use',
  end: 'End the data use',
};

export function DataUseSheet(props: {
  applicationId: string;
  applicationName: string;
  // declare: a new use; edit: change a use; confirm: a person confirms an AI draft; end: end it today.
  mode: 'declare' | 'edit' | 'confirm' | 'end';
  // The use to change, confirm or end.
  use?: DataUseItem;
  // A domain to start from (the domain record's "Declare a use").
  domainKey?: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const follow = useFollowUp();
  // Ending a use starts from the person's own calendar day, as every other date-only default does (wave 4b review).
  const initial = (): UseForm => ({ ...useFormOf(props.use, props.applicationId, props.domainKey ?? ''), endOn: props.mode === 'end' ? todayInZone() : '' });
  const [form, setForm] = useState<UseForm>(initial);
  const [app, setApp] = useState<AppChoice | null>(props.applicationId ? { id: props.applicationId, name: props.applicationName } : null);
  const [way, setWay] = useState<'data' | 'system'>('data');
  const [container, setContainer] = useState('');
  const set = (patch: Partial<UseForm>) => setForm(f => ({ ...f, ...patch }));
  const editing = props.mode !== 'end';
  const domains = useDomainChoices(open && editing);
  const locations = useLocationChoices(open && editing);
  const approaches = useListChoices('access-approach', open && editing);
  const settledContainer = useSettled(container.trim(), 400);
  const resolved = useRead('resolve_data', open && editing && way === 'system' && form.locationId
    ? { locationId: form.locationId, ...(settledContainer ? { container: settledContainer.slice(0, 200) } : {}) }
    : null);
  const applicationId = app?.id ?? '';
  const check = useRead('check_data_use', open && editing && applicationId && form.domainKey && form.access
    ? { applicationId, domainKey: form.domainKey, ...(form.locationId ? { locationId: form.locationId } : {}), access: form.access, reachesAi: form.reachesAi, isDeidentified: form.isDeidentified }
    : null);
  if (!session.can('save_data_use')) return null;

  const close = () => {
    setOpen(false);
    setForm(initial());
    setApp(props.applicationId ? { id: props.applicationId, name: props.applicationName } : null);
    setWay('data');
    setContainer('');
  };
  const use = props.use;
  const domainChoices = domainChoiceList(domains.choices, c => declarable(c) || c.key === form.domainKey);
  const holding = (l: DataLocationItem) => !!form.domainKey && l.domains.some(d => d.domain.key === form.domainKey);
  const locationChoices: Choice[] = [...locations.items]
    .sort((a, b) => Number(holding(b)) - Number(holding(a)) || a.label.localeCompare(b.label))
    .map(l => ({ value: l.dataLocationId, label: `${l.label}${l.stage ? ` (${l.stage.label})` : ''}${holding(l) ? ', holds this data' : ''}` }));
  const matches = dataOf(resolved)?.matches ?? [];
  const appName = app?.name ?? props.applicationName;

  return (
    <>
      <ActionSheet
        tool="save_data_use" site="pages/data/sheets#DataUseSheet"
        title={USE_TITLE[props.mode]}
        description={props.mode === 'confirm'
          ? 'An AI client drafted this use. Check it, change what is wrong, and confirm it. A confirmed use that needs a decision sends it to the data owner by name.'
          : props.mode === 'end'
            ? 'Ending a use is never refused. The use and its decision stay on record.'
            : props.mode === 'edit'
              ? 'A change that widens the use (it starts to write, reaches AI or points at other data) ends it and starts a new one, so the earlier decision stays with the earlier use.'
              : 'Which business data the application reads or writes, and why. A use the rules do not allow is refused when it is saved, and the refusal names the rule.'}
        trigger={props.trigger}
        open={open}
        onOpenChange={next => {
          // Each opening starts from the use as it is now: a change saved a moment before on this page stays (wave 4b QA).
          if (next) { setForm(initial()); setOpen(true); } else close();
        }}
        input={() => {
          const built = buildUseInput({ ...form, applicationId }, props.mode, use);
          return isProblem(built) ? built : built;
        }}
        submitLabel={props.mode === 'declare' ? 'Declare the use' : props.mode === 'confirm' ? 'Confirm the use' : props.mode === 'end' ? 'End the use' : 'Save the change'}
        submitTone={props.mode === 'end' ? 'destructive' : 'default'}
        preview={props.mode !== 'end'}
        confirm={props.mode === 'end' ? { title: 'End this use?', body: `${appName} stops using ${use?.domain.path ?? 'this data'} on the day you gave. Its decision stays on record.`, confirmLabel: 'End the use', tone: 'destructive' } : undefined}
        size={editing ? 'wide' : 'default'}
        onDone={(data, envelope) => {
          const decisionId = unsentDecision(data);
          // A person's confirmed use sends its own decision; only an unsent one gets the follow-up.
          if (decisionId && !data.decisionSubmitted) {
            follow.start({ decisionId, answer: readEngineAnswer('save_data_use', envelope) });
            close();
          }
        }}
      >
        {!editing && use && (
          <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2 text-sm text-navy">
            <span className="font-semibold">{`${use.recordNumber ? `${use.recordNumber}: ` : ''}${use.applicationName}`}</span>
            <span className="text-navy-2">{`${DATA_ACCESS_TEXT[use.access]} ${use.domain.path}${use.location ? ` at ${use.location.label}` : ''}${use.reachesAi ? ', with AI' : ''}.`}</span>
            <span className="text-xs text-navy-3">{use.purpose}</span>
          </div>
        )}
        {!editing && (
          <Field label="Ends on" name="endOn" required hint={`Your own calendar day. Today is ${fmtDate(todayInZone())}.`}>
            {p => <Input {...p} type="date" value={form.endOn} onChange={e => set({ endOn: e.target.value })} className="w-[200px]" />}
          </Field>
        )}
        {editing && (
          <>
            {props.applicationId || use ? (
              <div className="flex flex-col gap-1">
                <span className="text-sm font-medium text-navy">Application</span>
                <span className="text-sm text-navy-2">{appName}</span>
              </div>
            ) : (
              <AppField label="Application" name="applicationId" value={app} onChange={setApp} required hint="The application that reads or writes the data." />
            )}
            {props.mode === 'declare' && (
              <RadioField<'data' | 'system'>
                legend="Start from"
                name="use-way-in"
                value={way}
                onChange={v => { setWay(v); set({ domainKey: props.domainKey ?? '', locationId: '' }); setContainer(''); }}
                options={[
                  { value: 'data', label: 'The data', help: 'Pick the business data from the domain tree, then where the application reaches it.' },
                  { value: 'system', label: 'The system', help: 'Name the store and the table or file. The product finds which data it holds.' },
                ]}
              />
            )}
            {way === 'system' && props.mode === 'declare' ? (
              <>
                <SelectField
                  label="Where the application reaches the data"
                  name="locationId"
                  value={form.locationId}
                  onChange={v => set({ locationId: v, domainKey: '' })}
                  choices={locationChoices}
                  placeholder={locations.loading ? 'Reading the locations' : locationChoices.length ? 'Choose a location' : 'No location is registered yet'}
                  required
                />
                <Field label="Table, object or file" name="container" hint="Optional. As the tool names it, such as PAYMAST or dbo.Invoices. It narrows the data the store holds.">
                  {p => (
                    <div className="relative">
                      <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
                      <Input {...p} required={false} value={container} onChange={e => setContainer(e.target.value)} maxLength={200} className="pl-8" autoComplete="off" spellCheck={false} />
                    </div>
                  )}
                </Field>
                {form.locationId && (
                  <div aria-live="polite" className="flex flex-col gap-1.5">
                    {resolved.status === 'loading' ? <Muted>Finding the data this location holds.</Muted>
                      : resolved.status === 'done' && !resolved.envelope.ok ? <Muted>{`The data could not be found: ${resolved.envelope.error.message}`}</Muted>
                        : matches.length === 0 ? <Muted>Nothing is mapped at this location yet. Map its data first, or start from the data.</Muted>
                          : (
                            <RadioField<string>
                              legend="The data it holds"
                              name="use-resolved-domain"
                              value={form.domainKey}
                              onChange={v => set({ domainKey: v })}
                              required
                              options={matches.map(m => ({
                                value: m.domain.key,
                                label: m.domain.path,
                                help: [
                                  DATA_ORIGIN_TEXT[m.origin],
                                  m.containerMatched ? 'named for this table' : m.containers.length ? `in ${m.containers.slice(0, 3).join(', ')}` : 'the whole location',
                                  m.classification ? m.classification.label : null,
                                  m.owner ? `data owner ${m.owner.principal.displayLabel}` : 'no data owner',
                                ].filter(Boolean).join('. '),
                              }))}
                            />
                          )}
                  </div>
                )}
              </>
            ) : (
              <>
                <SelectField
                  label="The data"
                  name="domainKey"
                  value={form.domainKey}
                  onChange={v => set({ domainKey: v })}
                  choices={domainChoices}
                  placeholder={domains.loading ? 'Reading the domains' : domainChoices.length ? 'Choose a domain' : 'No data domain yet'}
                  hint="A domain at a level that takes data uses."
                  required
                />
                <SelectField
                  label="Where the application reaches it"
                  name="locationId"
                  value={form.locationId}
                  onChange={v => set({ locationId: v })}
                  choices={locationChoices}
                  noneLabel="Not known"
                  placeholder={locations.loading ? 'Reading the locations' : 'Not known'}
                  hint="Optional. The locations that hold this data come first."
                />
              </>
            )}
            <RadioField<'read' | 'write' | 'read-write'>
              legend="Access"
              name="use-access"
              value={form.access}
              onChange={v => set({ access: v })}
              required
              options={[
                { value: 'read', label: DATA_ACCESS_TEXT.read },
                { value: 'write', label: DATA_ACCESS_TEXT.write },
                { value: 'read-write', label: DATA_ACCESS_TEXT['read-write'] },
              ]}
            />
            <Field label="Purpose" name="purpose" required hint="Why the application needs the data, in one plain sentence.">
              {p => <Textarea {...p} rows={2} maxLength={500} value={form.purpose} onChange={e => set({ purpose: e.target.value })} />}
            </Field>
            <CheckField id="use-ai" label="The data reaches an AI model or agent" checked={form.reachesAi} onChange={v => set({ reachesAi: v })} hint="For grounding, prompts or training." />
            {form.reachesAi && (
              <Field label="AI service as named" name="aiServiceAsSeen" hint="Optional, such as Azure OpenAI or Copilot Studio.">
                {p => <Input {...p} required={false} value={form.aiServiceAsSeen} onChange={e => set({ aiServiceAsSeen: e.target.value })} maxLength={200} />}
              </Field>
            )}
            <CheckField id="use-masked" label="Only masked or de-identified data is used" checked={form.isDeidentified} onChange={v => set({ isDeidentified: v })} />
            <SelectField
              label="How the application signs in to the data"
              name="accessApproachKey"
              value={form.accessApproachKey}
              onChange={v => set({ accessApproachKey: v })}
              choices={approaches}
              noneLabel="Not stated"
              placeholder="Not stated"
            />
            {props.mode === 'declare' && (
              <Field label="Starts on" name="startOn" hint="Optional. Today when left empty.">
                {p => <Input {...p} required={false} type="date" value={form.startOn} onChange={e => set({ startOn: e.target.value })} className="w-[200px]" />}
              </Field>
            )}
            <UseVerdict state={check} declare={props.mode === 'declare'} />
          </>
        )}
      </ActionSheet>
      {follow.data && (
        <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The data-use decision for ${appName}`} open={follow.open} onOpenChange={follow.onOpenChange} />
      )}
    </>
  );
}

// What the rules say about the use as the form stands (check_data_use): the state it would have, the rule, who would be
// asked, and whether it would be refused when saved.
function UseVerdict({ state, declare }: { state: ReturnType<typeof useRead<'check_data_use'>>; declare: boolean }) {
  const verdict = dataOf(state);
  return (
    <section aria-label="What the rules say" aria-live="polite" className="flex flex-col gap-2 rounded-md border border-line bg-card px-3 py-2.5">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">What the rules say</span>
      {state.status === 'loading' && !verdict ? <Muted>Choose the application, the data and the access to see what the rules say.</Muted>
        : state.status === 'done' && !state.envelope.ok ? <Muted>{`The rules could not be read: ${state.envelope.error.message}`}</Muted>
          : verdict ? (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <DataUseStateBadge state={verdict.state} approvalsGiven={0} approvalsNeeded={verdict.approvalsNeeded} />
                {verdict.classification && <ToneBadge tone="brand" icon={null}>{verdict.classification.label}</ToneBadge>}
              </div>
              <p className="m-0 text-sm text-navy">{governingRuleText(verdict.governingRule)}</p>
              {verdict.refusedWhenSaved ? (
                <Alert variant="error" role="alert">
                  <AlertTitle>This use cannot be saved</AlertTitle>
                  <AlertDescription>
                    <p className="m-0">{`The ${verdict.governingRule.name === 'ai' ? 'AI use' : verdict.governingRule.name} rule for ${verdict.domain.path} is Never, ${ruleSourceText(verdict.governingRule)}. No decision can allow it, for a person or an AI client. Change the use, for example without AI or with masked data, or leave it out.`}</p>
                  </AlertDescription>
                </Alert>
              ) : verdict.decisionNeeded ? (
                <div className="flex flex-col gap-1">
                  <span className="text-sm text-navy-2">A decision is needed. Once the use is confirmed it asks:</span>
                  <ApproverList approvers={verdict.approvers} empty="The rules name no approver." />
                </div>
              ) : <p className="m-0 text-sm text-navy-2">No decision is needed: the rule allows this use.</p>}
              {declare && verdict.existingDataUseId && <Muted>This application already has an open use of this data here. Saving changes that use.</Muted>}
              {verdict.impliedAnswer && <Muted>{`The data implies the sensitivity answer "${verdict.impliedAnswer.label}".`}</Muted>}
            </>
          ) : null}
    </section>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// DataLocationSheet
// ---------------------------------------------------------------------------------------------------------------------

const LOCATION_TITLE: Record<'add' | 'edit' | 'confirm' | 'confirm-seen' | 'retire', string> = {
  add: 'Add a location',
  edit: 'Change the location',
  confirm: 'Confirm the location',
  'confirm-seen': 'Confirm it is still there',
  retire: 'Retire the location',
};

export function DataLocationSheet(props: {
  // confirm: a person checks a draft (an AI client's), changes what is wrong, and confirms it.
  mode: 'add' | 'edit' | 'confirm' | 'confirm-seen' | 'retire';
  location?: DataLocationItem;
  // A parent to add under.
  parentLocationId?: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const editingNow = open && (props.mode === 'add' || props.mode === 'edit' || props.mode === 'confirm');
  const platforms = useConfigLabels('data-platform', editingNow);
  const stages = useConfigLabels('target-stage', editingNow);
  const initial = () => locationFormOf(props.location, props.parentLocationId ?? '');
  const [form, setForm] = useState<LocationForm>(initial);
  const [app, setApp] = useState<AppChoice | null>(props.location?.hostedByApplication ? { id: props.location.hostedByApplication.businessApplicationId, name: props.location.hostedByApplication.name } : null);
  const set = (patch: Partial<LocationForm>) => setForm(f => ({ ...f, ...patch }));
  const editing = props.mode === 'add' || props.mode === 'edit' || props.mode === 'confirm';
  const locations = useLocationChoices(open && editing);
  const linkKinds = useConfigLabels('link-kind', editingNow);
  const targets = usePagedRead('list_deployment_targets', open && editing ? { top: 100 } : null, d => d.items, { maxPages: 5 });
  if (!session.can('save_data_location')) return null;

  const l = props.location;
  const close = () => {
    setOpen(false);
    setForm(initial());
    setApp(props.location?.hostedByApplication ? { id: props.location.hostedByApplication.businessApplicationId, name: props.location.hostedByApplication.name } : null);
  };
  const platformKeys = platforms.keys();
  const parents: Choice[] = locations.items
    .filter(i => !i.parent && !sameId(i.dataLocationId, l?.dataLocationId) && (!form.platformKey || i.platform.key === form.platformKey))
    .sort((a, b) => a.label.localeCompare(b.label))
    .map(i => ({ value: i.dataLocationId, label: `${i.label}${i.stage ? ` (${i.stage.label})` : ''}` }));
  const kinds: Choice[] = linkKinds.keys().filter(k => linkKinds.field(k, 'linkCategory') === 'data-location').map(k => ({ value: k, label: linkKinds.label(k) }));
  const targetList = targets.status === 'done' ? targetChoices(targets.items) : [];
  const instanceWord = form.platformKey ? platforms.field<string>(form.platformKey, 'instanceLabel') ?? null : null;
  const storeWord = form.platformKey ? platforms.field<string>(form.platformKey, 'storeLabel') ?? null : null;
  const hidden = (props.mode === 'edit' || props.mode === 'confirm') && l?.externalId === null && l?.url === null;

  return (
    <ActionSheet
      tool="save_data_location" site="pages/data/sheets#DataLocationSheet"
      title={l && props.mode !== 'add' ? `${LOCATION_TITLE[props.mode]}: ${l.label}` : LOCATION_TITLE[props.mode]}
      description={props.mode === 'confirm-seen'
        ? `This records that ${l?.label ?? 'the location'} was seen today, so it is no longer flagged as not seen recently.${l?.flags.draft ? ' Your save also confirms it: an AI client drafted it.' : ''}`
        : props.mode === 'confirm'
          ? 'An AI client drafted this location. Check it, change what is wrong, and confirm it. A confirmed location is no longer a draft.'
          : props.mode === 'retire'
            ? 'A retired location keeps its history. Nothing new can point at it.'
          : 'A store where business data sits, on any platform: an environment, a server, a database, an org or a host. It is held as a link: never a connection string, a key or a password.'}
      trigger={props.trigger}
      open={open}
      onOpenChange={next => { if (next) { setForm(initial()); setOpen(true); } else close(); }}
      input={() => {
        const built = buildLocationInput({ ...form, hostedByApplicationId: app?.id ?? '' }, props.mode, l);
        return isProblem(built) ? built : built;
      }}
      submitLabel={props.mode === 'add' ? 'Add the location' : props.mode === 'edit' ? 'Save the change' : props.mode === 'confirm' ? 'Confirm the location' : props.mode === 'confirm-seen' ? 'Confirm seen' : 'Retire'}
      submitTone={props.mode === 'retire' ? 'destructive' : 'default'}
      preview={editing}
      confirm={props.mode === 'retire' ? { title: `Retire ${l?.label ?? 'this location'}?`, body: 'It keeps its history, and its record stays readable. Nothing new can point at it.', confirmLabel: 'Retire', tone: 'destructive' } : undefined}
    >
      {!editing && l && (
        <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2 text-sm text-navy">
          <span className="font-semibold">{l.label}</span>
          <span className="text-navy-2">{[l.platform.label, l.stage?.label, l.parent ? `inside ${l.parent.label}` : null].filter(Boolean).join('. ')}</span>
        </div>
      )}
      {editing && (
        <>
          {(props.mode === 'edit' || props.mode === 'confirm') && l ? (
            <div className="flex flex-col gap-1">
              <span className="text-sm font-medium text-navy">Platform</span>
              <span className="text-sm text-navy-2">{l.platform.label}</span>
            </div>
          ) : (
            <SelectField
              label="Platform"
              name="items.platformKey"
              value={form.platformKey}
              onChange={v => set({ platformKey: v, parentLocationId: props.parentLocationId ?? '', linkKindKey: '' })}
              choices={platformKeys.map(k => ({ value: k, label: platforms.label(k) }))}
              placeholder={platformKeys.length ? 'Choose the kind of store' : 'Reading the platforms'}
              required
            />
          )}
          <SelectField
            label="Inside"
            name="items.parentLocationId"
            value={form.parentLocationId}
            onChange={v => set({ parentLocationId: v })}
            choices={parents}
            noneLabel={`Nothing: it is ${instanceWord ? `an ${instanceWord}`.replace(/^an ([^aeiou])/, 'a $1') : 'an instance'}`}
            placeholder="Nothing"
            hint={storeWord ? `A ${storeWord} sits inside an ${instanceWord ?? 'instance'}. Locations go two levels deep at most.`.replace(/inside an ([^aeiou])/, 'inside a $1') : 'A store inside an instance takes the instance\'s stage. Locations go two levels deep at most.'}
          />
          {!form.parentLocationId && (
            <SelectField
              label="Stage"
              name="items.targetStageKey"
              value={form.targetStageKey}
              onChange={v => set({ targetStageKey: v })}
              choices={stages.keys().map(k => ({ value: k, label: stages.label(k) }))}
              placeholder="Choose the stage"
              required
              hint="A copy of production data outside production is judged by the copy rule."
            />
          )}
          <Field label="Name" name="items.labelAsSeen" required hint="The name the store goes by in its own tool.">
            {p => <Input {...p} value={form.labelAsSeen} onChange={e => set({ labelAsSeen: e.target.value })} maxLength={200} />}
          </Field>
          {hidden ? (
            <Muted>Its ID and link are hidden from your access. They stay as they are.</Muted>
          ) : (
            <>
              <Field label="ID in its own tool" name="items.externalId" hint="Give this, the link, or both. The location is matched on its platform and this ID.">
                {p => <Input {...p} required={false} value={form.externalId} onChange={e => set({ externalId: e.target.value })} maxLength={400} autoComplete="off" spellCheck={false} />}
              </Field>
              <Field label="Link" name="items.url" hint="Opens it in its own tool. Starts with https://.">
                {p => <Input {...p} required={false} type="url" value={form.url} onChange={e => set({ url: e.target.value })} maxLength={2048} autoComplete="off" spellCheck={false} />}
              </Field>
            </>
          )}
          <SelectField
            label="Link kind"
            name="items.linkKindKey"
            value={form.linkKindKey}
            onChange={v => set({ linkKindKey: v })}
            choices={kinds}
            noneLabel="The platform's default"
            placeholder="The platform's default"
          />
          <SelectField
            label="Deployment target"
            name="items.deploymentTargetId"
            value={form.deploymentTargetId}
            onChange={v => set({ deploymentTargetId: v })}
            choices={targetList}
            noneLabel="Not a deployment target"
            placeholder={targets.status === 'loading' ? 'Reading the targets' : 'Not a deployment target'}
            hint="When the store is a place the register deploys to, such as a Dataverse environment."
          />
          <AppField label="The system it is" name="items.hostedByApplicationId" value={app} onChange={setApp} hint="Optional. When the store is a registered application's own database or org." />
          <CheckField id="location-seen" label="Seen today" checked={form.seenNow} onChange={v => set({ seenNow: v })} hint="Records that someone saw the store today." />
        </>
      )}
    </ActionSheet>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// DataMappingSheet
// ---------------------------------------------------------------------------------------------------------------------

export function DataMappingSheet(props: {
  // confirm: a person checks a draft mapping (an AI client's), changes what is wrong, and confirms it.
  mode: 'add' | 'edit' | 'confirm' | 'end';
  mapping?: DataMappingItem;
  // Prefills for a new mapping, from the domain record or the location record.
  domainKey?: string;
  dataLocationId?: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const platforms = useConfigLabels('data-platform', open && props.mode !== 'end');
  const follow = useFollowUp();
  const initial = (): MappingForm => ({ ...mappingFormOf(props.mapping, props.domainKey ?? '', props.dataLocationId ?? ''), endOn: props.mode === 'end' ? todayInZone() : '' });
  const [form, setForm] = useState<MappingForm>(initial);
  const set = (patch: Partial<MappingForm>) => setForm(f => ({ ...f, ...patch }));
  const editing = props.mode !== 'end';
  const domains = useDomainChoices(open && editing);
  const locations = useLocationChoices(open && editing);
  if (!session.can('save_data_mapping')) return null;

  const m = props.mapping;
  const close = () => { setOpen(false); setForm(initial()); };
  const domainChoices = domainChoiceList(domains.choices, c => declarable(c) || c.key === form.domainKey);
  const locationChoices: Choice[] = [...locations.items]
    .sort((a, b) => a.label.localeCompare(b.label))
    .map(i => ({ value: i.dataLocationId, label: `${i.label}${i.stage ? ` (${i.stage.label})` : ''}` }));
  const location = locations.items.find(i => sameId(i.dataLocationId, form.locationId)) ?? null;
  const domain = domains.choices.find(c => c.key === form.domainKey)?.item ?? null;
  const containerWord = location ? platforms.field<string>(location.platform.key, 'containerLabel') ?? 'table' : 'table';
  const sources: Choice[] = locations.items.flatMap(i => i.domains
    .filter(d => d.domain.key === form.domainKey && !sameId(d.dataMappingId, m?.dataMappingId))
    .map(d => ({ value: d.dataMappingId, label: `${i.label}: ${DATA_ORIGIN_TEXT[d.origin].toLowerCase()}` })));
  const isCopy = form.origin === 'production-copy' || form.origin === 'masked-copy';
  const outsideProduction = location ? !location.isProduction : false;
  const copyRule = domain && isCopy && outsideProduction ? domain.rules.copy : null;

  return (
    <>
      <ActionSheet
        tool="save_data_mapping" site="pages/data/sheets#DataMappingSheet"
        title={props.mode === 'add' ? 'Map data to a location' : props.mode === 'edit' ? 'Change the mapping' : props.mode === 'confirm' ? 'Confirm the mapping' : 'End the mapping'}
        description={props.mode === 'end'
          ? 'The data left this location. The mapping keeps its history.'
          : props.mode === 'confirm'
            ? 'An AI client drafted this mapping. Check it, change what is wrong, and confirm it. A confirmed mapping is no longer a draft.'
          : 'Which business data sits in this store, in which tables, and where it came from. A copy of production data outside production is judged by the copy rule.'}
        trigger={props.trigger}
        open={open}
        onOpenChange={next => {
          // Each opening starts from the mapping as it is now: a change saved a moment before on this page stays (wave 4b QA).
          if (next) { setForm(initial()); setOpen(true); } else close();
        }}
        input={() => {
          const built = buildMappingInput(form, props.mode, m);
          return isProblem(built) ? built : built;
        }}
        submitLabel={props.mode === 'add' ? 'Map the data' : props.mode === 'edit' ? 'Save the change' : props.mode === 'confirm' ? 'Confirm the mapping' : 'End the mapping'}
        submitTone={props.mode === 'end' ? 'destructive' : 'default'}
        preview={editing}
        confirm={props.mode === 'end' ? { title: 'End this mapping?', body: `${m?.domain.path ?? 'The data'} no longer sits at ${m?.location.label ?? 'this location'} from the day you gave.`, confirmLabel: 'End the mapping', tone: 'destructive' } : undefined}
        onDone={(data, envelope) => {
          const decisionId = unsentDecision(data);
          if (decisionId) {
            follow.start({ decisionId, answer: readEngineAnswer('save_data_mapping', envelope) });
            close();
          }
        }}
      >
        {!editing && m && (
          <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2 text-sm text-navy">
            <span className="font-semibold">{m.domain.path}</span>
            <span className="text-navy-2">{`${DATA_ORIGIN_TEXT[m.origin]} at ${m.location.label}${m.containers.length ? `, in ${m.containers.slice(0, 4).join(', ')}` : ''}.`}</span>
          </div>
        )}
        {!editing && (
          <Field label="Left on" name="items.endOn" required hint={`Your own calendar day. Today is ${fmtDate(todayInZone())}.`}>
            {p => <Input {...p} type="date" value={form.endOn} onChange={e => set({ endOn: e.target.value })} className="w-[200px]" />}
          </Field>
        )}
        {editing && (
          <>
            <SelectField
              label="The data"
              name="items.domainKey"
              value={form.domainKey}
              onChange={v => set({ domainKey: v, copiedFromMappingId: '' })}
              choices={domainChoices}
              placeholder={domains.loading ? 'Reading the domains' : domainChoices.length ? 'Choose a domain' : 'No data domain yet'}
              required
            />
            <SelectField
              label="Where it sits"
              name="items.locationId"
              value={form.locationId}
              onChange={v => set({ locationId: v })}
              choices={locationChoices}
              placeholder={locations.loading ? 'Reading the locations' : locationChoices.length ? 'Choose a location' : 'No location is registered yet'}
              required
            />
            <RadioField<DataOrigin>
              legend="Where this data comes from"
              name="mapping-origin"
              value={form.origin}
              onChange={v => set({ origin: v })}
              required
              options={ORIGINS.map(o => ({ value: o, label: DATA_ORIGIN_TEXT[o], help: ORIGIN_HELP[o] }))}
            />
            {copyRule && (
              <Alert variant={copyRule.value === 'never' ? 'error' : copyRule.value === 'allowed' ? 'info' : 'warning'}>
                <AlertTitle>{`The copy rule is ${DATA_RULE_TEXT[copyRule.value]}`}</AlertTitle>
                <AlertDescription>
                  <p className="m-0">
                    {`For ${domain!.path}, ${ruleSourceText(copyRule)}. `}
                    {copyRule.value === 'never' ? 'A copy outside production is refused when it is saved.'
                      : copyRule.value === 'masked-only' ? (form.origin === 'masked-copy' ? 'A masked copy needs a decision. The data owner is asked.' : 'Only a masked copy can be allowed. An unmasked copy breaks the rule.')
                        : copyRule.value === 'decision' ? 'The copy needs a decision. The data owner is asked.'
                          : 'The copy needs no decision.'}
                  </p>
                </AlertDescription>
              </Alert>
            )}
            {COPY_ORIGINS.includes(form.origin as DataOrigin) && (
              <SelectField
                label="Copied from"
                name="items.copiedFromMappingId"
                value={form.copiedFromMappingId}
                onChange={v => set({ copiedFromMappingId: v })}
                choices={sources}
                noneLabel="Not known"
                placeholder="Not known"
                hint="Optional. The mapping the copy came from."
              />
            )}
            <Field label="Containers" name="items.containers" hint={`One per line, such as the ${containerWord} names. Leave it empty when the data fills the whole location. Names only, never data.`}>
              {p => <Textarea {...p} rows={3} value={form.containers} onChange={e => set({ containers: e.target.value })} spellCheck={false} />}
            </Field>
          </>
        )}
      </ActionSheet>
      {follow.data && (
        <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The copy decision for ${m?.domain.path ?? domain?.path ?? 'the data'}`} open={follow.open} onOpenChange={follow.onOpenChange} />
      )}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// DataDecisionSheet
// ---------------------------------------------------------------------------------------------------------------------

export function DataDecisionSheet(props: {
  subjectKind: 'data-use' | 'data-mapping';
  subjectId: string;
  // The decision type: data-use for a use, the copy decision type for a mapping, data-retention at retirement.
  decisionTypeKey?: string;
  // What the decision is about, for the sheet's title.
  label: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const types = useConfigLabels('decision-type', open);
  const follow = useFollowUp();
  const initial = (): DecisionForm => ({ decisionTypeKey: props.decisionTypeKey ?? 'data-use', statement: '', context: '', outcomeKey: '', reason: '', optionText: '', optionWhyNot: '' });
  const [form, setForm] = useState<DecisionForm>(initial);
  const set = (patch: Partial<DecisionForm>) => setForm(f => ({ ...f, ...patch }));
  const outcomes = useOutcomeChoices(form.decisionTypeKey, open);
  if (!session.can('save_decision_proposal')) return null;

  const close = () => { setOpen(false); setForm(initial()); };
  // A use takes a data-use or a data-retention decision; a copy takes a data-use decision (a
  // choice under D-41).
  const typeKeys = props.subjectKind === 'data-use' ? ['data-use', 'data-retention'] : ['data-use'];
  const typeName = types.label(form.decisionTypeKey);
  const what = props.subjectKind === 'data-mapping' ? 'copy' : form.decisionTypeKey === 'data-retention' ? 'retention' : 'data use';
  return (
    <>
      <ActionSheet
        tool="save_decision_proposal" site="pages/data/sheets#DataDecisionSheet"
        title={`Propose a ${what} decision`}
        description={`About ${props.label}. The decision is proposed; it takes effect only when the people the rules ask accept it.`}
        trigger={props.trigger}
        open={open}
        onOpenChange={next => { if (next) setOpen(true); else close(); }}
        input={() => {
          const built = buildDecisionInput(form, props.subjectKind, props.subjectId, outcomes.needsOptions);
          return isProblem(built) ? built : built;
        }}
        submitLabel="Propose"
        preview
        size="wide"
        onDone={(data, envelope) => {
          if (data.recordId) {
            follow.start({ decisionId: data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) });
            close();
          }
        }}
      >
        {props.decisionTypeKey ? (
          <div className="flex flex-col gap-1">
            <span className="text-sm font-medium text-navy">Kind of decision</span>
            <span className="text-sm text-navy-2">{typeName}</span>
          </div>
        ) : (
          <RadioField<string>
            legend="Kind of decision"
            name="data-decision-type"
            value={form.decisionTypeKey}
            onChange={v => set({ decisionTypeKey: v, outcomeKey: '' })}
            options={typeKeys.map(k => ({
              value: k,
              label: types.label(k),
              help: k === 'data-retention' ? 'What happens to the data a retiring application wrote: keep, archive, delete or transfer.' : 'Whether this use of the data may go ahead.',
            }))}
          />
        )}
        <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
          {p => <Input {...p} value={form.statement} onChange={e => set({ statement: e.target.value })} maxLength={500} placeholder={`${typeName}: ${props.label}`} />}
        </Field>
        <Field label="Context" name="context" required hint="The situation the approvers need to know: what the data is used for, by whom, and any safeguards.">
          {p => <Textarea {...p} rows={4} maxLength={4000} value={form.context} onChange={e => set({ context: e.target.value })} />}
        </Field>
        <RadioField<string>
          legend="Proposed outcome"
          name="data-decision-outcome"
          value={form.outcomeKey}
          onChange={v => set({ outcomeKey: v })}
          required
          options={outcomes.options.map(o => ({ value: o.key, label: o.label }))}
          hint={outcomes.loading ? 'Reading the outcomes.' : outcomes.options.length ? undefined : 'This kind of decision has no outcome in the configuration in force.'}
        />
        <Field label="Reason" name="reason" required hint="Why this outcome.">
          {p => <Textarea {...p} rows={3} maxLength={2000} value={form.reason} onChange={e => set({ reason: e.target.value })} />}
        </Field>
        <Field label="An option you weighed" name="optionsConsidered" required={outcomes.needsOptions} hint={outcomes.needsOptions ? 'This kind of decision asks for one.' : 'Optional.'}>
          {p => <Input {...p} required={false} value={form.optionText} onChange={e => set({ optionText: e.target.value })} maxLength={500} />}
        </Field>
        {(form.optionText.trim() || outcomes.needsOptions) && (
          <Field label="Why it was not chosen" name="optionsConsidered.whyNot" required={outcomes.needsOptions}>
            {p => <Textarea {...p} required={false} rows={2} maxLength={1000} value={form.optionWhyNot} onChange={e => set({ optionWhyNot: e.target.value })} />}
          </Field>
        )}
      </ActionSheet>
      {follow.data && (
        <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The ${what} decision about ${props.label}`} open={follow.open} onOpenChange={follow.onOpenChange} />
      )}
    </>
  );
}

// ---------------------------------------------------------------------------------------------------------------------
// MoveDataReferencesSheet
// ---------------------------------------------------------------------------------------------------------------------

export function MoveDataReferencesSheet(props: {
  fromDomainKey: string;
  trigger?: ReactElement;
}): ReactElement | null {
  const session = useSession();
  const [open, setOpen] = useSheetOpen();
  const initial = (): MoveForm => ({ toDomainKey: '', mappings: true, uses: true, reason: '' });
  const [form, setForm] = useState<MoveForm>(initial);
  const set = (patch: Partial<MoveForm>) => setForm(f => ({ ...f, ...patch }));
  const domains = useDomainChoices(open);
  if (!session.can('move_data_references') || !session.hasRole('platform-admin', 'data-governance-lead')) return null;

  const close = () => { setOpen(false); setForm(initial()); };
  const from = domains.choices.find(c => c.key === props.fromDomainKey);
  const fromPath = from?.path ?? 'this domain';
  const choices = domainChoiceList(domains.choices, c => declarable(c) && c.key !== props.fromDomainKey);
  return (
    <ActionSheet
      tool="move_data_references" site="pages/data/sheets#MoveDataReferencesSheet"
      title={`Move data off ${fromPath}`}
      description="For a split, a merge or before a domain is retired. When both domains have the same data owner, each row keeps its identity and its decisions, and a use whose new domain is stricter needs a decision again. When the data owner changes, the decisions stay behind: a decided row is ended and declared again, a waiting decision asks the new data owner, and the engine opens a decision for each row that needs one. Check first to see who would be asked. Owners move only through an owner change."
      trigger={props.trigger}
      open={open}
      onOpenChange={next => { if (next) setOpen(true); else close(); }}
      input={() => {
        const built = buildMoveInput(form, props.fromDomainKey);
        return isProblem(built) ? built : built;
      }}
      submitLabel="Move"
      preview="Check first: what would change"
      confirm={{ title: `Move these rows off ${fromPath}?`, body: 'Each moved row is judged again under its new domain. When the data owner changes, the new data owner is asked about each row that needs a decision. A use that waits for a decision shows on the Uses tab.', confirmLabel: 'Move' }}
    >
      <SelectField
        label="Move to"
        name="toDomainKey"
        value={form.toDomainKey}
        onChange={v => set({ toDomainKey: v })}
        choices={choices}
        placeholder={domains.loading ? 'Reading the domains' : 'Choose a domain'}
        required
        hint="An active domain at a level that takes data."
      />
      <fieldset className="m-0 flex flex-col gap-2 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">What moves</legend>
        <CheckField id="move-mappings" label="The mappings: where the data sits" checked={form.mappings} onChange={v => set({ mappings: v })} />
        <CheckField id="move-uses" label="The uses: which applications use it" checked={form.uses} onChange={v => set({ uses: v })} />
      </fieldset>
      <Field label="Reason" name="reason" required hint="Such as the split it serves. Kept with the call's audit entry.">
        {p => <Textarea {...p} rows={3} maxLength={500} value={form.reason} onChange={e => set({ reason: e.target.value })} />}
      </Field>
    </ActionSheet>
  );
}
