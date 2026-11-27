// Small pieces the AI activity, Agents and Audit pages share: the actor badge, a round lead icon, a labeled select for a
// filter bar, a count beside a tab label, "Show more" for a paged list, and the failure of a paged read.
import { Bot, Cog, GitBranch, Inbox, UserRound, Workflow } from 'lucide-react';
import { useId, type ReactElement, type ReactNode } from 'react';
import { Muted, ToneBadge } from '../../components/blocks';
import { Button } from '../../components/ui/button';
import { Label } from '../../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../../components/ui/select';
import type { ActorKind } from '../../data/contracts';
import { useConfigRows } from '../../data/reads';
import { cn } from '../../lib/utils';
import { actorKindLabel, actorTone } from './auditLogic';

// ---------- actors ----------

const ACTOR_ICON: Record<string, ReactElement> = {
  person: <UserRound aria-hidden />,
  'ai-client-for-person': <Bot aria-hidden />,
  'agent-identity': <Bot aria-hidden />,
  pipeline: <GitBranch aria-hidden />,
  'approval-runner': <Workflow aria-hidden />,
  engine: <Cog aria-hidden />,
};

// The configured labels of the actor kinds, by engine meaning.
export function useActorLabel(): (kind: ActorKind | string) => string {
  const { rows } = useConfigRows('actor-kind');
  return kind => actorKindLabel(kind, rows);
}

// The kind of actor, as a badge with an icon and words, never color alone.
export function ActorBadge({ kind, label }: { kind: ActorKind | string; label: string }) {
  return <ToneBadge tone={actorTone(kind)} icon={ACTOR_ICON[kind] ?? <Inbox aria-hidden />}>{label}</ToneBadge>;
}

// ---------- layout ----------

const LEAD_TONE = {
  brand: 'bg-info-soft text-brand-dark',
  danger: 'bg-danger-soft text-danger',
  warning: 'bg-warning-soft text-warning',
  success: 'bg-success-soft text-success',
  subtle: 'bg-secondary text-navy-2',
} as const;

// The round icon at the start of a list row. Decorative: the row's text says what it is.
export function LeadIcon({ children, tone = 'subtle' }: { children: ReactNode; tone?: keyof typeof LEAD_TONE }) {
  return <span className={cn('grid size-8 place-items-center rounded-full [&>svg]:size-4', LEAD_TONE[tone])} aria-hidden>{children}</span>;
}

// A count beside a tab label, read out with its noun.
export function TabCount({ value, noun }: { value: number | null; noun: string }) {
  if (value === null) return null;
  return (
    <span className="rounded-full bg-muted px-1.5 text-xs leading-[18px] font-semibold text-navy-3 tabular-nums" aria-label={`${value} ${noun}`}>
      {value}
    </span>
  );
}

export const filterBarClass = 'flex flex-wrap items-end gap-x-3 gap-y-2.5 rounded-xl border bg-card px-4 py-3 shadow-xs narrow:px-3';

// One select of a filter bar, with its label. "all" stands for no filter.
export function FilterSelect(props: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  allLabel: string;
  options: { value: string; label: string }[];
  width?: string;
}) {
  const id = useId();
  return (
    <div className="flex min-w-0 flex-col gap-1 narrow:w-full">
      <Label htmlFor={id} className="text-xs text-navy-2">{props.label}</Label>
      <Select value={props.value} onValueChange={props.onChange}>
        <SelectTrigger id={id} className={cn(props.width ?? 'w-[220px]', 'narrow:w-full')}>
          <SelectValue />
        </SelectTrigger>
        <SelectContent className="max-h-[320px]">
          <SelectItem value="all">{props.allLabel}</SelectItem>
          {props.options.map(o => <SelectItem key={o.value} value={o.value}>{o.label}</SelectItem>)}
        </SelectContent>
      </Select>
    </div>
  );
}

// ---------- paged lists ----------

// "Show more" under a list that read one page of several, or the note that a later page failed.
export function MoreButton(props: { skipToken: string | undefined; loading: boolean; onMore: () => void; what: string; laterFailure?: { error: { message: string }; correlationId: string } | null }) {
  if (props.laterFailure) {
    return <Muted>{`More ${props.what} could not be read: ${props.laterFailure.error.message} Reference ${props.laterFailure.correlationId}.`}</Muted>;
  }
  if (!props.skipToken) return null;
  return (
    <div className="flex justify-center pt-1 print:hidden">
      <Button variant="outline" size="sm" onClick={props.onMore} disabled={props.loading} aria-label={`Show more ${props.what}`}>
        {props.loading ? 'Reading more' : 'Show more'}
      </Button>
    </div>
  );
}
