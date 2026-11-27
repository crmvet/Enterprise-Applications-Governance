// The Agents page's writes, each in the shared side sheet (components/actions.tsx). The engine checks every one exactly
// as it checks an AI client's write, and the sheet shows its answer.
//   ProposeAutonomySheet     save_decision_proposal, type autonomy-level, with the level it stores once people accept it
//                            (outcome agree). Saved as the person's proposal: nobody is asked until they submit it from
//                            the decision page, where the security reviewer then decides (Balanced).
//   AgentDecisionSheet       save_decision_proposal, type flagged-agent: keep, reassign, consolidate or retire an agent.
//                            The AI lead decides (Balanced).
//   SecurityReviewSheet      request_security_review (tool 94): a request, never a decision. The security reviewer's
//                            queue gets it; an open review is returned instead of a second.
// Wave 4: the two proposals open the submit follow-up right after they are saved ("Send it for
// approval now?", with the engine's answer), as the data sheets do; Cancel leaves the decision proposed for its page.
import { Flag, Gauge, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Input } from '../../components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import { Textarea } from '../../components/ui/textarea';
import type { WriteInput } from '../../components/actions';
import { useListValues, useOutcomes } from '../../data/labels';
import { agreeOutcome, autonomyProposal, FLAGGED_TYPE, AUTONOMY_TYPE } from './agentsLogic';
import { isIsoDate } from './auditLogic';
import { readEngineAnswer } from '../../data/writes';
import { SubmitFollowUp, useFollowUp } from '../data/sheetParts';
import { ReviewDueHint } from '../../components/reviewDue';

export interface AgentRef {
  id: string;
  name: string;
}

function ChoiceField(props: { label: string; name: string; value: string; onChange: (v: string) => void; options: { key: string; label: string }[]; placeholder: string; hint?: string; loading?: boolean }) {
  return (
    <Field label={props.label} name={props.name} required hint={props.hint}>
      {p => props.options.length ? (
        <Select value={props.value} onValueChange={props.onChange}>
          <SelectTrigger id={p.id} aria-describedby={p['aria-describedby']} aria-invalid={p['aria-invalid']} className="w-full">
            <SelectValue placeholder={props.placeholder} />
          </SelectTrigger>
          <SelectContent>
            {props.options.map(o => <SelectItem key={o.key} value={o.key}>{o.label}</SelectItem>)}
          </SelectContent>
        </Select>
      ) : (
        <Muted>{props.loading ? 'Reading the choices the configuration allows.' : 'The choices could not be read. Close the sheet and try again.'}</Muted>
      )}
    </Field>
  );
}

// ---------- autonomy level ----------

