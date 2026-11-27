// The Documents page's writes, each in the shared side sheet (components/actions.tsx), checked by the engine exactly as
// it checks an AI client's write:
//   LinkDocumentSheet     link a document: a missing one from its row (application and type set), or any document of
//                         any application from the page header (record_document_link)
//   ConfirmDocumentSheet  confirm a draft current, or a stale or current document still true (confirm_document_checked)
// One sheet of each kind serves the whole list: a row's button opens it for that row. Marking a document stale and
// editing its link stay on the document's own page (DocumentLinkRecord in Records.tsx).
//
// Design choice (D-41, 2026-11-24): the list uses its own controlled sheets rather than
// DocumentLinkSheet from Records.tsx. That sheet carries its own trigger and reads the document and link kinds when it
// mounts, so one per row would cost two reads a row. The fields and checks are the same, with one more: a link
// reported current for a type that turns stale by date needs its last updated date. record_document_link does not set
// the last check, so without that date the engine (OP-06) has nothing to count freshness from and reads it as stale.
import { useState } from 'react';
import { ActionSheet, Field } from '../../components/actions';
import { Muted } from '../../components/blocks';
import { PersonSearchField } from '../../components/people';
import { ExternalLink } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Input } from '../../components/ui/input';
import { Textarea } from '../../components/ui/textarea';
import type { PrincipalRef, RecordDocumentLinkInput } from '../../data/contracts';
import { useLabels, type Labels } from '../../data/labels';
import { isHttpsUrl } from '../changes-records/logic';
import { choicesOf, SelectField, type Choice } from '../changes-records/parts';
import { type DocRow } from './documents';
import { AppSearchField } from './parts';

type App = { id: string; name: string };

// A missing document's row sets the application and the type; the header's button leaves both to choose.
export interface LinkTarget {
  app: App;
  typeKey: string;
  typeLabel: string;
}

// The link kinds a document may use. Rendered only inside an open sheet, so a closed sheet reads nothing.
function LinkKindField(props: { value: string; defaultKind: string; onChange: (v: string) => void }) {
  const kinds = useLabels('link-kind');
  const choices = choicesOf(kinds, k => kinds.field<string>(k, 'linkCategory') === 'document');
  return (
    <SelectField
      label="Link kind"
      name="linkKindKey"
      value={props.value || props.defaultKind}
      onChange={props.onChange}
      choices={choices}
      placeholder="The type's default"
      hint="Where the document lives. The engine checks the address against the kind's pattern."
    />
  );
}

