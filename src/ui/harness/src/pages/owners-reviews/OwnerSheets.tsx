// The owner writes of the Owners and person pages, each in the shared side sheet (components/actions.tsx):
//   ProposeOwnerChangeSheet   propose_owner_change: a new holder or deputy, a successor, or an assignment that ends.
//                             It records an ownership decision; nothing changes until people accept it (OP-08).
//   RecordLeaveSheet          record_owner_leave: the leave date, as reported. The engine opens an owner check review on
//                             each application the person owns and names the roles that need a successor.
//   SubmitDecisionSheet       submit_record for a decision: sends a proposal to its approvers.
//   ProposalBanner            after a proposal: submit it now, or open the decision page.
// Wave 4: an owner change opens the submit follow-up right after it is saved ("Send it for
// approval now?", with the engine's answer), as the data sheets do. Cancel leaves it proposed; the banner then still
// offers the submit, and says it is sent once the follow-up sent it (useSentHere).
import { CircleCheck, X } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { hrefTo } from '../../app/links';
import { ActionSheet, Field, type FieldControl } from '../../components/actions';
import { TextLink } from '../../components/blocks';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import { Button } from '../../components/ui/button';
import { Checkbox } from '../../components/ui/checkbox';
import { Input } from '../../components/ui/input';
import { PersonSearchField } from '../../components/people';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { IsoDate, PrincipalRef } from '../../data/contracts';
import { fmtDate } from '../../data/format';
import { useLabels } from '../../data/labels';
import { dataOf } from '../../data/toolClient';
import { readEngineAnswer } from '../../data/writes';
import { SubmitFollowUp, useFollowUp, useSentHere } from '../data/sheetParts';
import { useAllPages } from './hooks';
import { buildLeave, buildOwnerChange, buildSubmit, heldOnApplications, isProblem, personOptions, sameId, type OwnerChangeMode, type PersonOption, type WhoAmI } from './model';

// The props a Select trigger takes from a Field (a button has no required attribute).
export function triggerProps(p: FieldControl) {
  return { id: p.id, 'aria-describedby': p['aria-describedby'], 'aria-invalid': p['aria-invalid'] };
}

const OTHER = 'someone-else';

export interface Proposal {
  decisionId: string;
  subject: string;
}

// The person picker: the people the product already knows as owners, or anyone else the product knows, found by name
// (find_people). No page asks for an object ID.
function PersonPicker(props: {
  label: string;
  name: string;
  options: PersonOption[];
  loading: boolean;
  value: string;
  onChange: (objectId: string) => void;
  hint?: string;
  exclude?: string[];
}) {
  const known = props.options.some(o => sameId(o.objectId, props.value));
  const [other, setOther] = useState(!!props.value && !known && !props.loading);
  const [searched, setSearched] = useState<PrincipalRef | null>(null);
  const choice = other ? OTHER : known ? props.options.find(o => sameId(o.objectId, props.value))!.objectId : '';
  return (
    <>
      <Field label={props.label} name={other ? undefined : props.name} required hint={props.hint}>
        {p => (
          <Select
            value={choice}
            onValueChange={v => {
              if (v === OTHER) {
                setOther(true);
                setSearched(null);
                props.onChange('');
              } else {
                setOther(false);
                props.onChange(v);
              }
            }}
          >
            <SelectTrigger {...triggerProps(p)} className="w-full">
              <SelectValue placeholder={props.loading ? 'Reading the people the product knows' : 'Choose a person'} />
            </SelectTrigger>
            <SelectContent className="max-h-[320px]">
              {props.options.map(o => (
                <SelectItem key={o.objectId} value={o.objectId}>
                  {o.displayLabel}
                  {o.note && <span className="text-xs text-navy-3">({o.note})</span>}
                </SelectItem>
              ))}
              <SelectItem value={OTHER}>Someone else: find them by name</SelectItem>
            </SelectContent>
          </Select>
        )}
      </Field>
      {other && (
        <PersonSearchField
          label="Find the person"
          name={props.name}
          required
          value={searched}
          onChange={person => { setSearched(person); props.onChange(person?.objectId ?? ''); }}
          exclude={props.exclude}
          activeOnly
        />
      )}
    </>
  );
}

