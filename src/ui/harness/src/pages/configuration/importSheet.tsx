// "Import data domains" (import_data_taxonomy, tool 99; docs/architecture/data-taxonomy.md). A configuration admin turns a CSV, or a domain list exported from Microsoft Fabric or Microsoft
// Purview, into a configuration draft of business data domains. The sheet reads the file in the browser and sends its
// text: the product holds no Fabric or Purview permission. Nothing is in force until the draft is submitted from its page
// and another person accepts it on the decision page (D-38).
//
// Choices (D-41), each with its reason:
//   - The check is the sheet's own button, as the draft builder's is (sheets.tsx): the import's check answers ok even
//     with row problems, and the page shows every problem by row and column, the rows the draft changes, the domains
//     the file leaves out, the prepared owner changes and the catalog owner differences, which the shared answer has no
//     room for.
//   - The file is read in the browser and shown in a text box the person can correct before the check, so a typo in one
//     row needs no round trip through a spreadsheet.
//   - Owners in the file are listed as prepared owner changes and never sent from here: an owner changes only through an
//     owner change a person sends from Owners or the Data page (docs/architecture/data-taxonomy.md). The sheet says so.
//   - A domain the file leaves out is listed and kept: the import never retires a domain.
import { FileUp } from 'lucide-react';
import { useEffect, useId, useRef, useState, type ReactElement } from 'react';
import { ActionSheet, EngineAnswerView, Field, useWriteAction, type FormProblem } from '../../components/actions';
import { Muted, ToneBadge } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Label } from '../../components/ui/label';
import { Textarea } from '../../components/ui/textarea';
import type { Envelope, ImportDataTaxonomyInput, ImportDataTaxonomyResult } from '../../data/contracts';
import { importFacts, plainEngineText, type EngineAnswer, type WriteInput } from '../../data/writes';
import { SelectField, type Choice } from '../changes-records/parts';
import { csvProblem, domainLabelOf, IMPORT_CSV_MAX, IMPORT_SOURCE_TEXT, type ImportSource } from './logic';
import { DiffList, RuleCheck, Section } from './parts';
import { useConfigRowsInForce, type ConfigRowsInForce } from './rows';

type ImportInput = WriteInput<'import_data_taxonomy'>;

const SOURCES: Choice[] = (Object.keys(IMPORT_SOURCE_TEXT) as ImportSource[]).map(k => ({ value: k, label: IMPORT_SOURCE_TEXT[k] }));

const COLUMNS = 'key, label, parent, level, classification, description, examples, use_rule, ai_rule, copy_rule, retention, match_vocabulary, match_term, match_url, owner, deputy, steward';

export function ImportDomainsSheet({ trigger }: { trigger?: ReactElement }) {
  const build = useRef<(() => ImportInput | FormProblem) | null>(null);
  return (
    <ActionSheet
      tool="import_data_taxonomy" site="pages/configuration/importSheet#ImportDomainsSheet"
      title="Import data domains"
      description="Turn a domain list into a configuration draft: a CSV, or the domains exported from Microsoft Fabric or Microsoft Purview. Nothing changes until the draft is submitted and another person accepts it."
      trigger={trigger ?? <Button size="sm" variant="outline"><FileUp aria-hidden />Import data domains</Button>}
      size="wide"
      input={() => build.current?.() ?? { problem: 'The sheet is still opening. Try again.' }}
      submitLabel="Save the draft"
      confirm={{
        title: 'Save the draft?',
        body: 'The draft holds the domains the file adds or changes. It changes nothing until you submit it from its page and another person accepts it. Owners in the file are not changed.',
        confirmLabel: 'Save the draft',
      }}
    >
      <ImportBody build={build} />
    </ActionSheet>
  );
}

