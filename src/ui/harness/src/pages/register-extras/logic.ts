// Pure helpers for the applications register and its "Register an app already built" sheet, with no React in them, so
// the unit tests run them in Node (logic.test.ts).
//   USES_AI_PARAM, BUILD_PARAM            the page's own filters in the address: ai=yes|no|unknown, build=<list value key>
//   usesAiOf(card.usesAi)                 the uses AI answer of one application: yes, no or unknown (not stated)
//   keepRow(facts, ai, build)             true, false, or null while the application's card is still being read
//   scanCounts(facts)                     the counts behind the uses AI and build path filters, over the cards read
//   buildRegisterInput(form)              the save_application_draft input, or the problem to show first
// Fictional data only in the tests.
import type { PrincipalRef } from '../../data/contracts';
import type { WriteInput } from '../../data/writes';
import { isHttpsUrl, type Problem } from '../changes-records/logic';

export type { Problem } from '../changes-records/logic';
export { isProblem } from '../changes-records/logic';

// ---------- the page's own filters ----------

export type UsesAiFilter = 'yes' | 'no' | 'unknown';
export const USES_AI_FILTERS: readonly UsesAiFilter[] = ['yes', 'no', 'unknown'];
export const USES_AI_PARAM = /^(yes|no|unknown)$/;
export const BUILD_PARAM = /^[a-z][a-z0-9-]{0,63}$/;
// The value the build path filter takes for an application whose build path is not stated.
export const BUILD_NOT_STATED = 'not-stated';

export const USES_AI_TEXT: Record<UsesAiFilter, string> = {
  yes: 'Uses AI',
  no: 'Does not use AI',
  unknown: 'Not stated',
};

// The most rows the page reads application cards for, to apply the uses AI and build path filters. find_applications
// returns neither field, so the page reads each row's card (its smallest section) four at a time.
export const SCAN_LIMIT = 100;

export function usesAiOf(value: boolean | null | undefined): UsesAiFilter {
  return value === true ? 'yes' : value === false ? 'no' : 'unknown';
}

// What the page reads from one application card to filter by.
export interface CardFacts {
  usesAi: boolean | null;
  buildPath: { key: string; label: string } | null;
  // The registration meaning, so a draft or an archived row is marked in a mixed list.
  registration: string | null;
}

// True when the row passes the uses AI and build path filters, false when it does not, null while its card is unread.
export function keepRow(facts: CardFacts | undefined, ai: UsesAiFilter | undefined, build: string | undefined): boolean | null {
  if (!ai && !build) return true;
  if (!facts) return null;
  if (ai && usesAiOf(facts.usesAi) !== ai) return false;
  if (build && (facts.buildPath?.key ?? BUILD_NOT_STATED) !== build) return false;
  return true;
}

export interface ScanCounts {
  usesAi: Record<UsesAiFilter, number>;
  // Each build path seen, by count then label; the applications with none stated last.
  buildPaths: { key: string; label: string; count: number }[];
  read: number;
}

export function scanCounts(facts: CardFacts[]): ScanCounts {
  const usesAi: Record<UsesAiFilter, number> = { yes: 0, no: 0, unknown: 0 };
  const paths = new Map<string, { key: string; label: string; count: number }>();
  let none = 0;
  for (const f of facts) {
    usesAi[usesAiOf(f.usesAi)] += 1;
    if (f.buildPath) {
      const seen = paths.get(f.buildPath.key);
      if (seen) seen.count += 1;
      else paths.set(f.buildPath.key, { key: f.buildPath.key, label: f.buildPath.label, count: 1 });
    } else none += 1;
  }
  const buildPaths = [...paths.values()].sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
  if (none) buildPaths.push({ key: BUILD_NOT_STATED, label: 'Not stated', count: none });
  return { usesAi, buildPaths, read: facts.length };
}

// ---------- registration ----------

export type RegistrationMeaning = 'draft' | 'registered' | 'archived';

export const REGISTRATION_TEXT: Record<RegistrationMeaning, { label: string; help: string }> = {
  draft: { label: 'Draft', help: 'Not in the register yet: waiting for its ownership decision or its intake decision.' },
  registered: { label: 'Registered', help: 'In the register and in every portfolio figure.' },
  archived: { label: 'Archived', help: 'Taken out of the register. Its records stay.' },
};

// The registration meanings a list shows: registered only when none are chosen (find_applications' default).
export function shownRegistrations(chosen: readonly RegistrationMeaning[] | undefined): RegistrationMeaning[] {
  return chosen?.length ? [...chosen] : ['registered'];
}

