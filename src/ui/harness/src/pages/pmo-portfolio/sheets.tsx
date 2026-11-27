// The PMO and portfolio pages' own writes, each in the shared side sheet (components/actions.tsx). The engine checks
// every one exactly as it checks an AI client's write, and its answer shows in the sheet.
//   PortfolioSheet          save_portfolio: a new portfolio, or a change to one. The platform admin creates, moves and
//                           names the accountable person (found by name, never typed as an ID); the PMO edits the name,
//                           description and level.
//   PortfolioActiveSheet    save_portfolio isActive: retire a portfolio, or bring it back (platform admin)
//   ProposeDecisionSheet    save_decision_proposal: a decision of one type about an application, with its outcome
//                           chosen up front (a portfolio decision to retire, the retention decision, retirement
//                           complete). It is saved as the person's proposal. Wave 4b: the sheet then closes and
//                           asks "Send it for approval now?" with the engine's answer on top, as every proposal sheet
//                           does, in place of moving to the decision page; Cancel leaves it proposed for that page. The
//                           pages keep it mounted (Sticky) after the record reads again, so the follow-up stays.
//   RetireComponentSheet    link_component outcome retired: a component the gap check no longer sees is gone (P09 step 6)
import { Archive, ArchiveRestore, Pencil, Plus } from 'lucide-react';
import { useState, type ReactElement } from 'react';
import { goTo } from '../../app/links';
import { ActionSheet, Field, type FieldControl } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { PersonSearchField } from '../../components/people';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { PortfolioItem, PrincipalRef } from '../../data/contracts';
import { fmtDateTime } from '../../data/format';
import { useListValues, useOutcomes } from '../../data/labels';
import { proposalHandOff } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { buildPortfolioInput, formOfPortfolio, isProblem, pathText, portfolioTree, withDescendants, type CardComponent, type PortfolioForm } from './logic';

const NONE = '__none__';

function triggerProps(p: FieldControl) {
  return { id: p.id, 'aria-describedby': p['aria-describedby'], 'aria-invalid': p['aria-invalid'] };
}

// ---------- save_portfolio ----------

