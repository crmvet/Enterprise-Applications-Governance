// The data records' reads beyond useRead:
//   useRowInDomain(...)       finds one use or one mapping by its ID, with its domain's detail (get_data_domain: the
//                             decisions, the classification, the other rows). The domain is the one the link names
//                             (?domain=<key>) or, without one, the row's own, read by its ID (list_data_uses dataUseIds,
//                             list_data_mappings dataMappingIds; wave 4, B24-21), which also finds an ended row
//   rowHref(recordUrl, key)   a use or mapping record link with its domain, so the record page reads it in one step
import { useEffect, useState } from 'react';
import { recordHref, withParams } from '../../app/links';
import type { DataDomainDetail, DataDomainSection, Failure, Success } from '../../data/contracts';
import { useRead } from '../../data/reads';
import { dataOf } from '../../data/toolClient';
import { sameId } from './logic';

export type RowLookup<T> =
  | { status: 'loading' }
  | { status: 'failed'; failure: Failure }
  // The row exists, but no read lists it: it has ended or was archived, or its domain is retired.
  | { status: 'gone'; domainKey: string | null; detail: Success<DataDomainDetail> | null }
  | { status: 'found'; item: T; domainKey: string; detail: Success<DataDomainDetail> };

export function useRowInDomain<T>(options: {
  entity: 'data-use' | 'data-mapping';
  id: string;
  hint: string | undefined;
  sections: DataDomainSection[];
  pick: (detail: DataDomainDetail) => T[] | null;
  idOf: (row: T) => string;
}): RowLookup<T> {
  const { entity, id, hint, sections, pick, idOf } = options;
  // The domain the link named does not hold the row (it moved, or the link was old): trace it instead.
  const [hintMissed, setHintMissed] = useState(false);
  const byHint = !!hint && !hintMissed;
  const useRow = useRead('list_data_uses', !byHint && entity === 'data-use' ? { dataUseIds: [id], includeEnded: true, top: 1 } : null);
  const mappingRow = useRead('list_data_mappings', !byHint && entity === 'data-mapping' ? { dataMappingIds: [id], includeEnded: true, top: 1 } : null);
  const trace = entity === 'data-use' ? useRow : mappingRow;
  const traced = (entity === 'data-use' ? dataOf(useRow)?.items[0]?.domain.key : dataOf(mappingRow)?.items[0]?.domain.key) ?? null;
  const domainKey = byHint ? hint! : traced;
  const detail = useRead('get_data_domain', domainKey ? { domainKey, sections } : null);
  const envelope = detail.status === 'done' ? detail.envelope : null;
  const item = envelope?.ok ? (pick(envelope.data) ?? []).find(r => sameId(idOf(r), id)) ?? null : null;

  useEffect(() => {
    if (byHint && envelope && (!envelope.ok || !item)) setHintMissed(true);
  }, [byHint, envelope, item]);

  if (byHint) {
    if (!envelope || !envelope.ok || !item) return { status: 'loading' };
    return { status: 'found', item, domainKey: hint!, detail: envelope };
  }
  if (trace.status === 'loading') return { status: 'loading' };
  if (!trace.envelope.ok) return { status: 'failed', failure: trace.envelope };
  if (!domainKey) return { status: 'gone', domainKey: null, detail: null };
  if (!envelope) return { status: 'loading' };
  if (!envelope.ok) return { status: 'failed', failure: envelope };
  return item ? { status: 'found', item, domainKey, detail: envelope } : { status: 'gone', domainKey, detail: envelope };
}

// A use or mapping record link with its domain named, so its record page finds it with one read. Null when the tool
// gave no link the web app can open.
export function rowHref(recordUrl: string | null | undefined, domainKey: string): string | null {
  const href = recordHref(recordUrl);
  return href ? withParams(href, { domain: domainKey }) : null;
}