// The people the product knows as current owners, read once the sheet opens.
function usePeopleOptions(open: boolean, me: WhoAmI | null) {
  const state = useAllPages('list_owner_assignments', open ? { state: 'current', top: 100 } : null);
  const data = dataOf(state);
  return { options: personOptions(data?.items ?? [], me), loading: open && state.status === 'loading' };
}

export function ProposeOwnerChangeSheet(props: {
  application: { id: string; name: string };
  mode: OwnerChangeMode;
  ownerRoleKey?: string;
  isDeputy?: boolean;
  // The assignment that ends, for replace and end.
  ending?: { assignmentId: string; principal: PrincipalRef; isDeputy: boolean } | null;
  // A suggested person, such as the deputy as the successor.
  suggested?: PrincipalRef | null;
  me: WhoAmI | null;
  trigger: ReactElement;
  onProposed?: (proposal: Proposal) => void;
}) {
  const ownerRoles = useLabels('owner-role');
  const [open, setOpen] = useState(false);
  const presetRole = props.ownerRoleKey ?? '';
  const presetDeputy = props.ending ? props.ending.isDeputy : !!props.isDeputy;
  const presetPerson = props.suggested?.objectId ?? '';
  const [role, setRole] = useState(presetRole);
  const [person, setPerson] = useState(presetPerson);
  const [deputy, setDeputy] = useState(presetDeputy);
  const [startOn, setStartOn] = useState('');
  const [reason, setReason] = useState('');
  const people = usePeopleOptions(open, props.me);
  const follow = useFollowUp();
  const reset = () => {
    setRole(presetRole);
    setPerson(presetPerson);
    setDeputy(presetDeputy);
    setStartOn('');
    setReason('');
  };

  const roleName = role ? ownerRoles.label(role).toLowerCase() : 'owner';
  const title = props.mode === 'replace'
    ? 'Propose a successor'
    : props.mode === 'end'
      ? 'End an owner assignment'
      : `Propose a ${deputy ? 'deputy' : 'holder'}`;
  const roleFixed = !!presetRole;

  const subject = `Owner change for ${props.application.name}`;
  return (
    <>
    <ActionSheet
      tool="propose_owner_change" site="pages/owners-reviews/OwnerSheets#ProposeOwnerChangeSheet"
      title={title}
      description={(
        <>
          For {props.application.name}. This records an ownership decision for people to accept. Nothing changes until it
          is accepted; submit it after saving to ask the approvers.
        </>
      )}
      trigger={props.trigger}
      open={open}
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      input={() => {
        const built = buildOwnerChange({
          applicationId: props.application.id,
          mode: props.mode,
          ownerRoleKey: role,
          addObjectId: person,
          isDeputy: deputy,
          startOn,
          endAssignmentId: props.ending?.assignmentId ?? null,
          endingObjectId: props.ending?.principal.objectId ?? null,
          reason,
        });
        return isProblem(built) ? built : built.input;
      }}
      submitLabel="Propose"
      preview
      onDone={(data, envelope) => {
        if (!data.decisionId) return;
        props.onProposed?.({ decisionId: data.decisionId, subject });
        follow.start({ decisionId: data.decisionId, answer: readEngineAnswer('propose_owner_change', envelope) });
        setOpen(false);
        reset();
      }}
    >
      {roleFixed ? (
        <div className="flex flex-col gap-1">
          <span className="text-sm font-medium text-navy">Owner role</span>
          <span className="text-sm text-navy-2">{ownerRoles.label(role)}{presetDeputy ? ', deputy' : ''}</span>
        </div>
      ) : (
        <Field label="Owner role" name="changes.ownerRoleKey" required>
          {p => (
            <Select value={role} onValueChange={setRole}>
              <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder="Choose the owner role" /></SelectTrigger>
              <SelectContent>
                {ownerRoles.keys().filter(k => heldOnApplications(ownerRoles.field<unknown>(k, 'subjectKinds'))).map(k => <SelectItem key={k} value={k}>{ownerRoles.label(k)}</SelectItem>)}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
      {props.ending && (
        <div className="flex flex-col gap-1 rounded-md border border-line bg-secondary px-3 py-2">
          <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Ends</span>
          <span className="text-sm text-navy">
            {props.ending.principal.displayLabel || props.ending.principal.objectId}, {ownerRoles.label(role)}{props.ending.isDeputy ? ' (deputy)' : ''}
          </span>
        </div>
      )}
      {props.mode !== 'end' && (
        <>
          <PersonPicker
            label={props.mode === 'replace' ? 'Successor' : deputy ? 'New deputy' : 'New holder'}
            name="changes.addObjectId"
            options={people.options.filter(o => !sameId(o.objectId, props.ending?.principal.objectId))}
            exclude={props.ending ? [props.ending.principal.objectId] : undefined}
            loading={people.loading}
            value={person}
            onChange={setPerson}
            hint={props.suggested ? `Suggested: ${props.suggested.displayLabel}, the current deputy.` : undefined}
          />
          {props.mode === 'add' && !roleFixed && (
            <div className="flex items-center gap-2">
              <Checkbox id="owner-change-deputy" checked={deputy} onCheckedChange={v => setDeputy(v === true)} />
              <Label htmlFor="owner-change-deputy" className="font-normal">As a deputy for the {roleName} role</Label>
            </div>
          )}
          <Field label="Starts on" name="changes.startOn" hint="Once the decision is accepted. Leave it empty to start on acceptance.">
            {p => <Input {...p} required={false} type="date" value={startOn} onChange={e => setStartOn(e.target.value)} className="w-[200px]" />}
          </Field>
        </>
      )}
      <Field label="Reason" name="reason" required hint="The approvers read this. Stored as you type it.">
        {p => <Textarea {...p} rows={4} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={subject} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

export function RecordLeaveSheet(props: {
  // The person who leaves, or null to choose from the candidates.
  person: PrincipalRef | null;
  candidates?: PersonOption[];
  currentLeavesOn?: IsoDate | null;
  trigger: ReactElement;
}) {
  const [open, setOpen] = useState(false);
  const [who, setWho] = useState(props.person?.objectId ?? '');
  const [leavesOn, setLeavesOn] = useState(props.currentLeavesOn ?? '');
  const [kind, setKind] = useState<'leave' | 'role-change'>('leave');
  const [note, setNote] = useState('');
  const reset = () => {
    setWho(props.person?.objectId ?? '');
    setLeavesOn(props.currentLeavesOn ?? '');
    setKind('leave');
    setNote('');
  };
  const name = props.person?.displayLabel
    ?? props.candidates?.find(c => sameId(c.objectId, who))?.displayLabel
    ?? 'this person';

  return (
    <ActionSheet
      tool="record_owner_leave" site="pages/owners-reviews/OwnerSheets#RecordLeaveSheet"
      title={props.person ? `Record a leave: ${name}` : 'Record a leave'}
      description="The leave date is recorded as reported. The engine opens an owner check review on each application the person owns and names the owner roles that need a successor. It changes no owner by itself."
      trigger={props.trigger}
      open={open}
      // The form starts from the props each time it opens: Application 360 learns the current leave date after the row
      // mounted (fix round 2).
      onOpenChange={next => { if (next) reset(); setOpen(next); if (!next) reset(); }}
      input={() => {
        const built = buildLeave({ principalObjectId: who, leavesOn, kind, note });
        return isProblem(built) ? built : built.input;
      }}
      submitLabel="Record the leave"
      preview
      confirm={{
        title: `Record that ${name} ${kind === 'leave' ? 'leaves' : 'changes role'} on ${leavesOn ? fmtDate(leavesOn) : 'that date'}?`,
        body: 'Owner check reviews open on the applications the person owns, due on the leave date. Their approvals and owner roles still need a successor, through an ownership decision.',
        confirmLabel: 'Record the leave',
      }}
    >
      {!props.person && (
        <Field label="Who leaves" name="principalObjectId" required hint="You can record your own leave, the leave of someone who owns an application with you, or anyone's as a platform admin.">
          {p => (
            <Select value={who} onValueChange={setWho}>
              <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder="Choose the person" /></SelectTrigger>
              <SelectContent className="max-h-[320px]">
                {(props.candidates ?? []).map(c => (
                  <SelectItem key={c.objectId} value={c.objectId}>
                    {c.displayLabel}
                    {c.note && <span className="text-xs text-navy-3">({c.note})</span>}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        </Field>
      )}
      <Field label="Leave date" name="leavesOn" required hint={props.currentLeavesOn ? `A leave on ${fmtDate(props.currentLeavesOn)} is recorded now. A new date replaces it.` : undefined}>
        {p => <Input {...p} type="date" value={leavesOn} onChange={e => setLeavesOn(e.target.value)} className="w-[200px]" />}
      </Field>
      <Field label="What happens" name="kind" required>
        {p => (
          <Select value={kind} onValueChange={v => setKind(v as 'leave' | 'role-change')}>
            <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="leave">Leaves the organization</SelectItem>
              <SelectItem value="role-change">Moves to another role</SelectItem>
            </SelectContent>
          </Select>
        )}
      </Field>
      <Field label="Note" name="note" hint="Optional. A plain note for the owner check.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

export function SubmitDecisionSheet(props: { decisionId: string; subject: string; trigger: ReactElement; onSubmitted?: () => void; onOpenChange?: (open: boolean) => void }) {
  const [note, setNote] = useState('');
  return (
    <ActionSheet
      tool="submit_record" site="pages/owners-reviews/OwnerSheets#SubmitDecisionSheet"
      title={`Submit for approval: ${props.subject}`}
      description="The engine opens the approval requests the rules ask for. Check first to see who would be asked."
      trigger={props.trigger}
      onOpenChange={next => { if (!next) setNote(''); props.onOpenChange?.(next); }}
      input={() => buildSubmit(props.decisionId, note)}
      submitLabel="Submit"
      preview
      onDone={() => props.onSubmitted?.()}
    >
      <Field label="Note" name="note" hint="Optional. Shown to the approvers with the request.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}

// Keeps an action on screen while its sheet is open, even when the page data read again after the write no longer
// offers it (a review with its outcome proposed, a decision now submitted), so the engine's answer stays in view.
// Pass the setter to the sheet's onOpenChange and show the action when it applies or while sticky is true.
export function useStickyAction(): [boolean, (open: boolean) => void] {
  return useState(false);
}

// After a proposal: nobody is asked until it is submitted, so the page offers the submit right away.
// alreadySubmitted: the page's data shows the decision submitted another way (the review page's own submit step).
export function ProposalBanner({ proposal, onDismiss, alreadySubmitted }: { proposal: Proposal | null; onDismiss: () => void; alreadySubmitted?: boolean }) {
  const [submittedHere, setSubmitted] = useState(false);
  const [sheetOpen, setSheetOpen] = useStickyAction();
  // Sent from the follow-up the proposal sheet opened.
  const sentFromFollowUp = useSentHere(proposal?.decisionId);
  if (!proposal) return null;
  const submitted = submittedHere || sentFromFollowUp || !!alreadySubmitted;
  return (
    <Alert variant="success" className="mb-4" role="status">
      <CircleCheck aria-hidden />
      <AlertTitle>{submitted ? 'Submitted for approval' : 'Proposed. Nobody is asked until it is submitted.'}</AlertTitle>
      <AlertDescription>
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <span>{proposal.subject}.</span>
          {(!submitted || sheetOpen) && (
            <SubmitDecisionSheet
              decisionId={proposal.decisionId}
              subject={proposal.subject}
              trigger={<Button size="sm">Submit for approval</Button>}
              onSubmitted={() => setSubmitted(true)}
              onOpenChange={setSheetOpen}
            />
          )}
          <TextLink href={hrefTo({ page: 'decision', id: proposal.decisionId.toLowerCase() })}>Open the decision</TextLink>
          <Button variant="ghost" size="icon-sm" className="ml-auto" aria-label="Dismiss this message" onClick={onDismiss}>
            <X aria-hidden />
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}