function ImportBody({ build }: { build: { current: (() => ImportInput | FormProblem) | null } }) {
  const config = useConfigRowsInForce();
  const [source, setSource] = useState<ImportSource>('csv');
  const [csv, setCsv] = useState('');
  const [fileName, setFileName] = useState<string | null>(null);
  const [fileProblem, setFileProblem] = useState<string | null>(null);
  const [rationale, setRationale] = useState('');
  const check = useWriteAction('import_data_taxonomy', { refresh: false });
  const checkRef = useRef<HTMLDivElement>(null);
  const [checkProblem, setCheckProblem] = useState<string | null>(null);
  const fileId = useId();

  const buildInput = (): ImportInput | FormProblem => {
    if (config.version === null) {
      return { problem: config.failure ? `The rules in force could not be read: ${config.failure.error.message}` : 'The rules in force are still being read. Try again in a moment.' };
    }
    const problem = csvProblem(csv);
    if (problem) return { problem, field: 'csv' };
    const text = rationale.trim();
    if (!text) return { problem: 'Say why the import is wanted, in your words.', field: 'proposalRationale' };
    const input: ImportDataTaxonomyInput = { csv, source, baseVersionNumber: config.version, proposalRationale: text };
    return input;
  };
  build.current = buildInput;

  // A check answers the file as it was: any change clears it.
  const payload = JSON.stringify([csv, source, rationale.trim()]);
  const { reset } = check;
  useEffect(() => { reset(); }, [payload, reset]);
  useEffect(() => { if (check.envelope) checkRef.current?.focus({ preventScroll: false }); }, [check.envelope]);

  const runCheck = () => {
    const input = buildInput();
    if ('problem' in input) {
      setCheckProblem(input.problem);
      return;
    }
    setCheckProblem(null);
    void check.run(input, { preview: true });
  };

  const readFile = async (file: File | undefined) => {
    setFileProblem(null);
    if (!file) return;
    if (file.size > IMPORT_CSV_MAX * 4) {
      setFileProblem(`The file is too large: the import takes up to ${IMPORT_CSV_MAX.toLocaleString('en-US')} characters, about 500 domains.`);
      return;
    }
    try {
      const text = await file.text();
      setCsv(text.replace(/^﻿/, ''));
      setFileName(file.name);
    } catch {
      setFileProblem('The browser could not read the file. Paste its text in the box instead.');
    }
  };

  return (
    <>
      <div className="flex flex-col gap-1 rounded-md border border-line bg-background px-3 py-2.5 text-sm text-navy-2">
        <span className="font-semibold text-navy">
          {config.version !== null ? `Read against version ${config.version}, the version in force` : config.failure ? 'The rules in force could not be read' : 'Reading the rules in force'}
        </span>
        <span>
          Each row is one domain. A domain already in the configuration is changed to match the row; a new one is added. A
          domain the file leaves out is kept: the import never retires one.
        </span>
      </div>

      <SelectField
        label="Where the list came from"
        name="source"
        value={source}
        onChange={v => setSource((v as ImportSource) || 'csv')}
        choices={SOURCES}
        hint="It names the outside vocabulary a row's match term belongs to when the row does not name one."
        required
      />

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={fileId}>The file</Label>
        <input
          id={fileId}
          type="file"
          accept=".csv,text/csv,text/plain"
          onChange={e => { void readFile(e.target.files?.[0]); }}
          className="block w-full text-sm text-navy file:mr-3 file:rounded-md file:border file:border-line file:bg-card file:px-3 file:py-1.5 file:text-sm file:font-medium file:text-navy hover:file:border-primary"
        />
        <p className="m-0 text-xs leading-relaxed text-navy-3">
          {fileName ? `Read ${fileName}. Check or correct the text below.` : 'Choose a CSV file, or paste its text below.'}
        </p>
        {fileProblem && <p className="m-0 text-xs font-medium text-danger" role="alert">{fileProblem}</p>}
      </div>

      <Field label="The CSV text" name="csv" required hint={`A header row, then one domain per row. The columns: ${COLUMNS}. Only label is required; parent names the parent domain by its key or label.`}>
        {p => <Textarea {...p} required={false} rows={8} maxLength={IMPORT_CSV_MAX} spellCheck={false} className="font-mono text-xs" value={csv} onChange={e => setCsv(e.target.value)} />}
      </Field>

      <Field label="Why the import is wanted" name="proposalRationale" required hint="In your words. The people asked to accept the draft read this.">
        {p => <Textarea {...p} required={false} rows={3} maxLength={2000} value={rationale} onChange={e => setRationale(e.target.value)} />}
      </Field>

      <div className="flex flex-col gap-2 border-t border-line pt-3">
        <div className="flex flex-wrap items-center gap-2">
          <Button type="button" variant="outline" onClick={runCheck} disabled={check.sending}>
            {check.sending ? 'Checking' : 'Check the import'}
          </Button>
          <Muted>Every row and every rule is checked, and nothing is saved.</Muted>
        </div>
        {checkProblem && <p className="m-0 text-sm text-warning" role="alert">{checkProblem}</p>}
        {check.envelope && (
          <div ref={checkRef} tabIndex={-1} aria-live="polite" className="outline-none">
            <ImportCheckResult envelope={check.envelope} answer={check.answer} config={config} />
          </div>
        )}
      </div>
    </>
  );
}

