// Types of a11y-pages.mjs, for src/app/a11yPages.test.ts (the typecheck reads this file next to the script).
export interface A11yPage {
  name: string;
  path: string | null;
  persona?: false;
  sheet?: false | string;
  sheetOnly?: true;
}

export interface DirectorRecord {
  tool: string;
  input: Record<string, unknown>;
  field: string;
}

export const DEMO_IDS: Readonly<Record<string, string>>;
export const DIRECTOR_RECORDS: Readonly<Record<'claim' | 'gapCheckRun' | 'configurationDraft', DirectorRecord>>;
export const DEFAULT_PERSONAS: readonly string[];
export const LOOKUP_PERSONA: string;
export function appPages(ids: Record<string, string | null>): A11yPage[];