export function PortfolioSheet(props: {
  // The portfolio to change, or null for a new one.
  existing: PortfolioItem | null;
  // Every portfolio, for the parent choice.
  portfolios: readonly PortfolioItem[];
  // The person may move it and name the accountable person (the platform admin). The PMO edits the rest.
  fullRights: boolean;
  // A new portfolio's parent, when it is created from a parent's page.
  parentPortfolioId?: string;
  trigger?: ReactElement;
}) {
  const { existing } = props;
  const levels = useListValues('portfolio-level');
  const [form, setForm] = useState<PortfolioForm>(() => formOfPortfolio(existing, props.parentPortfolioId));
  const set = <K extends keyof PortfolioForm>(key: K, value: PortfolioForm[K]) => setForm(f => ({ ...f, [key]: value }));
  // A portfolio never sits inside itself or one of its own children.
  const blocked = existing ? withDescendants(props.portfolios, existing.portfolioId) : new Set<string>();
  const parents = portfolioTree(props.portfolios.filter(p => p.isActive || p.portfolioId === form.parentPortfolioId))
    .filter(r => !blocked.has(r.item.portfolioId.toLowerCase()));
  const accountable: PrincipalRef | null = form.accountable ? { objectId: form.accountable.objectId, displayLabel: form.accountable.displayLabel } : null;

  return (
    <ActionSheet
      tool="save_portfolio" site="pages/pmo-portfolio/sheets#PortfolioSheet"
      title={existing ? `Edit ${existing.name}` : 'New portfolio'}
      description={existing
        ? 'The engine checks the change. A portfolio scopes approval rules, decision rights and access, so moving it or naming another accountable person is the platform admin\'s.'
        : 'A new portfolio in the tree. Applications move into it on their own records; the portfolio starts empty.'}
      trigger={props.trigger ?? (
        <Button variant={existing ? 'outline' : 'default'}>
          {existing ? <Pencil aria-hidden /> : <Plus aria-hidden />}
          {existing ? 'Edit' : 'New portfolio'}
        </Button>
      )}
      onOpenChange={open => { if (open) setForm(formOfPortfolio(existing, props.parentPortfolioId)); }}
      input={() => {
        const built = buildPortfolioInput(form, existing, props.fullRights);
        return isProblem(built) ? built : built;
      }}
      submitLabel={existing ? 'Save' : 'Create'}
      preview
      onDone={data => { if (!existing && data.portfolioId) goTo({ page: 'portfolio', id: data.portfolioId.toLowerCase() }); }}
    >
      <Field label="Name" name="name" required hint="Two portfolios under the same parent never share a name.">
        {p => <Input {...p} maxLength={200} value={form.name} onChange={e => set('name', e.target.value)} />}
      </Field>
      <Field
        label="Description"
        name="description"
        hint={existing ? 'What the portfolio holds. Leave it empty to keep the description as it is.' : 'Optional. What the portfolio holds.'}
      >
        {p => <Textarea {...p} rows={3} maxLength={2000} value={form.description} onChange={e => set('description', e.target.value)} />}
      </Field>
      <Field label="Level" name="levelKey" hint="From the portfolio level list in the configuration.">
        {p => (
          <Select value={form.levelKey || NONE} onValueChange={v => set('levelKey', v === NONE ? '' : v)} disabled={!levels.loaded}>
            <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder={levels.loaded ? 'Choose' : 'Reading the levels'} /></SelectTrigger>
            <SelectContent>
              <SelectItem value={NONE} disabled={!!existing?.level}>{existing?.level ? 'Keep the level' : 'No level'}</SelectItem>
              {levels.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
            </SelectContent>
          </Select>
        )}
      </Field>
      {props.fullRights ? (
        <>
          <Field label="Sits inside" name="parentPortfolioId" hint="Moving a portfolio moves its applications' scope with it: the rules of the new parent apply to them.">
            {p => (
              <Select value={form.parentPortfolioId || NONE} onValueChange={v => set('parentPortfolioId', v === NONE ? '' : v)}>
                <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue /></SelectTrigger>
                <SelectContent className="max-h-[320px]">
                  <SelectItem value={NONE}>The top of the tree</SelectItem>
                  {parents.map(r => (
                    <SelectItem key={r.item.portfolioId} value={r.item.portfolioId}>
                      {`${'  '.repeat(r.depth)}${r.item.name}`}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </Field>
          <PersonSearchField
            label="Accountable person"
            name="accountablePrincipalObjectId"
            value={accountable}
            onChange={person => set('accountable', person ? { objectId: person.objectId, displayLabel: person.displayLabel } : null)}
            hint={accountable
              ? 'The person who answers for the portfolio. Choose someone else to change it, or clear it.'
              : 'The person who answers for the portfolio. Type two or more letters of the name.'}
            activeOnly
          />
        </>
      ) : existing ? (
        <p className="m-0 text-xs leading-relaxed text-navy-3">
          Where the portfolio sits and who is accountable for it are the platform admin's to change.
        </p>
      ) : null}
    </ActionSheet>
  );
}

export function PortfolioActiveSheet({ portfolio }: { portfolio: PortfolioItem }) {
  const retire = portfolio.isActive;
  return (
    <ActionSheet
      tool="save_portfolio" site="pages/pmo-portfolio/sheets#PortfolioActiveSheet"
      title={retire ? `Retire ${portfolio.name}` : `Bring back ${portfolio.name}`}
      description={retire
        ? 'A retired portfolio leaves the lists and the choices. Its applications keep their history and their place until they move.'
        : 'The portfolio shows in the lists and the choices again.'}
      trigger={(
        <Button variant="outline">
          {retire ? <Archive aria-hidden /> : <ArchiveRestore aria-hidden />}
          {retire ? 'Retire' : 'Bring back'}
        </Button>
      )}
      input={() => ({ portfolioId: portfolio.portfolioId, isActive: !retire })}
      submitLabel={retire ? 'Retire' : 'Bring back'}
      submitTone={retire ? 'destructive' : 'default'}
      preview
      confirm={{
        title: retire ? `Retire ${pathText(portfolio)}?` : `Bring back ${pathText(portfolio)}?`,
        body: retire
          ? `It holds ${portfolio.counts.applications} ${portfolio.counts.applications === 1 ? 'application' : 'applications'}. They stay where they are.`
          : undefined,
        confirmLabel: retire ? 'Retire' : 'Bring back',
        tone: retire ? 'destructive' : 'default',
      }}
    />
  );
}

// ---------- save_decision_proposal ----------

export function ProposeDecisionSheet(props: {
  decisionTypeKey: string;
  typeLabel: string;
  subjectId: string;
  subjectLabel: string;
  triggerLabel: string;
  // The outcome the sheet starts on, such as retire; the person may choose another the type allows.
  outcome?: string;
  statement?: string;
  // The accepted decision of the same type the new one replaces once accepted.
  supersedes?: { decisionId: string; label: string } | null;
  // What happens once it is accepted, in a sentence.
  effect?: string;
  variant?: 'default' | 'outline';
  size?: 'sm' | 'default';
}) {
  const outcomes = useOutcomes(props.decisionTypeKey);
  const [statement, setStatement] = useState(props.statement ?? '');
  const [context, setContext] = useState('');
  const [outcome, setOutcome] = useState(props.outcome ?? '');
  const [reason, setReason] = useState('');
  const [option, setOption] = useState('');
  const [whyNot, setWhyNot] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const reset = () => {
    setStatement(props.statement ?? '');
    setContext('');
    setOutcome(props.outcome ?? '');
    setReason('');
    setOption('');
    setWhyNot('');
  };
  const allowed = outcomes.options.some(o => o.key === outcome);
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/pmo-portfolio/sheets#ProposeDecisionSheet"
      title={`Propose a ${props.typeLabel.toLowerCase()} decision`}
      description={(
        <>
          About {props.subjectLabel}. It is saved as your proposal, and the next sheet asks whether to send it for approval
          now; nobody is asked before that.{props.effect ? ` ${props.effect}` : ''}
        </>
      )}
      size="wide"
      open={open}
      trigger={(
        // The button names its subject too, so several on one page each say which row they are for (a11y, fix round 1).
        <Button size={props.size ?? 'sm'} variant={props.variant ?? 'outline'} aria-label={`${props.triggerLabel}: ${props.subjectLabel}`}>
          <Plus aria-hidden />
          {props.triggerLabel}
        </Button>
      )}
      input={() => {
        const s = statement.trim();
        const c = context.trim();
        const r = reason.trim();
        const o = option.trim();
        const w = whyNot.trim();
        if (!s) return { problem: 'Say what is decided, in one sentence.', field: 'statement' };
        if (!c) return { problem: 'Give the context: the facts the decider needs.', field: 'context' };
        if (!outcome || !allowed) return { problem: 'Choose the outcome you propose.', field: 'proposedOutcomeKey' };
        if (!r) return { problem: 'Say why.', field: 'reason' };
        if ((o && !w) || (!o && w)) return { problem: 'An option weighed needs the option and why it was not chosen.', field: 'optionsConsidered' };
        if (outcomes.needsOptions && !o) return { problem: 'This decision type needs at least one option weighed.', field: 'optionsConsidered' };
        return {
          decisionTypeKey: props.decisionTypeKey,
          subjectKind: 'business-application' as const,
          subjectId: props.subjectId,
          statement: s,
          context: c,
          proposedOutcomeKey: outcome,
          reason: r,
          ...(o ? { optionsConsidered: [{ option: o, whyNot: w }] } : {}),
          ...(props.supersedes ? { supersedesDecisionId: props.supersedes.decisionId } : {}),
        };
      }}
      submitLabel="Propose"
      preview
      // The form starts from the props each time it opens, so a statement read after the page mounted is there.
      onOpenChange={next => { if (next) reset(); setOpen(next); if (!next) reset(); }}
      onDone={(_, envelope) => {
        const handed = proposalHandOff(envelope);
        if (!handed) return;
        follow.start(handed);
        setOpen(false);
        reset();
      }}
    >
      <Field label="Statement" name="statement" required hint="One sentence: what is decided.">
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} />}
      </Field>
      <Field label="Proposed outcome" name="proposedOutcomeKey" required>
        {p => outcomes.options.length ? (
          <Select value={allowed ? outcome : ''} onValueChange={setOutcome}>
            <SelectTrigger {...triggerProps(p)} className="w-full"><SelectValue placeholder="Choose an outcome" /></SelectTrigger>
            <SelectContent>
              {outcomes.options.map(x => <SelectItem key={x.key} value={x.key}>{x.label}</SelectItem>)}
            </SelectContent>
          </Select>
        ) : (
          <Muted>{outcomes.loading ? 'Reading the outcomes this decision type allows.' : 'The outcomes of this decision type could not be read. Close the sheet and try again.'}</Muted>
        )}
      </Field>
      <Field label="Context" name="context" required hint="The facts the decider needs: use, cost, dependants, the data it holds.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      <div className="grid grid-cols-2 gap-2 narrow:grid-cols-1">
        <Field label="An option weighed" name="optionsConsidered" hint={outcomes.needsOptions ? 'Required for this decision type.' : 'Optional.'}>
          {p => <Input {...p} required={false} maxLength={500} value={option} onChange={e => setOption(e.target.value)} />}
        </Field>
        <Field label="Why not">
          {p => <Input {...p} maxLength={1000} value={whyNot} onChange={e => setWhyNot(e.target.value)} />}
        </Field>
      </div>
      {props.supersedes && (
        <p className="m-0 text-xs leading-relaxed text-navy-3">
          Once accepted it supersedes {props.supersedes.label}, which stays in the decision log.
        </p>
      )}
    </ActionSheet>
    {follow.data && (
      <SubmitFollowUp
        decisionId={follow.data.decisionId}
        previous={follow.data.answer}
        subject={`The ${props.typeLabel.toLowerCase()} decision about ${props.subjectLabel}`}
        open={follow.open}
        onOpenChange={follow.onOpenChange}
      />
    )}
    </>
  );
}

// ---------- link_component (retired) ----------

export function RetireComponentSheet(props: {
  businessApplicationId: string;
  component: CardComponent;
  // The vanished claim the gap check left for it, resolved by the same call.
  claimId: string | null;
}) {
  const [note, setNote] = useState('');
  const c = props.component;
  const name = c.labelAsSeen || c.externalId;
  return (
    <ActionSheet
      tool="link_component" site="pages/pmo-portfolio/sheets#RetireComponentSheet"
      title={`Confirm ${name} is gone`}
      description="The component is marked retired, with today's date. It stays on record and nothing is deleted. Confirm it only once the item is removed in its own tool and the gap check no longer sees it."
      trigger={<Button size="sm" variant={props.claimId ? 'default' : 'outline'}>Confirm retired</Button>}
      input={() => ({
        businessApplicationId: props.businessApplicationId,
        outcome: 'retired' as const,
        technicalComponentId: c.technicalComponentId,
        ...(props.claimId ? { claimId: props.claimId } : {}),
        ...(note.trim() ? { note: note.trim() } : {}),
      })}
      submitLabel="Confirm retired"
      preview
      confirm={{
        title: `Mark ${name} retired?`,
        body: (
          <>
            {c.kind.label}{c.lastSeenOn ? `, last seen ${fmtDateTime(c.lastSeenOn)}` : ''}.
            {props.claimId ? ' The vanished claim the gap check opened for it closes too: its status becomes claimed.' : ' No vanished claim names it: the last gap check may still see it.'}
          </>
        ),
        confirmLabel: 'Mark retired',
      }}
      onOpenChange={open => { if (!open) setNote(''); }}
    >
      <Field label="Note" name="note" hint="Optional. Where it went, or the change that removed it.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}
