// Intake and demand: the list (/app/intake?view=triage|demand|mine|drafts|all&q=<text>), the intake wizard for a new
// request (/app/intake/new) and on an existing one (/app/records/intake-request/<id>/edit), and the intake request
// record (/app/records/intake-request/<id>). Tools: list_intake_requests, get_intake_request, check_duplicates,
// get_tier_questions, get_requirements, get_approval_status, list_approvals, whoami; save_intake_draft,
// answer_tier_questions, submit_record, withdraw_record, save_link, answer_approval.
// The pages live in ./intake: ListPage.tsx, Wizard.tsx, RecordPage.tsx, with their shared parts (shared.tsx, sheets.tsx)
// and their logic (logic.ts, tested in logic.test.ts).
import type { RouteOf } from '../app/routes';
import { IntakeListPage } from './intake/ListPage';
import { IntakeRecordPage } from './intake/RecordPage';
import { IntakeEditPage, IntakeNewPage } from './intake/Wizard';

export function IntakeList({ route }: { route: RouteOf<'intake'> }) {
  return <IntakeListPage route={route} />;
}

export function IntakeNew() {
  return <IntakeNewPage />;
}

export function IntakeEdit({ intakeRequestId }: { intakeRequestId: string }) {
  return <IntakeEditPage intakeRequestId={intakeRequestId} />;
}

export function IntakeRecord({ intakeRequestId }: { intakeRequestId: string }) {
  return <IntakeRecordPage intakeRequestId={intakeRequestId} />;
}
