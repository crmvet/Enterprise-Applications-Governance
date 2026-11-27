// Who wrote a row, as the engine stamped it (StampedBy on the reads), for every page (D-28: AI drafts, a person accepts).
//   AiMark      "Drafted by Claude Code for Priya Castell" on any row an AI client created. Nothing for a person's row.
//   StampLine   "Last change by Nadia Ferreira through Web app, demo operator, 24 Nov 2026, 09:00", for a record header.
// The stamp comes from the engine, never from the reader's own session. A row the engine never stamped (seeded data)
// carries no stamp and shows none.
import { Bot } from 'lucide-react';
import type { StampedBy } from '../data/contracts';
import { fmtDateTime, forSomeoneElse } from '../data/format';
import { cn } from '../lib/utils';
import { Badge } from './ui/badge';

const who = (p: { displayLabel: string; objectId: string } | null | undefined) => (p ? p.displayLabel || p.objectId : null);

// The words of the AI mark, or null for a row no AI client created.
export function aiMarkText(isAiCreated: boolean, performedBy: StampedBy | null | undefined): string | null {
  // The drafted by pair (document 22, B22-15) names the AI client and the person it acted for even after a person edits
  // the row, so the mark still reads "Drafted by Claude Code for Priya Castell". Another person's later edit is named too.
  const drafted = performedBy?.draftedBy;
  if (drafted) {
    const person = who(drafted.forPrincipal);
    const head = `Drafted by ${drafted.clientAppLabel || 'an AI client'}${person ? ` for ${person}` : ''}`;
    const editedBy = performedBy?.actorKind === 'person' ? performedBy.principal : null;
    const sameAsDraftedFor = !!editedBy && !!drafted.forPrincipal && editedBy.objectId.toLowerCase() === drafted.forPrincipal.objectId.toLowerCase();
    return editedBy && !sameAsDraftedFor ? `${head}, then edited by ${who(editedBy)}` : head;
  }
  const byAi = isAiCreated || performedBy?.actorKind === 'ai-client-for-person' || performedBy?.actorKind === 'agent-identity';
  if (!byAi) return null;
  // The stamp is the last write's. When a person wrote last, it names the person and their client (the web app), not
  // the AI client that drafted the row, so the mark says an AI drafted it and the person edited it (QA 2026-11-24: it
  // read "Drafted by Web app").
  if (performedBy?.actorKind === 'person') {
    const editor = who(performedBy.principal);
    return `Drafted by an AI client${editor ? `, then edited by ${editor}` : ''}`;
  }
  const client = performedBy?.clientAppLabel || (performedBy?.actorKind === 'agent-identity' ? who(performedBy.principal) : null) || 'an AI client';
  const person = who(performedBy?.onBehalfOf);
  return `Drafted by ${client}${person ? ` for ${person}` : ''}`;
}

export function AiMark({ isAiCreated, performedBy, className }: { isAiCreated: boolean; performedBy: StampedBy | null | undefined; className?: string }) {
  const text = aiMarkText(isAiCreated, performedBy);
  if (!text) return null;
  return (
    <Badge variant="brand" className={cn('max-w-full font-medium whitespace-normal', className)}>
      <Bot aria-hidden />
      <span>{text}</span>
    </Badge>
  );
}

// The words of the stamp line, or null when the row carries no stamp.
export function stampText(performedBy: StampedBy | null | undefined): string | null {
  if (!performedBy) return null;
  const actor = who(performedBy.principal);
  const forWhom = who(forSomeoneElse(performedBy.principal, performedBy.onBehalfOf));
  const parts = [`Last change by ${actor}`];
  if (forWhom) parts.push(`for ${forWhom}`);
  if (performedBy.clientAppLabel) parts.push(`through ${performedBy.clientAppLabel}`);
  const line = parts.join(' ');
  return performedBy.stampedOn ? `${line}, ${fmtDateTime(performedBy.stampedOn)}` : line;
}

export function StampLine({ performedBy, className }: { performedBy: StampedBy | null | undefined; className?: string }) {
  const text = stampText(performedBy);
  if (!text) return null;
  return <span className={cn('text-xs text-navy-3 [overflow-wrap:anywhere]', className)}>{text}.</span>;
}
