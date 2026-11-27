// The register extras and the wave 2 leftovers on existing pages (wave 3). No new page: the applications register moved
// here (ApplicationsRegister.tsx), and the other work lands on the pages that already exist.
//
// What this group built (from the backend slices' reports, document 24 B24-17 and B24-18):
//   Applications register   ApplicationsRegister.tsx: the owner filter (people search, or "Applications I own"; the
//                           Owners page links here with owner=), the registration filter with a count per meaning, and
//                           uses AI and build path (ai=, build=) read from each row's card while in use, with counts;
//                           drafts and archived rows marked in a mixed list. RegisterSheet.tsx: "Register an app that is
//                           already built" (save_application_draft with its owners and components), then the
//                           confirmation (submit_record on its ownership decision). "New request" opens the wizard.
//   Places                  retire a target with a reason and return it to service (save_deployment_target retire, on
//                           the target's page: deployments-places/sheets.tsx RetireTargetSheet); the state filter
//                           (state=active|retired) and a Retired figure; a retired target keeps its history
//   Deployments             the new-deployment form offers the targets in service only (state active)
//   Agents                  list_agents (tool 93) in place of the four reads per agent; "Request a security review"
//                           (request_security_review, tool 94: ai-audit/agentSheets.tsx SecurityReviewSheet); the go-live
//                           requirements with their guideline marks; each agent's data uses, linked to the Data page
//   AI activity             the clients view (get_ai_activity clients), each number opening its rows, and the Tool
//                           Policy per role (get_role_access), as panels; no route change
//   Dependency map          component and data place nodes (mapParts.ts), from the application card, with the relation
//                           words "Reads data from", "Writes data to", "Reads and writes data in"; a list of them too
//   Timeline and Overview   the portfolio filter (?portfolio=) on the Timeline, shared with the Overview's picker; the
//                           Overview's change windows and flagged deployments follow the portfolio; a data gaps tile
//   Owner gaps              Overview, Owners and Portfolios read the server's one owner gap count for the same scope
//                           (get_portfolio_summary ownerGaps, list_owner_assignments ownerGaps, list_portfolios
//                           counts.ownerGaps); a person's page links to the register with owner=
//
// Design choices (D-41), each recorded with its reason in the header of the page it shapes: the register's counts are
// the rows each filter gives; the business owner is required on the register sheet; components go in the same write as
// the draft; the Agents page offers the security review in place of the architecture review; the map's parts sit one hop
// away on the "What it needs" side; the Timeline narrows by the portfolio (portfolioId, wave 4).
//
// What the backend lacked in wave 3, and where it stands after wave 4 (B24-21, B24-22):
//   - find_applications now filters on uses AI and build path: the register sends ai=yes|no and build=<key> to the tool
//     and counts each answer with the tool's totals. "Not stated" (no answer yet) still reads one card per row, up to
//     100, because the tool cannot filter on a missing answer. get_portfolio_summary still cannot group by them.
//   - find_applications rows still carry no registration status; a mixed list reads the drafts and the archived rows
//     again to mark them.
//   - get_dependency_impact now starts from a component or a data location ("What depends on it?", WhatDependsOnIt.tsx
//     in data-records). The map itself still starts from an application and reads the card for its one hop of parts.
//   - get_change_schedule and get_deployments take portfolioId: the Timeline and the Overview send it.
//   - get_audit_trail takes clientApp: an AI client's calls open its entries in the audit trail (client=).
//   - list_agents' decisionRef carries number, type, status and link, which data/contracts.ts types as an ID only.
export { ApplicationsList } from './ApplicationsRegister';
export { RegisterBuiltAppSheet, SavedDraftNotice, type SavedDraft } from './RegisterSheet';
export { GuidelineMark } from './parts';