// What the import would do: the counts, the row problems, the rules check, the rows the draft changes, the domains the
// file leaves out, the prepared owner changes and the catalog owners that differ (DC-15).
function ImportCheckResult({ envelope, answer, config }: { envelope: Envelope<ImportDataTaxonomyResult>; answer: EngineAnswer | null; config: ConfigRowsInForce }) {
  if (!envelope.ok) return answer ? <EngineAnswerView answer={answer} /> : null;
  const d = envelope.data;
  const label = (key: string) => domainLabelOf(key, d.diffSummary, config.lookup);
  const ownerRole = (key: string) => config.lookup('owner-role', key) ?? 'Owner role';
  const errors = d.errors.map(e => ({ entity: e.entity, key: e.key, message: e.message }));
  return (
    <div className="flex flex-col gap-3 rounded-lg border border-info-line bg-card p-3">
      <span className="text-sm font-semibold text-navy">The engine's check. Nothing was saved yet.</span>
      <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
        {importFacts(d, true).map(line => <li key={line}>{line}</li>)}
      </ul>
      {d.problems.length > 0 && (
        <Section title={`Rows with a problem (${d.problems.length})`}>
          <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm text-navy" aria-label="Rows with a problem">
            {d.problems.map((p, i) => (
              <li key={`${p.row}-${p.column ?? ''}-${i}`} className="flex flex-wrap items-baseline gap-x-2">
                <ToneBadge tone="danger">{`Row ${p.row}${p.column ? `, ${p.column}` : ''}`}</ToneBadge>
                <span className="min-w-0 flex-1">{plainEngineText(p.message, config.labelOfAnyKey)}</span>
              </li>
            ))}
          </ul>
        </Section>
      )}
      <RuleCheck valid={d.valid} errors={errors} warnings={[]} labelOfAnyKey={config.labelOfAnyKey} saved={false} />
      <Section title="What the draft changes">
        <DiffList diff={d.diffSummary} lookup={config.lookup} empty="The draft would change no domain: every row is already as the configuration holds it." />
      </Section>
      {d.notInSource.length > 0 && (
        <Section title={`In the configuration, not in the file (${d.notInSource.length})`}>
          <Muted>These domains are kept as they are. Retire one through a configuration change if it should go.</Muted>
          <ul className="m-0 flex list-disc flex-col gap-0.5 pl-5 text-sm text-navy">
            {d.notInSource.map(n => <li key={n.key}>{n.path || label(n.key)}</li>)}
          </ul>
        </Section>
      )}
      {d.preparedOwnerChanges.length > 0 && (
        <Section title={`Owners in the file (${d.preparedOwnerChanges.length})`}>
          <Muted>
            Prepared, not sent. An owner changes only through an owner change a person sends from the domain's page, and the
            data owner of the domain above accepts it.
          </Muted>
          <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm text-navy" aria-label="Prepared owner changes">
            {d.preparedOwnerChanges.map((o, i) => (
              <li key={`${o.domainKey}-${o.ownerRoleKey}-${o.isDeputy}-${i}`} className="flex flex-col gap-0.5 border-t border-line pt-1 first:border-t-0 first:pt-0">
                <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span className="font-semibold">{`${label(o.domainKey)}: ${ownerRole(o.ownerRoleKey)}${o.isDeputy ? ', deputy' : ''}`}</span>
                  <ToneBadge tone={o.readyToSend ? 'success' : 'warning'}>{o.readyToSend ? 'Ready to send' : 'Not ready'}</ToneBadge>
                </span>
                <span className="text-xs text-navy-2">{`${o.userPrincipalName ?? 'No directory match'}. ${plainEngineText(o.note, config.labelOfAnyKey)}`}</span>
              </li>
            ))}
          </ul>
        </Section>
      )}
      {d.catalogOwnerDifferences.length > 0 && (
        <Section title={`The catalog names another data owner (${d.catalogOwnerDifferences.length})`}>
          <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm text-navy" aria-label="Catalog owner differences">
            {d.catalogOwnerDifferences.map((c, i) => (
              <li key={`${c.domainKey}-${c.ownerRoleKey}-${i}`}>
                <span className="font-semibold">{`${label(c.domainKey)}: `}</span>
                {`${ownerRole(c.ownerRoleKey)} here is ${c.currentHolder?.displayLabel ?? 'nobody'}. `}
                <span className="text-navy-2">{plainEngineText(c.message, config.labelOfAnyKey)}</span>
              </li>
            ))}
          </ul>
        </Section>
      )}
    </div>
  );
}