// ---------- register an app already built ----------

export interface ComponentRow {
  kindKey: string;
  externalId: string;
  url: string;
  label: string;
}

export interface RegisterForm {
  name: string;
  purpose: string;
  kindKey: string;
  portfolioId: string;
  buildPathKey: string;
  usesAi: '' | 'yes' | 'no';
  businessOwner: PrincipalRef | null;
  technicalOwner: PrincipalRef | null;
  components: ComponentRow[];
}

export const emptyComponent = (): ComponentRow => ({ kindKey: '', externalId: '', url: '', label: '' });

export const emptyRegisterForm = (me: PrincipalRef | null = null): RegisterForm => ({
  name: '',
  purpose: '',
  kindKey: '',
  portfolioId: '',
  buildPathKey: '',
  usesAi: '',
  businessOwner: me,
  technicalOwner: null,
  components: [],
});

export const BUSINESS_OWNER = 'business-owner';
export const TECHNICAL_OWNER = 'technical-owner';

const blankComponent = (c: ComponentRow) => !c.kindKey && !c.externalId.trim() && !c.url.trim() && !c.label.trim();

// The save_application_draft input. The business owner is required on this form: a draft becomes registered only when
// its ownership decision is accepted, and that decision asks the named owners to confirm. A component row left blank
// is dropped; a half-filled one is a problem.
export function buildRegisterInput(form: RegisterForm, ownerRoles: { business: boolean; technical: boolean } = { business: true, technical: true }): WriteInput<'save_application_draft'> | Problem {
  const name = form.name.trim();
  const purpose = form.purpose.trim();
  if (!name) return { problem: 'Give the application its name.', field: 'name' };
  if (name.length > 200) return { problem: 'The name is longer than 200 characters.', field: 'name' };
  if (!purpose) return { problem: 'Say what the application is for, in one or two plain sentences.', field: 'purpose' };
  if (purpose.length > 2000) return { problem: 'The purpose is longer than 2,000 characters.', field: 'purpose' };
  if (!form.kindKey) return { problem: 'Choose what kind of application it is.', field: 'applicationKindKey' };
  if (ownerRoles.business && !form.businessOwner) {
    return { problem: 'Name the business owner. The application is registered once its business owner confirms.', field: 'proposedOwners' };
  }
  if (form.businessOwner && form.technicalOwner && form.businessOwner.objectId.toLowerCase() === form.technicalOwner.objectId.toLowerCase()) {
    return { problem: 'The business owner and the technical owner are the same person. Name a different technical owner, or leave it empty.', field: 'proposedOwners' };
  }

  const components: NonNullable<WriteInput<'save_application_draft'>['components']> = [];
  for (const [i, c] of form.components.entries()) {
    if (blankComponent(c)) continue;
    const n = i + 1;
    if (!c.kindKey) return { problem: `Choose the kind of component ${n}.`, field: 'components.componentKindKey' };
    const externalId = c.externalId.trim();
    const url = c.url.trim();
    const label = c.label.trim();
    if (!externalId) return { problem: `Give the ID of component ${n} in its own tool.`, field: 'components.externalId' };
    if (externalId.length > 400) return { problem: `The ID of component ${n} is longer than 400 characters.`, field: 'components.externalId' };
    if (!url || !isHttpsUrl(url)) return { problem: `Give the full https address that opens component ${n}.`, field: 'components.url' };
    if (label.length > 200) return { problem: `The name of component ${n} is longer than 200 characters.`, field: 'components.labelAsSeen' };
    components.push({ componentKindKey: c.kindKey, externalId, url, ...(label ? { labelAsSeen: label } : {}) });
  }

  const proposedOwners: NonNullable<WriteInput<'save_application_draft'>['proposedOwners']> = [];
  if (ownerRoles.business && form.businessOwner) proposedOwners.push({ ownerRoleKey: BUSINESS_OWNER, objectId: form.businessOwner.objectId });
  if (ownerRoles.technical && form.technicalOwner) proposedOwners.push({ ownerRoleKey: TECHNICAL_OWNER, objectId: form.technicalOwner.objectId });

  return {
    name,
    purpose,
    applicationKindKey: form.kindKey,
    ...(form.portfolioId ? { portfolioId: form.portfolioId } : {}),
    ...(form.buildPathKey ? { buildPathKey: form.buildPathKey } : {}),
    ...(form.usesAi ? { usesAi: form.usesAi === 'yes' } : {}),
    ...(proposedOwners.length ? { proposedOwners } : {}),
    ...(components.length ? { components } : {}),
  };
}
