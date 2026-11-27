// The gate that holds a proposed decision (get_decision gate; OP-07, G-14). The engine accepts a gate's decision only
// when the gate is ready, even once every approval is in, so a page that says "every approval is in" next to a
// decision still proposed says why here, and what is not met.
import { TriangleAlert } from 'lucide-react';
import type { DecisionGate, DecisionSubjectKind } from '../data/contracts';
import { useLabels, type Labels } from '../data/labels';
import { recordHref } from '../app/links';
import { GuidelineChip } from './actions';
import { TextLink } from './blocks';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';

function requirementText(u: DecisionGate['unmet'][number], ownerRoles: Labels, subjectKind: DecisionSubjectKind | undefined): string {
  if (u.requirementKind === 'owner-assigned' && u.requiredOwnerRoleKey) {
    const role = ownerRoles.label(u.requiredOwnerRoleKey).toLowerCase();
    return subjectKind === 'intake-request'
      ? `No ${role} is assigned. A request for a new application has no owner assignments yet, so this stays unmet until the application has one.`
      : `No ${role} is assigned to the application. Propose an owner change to name one.`;
  }
  return u.label;
}

function openUseStateText(state: string): string {
  switch (state) {
    case 'needed': return 'needs a decision';
    case 'proposed': return 'its decision waits for answers';
    case 'no-one-to-ask': return 'its domain has no data owner to ask';
    case 're-decide': return 'needs a new decision';
    case 'denied': return 'denied';
    case 'breaks-rule': return 'breaks a rule';
    default: return state.replace(/-/g, ' ');
  }
}

export function GateHold({ gate, approvalsIn, subjectKind }: { gate: DecisionGate | null | undefined; approvalsIn: boolean; subjectKind?: DecisionSubjectKind }) {
  const ownerRoles = useLabels('owner-role');
  if (!gate || gate.ready) return null;
  return (
    <Alert variant="warning">
      <TriangleAlert aria-hidden />
      <AlertTitle>
        {approvalsIn
          ? `The approvals are in, and the ${gate.label.toLowerCase()} gate holds the decision`
          : `The ${gate.label.toLowerCase()} gate must also be ready`}
      </AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-2">
          <p className="m-0">
            {approvalsIn
              ? 'The engine accepts this decision only when every requirement of the gate is met. It stays proposed until then, and the engine accepts it as soon as the last one is met. Not met yet:'
              : 'Once the approvals are in, the engine accepts this decision only if every requirement of the gate is met too. Otherwise it stays proposed until the last one is met, and the engine then accepts it. Not met yet:'}
          </p>
          <ul className="m-0 flex list-disc flex-col gap-1.5 pl-5">
            {gate.unmet.map(u => (
              <li key={u.requirementKey}>
                <span className="flex flex-col items-start gap-1">
                  <span>{requirementText(u, ownerRoles, subjectKind)}</span>
                  {u.openUses && u.openUses.length > 0 && (
                    <ul className="m-0 flex list-none flex-col gap-1 p-0" aria-label="Data uses not decided yet">
                      {u.openUses.map(o => (
                        <li key={o.dataUseId} className="text-sm">
                          <TextLink href={recordHref(o.recordUrl) ?? undefined}>{`${o.recordNumber ? `${o.recordNumber}: ` : ''}${o.label}`}</TextLink>
                          <span className="text-navy-2">{o.decisionId ? `, ${openUseStateText(o.state)}: answer its decision` : `, ${openUseStateText(o.state)}`}</span>
                        </li>
                      ))}
                    </ul>
                  )}
                  {u.guideline && <GuidelineChip number={u.guideline} guideline={u.guidelineRef} />}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </AlertDescription>
    </Alert>
  );
}
