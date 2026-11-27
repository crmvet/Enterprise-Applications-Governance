// Finds the subject of an evidence pack: an application by its name or number (find_applications, every registration
// status), or a decision by its number (find_records). Each result opens the pack. Any role reads get_evidence, so the
// finder shows for everyone, also on the audit page of a person who may not read the audit trail.
import { FileStack, Gavel, Search } from 'lucide-react';
import { useId, useState } from 'react';
import { evidenceHref } from '../../app/links';
import { parseSearch } from '../../app/shellLogic';
import { Muted, TextLink } from '../../components/blocks';
import { useSettled } from '../../components/people';
import { Row, RowList } from '../../components/rows';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { LeadIcon } from './parts';

export function EvidenceFinder({ autoFocus }: { autoFocus?: boolean }) {
  const id = useId();
  const [text, setText] = useState('');
  const settled = useSettled(text.trim());
  const query = parseSearch(settled);
  const byNumber = query.recordNumber && (query.recordNumber.entity === 'decision' || query.recordNumber.entity === 'business-application') ? query.recordNumber.number : null;
  const asking = settled.length >= 2;
  const records = useRead('find_records', byNumber ? { recordNumber: byNumber } : null);
  const apps = useRead('find_applications', asking && !byNumber ? { text: settled, registrationMeanings: ['draft', 'registered', 'archived'], top: 8 } : null);

  const results: { key: string; kind: 'business-application' | 'decision'; id: string; label: string; note: string }[] = [];
  for (const r of dataOf(records)?.items ?? []) {
    if (r.entity === 'decision' || r.entity === 'business-application') {
      results.push({ key: r.recordId, kind: r.entity, id: r.recordId.toLowerCase(), label: `${r.recordNumber}: ${r.label}`, note: r.entity === 'decision' ? 'Decision' : 'Application' });
    }
  }
  for (const a of dataOf(apps)?.items ?? []) {
    results.push({ key: a.businessApplicationId, kind: 'business-application', id: a.businessApplicationId.toLowerCase(), label: a.name, note: [a.kind.label, a.tier?.label].filter(Boolean).join(', ') });
  }
  const loading = (byNumber ? records.status : asking ? apps.status : 'done') === 'loading';
  const failure = byNumber
    ? records.status === 'done' && !records.envelope.ok ? records.envelope : null
    : apps.status === 'done' && !apps.envelope.ok ? apps.envelope : null;

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id} className="text-xs text-navy-2">Application name, or a decision number such as DEC-0112</Label>
      <div className="relative">
        <Search className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-navy-3" aria-hidden />
        <Input
          id={id}
          type="search"
          className="pl-8"
          placeholder="Invoice Matching, DEC-0112"
          value={text}
          onChange={e => setText(e.target.value)}
          autoFocus={autoFocus}
          autoComplete="off"
          spellCheck={false}
          maxLength={100}
        />
      </div>
      <div aria-live="polite" className="flex flex-col gap-1">
        {!asking && <Muted>Type two letters or more.</Muted>}
        {asking && loading && <Muted>Searching.</Muted>}
        {failure && <Muted>{`The search did not answer: ${failure.error.message} Reference ${failure.correlationId}.`}</Muted>}
        {asking && !loading && !failure && !results.length && <Muted>{`Nothing matches "${settled}".`}</Muted>}
        {results.length > 0 && (
          <RowList label="Evidence packs found">
            {results.map(r => (
              <Row
                key={r.key}
                lead={<LeadIcon tone="brand">{r.kind === 'decision' ? <Gavel /> : <FileStack />}</LeadIcon>}
                primary={<TextLink href={evidenceHref(r.kind, r.id)}>{r.label}</TextLink>}
                secondary={r.note}
              />
            ))}
          </RowList>
        )}
      </div>
    </div>
  );
}
