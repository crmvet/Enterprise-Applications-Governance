// The reads of the PMO and portfolio pages beyond useRead:
//   useAllRows(tool, input)       every page of a list tool, merged into one answer (the demand board counts the whole
//                                 list; the review pack groups a portfolio's applications)
//   usePortfolioMembers(items)    the applications in each portfolio and below it (find_applications portfolioId), so
//                                 a page can say which portfolio a request or an application sits in: no list carries it
//   useRightRows()                the Decision Right rows (C31) in force, for mayPropose
// Like useTool, a read again after a write keeps the last answer on screen until the new one comes.
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Envelope, PortfolioItem, Success, ToolData, ToolName } from '../../data/contracts';
import { dataOf, useTool, useToolClient, type ToolState } from '../../data/toolClient';
import type { RightRow } from './logic';

type ListTool = { [N in ToolName]: ToolData[N] extends { items: unknown[] } ? N : never }[ToolName];

const failure = (e: unknown) => ({
  ok: false as const,
  error: { code: 'upstream_unavailable' as const, message: e instanceof Error ? e.message : 'The call did not complete.' },
  correlationId: 'client-side',
});

// Every page of a list tool, up to maxPages (100 rows each), as one envelope: the first page's, with all the rows.
// skipToken stays set only when the read stopped before the end, so MoreRowsNote can say the list is not complete.
export function useAllRows<N extends ListTool>(tool: N, input: Record<string, unknown> | null, maxPages = 10): ToolState<ToolData[N]> {
  const client = useToolClient();
  const inputKey = input === null ? null : JSON.stringify(input);
  const [state, setState] = useState<ToolState<ToolData[N]>>({ status: 'loading' });
  const asked = useRef<string | null>(null);

  useEffect(() => {
    if (!client || inputKey === null) return;
    let live = true;
    const question = `${tool}\n${inputKey}`;
    if (asked.current !== question) setState({ status: 'loading' });
    asked.current = question;
    const base = JSON.parse(inputKey) as Record<string, unknown>;
    const read = async (): Promise<Envelope<ToolData[N]>> => {
      let first: Success<ToolData[N]> | null = null;
      const items: unknown[] = [];
      let token: string | undefined;
      let pages = 0;
      let later: string | null = null;
      do {
        const envelope = (await client.call(tool, token ? { ...base, skipToken: token } : base)) as Envelope<ToolData[N]>;
        if (!envelope.ok) {
          if (!first) return envelope;
          later = `${envelope.error.message} Reference ${envelope.correlationId}.`;
          break;
        }
        first = first ?? envelope;
        const data = envelope.data as unknown as { items: unknown[]; skipToken?: string };
        items.push(...data.items);
        token = typeof data.skipToken === 'string' && data.skipToken ? data.skipToken : undefined;
        pages++;
      } while (token && pages < maxPages);
      const data = { ...(first.data as object), items, skipToken: token } as unknown as ToolData[N];
      const warnings = later ? [...first.warnings, { message: `Only the first ${items.length} rows could be read. ${later}` }] : first.warnings;
      return { ...first, data, warnings };
    };
    read()
      .then(envelope => { if (live) setState({ status: 'done', envelope }); })
      .catch((e: unknown) => { if (live) setState({ status: 'done', envelope: failure(e) }); });
    return () => { live = false; };
  }, [client, tool, inputKey, maxPages]);

  return state;
}

export interface PortfolioMembers {
  // True while any portfolio's applications are still being read.
  loading: boolean;
  // The portfolios whose applications could not be read; their requests show with no portfolio, and the page says so.
  failed: number;
  // Portfolio ID (lower case) to the IDs (lower case) of the applications in it and below it.
  members: ReadonlyMap<string, ReadonlySet<string>>;
}

// The most portfolios the page reads the applications of. A larger tree reads the first ones and says so.
export const MAX_PORTFOLIOS = 40;

// The applications of each portfolio, draft applications included (a request for a new application holds one once
// triage accepts it). One find_applications read per portfolio, five pages at most each.
export function usePortfolioMembers(items: readonly PortfolioItem[] | null): PortfolioMembers {
  const client = useToolClient();
  const ids = items ? items.slice(0, MAX_PORTFOLIOS).map(i => i.portfolioId.toLowerCase()) : null;
  const key = ids ? ids.join(',') : null;
  const [state, setState] = useState<PortfolioMembers>({ loading: true, failed: 0, members: new Map() });

  useEffect(() => {
    if (!client || key === null) return;
    let live = true;
    const list = key ? key.split(',') : [];
    setState(s => ({ ...s, loading: true }));
    void (async () => {
      const members = new Map<string, Set<string>>();
      let failed = 0;
      await Promise.all(list.map(async id => {
        const apps = new Set<string>();
        let token: string | undefined;
        let pages = 0;
        try {
          do {
            const envelope = await client.call('find_applications', {
              portfolioId: id,
              registrationMeanings: ['registered', 'draft'],
              top: 100,
              ...(token ? { skipToken: token } : {}),
            });
            if (!envelope.ok) {
              failed++;
              return;
            }
            for (const a of envelope.data.items) apps.add(a.businessApplicationId.toLowerCase());
            token = envelope.data.skipToken || undefined;
            pages++;
          } while (token && pages < 5);
          members.set(id, apps);
        } catch {
          failed++;
        }
      }));
      if (live) setState({ loading: false, failed, members });
    })();
    return () => { live = false; };
  }, [client, key]);

  return state;
}

// The Decision Right rows in force. Empty while they load or when the read fails: the page then hides the propose
// actions it would base on them, and the engine still decides.
export function useRightRows(): { rows: RightRow[]; known: boolean } {
  const state = useTool('get_configuration', { entity: 'decision-right', top: 100 });
  const data = dataOf(state);
  return useMemo(() => ({ rows: data?.rows ?? [], known: data !== null }), [data]);
}

// The applications of one portfolio and the portfolios below it, drafts included (find_applications portfolioId), for a
// page whose reads take no portfolio: the Timeline and the Overview's change windows and flagged deployments keep the
// rows of these applications. Null ids while nothing is chosen or the read has not answered.
export interface PortfolioApps {
  loading: boolean;
  failure: Extract<Envelope<unknown>, { ok: false }> | null;
  ids: ReadonlySet<string> | null;
  // True when the portfolio holds more than the 500 applications read.
  more: boolean;
}

export function usePortfolioApps(portfolioId: string | null | undefined): PortfolioApps {
  const state = useAllRows('find_applications', portfolioId ? { portfolioId, registrationMeanings: ['draft', 'registered'], top: 100 } : null, 5);
  return useMemo(() => {
    if (!portfolioId) return { loading: false, failure: null, ids: null, more: false };
    if (state.status === 'loading') return { loading: true, failure: null, ids: null, more: false };
    if (!state.envelope.ok) return { loading: false, failure: state.envelope, ids: null, more: false };
    return {
      loading: false,
      failure: null,
      ids: new Set(state.envelope.data.items.map(a => a.businessApplicationId.toLowerCase())),
      more: !!state.envelope.data.skipToken,
    };
  }, [portfolioId, state]);
}
