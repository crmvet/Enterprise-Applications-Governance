// One request per identical read. Application 360 and the dependency map mount many small parts that each read the
// same configuration labels (every owner row's sheets read the owner roles, every gate the stages), and the server
// limits each person's calls per minute (CallerRequestLimits ApiPerMinute, 300 by default). This client answers an
// identical read (same tool, same input) with the one request already made, for as long as the client lives. The web
// app's shell hands out a new client after every write and refresh, so a cached answer never outlives the data it read.
// Writes always go through, and a failed read is not kept, so the next asking tries again.
import { WRITE_TOOLS, type Envelope, type ToolData, type ToolName } from '../../data/contracts';
import type { ToolClient } from '../../data/toolClient';

const WRITES = new Set<string>(WRITE_TOOLS);

export function dedupeReads(parent: ToolClient): ToolClient {
  const cache = new Map<string, Promise<Envelope<unknown>>>();
  return {
    call<N extends ToolName>(tool: N, input: Record<string, unknown>): Promise<Envelope<ToolData[N]>> {
      if (WRITES.has(tool)) return parent.call(tool, input);
      const key = `${tool}\n${JSON.stringify(input)}`;
      const hit = cache.get(key);
      if (hit) return hit as Promise<Envelope<ToolData[N]>>;
      const asked = parent.call(tool, input).then(
        envelope => {
          if (!envelope.ok) cache.delete(key);
          return envelope;
        },
        (error: unknown) => {
          cache.delete(key);
          throw error;
        },
      );
      cache.set(key, asked as Promise<Envelope<unknown>>);
      return asked;
    },
  };
}
