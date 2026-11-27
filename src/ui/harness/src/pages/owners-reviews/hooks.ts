// Reads the Owners and Reviews pages need beyond useTool:
//   useWhoAmI()           the signed-in person (or the demo persona): roles and the tools they may call (whoami)
//   useAllPages(...)      every page of a list tool, merged, so a page can group rows (by person, by type)
//   useQueryParam(...)    a page's own query parameter that the shared route does not carry (role, within, mine)
//   withParams(...)       an address with such parameters added
import { useEffect, useRef, useState } from 'react';
import type { Envelope, Success, ToolData, ToolName } from '../../data/contracts';
import { useTool, useToolClient, type ToolState } from '../../data/toolClient';
import type { WhoAmI } from './model';

// whoami is served on POST /api/whoami like every read tool, but data/contracts.ts does not type it yet: the call goes
// through useTool under its own name and the answer is read as WhoAmI.
export function useWhoAmI(): { me: WhoAmI | null; state: ToolState<WhoAmI> } {
  const state = useTool('whoami' as string as ToolName, {}) as unknown as ToolState<WhoAmI>;
  const me = state.status === 'done' && state.envelope.ok && isWhoAmI(state.envelope.data) ? state.envelope.data : null;
  return { me, state };
}

function isWhoAmI(value: unknown): value is WhoAmI {
  const v = value as WhoAmI | null;
  return !!v && typeof v === 'object' && !!v.principal && typeof v.principal.objectId === 'string'
    && Array.isArray(v.productRoles) && Array.isArray(v.tools);
}

type PagedTool = 'list_owner_assignments' | 'list_reviews';

export interface AllPagesOptions<N extends PagedTool> {
  // The most pages to read (100 rows each at most). Default 10.
  maxPages?: number;
  // Stops early once the rows read so far hold what the page looks for, such as one review by its ID.
  until?: (items: ToolData[N]['items']) => boolean;
}

// Reads every page of a list tool and answers one envelope with all the rows. The envelope is the first page's, so
// its counts, coverage and notes stand for the whole list; skipToken stays set only when the page stopped before the
// end (maxPages, or a later page failed), so MoreRowsNote can say the list is not complete. Like useTool, a refresh
// after a write keeps the rows on screen until the new answer comes.
export function useAllPages<N extends PagedTool>(tool: N, input: Record<string, unknown> | null, options: AllPagesOptions<N> = {}): ToolState<ToolData[N]> {
  const client = useToolClient();
  const inputKey = input === null ? null : JSON.stringify(input);
  const maxPages = options.maxPages ?? 10;
  const until = useRef(options.until);
  until.current = options.until;
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
      let failedLater: string | null = null;
      do {
        const envelope = await client.call(tool, token ? { ...base, skipToken: token } : base);
        if (!envelope.ok) {
          if (!first) return envelope;
          failedLater = `${envelope.error.message} Reference ${envelope.correlationId}.`;
          break;
        }
        if (!first) first = envelope;
        items.push(...envelope.data.items);
        token = envelope.data.skipToken;
        pages++;
        if (until.current?.(items as ToolData[N]['items'])) break;
      } while (token && pages < maxPages);
      const data = { ...first.data, items, skipToken: token } as ToolData[N];
      const warnings = failedLater
        ? [...first.warnings, { message: `Only the first ${items.length} rows could be read. ${failedLater}` }]
        : first.warnings;
      return { ...first, data, warnings };
    };
    read()
      .then(envelope => { if (live) setState({ status: 'done', envelope }); })
      .catch((e: unknown) => {
        if (!live) return;
        setState({
          status: 'done',
          envelope: {
            ok: false,
            error: { code: 'upstream_unavailable', message: e instanceof Error ? e.message : 'The call did not complete.' },
            correlationId: 'client-side',
          },
        });
      });
    return () => { live = false; };
  }, [client, tool, inputKey, maxPages]);

  return state;
}

// Shared with every page in app/links.ts; re-exported so the Owners and Reviews pages keep their imports.
export { useQueryParam, withParams } from '../../app/links';
