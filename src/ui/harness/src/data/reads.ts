// Shared reads for every page, beyond useTool:
//   useRead(tool, input)            useTool with the tool's typed input (data/contracts.ts ToolInput), so a misspelled or
//                                   missing input field is a type error. A null input reads nothing.
//   usePagedRead(tool, input, pick) one list read, page after page: "Show more" (more()), or until a row is found (a
//                                   record page whose entity has no read by ID). Moved here from the Changes pages.
//   useConfigRows(entity, input)    the configuration rows of one entity (get_configuration), with their fields.
// Child configuration rows come back as rows of their own entity: decision outcomes as decision-outcome rows
// ("<type>/<outcome>"), list values as list-value rows ("<list>/<value>"). Read them with useOutcomes and useListValues
// in data/labels.ts, which sort and filter them through data/configRows.ts.
// Every read again in place after a write, as useTool does: the last answer stays on screen until the new one comes.
import { useEffect, useMemo, useRef, useState } from 'react';
import type { ConfigurationRows, Envelope, Failure, Success, ToolData, ToolInput, ToolName } from './contracts';
import { dataOf, useTool, useToolClient, type ToolState } from './toolClient';

// ---------- typed reads ----------

export function useRead<N extends ToolName>(tool: N, input: ToolInput<N> | null): ToolState<ToolData[N]> {
  return useTool(tool, input as Record<string, unknown> | null);
}

// ---------- configuration rows ----------

export type ConfigRowsInput = { keys?: string[]; includeInactive?: boolean; top?: number };

export function useConfigRows(entity: string, input: ConfigRowsInput = {}): {
  rows: ConfigurationRows['rows'];
  state: ToolState<ConfigurationRows>;
  loading: boolean;
  failed: boolean;
} {
  const state = useTool('get_configuration', { entity, top: 100, ...input });
  const data = dataOf(state);
  return useMemo(() => ({
    rows: data?.rows ?? [],
    state,
    loading: state.status === 'loading',
    failed: state.status === 'done' && !state.envelope.ok,
  }), [data, state]);
}

// One setting of the configuration in force (get_configuration entity setting, by key): its value, or undefined while
// it is read, when it is not set, or when the read fails. A null key reads nothing.
export function useSettingValue(key: string | null): unknown {
  const state = useTool('get_configuration', key ? { entity: 'setting', keys: [key] } : null);
  return key ? dataOf(state)?.rows.find(r => r.key === key)?.fields.value : undefined;
}

// ---------- paged reads ----------

export type PagedState<T, D> =
  | { status: 'loading'; pagesRead: number }
  | { status: 'failed'; failure: Failure }
  | {
    status: 'done';
    items: T[];
    first: Success<D>;
    // Set when the server holds more rows than were read.
    skipToken: string | undefined;
    pagesRead: number;
    // A later page that failed; the rows read before it still show.
    laterFailure: Failure | null;
    loadingMore: boolean;
  };

export interface PagedOptions<T> {
  // Stop as soon as this is true for the rows read so far (a record lookup).
  until?: (items: T[]) => boolean;
  // The most pages to read. Default 1; with until, default 10.
  maxPages?: number;
}

const failure = (e: unknown): Failure => ({
  ok: false,
  error: { code: 'upstream_unavailable', message: e instanceof Error ? e.message : 'The call did not complete.' },
  correlationId: 'client-side',
});

// Reads a list tool page by page. pick takes the rows out of a page's data. more() reads one more page (only without
// until). A null input reads nothing.
export function usePagedRead<N extends ToolName, T>(
  tool: N,
  input: Record<string, unknown> | null,
  pick: (data: ToolData[N]) => T[],
  options: PagedOptions<T> = {},
): PagedState<T, ToolData[N]> & { more: () => void } {
  const client = useToolClient();
  const inputKey = input === null ? null : JSON.stringify(input);
  const pickRef = useRef(pick);
  pickRef.current = pick;
  const untilRef = useRef(options.until);
  untilRef.current = options.until;
  const scanning = options.until !== undefined;
  const maxPages = options.maxPages ?? (scanning ? 10 : 1);
  const [extra, setExtra] = useState(0);
  const [state, setState] = useState<PagedState<T, ToolData[N]>>({ status: 'loading', pagesRead: 0 });
  const asked = useRef<string | null>(null);

  useEffect(() => { setExtra(0); }, [tool, inputKey]);

  useEffect(() => {
    if (!client || inputKey === null) return;
    let live = true;
    const question = `${tool}\n${inputKey}`;
    const fresh = asked.current !== question;
    asked.current = question;
    if (fresh) setState({ status: 'loading', pagesRead: 0 });
    else setState(s => (s.status === 'done' ? { ...s, loadingMore: true } : s));
    const limit = maxPages + extra;
    const base = JSON.parse(inputKey) as Record<string, unknown>;

    void (async () => {
      let items: T[] = [];
      let first: Success<ToolData[N]> | null = null;
      let token: string | undefined;
      let pages = 0;
      for (;;) {
        const envelope: Envelope<ToolData[N]> = await client
          .call(tool, token ? { ...base, skipToken: token } : base)
          .catch(failure);
        if (!live) return;
        if (!envelope.ok) {
          if (!first) setState({ status: 'failed', failure: envelope });
          else setState({ status: 'done', items, first, skipToken: token, pagesRead: pages, laterFailure: envelope, loadingMore: false });
          return;
        }
        pages += 1;
        first = first ?? envelope;
        items = items.concat(pickRef.current(envelope.data));
        const data = envelope.data as unknown as { skipToken?: string };
        token = typeof data.skipToken === 'string' && data.skipToken ? data.skipToken : undefined;
        const found = untilRef.current ? untilRef.current(items) : false;
        if (!token || found || pages >= limit) break;
        // A first lookup says how far it has looked. A lookup again after a write keeps the last answer on screen.
        if (scanning && fresh) setState({ status: 'loading', pagesRead: pages });
      }
      setState({ status: 'done', items, first: first!, skipToken: token, pagesRead: pages, laterFailure: null, loadingMore: false });
    })();
    return () => { live = false; };
  }, [client, tool, inputKey, maxPages, extra]);

  return { ...state, more: () => setExtra(n => n + 1) };
}