export function ProposeAutonomySheet({ agent, size = 'sm' }: { agent: AgentRef; size?: 'sm' | 'xs' }) {
  const levels = useListValues('autonomy-level');
  const outcomes = useOutcomes(AUTONOMY_TYPE);
  const [level, setLevel] = useState('');
  const [context, setContext] = useState('');
  const [reason, setReason] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  const reset = () => { setLevel(''); setContext(''); setReason(''); };
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/ai-audit/agentSheets#ProposeAutonomySheet"
      title={`Propose an autonomy level: ${agent.name}`}
      description="How much the agent may do on its own. It is saved as your proposal; nobody is asked until you send it, right after saving or later from its decision page. Once people agree it, the level is stored on the agent."
      trigger={(
        <Button size={size} variant="outline" aria-label={`Propose autonomy level: ${agent.name}`}>
          <Gauge aria-hidden />
          Propose autonomy level
        </Button>
      )}
      input={() => {
        const built = autonomyProposal(
          { levelKey: level, levelLabel: levels.label(level), agentName: agent.name, context, reason },
          agent.id,
          agreeOutcome(outcomes.options),
        );
        if ('problem' in built) return built;
        // autonomyLevelKey is in the tool's schema; the typed input of data/contracts.ts does not list it yet.
        const input: WriteInput<'save_decision_proposal'> & { autonomyLevelKey: string } = built;
        return input;
      }}
      submitLabel="Propose"
      preview
      open={open}
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      onDone={(data, envelope) => {
        if (!data.recordId) return;
        follow.start({ decisionId: data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) });
        setOpen(false);
        reset();
      }}
    >
      <ChoiceField
        label="Autonomy level"
        name="autonomyLevelKey"
        value={level}
        onChange={setLevel}
        options={levels.options}
        placeholder="Choose a level"
        hint="From the autonomy level list of the configuration in force."
        loading={levels.loading}
      />
      <Field label="What the agent does" name="context" required hint="What it acts on, and what it may do without a person.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Why this level" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      {!outcomes.loading && !agreeOutcome(outcomes.options) && (
        <Muted>The autonomy level decision type has no "agree" outcome in the configuration in force, so a level cannot be proposed.</Muted>
      )}
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The autonomy level of ${agent.name}`} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

// ---------- a decision on a flagged agent ----------

export function AgentDecisionSheet({ agents, fixed, size = 'sm', label = 'Decide on this agent' }: { agents: AgentRef[]; fixed?: AgentRef; size?: 'sm' | 'xs'; label?: string }) {
  const outcomes = useOutcomes(FLAGGED_TYPE);
  const [agentId, setAgentId] = useState(fixed?.id ?? '');
  const [outcome, setOutcome] = useState('');
  const [statement, setStatement] = useState('');
  const [context, setContext] = useState('');
  const [reason, setReason] = useState('');
  const [open, setOpen] = useState(false);
  const follow = useFollowUp();
  // The agent as it was proposed: the form resets once the follow-up opens.
  const [followName, setFollowName] = useState('');
  const reset = () => { setAgentId(fixed?.id ?? ''); setOutcome(''); setStatement(''); setContext(''); setReason(''); };
  const agent = fixed ?? agents.find(a => a.id === agentId) ?? null;
  const outcomeLabel = outcomes.options.find(o => o.key === outcome)?.label ?? '';
  const suggested = agent && outcomeLabel ? `${outcomeLabel} ${agent.name}` : '';
  return (
    <>
    <ActionSheet
      tool="save_decision_proposal" site="pages/ai-audit/agentSheets#AgentDecisionSheet"
      title={fixed ? `Decide on an agent: ${fixed.name}` : 'Decide on a flagged agent'}
      description="Keep, reassign, consolidate or retire an agent that was flagged, for example one with no owner or one Microsoft flags as inactive. It is saved as your proposal; nobody is asked until you send it, right after saving or later from its decision page."
      size="wide"
      trigger={(
        <Button size={size} variant="outline">
          <Flag aria-hidden />
          {label}
        </Button>
      )}
      input={() => {
        if (!agent) return { problem: 'Choose the agent.', field: 'subjectId' };
        if (!outcome) return { problem: 'Choose what you propose: keep, reassign, consolidate or retire.', field: 'proposedOutcomeKey' };
        const c = context.trim();
        const r = reason.trim();
        const s = statement.trim() || suggested;
        if (!c) return { problem: 'Say why the agent was flagged.', field: 'context' };
        if (!r) return { problem: 'Say why this is the right answer.', field: 'reason' };
        return {
          decisionTypeKey: FLAGGED_TYPE,
          subjectKind: 'business-application' as const,
          subjectId: agent.id,
          statement: s.slice(0, 500),
          context: c,
          proposedOutcomeKey: outcome,
          reason: r,
        };
      }}
      submitLabel="Propose"
      preview
      open={open}
      onOpenChange={next => { setOpen(next); if (!next) reset(); }}
      onDone={(data, envelope) => {
        if (!data.recordId) return;
        setFollowName(agent?.name ?? 'the agent');
        follow.start({ decisionId: data.recordId, answer: readEngineAnswer('save_decision_proposal', envelope) });
        setOpen(false);
        reset();
      }}
    >
      {!fixed && (
        <ChoiceField
          label="Agent"
          name="subjectId"
          value={agentId}
          onChange={setAgentId}
          options={agents.map(a => ({ key: a.id, label: a.name }))}
          placeholder="Choose an agent"
        />
      )}
      <ChoiceField
        label="What you propose"
        name="proposedOutcomeKey"
        value={outcome}
        onChange={setOutcome}
        options={outcomes.options}
        placeholder="Choose an outcome"
        loading={outcomes.loading}
      />
      <Field label="Statement" name="statement" hint={suggested ? `Leave it empty to use "${suggested}".` : 'One sentence: what is decided.'}>
        {p => <Input {...p} maxLength={500} value={statement} onChange={e => setStatement(e.target.value)} placeholder={suggested} />}
      </Field>
      <Field label="Why it was flagged" name="context" required hint="What was found: no owner, unused for months, a duplicate of another agent, or a flag from Microsoft.">
        {p => <Textarea {...p} rows={4} maxLength={4000} value={context} onChange={e => setContext(e.target.value)} />}
      </Field>
      <Field label="Reason" name="reason" required>
        {p => <Textarea {...p} rows={3} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
    </ActionSheet>
    {follow.data && <SubmitFollowUp decisionId={follow.data.decisionId} previous={follow.data.answer} subject={`The decision on ${followName}`} open={follow.open} onOpenChange={follow.onOpenChange} />}
    </>
  );
}

// ---------- a security review ----------

// request_security_review (tool 94): a request, never a decision. The engine opens a review of type security review,
// due, for the security reviewer the go-live gate asks; the review ends in a decision that people accept. An open one is
// returned, and nothing new is written.
export function SecurityReviewSheet({ agent, size = 'sm', label = 'Request a security review' }: { agent: AgentRef; size?: 'sm' | 'xs'; label?: string }) {
  const [reason, setReason] = useState('');
  const [dueOn, setDueOn] = useState('');
  const reset = () => { setReason(''); setDueOn(''); };
  return (
    <ActionSheet
      tool="request_security_review" site="pages/ai-audit/agentSheets#SecurityReviewSheet"
      title={`Request a security review: ${agent.name}`}
      description="A request, never a decision. The security reviewer gets it in their queue, and the review ends in a decision that people accept. If one is already open, the product returns it and opens no other."
      trigger={(
        <Button size={size} variant="outline" aria-label={`${label}: ${agent.name}`}>
          <ShieldCheck aria-hidden />
          {label}
        </Button>
      )}
      input={() => {
        const r = reason.trim();
        if (!r) return { problem: 'Say what the reviewer should look at, and why.', field: 'reason' };
        if (dueOn && !isIsoDate(dueOn)) return { problem: 'Give the date as a calendar date.', field: 'dueOn' };
        return { businessApplicationId: agent.id, reason: r, ...(dueOn ? { dueOn } : {}) };
      }}
      submitLabel="Request the review"
      preview
      onOpenChange={open => { if (!open) reset(); }}
    >
      <Field label="What to look at" name="reason" required hint="Such as the data it reads, the tools it may call, or what changed since the last review.">
        {p => <Textarea {...p} rows={4} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} />}
      </Field>
      <Field label="Wanted by" name="dueOn" hint={<ReviewDueHint />}>
        {p => <Input {...p} type="date" value={dueOn} onChange={e => setDueOn(e.target.value)} className="w-[200px]" />}
      </Field>
    </ActionSheet>
  );
}
