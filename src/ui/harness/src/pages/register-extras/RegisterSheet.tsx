// "Register an app that is already built" (screen "Applications register", UC-05): save_application_draft in the shared
// side sheet, then the confirmation. The engine checks the write as it checks an AI client's (D-36). The application is
// saved as a draft, with a proposed ownership decision naming its owners; it joins the register once that decision is
// accepted, which asks each named owner to confirm. The page then offers the next step: send the decision for that
// confirmation (submit_record), and open the draft's 360.
//
// Design choices (D-41), recorded here:
// - The business owner is required on this form. A draft becomes registered only through its ownership decision, so a
//   draft with no named owner could never finish here. It starts as the signed-in person, who can change it.
// - Components are entered with the draft (save_application_draft components), not with link_component afterwards:
//   the tool takes them in the same write, so one engine check covers the whole registration. link_component stays for
//   claims and vanished items.
// - Similar applications show while the name is typed (check_duplicates), as on the intake wizard. A suggestion only:
//   the engine warns on a duplicate name and never refuses one.
import { Plus, Send, Trash2 } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { hrefTo } from '../../app/links';
import type { ApplicationDraftResult, PrincipalRef, Success } from '../../data/contracts';
import { readEngineAnswer } from '../../data/writes';
import { SubmitFollowUp, useFollowUp, useSentHere } from '../data/sheetParts';
import { useLabels, useListValues } from '../../data/labels';
import { useSession } from '../../data/session';
import { dataOf, useTool } from '../../data/toolClient';
import { ActionSheet, Field } from '../../components/actions';
import { Muted, TextLink } from '../../components/blocks';
import { PersonSearchField, useSettled } from '../../components/people';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Textarea } from '../../components/ui/textarea';
import { choicesOf, SelectField, useSheet } from '../changes-records/parts';
import { DuplicateList } from '../intake/shared';
import { SubmitSheet } from '../work-approvals/parts';
import { BUSINESS_OWNER, buildRegisterInput, emptyComponent, emptyRegisterForm, TECHNICAL_OWNER, type ComponentRow, type RegisterForm } from './logic';

// What the page keeps of a saved draft, for the next step.
export interface SavedDraft {
  applicationId: string;
  name: string;
  ownershipDecisionId: string | null;
  owners: string[];
  askedLater: number;
}

const USES_AI_CHOICES = [
  { value: 'yes', label: 'Yes, it uses AI' },
  { value: 'no', label: 'No, it does not use AI' },
];

