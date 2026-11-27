// Application 360 in the web app (screen "Application 360 (existing, extended)", priority 1): the hub for one
// application, where people do its work (D-36). pages/Application360.tsx keeps the summary, which also runs in the
// harness on fixture JSON; in the web app it wraps the page in <App360Scope> (context.tsx), adds the section bar and the
// summary's actions from summary.tsx, and renders <App360Panels tab> for every other section.
//
// Sections (?tab=<key>, the route stays /app/records/business-application/<id>; logic.ts APP360_TABS):
//   summary     the facts, the lifecycle strip, and the panels with their actions: propose an owner or a successor,
//               end an assignment, record a leave, hand over; link a document, confirm it current or still true;
//               request an architecture review, complete a review; plan a change, record a deployment
//   lifecycle   the stepper, each gate met or held with the action that fixes each requirement, and retirement
//               (lifecycle.tsx)
//   tier        the latest tier assessment, Microsoft's words for the tier, what it needs, the tier decision (tier.tsx)
//   data        the sensitive-data answer, the data owner, data-use decisions with their yeses, data links (data.tsx)
//   work        every change, deployment (route, flags, gate level) and review of the application (work.tsx)
//   decisions   the approval requests open on its decisions, and every decision about it (decisions.tsx)
//   evidence    AI activity on the application and the evidence pack in numbers (evidence.tsx)
// Every write goes through the shared side sheet (sheets.tsx and the wave 1 sheets); the engine checks each. Actions
// show only when whoami gives the person the tool and, for owner and decision writes, when their roles or owner roles
// fit (context.tsx, logic.ts proposableTypes); the engine still decides.
//
// Design choices made under D-41 (recorded here and in the lead's report):
//   - Sections, not one long page. The hub holds about 20 panels; sections keep each use case one click away and the
//     address names the section, so Back and a shared link land on it. The summary keeps the panels people open first
//     (owners, documents, reviews, deployments, changes) with their actions, and a one-line lifecycle strip, so the
//     demo's "Go-live gate not met, confirm the runbook current" happens on the summary.
//   - A gate that is passed still shows its requirements now: a runbook that went stale after go-live shows the gate
//     as "passed, not met now", not as passed and clean.
//   - Owner-assigned requirements, which get_requirements does not list, are worked out from the owners: the role has
//     a current holder. get_requirements names no requirement key, so its rows are matched to the configured rows by
//     gate, kind and type.
//   - Which decision types a person may propose follows the decision rights in force: a responsible, accountable or
//     consulted row that names a product role they hold or an owner role they hold on the application (the engine's
//     proposal rights). The engine still checks.
//   - Hand over (graduation) is one ownership decision with the new holder and deputy of one owner role, ending the
//     ones they replace, so the approvers answer once.
//   - A tier decision names the tier it sets; the outcome (confirm, raise, lower) follows from the tier against the one
//     the latest answers compute.
//   - Retirement starts with a portfolio decision with the outcome retire and ends with the retirement decision with the
//     outcome complete (P09); the retirement tracker page holds the steps between.
//   - Approve and reject stay on the decision page (D-38); this page links each request there.
import type { ReactNode } from 'react';
import { DataSection } from './data';
import { DecisionsSection } from './decisions';
import { EvidenceSection } from './evidence';
import { LifecycleSection } from './lifecycle';
import type { App360Tab } from './logic';
import { TierSection } from './tier';
import { WorkSection } from './work';

export { App360Scope, ReadDedupe, useApp360 } from './context';
export { LifecycleStrip } from './lifecycle';
export { tabOf, TAB_PATTERN, portfolioPathOf, deploymentExtras, changeStamp, type App360Tab } from './logic';
export { tabHref } from './nav';
export { ChangeTypeBadge, CriticalityBadge, criticalityTone } from './parts';
export {
  App360TabBar,
  ChangesPanelActions,
  DeploymentsPanelActions,
  DocumentRowActions,
  DraftBanner,
  OwnerRowActions,
  OwnersPanelActions,
  ProposalNotice,
  ReviewRowActions,
  ReviewsPanelActions,
} from './summary';

// The section the address names, other than the summary, which Application360.tsx renders itself.
export function App360Panels({ tab }: { tab: App360Tab }): ReactNode {
  switch (tab) {
    case 'lifecycle':
      return <LifecycleSection />;
    case 'tier':
      return <TierSection />;
    case 'data':
      return <DataSection />;
    case 'work':
      return <WorkSection />;
    case 'decisions':
      return <DecisionsSection />;
    case 'evidence':
      return <EvidenceSection />;
    default:
      return null;
  }
}
