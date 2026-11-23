// How an engine meaning shows on screen.
//
// WORKING DEFAULT (open design question 1): the pages pick a tone and an icon from the fixed engine meaning,
// never from the label. The label a person reads always comes from configuration (the status row's label).
// If that question is answered no, this table is the one place the pages would change.
import type { DeploymentFlags, LifecycleMeaning, StatusMeaning } from './contracts';

export type Tone = 'success' | 'warning' | 'danger' | 'brand' | 'informative' | 'subtle';

export const toneOfMeaning: Record<StatusMeaning, Tone> = {
  // records (intake, change request)
  draft: 'informative',
  submitted: 'brand',
  'in-progress': 'brand',
  completed: 'success',
  rejected: 'danger',
  withdrawn: 'subtle',
  cancelled: 'subtle',
  // decisions
  proposed: 'warning',
  accepted: 'success',
  superseded: 'subtle',
  // registration
  registered: 'brand',
  archived: 'subtle',
  // approvals
  pending: 'warning',
  approved: 'success',
  expired: 'danger',
  'recommendation-only': 'informative',
  // documents
  current: 'success',
  stale: 'warning',
  missing: 'danger',
  // claims
  open: 'warning',
  claimed: 'success',
  dismissed: 'subtle',
  // deployments
  succeeded: 'success',
  'partly-succeeded': 'warning',
  failed: 'danger',
  'not-deployed': 'informative',
  // reviews
  due: 'warning',
  done: 'success',
  // configuration drafts
  activated: 'success',
  discarded: 'subtle',
};

// Lifecycle meanings carry no configured label of their own in the tool output, so the fixed engine words show.
export const lifecycleText: Record<LifecycleMeaning, string> = {
  'pre-live': 'Pre-live',
  live: 'Live',
  retired: 'Retired',
};

export const lifecycleTone: Record<LifecycleMeaning, Tone> = {
  'pre-live': 'brand',
  live: 'success',
  retired: 'subtle',
};

// The four engine flags on a deployment record, in the order the pages list them.
// WORKING DEFAULT (open design question 7): an AI-only code approval never counts as authorization,
// and the shipped gate default flags it. The page shows it as a flag like the other three.
export const flagText: Record<keyof DeploymentFlags, string> = {
  missingApproval: 'Missing approval',
  otherPath: 'Came another way',
  outsideWindow: 'Outside window',
  aiOnlyCodeApproval: 'AI-only code approval',
};

export const flagHelp: Record<keyof DeploymentFlags, string> = {
  missingApproval: 'No authorization where the gate policy expects one.',
  otherPath: 'The route is not an allowed route of the target\'s change path.',
  outsideWindow: 'Finished outside the linked change\'s window.',
  aiOnlyCodeApproval: 'The only code approval came from an AI. It never counts as authorization.',
};

export function activeFlags(flags: DeploymentFlags): (keyof DeploymentFlags)[] {
  return (Object.keys(flagText) as (keyof DeploymentFlags)[]).filter(k => flags[k]);
}