export function LinkDocumentSheet(props: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  target: LinkTarget | null;
  // The document types, from the page's own read of the configuration.
  docTypes: Labels;
}) {
  const fixed = props.target;
  const [app, setApp] = useState<App | null>(null);
  const [typeKey, setTypeKey] = useState('');
  const [url, setUrl] = useState('');
  const [status, setStatus] = useState<'draft' | 'current'>('current');
  const [updated, setUpdated] = useState('');
  const [kind, setKind] = useState('');
  const [owner, setOwner] = useState<PrincipalRef | null>(null);
  const chosenApp = fixed?.app ?? app;
  const chosenType = fixed?.typeKey ?? typeKey;
  const defaultKind = chosenType ? props.docTypes.field<string>(chosenType, 'defaultLinkKindKey') ?? '' : '';
  const needsPerson = chosenType ? props.docTypes.field<boolean>(chosenType, 'currentNeedsPerson') === true : false;
  const freshness = chosenType ? props.docTypes.field<number>(chosenType, 'freshnessDays') : undefined;
  const freshnessDays = typeof freshness === 'number' ? freshness : undefined;
  const typeChoices: Choice[] = choicesOf(props.docTypes);
  const reset = () => {
    setApp(null);
    setTypeKey('');
    setUrl('');
    setStatus('current');
    setUpdated('');
    setKind('');
    setOwner(null);
  };
  const typeLabel = fixed ? fixed.typeLabel : chosenType ? props.docTypes.label(chosenType) : 'document';
  return (
    <ActionSheet
      tool="record_document_link" site="pages/guide-log-docs/DocumentSheets#LinkDocumentSheet"
      title={fixed ? `Link the ${fixed.typeLabel.toLowerCase()} of ${fixed.app.name}` : 'Link a document'}
      description="The product stores the link and the status you report. It never reads or writes the document itself."
      open={props.open}
      onOpenChange={next => {
        props.onOpenChange(next);
        if (!next) reset();
      }}
      input={() => {
        if (!chosenApp) return { problem: 'Choose the application the document belongs to.', field: 'businessApplicationId' };
        if (!chosenType) return { problem: 'Choose the document type.', field: 'documentTypeKey' };
        const u = url.trim();
        if (!isHttpsUrl(u)) return { problem: 'Give the full https address of the document.', field: 'url' };
        if (updated && !/^\d{4}-\d{2}-\d{2}$/.test(updated)) return { problem: 'The last updated date is not a date.', field: 'lastUpdatedOn' };
        // A current document of a type that turns stale by date counts its freshness from its last update: with no date,
        // the engine reads it as stale at once.
        if (status === 'current' && freshnessDays !== undefined && !updated) {
          return { problem: `A ${typeLabel.toLowerCase()} turns stale ${freshnessDays} days after its last update. Give the date it last changed.`, field: 'lastUpdatedOn' };
        }
        const chosenKind = kind || defaultKind;
        const input: RecordDocumentLinkInput = {
          businessApplicationId: chosenApp.id,
          documentTypeKey: chosenType,
          url: u,
          statusMeaning: status,
          ...(updated ? { lastUpdatedOn: updated } : {}),
          ...(owner ? { documentOwnerObjectId: owner.objectId } : {}),
          ...(chosenKind ? { linkKindKey: chosenKind } : {}),
        };
        return input;
      }}
      submitLabel="Link it"
      preview
    >
      {fixed
        ? (
          <p className="m-0 rounded-md border border-line bg-background px-3 py-2.5 text-sm text-navy-2">
            The {fixed.typeLabel.toLowerCase()} of <span className="font-semibold text-navy">{fixed.app.name}</span> is required and has no link yet.
          </p>
        )
        : (
          <>
            <AppSearchField label="Application" name="businessApplicationId" value={app} onChange={setApp} required />
            <SelectField label="Document type" name="documentTypeKey" value={typeKey} onChange={v => { setTypeKey(v); setKind(''); }} choices={typeChoices} placeholder={typeChoices.length ? 'Choose a type' : 'The document types could not be read'} required />
          </>
        )}
      <Field label="Link" name="url" required hint="The full https address of the document.">
        {p => <Input {...p} type="url" inputMode="url" value={url} onChange={e => setUrl(e.target.value)} placeholder="https://" maxLength={2000} />}
      </Field>
      {chosenType && <LinkKindField value={kind} defaultKind={defaultKind} onChange={setKind} />}
      <SelectField
        label="Status"
        name="statusMeaning"
        value={status}
        onChange={v => setStatus(v === 'draft' ? 'draft' : 'current')}
        choices={[{ value: 'current', label: 'Current: I read it and confirm it' }, { value: 'draft', label: 'Draft: not confirmed yet' }]}
        hint={needsPerson ? `A person confirms a ${typeLabel.toLowerCase()} current. You can, as a person; an AI client can only store a draft.` : undefined}
        required
      />
      <Field
        label="Last updated"
        name="lastUpdatedOn"
        required={status === 'current' && freshnessDays !== undefined}
        hint={freshnessDays !== undefined
          ? `When the document itself last changed. It stays current for ${freshnessDays} days from then, or from its last check.`
          : 'Optional. When the document itself last changed.'}
      >
        {p => <Input {...p} type="date" value={updated} onChange={e => setUpdated(e.target.value)} className="w-[200px]" />}
      </Field>
      <PersonSearchField
        label="Document owner"
        name="documentOwnerObjectId"
        value={owner}
        onChange={setOwner}
        activeOnly
        hint="Optional. Leave it empty and you own the document. Type two or more letters to name someone else."
      />
    </ActionSheet>
  );
}

export function ConfirmDocumentSheet(props: { open: boolean; onOpenChange: (open: boolean) => void; row: DocRow | null }) {
  const [note, setNote] = useState('');
  const row = props.row;
  const draft = row?.state === 'draft';
  const typeLabel = row?.documentType.label.toLowerCase() ?? 'document';
  return (
    <ActionSheet
      tool="confirm_document_checked" site="pages/guide-log-docs/DocumentSheets#ConfirmDocumentSheet"
      title={row ? (draft ? `Confirm the ${typeLabel} current` : `Confirm the ${typeLabel} still true`) : 'Confirm the document'}
      description={draft
        ? 'You read the document and it is ready to rely on. It becomes current under your name, and last checked becomes today, your own calendar day.'
        : 'You read the document and it still describes the application. It shows as current, and last checked becomes today, your own calendar day.'}
      open={props.open}
      onOpenChange={next => {
        props.onOpenChange(next);
        if (!next) setNote('');
      }}
      input={() => {
        if (!row?.documentLinkId) return { problem: 'This document has no link to confirm. Link it first.' };
        return { documentLinkId: row.documentLinkId, stillTrue: true, ...(note.trim() ? { note: note.trim() } : {}) };
      }}
      submitLabel={draft ? 'Confirm current' : 'Confirm still true'}
      preview
    >
      {row && (
        <div className="flex flex-col gap-1.5 rounded-md border border-line bg-background px-3 py-2.5">
          <span className="text-sm font-semibold [overflow-wrap:anywhere] text-navy">{row.documentType.label}: {row.applicationName}</span>
          {row.url ? <ExternalLink href={row.url}>Open the document</ExternalLink> : <Muted>The link is not shown to you.</Muted>}
          <AiMark isAiCreated={row.isAiCreated} performedBy={row.performedBy} />
          {draft && row.isAiCreated && <Muted>An AI client recorded this link. Confirming it makes it current under your name: read it first.</Muted>}
        </div>
      )}
      <Field label="Note" name="note" hint="Optional. What you checked.">
        {p => <Textarea {...p} rows={3} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} />}
      </Field>
    </ActionSheet>
  );
}
