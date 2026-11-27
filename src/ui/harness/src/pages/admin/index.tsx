// The Govern pages other than Configuration (screens "Roles and access", "Guidelines and playbook", "Measures and
// slide-back signals" and "Admin", priority 3). Each page lives in its own file; the shell imports them from here.
//   /app/roles?role=<key>     RolesPage.tsx: each role with its Entra groups and their members (read live), its
//                             workload grants by expiry, the tools it may call with how far an AI client may go, its
//                             decision rights and approval rules, and the separation rules in one sentence each
//                             (get_role_access, list_reviews). Platform admin: run the binding check
//                             (check_role_bindings). Configuration admin: propose a binding change
//                             (ConfigurationChangeSheet from ../configuration/sheets.tsx)
//   /app/guidelines?g=<G17>   GuidelinesPage.tsx: the guidelines with the organization's own copy and whether each is
//                             adopted, the playbook chapters by track, the build paths and the sanctioned clients
//                             (list_guidelines). A guideline chip anywhere links here (links.ts guidelineHref), and the
//                             page opens at that guideline
//   /app/measures             MeasuresPage.tsx: the 9 measures and 10 slide-back signals over six windows, each opening
//                             to the rows behind it (get_measures)
//   /app/admin                AdminPage.tsx: the host settings that are safe to show, the configuration in force and its
//                             health, the Tool Policy as a list, each client and what it did recently, the notices the
//                             daily check sent, the approval outbox, and "Run the daily check now"
//                             (get_admin_status, get_configuration_health; run_due_state_refresh, person only, platform
//                             admin). Platform admin, configuration admin and auditor only
// Every page is read only except the two actions named, which go through shared side sheets.
//
// Design choices (D-41):
//   - Roles: the list and one role are two views of one address (?role=). One role is a page of its own with "All
//     roles" to go back, because its tool list runs to dozens of rows and reads badly beside the list on a phone. Holders
//     are read only for the open role (includeHolders), so the list costs no directory read per role.
//   - Roles: the binding check's findings are written from its rows, not from its finding sentences, because those name
//     role keys and object IDs. It runs on a button (a read that logs the check), and "Run it again" reads it afresh.
//   - Roles: "Propose a binding change" starts from the role's binding rows as get_configuration gives them
//     (role-group-binding), because get_role_access gives no row key; a role with no binding starts a new row named
//     "<role>-group". The configuration change sheet lets the person change the group before the check.
//   - Roles: the demo persona switch (D-39) is never listed as a role. In a demo install a line under the header says
//     what it is, so a presenter is not asked why it is missing.
//   - Tools are named in words ("Save data use") with their number, not by their API name, so no internal key shows.
//   - Guidelines: the open guideline always shows, whatever the search or filter, so a chip never lands on an empty
//     list. Its "used by" counts open to the rule rows that name it (get_configuration of approval rules, gate
//     requirements and protected fields, read only for the open one).
//   - Guidelines: compact guideline links (parts.tsx GuidelineLink) in long lists; the full GuidelineChip with its rule
//     text stays for refusals, because it reads the configuration once per chip.
//   - Measures: a window switch (7, 30 or 90 days, ?window=) beside the six windows the design names; percent measures
//     share a 0 to 100 scale so windows compare honestly. The figures count measures better, worse or with nothing to
//     compare since the window before, and each narrows the grid (?trend=). The rows behind a figure open in a panel
//     at the top (?measure= or ?signal=), so Back closes it.
//   - Admin: the run's full answer (reviews opened, documents stale, notices, failures) stays on the page after the
//     sheet closes, under "The daily check"; the sheet itself shows the counts, as for any write. A confirm step comes
//     before the run, because it sends reminders to people.
//   - Admin: a client's calls, refusals and drafts open the AI activity page filtered by that client. The web app's own
//     calls do not drill, because no list filters the audit trail by client yet.
//   - Admin: the binding check is not repeated here; a panel points to Roles and access, where the platform admin runs it.
export { AdminPage } from './AdminPage';
export { GuidelinesPage } from './GuidelinesPage';
export { MeasuresPage } from './MeasuresPage';
export { RolesPage } from './RolesPage';