export function RegisterBuiltAppSheet({ trigger, onSaved }: { trigger: ReactElement; onSaved?: (saved: SavedDraft) => void }) {
  const session = useSession();
  const me: PrincipalRef | null = session.who ? { objectId: session.who.principal.objectId, displayLabel: session.who.principal.displayLabel } : null;
  const kinds = useLabels('application-kind');
  const componentKinds = useLabels('component-kind');
  const ownerRoles = useLabels('owner-role');
  const buildPaths = useListValues('build-path');
  const portfolios = useTool('list_portfolios', { top: 100 });
  const [form, setForm] = useState<RegisterForm>(() => emptyRegisterForm(me));
  const sheet = useSheet(() => setForm(emptyRegisterForm(me)));
  const follow = useFollowUp();
  // The name as it was saved: the form resets once the follow-up opens.
  const [followName, setFollowName] = useState('');
  const set = <K extends keyof RegisterForm>(key: K, value: RegisterForm[K]) => setForm(f => ({ ...f, [key]: value }));
  const setComponent = (i: number, patch: Partial<ComponentRow>) => set('components', form.components.map((c, j) => (j === i ? { ...c, ...patch } : c)));

  // The owner roles of the configuration in force. While they load, both are offered; the engine checks the keys.
  const loaded = ownerRoles.keys().length > 0;
  const roles = { business: !loaded || ownerRoles.has(BUSINESS_OWNER), technical: !loaded || ownerRoles.has(TECHNICAL_OWNER) };

  const nameText = useSettled(form.name.trim(), 400);
  const duplicates = useTool('check_duplicates', sheet.open && nameText.length >= 3 ? { text: nameText, ...(form.kindKey ? { proposedKindKey: form.kindKey } : {}) } : null);
  const matches = dataOf(duplicates)?.matches ?? [];

  const portfolioChoices = (dataOf(portfolios)?.items ?? [])
    .filter(p => p.isActive)
    .map(p => ({ value: p.portfolioId.toLowerCase(), label: p.path.length > 1 ? p.path.map(x => x.name).join(' / ') : p.name }))
    .sort((a, b) => a.label.localeCompare(b.label));

  const done = (data: ApplicationDraftResult, envelope: Success<ApplicationDraftResult>) => {
    if (!data.recordId) return;
    // The ownership decision waits, proposed: send it now from the follow-up, or later from the notice or the 360.
    if (data.ownershipDecisionId) {
      setFollowName(form.name.trim());
      follow.start({ decisionId: data.ownershipDecisionId, answer: readEngineAnswer('save_application_draft', envelope) });
      sheet.onOpenChange(false);
    }
    onSaved?.({
      applicationId: data.recordId.toLowerCase(),
      name: form.name.trim(),
      ownershipDecisionId: data.ownershipDecisionId,
      owners: [form.businessOwner?.displayLabel, form.technicalOwner?.displayLabel].filter((x): x is string => !!x),
      askedLater: data.approvalsNeeded.length,
    });
  };

  return (
    <>
    <ActionSheet
      tool="save_application_draft" site="pages/register-extras/RegisterSheet#RegisterBuiltAppSheet"
      title="Register an app that is already built"
      description="It is saved as a draft with its owners proposed. It joins the register once its ownership decision is accepted: each named owner is asked to confirm."
      trigger={trigger}
      open={sheet.open}
      onOpenChange={next => {
        // Each opening starts from an empty form with the signed-in person as the business owner.
        if (next) setForm(emptyRegisterForm(me));
        sheet.onOpenChange(next);
      }}
      size="wide"
      input={() => buildRegisterInput(form, roles)}
      submitLabel="Save the draft"
      preview
      onDone={done}
    >
      <Field label="Name" name="name" required hint="As people know it.">
        {p => <Input {...p} value={form.name} onChange={e => set('name', e.target.value)} maxLength={200} autoComplete="off" />}
      </Field>
      {nameText.length >= 3 && duplicates.status === 'done' && duplicates.envelope.ok && matches.length > 0 && (
        <div className="flex flex-col gap-1.5 rounded-md border border-warning-line bg-warning-soft px-3 py-2">
          <span className="text-sm font-semibold text-navy">Already in the register?</span>
          <Muted>These look similar. Open one to check before you register a second. Each opens in a new tab.</Muted>
          <DuplicateList matches={matches.slice(0, 5)} newTab />
        </div>
      )}
      <Field label="What it is for" name="purpose" required hint="One or two plain sentences.">
        {p => <Textarea {...p} rows={3} maxLength={2000} value={form.purpose} onChange={e => set('purpose', e.target.value)} />}
      </Field>
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Kind" name="applicationKindKey" value={form.kindKey} onChange={v => set('kindKey', v)} choices={choicesOf(kinds)} required placeholder={kinds.keys().length ? 'Choose a kind' : 'Reading the kinds'} />
        <SelectField label="Portfolio" name="portfolioId" value={form.portfolioId} onChange={v => set('portfolioId', v)} choices={portfolioChoices} noneLabel="Not in a portfolio yet" />
      </div>
      <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
        <SelectField label="Build path" name="buildPathKey" value={form.buildPathKey} onChange={v => set('buildPathKey', v)} choices={buildPaths.choices} noneLabel="Not stated" hint="How it was built, from the build path list." />
        <SelectField label="Uses AI" name="usesAi" value={form.usesAi} onChange={v => set('usesAi', v as RegisterForm['usesAi'])} choices={USES_AI_CHOICES} noneLabel="Not stated" hint="The owner's statement." />
      </div>
      {roles.business && (
        <PersonSearchField
          label={ownerRoles.has(BUSINESS_OWNER) ? ownerRoles.label(BUSINESS_OWNER) : 'Business owner'}
          name="proposedOwners"
          value={form.businessOwner}
          onChange={v => set('businessOwner', v)}
          required
          activeOnly
          hint="Proposed, not assigned: they become the owner once the ownership decision is accepted. They are asked to confirm."
        />
      )}
      {roles.technical && (
        <PersonSearchField
          label={ownerRoles.has(TECHNICAL_OWNER) ? `${ownerRoles.label(TECHNICAL_OWNER)} (optional)` : 'Technical owner (optional)'}
          name="proposedOwners.technical"
          value={form.technicalOwner}
          onChange={v => set('technicalOwner', v)}
          activeOnly
          exclude={form.businessOwner ? [form.businessOwner.objectId] : []}
          hint="Type two or more letters of the name. Leave it empty to name one later."
        />
      )}
      <fieldset className="m-0 flex min-w-0 flex-col gap-2 border-0 p-0">
        <legend className="mb-1 text-sm font-medium text-navy">Components</legend>
        <Muted>Optional. The parts it is built from, as links: a Power Platform solution, an app, a flow, an agent. Each opens in its own tool.</Muted>
        {form.components.map((c, i) => (
          <div key={i} className="flex flex-col gap-2 rounded-md border border-line p-3">
            <div className="flex items-center justify-between gap-2">
              <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{`Component ${i + 1}`}</span>
              <Button type="button" variant="ghost" size="sm" aria-label={`Remove component ${i + 1}`} onClick={() => set('components', form.components.filter((_, j) => j !== i))}>
                <Trash2 aria-hidden />Remove
              </Button>
            </div>
            <div className="grid grid-cols-2 gap-3 narrow:grid-cols-1">
              <SelectField label="Kind" name="components.componentKindKey" value={c.kindKey} onChange={v => setComponent(i, { kindKey: v })} choices={choicesOf(componentKinds)} required />
              <Field label="ID in its tool" name="components.externalId" required>
                {p => <Input {...p} value={c.externalId} onChange={e => setComponent(i, { externalId: e.target.value })} maxLength={400} className="font-mono" />}
              </Field>
            </div>
            <Field label="Link" name="components.url" required hint="The full https address that opens it.">
              {p => <Input {...p} type="url" inputMode="url" value={c.url} onChange={e => setComponent(i, { url: e.target.value })} placeholder="https://" maxLength={2000} />}
            </Field>
            <Field label="Name in its tool" name="components.labelAsSeen" hint="Optional.">
              {p => <Input {...p} value={c.label} onChange={e => setComponent(i, { label: e.target.value })} maxLength={200} />}
            </Field>
          </div>
        ))}
        {form.components.length < 10 && (
          <Button type="button" variant="link" size="sm" className="h-7 self-start px-0" onClick={() => set('components', [...form.components, emptyComponent()])}>
            <Plus aria-hidden />Add a component
          </Button>
        )}
      </fieldset>
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`Ownership of ${followName}`} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

// The next step after a draft is saved: the confirmation. The ownership decision waits, proposed, until its proposer
// submits it; submitting asks each named owner to confirm, and acceptance registers the application.
export function SavedDraftNotice({ saved, onDismiss }: { saved: SavedDraft; onDismiss: () => void }) {
  const session = useSession();
  const [sentHere, setSent] = useState(false);
  // Sent from the follow-up the sheet opened after saving.
  const sentFromFollowUp = useSentHere(saved.ownershipDecisionId);
  const sent = sentHere || sentFromFollowUp;
  const app = hrefTo({ page: 'app', id: saved.applicationId });
  const drafts = hrefTo({ page: 'applications', filter: { registrationMeanings: ['draft'] } });
  return (
    <section
      aria-label="The draft you saved"
      className="mb-4 flex flex-col gap-2 rounded-xl border border-info-line bg-info-soft p-4 text-navy narrow:p-3"
      role="status"
    >
      <span className="text-sm font-semibold">{`${saved.name} is saved as a draft.`}</span>
      <p className="m-0 text-sm leading-relaxed text-navy-2">
        {saved.ownershipDecisionId
          ? sent
            ? `Its ownership decision is submitted. ${saved.owners.length ? `${saved.owners.join(' and ')} ${saved.owners.length === 1 ? 'is' : 'are'} asked to confirm.` : 'The people the rules name are asked.'} The application joins the register once the decision is accepted.`
            : `It joins the register once its ownership decision is accepted. Next, submit that decision, so ${saved.owners.length ? saved.owners.join(' and ') : 'the named owners'} ${saved.owners.length === 1 ? 'is' : 'are'} asked to confirm.`
          : 'No owner was proposed, so it has no ownership decision yet. Propose its owners on its 360 to have it registered.'}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        {saved.ownershipDecisionId && !sent && session.can('submit_record') && (
          <SubmitSheet
            subjectKind="decision"
            subjectId={saved.ownershipDecisionId}
            title={`Ownership of ${saved.name}`}
            isAiDraft={false}
            trigger={<Button size="sm"><Send aria-hidden />Send for confirmation</Button>}
            onDone={() => setSent(true)}
          />
        )}
        <Button size="sm" variant="outline" asChild><a href={app}>Open the draft</a></Button>
        <TextLink href={drafts} className="text-sm">Show the drafts</TextLink>
        <Button size="sm" variant="ghost" onClick={onDismiss} className="ml-auto narrow:ml-0">Close</Button>
      </div>
    </section>
  );
}
