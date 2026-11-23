// Shared building blocks for the four pages: panel, status badge, KPI tile, bar list, page header, links and text
// helpers. Built from the shadcn/ui primitives in ./ui and Tailwind classes on the theme tokens (styles/globals.css).
import { CircleAlert, CircleCheck, Clock, EyeOff, Info, TriangleAlert } from 'lucide-react';
import { useSyncExternalStore, type ComponentProps, type CSSProperties, type ReactElement, type ReactNode } from 'react';
import type { CouldNotReadItem, Envelope, Failure, StatusValue, Success, Warning } from '../data/contracts';
import { fmtDateTime, humanizeKey } from '../data/format';
import { toneOfMeaning, type Tone } from '../data/meanings';
import type { ToolState } from '../data/toolClient';
import { cn } from '../lib/utils';
import { Alert, AlertDescription, AlertTitle } from './ui/alert';
import { Badge } from './ui/badge';
import { Skeleton } from './ui/skeleton';
import { Tabs, TabsList, TabsTrigger } from './ui/tabs';
import { Tooltip, TooltipContent, TooltipTrigger } from './ui/tooltip';

// ---------- layout helpers ----------

const NARROW = '(max-width: 719px)';

function subscribeNarrow(callback: () => void) {
  const mq = window.matchMedia(NARROW);
  mq.addEventListener('change', callback);
  return () => mq.removeEventListener('change', callback);
}

// True below 720 px: phones and narrow panes. Pages switch to lists there. In class names, the same breakpoint is the
// `narrow:` variant (and `medium:` below 1024 px), defined in styles/globals.css.
export function useNarrow(): boolean {
  return useSyncExternalStore(subscribeNarrow, () => window.matchMedia(NARROW).matches, () => false);
}

// ---------- links ----------

// A link inside the product's text. Same look for an anchor and for a button that acts like a link.
export const linkClass =
  'rounded-sm font-medium text-primary underline-offset-4 hover:text-brand-dark hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';

export function TextLink({ className, ...props }: ComponentProps<'a'>) {
  return <a className={cn(linkClass, className)} {...props} />;
}

export function LinkButton({ className, type, ...props }: ComponentProps<'button'>) {
  return (
    <button
      type={type ?? 'button'}
      className={cn(linkClass, 'cursor-pointer border-0 bg-transparent p-0 text-left text-[length:inherit] leading-[inherit]', className)}
      {...props}
    />
  );
}

// ---------- status badge ----------

const toneIcon: Partial<Record<Tone, ReactElement>> = {
  success: <CircleCheck aria-hidden />,
  warning: <TriangleAlert aria-hidden />,
  danger: <CircleAlert aria-hidden />,
  informative: <Info aria-hidden />,
};

export function ToneBadge({ tone, children, icon, className }: { tone: Tone; children: ReactNode; icon?: ReactElement | null; className?: string }) {
  // Status colors always come with an icon and a text label, never color alone.
  const shown = icon === null ? undefined : icon ?? toneIcon[tone];
  return (
    <Badge variant={tone} className={className}>
      {shown}
      {children}
    </Badge>
  );
}

export function StatusBadge({ status, tone }: { status: StatusValue; tone?: Tone }) {
  const t = tone ?? toneOfMeaning[status.meaning];
  const icon = status.meaning === 'due' || status.meaning === 'pending' ? <Clock aria-hidden /> : undefined;
  return <ToneBadge tone={t} icon={icon}>{status.label}</ToneBadge>;
}

// ---------- panel ----------

type PanelBody<T> = (data: T, envelope: Success<T>) => ReactNode;

export interface PanelProps<T> {
  title: string;
  subtitle?: ReactNode;
  count?: number;
  actions?: ReactNode;
  state?: ToolState<T>;
  children: PanelBody<T> | ReactNode;
  className?: string;
  id?: string;
  skeletonRows?: number;
  // Which hidden-row notes (couldNotRead kinds) this panel shows. Default: all notes and all warnings.
  notes?: 'all' | string[];
}

export function Panel<T>(props: PanelProps<T>) {
  const { state } = props;
  let body: ReactNode;
  let notes: ReactNode = null;
  if (!state) {
    body = typeof props.children === 'function' ? null : props.children;
  } else if (state.status === 'loading') {
    body = (
      <div role="status" aria-label={`Loading ${props.title}`} className="flex flex-col gap-2.5 pt-1">
        {Array.from({ length: props.skeletonRows ?? 3 }, (_, i) => <Skeleton key={i} className="h-4 w-full" />)}
      </div>
    );
  } else if (!state.envelope.ok) {
    body = <PanelFailure failure={state.envelope} />;
  } else {
    const env = state.envelope;
    body = typeof props.children === 'function' ? (props.children as PanelBody<T>)(env.data, env) : props.children;
    // One envelope can feed several panels. A panel that names its kinds shows only those hidden-row notes,
    // and leaves the warnings to the panel that shows them all.
    const kinds = props.notes ?? 'all';
    notes = kinds === 'all'
      ? <EnvelopeNotes couldNotRead={env.couldNotRead} warnings={env.warnings} />
      : <EnvelopeNotes couldNotRead={env.couldNotRead.filter(c => kinds.includes(c.kind))} warnings={[]} />;
  }
  const headingId = props.id ? `${props.id}-title` : undefined;
  return (
    <section
      className={cn(
        'flex min-w-0 flex-col gap-3 rounded-xl border bg-card px-5 pt-[18px] pb-4 text-card-foreground shadow-xs narrow:rounded-lg narrow:px-3.5 narrow:pt-3.5 narrow:pb-3',
        props.className,
      )}
      id={props.id}
      aria-labelledby={headingId}
    >
      <div className="flex min-h-7 flex-wrap items-center gap-x-2.5 gap-y-1.5">
        <h2 className="m-0 text-base leading-6 font-semibold text-navy" id={headingId}>
          {props.title}
          {props.count !== undefined && (
            <span className="ml-2 inline-block rounded-full bg-muted px-2 py-px align-[1px] text-xs leading-[18px] font-semibold text-navy-3">
              {props.count}
            </span>
          )}
        </h2>
        {props.actions && <div className="ml-auto flex items-center gap-2">{props.actions}</div>}
      </div>
      {props.subtitle && <div className="-mt-2 text-xs leading-4 text-navy-3">{props.subtitle}</div>}
      {body}
      {notes}
    </section>
  );
}

export function PanelFailure({ failure }: { failure: Failure }) {
  const code = failure.error.code;
  const variant = code === 'not_found' ? 'info' : code === 'not_authorized' || code === 'not_signed_in' || code === 'rate_limited' ? 'warning' : 'error';
  const title =
    code === 'not_found' ? 'Nothing to show'
      : code === 'not_authorized' ? 'You cannot see this section'
        : code === 'not_signed_in' ? 'Sign in to see this section'
          : code === 'rate_limited' ? 'Too many requests just now'
            : 'This section could not load';
  const Icon = variant === 'info' ? Info : variant === 'warning' ? TriangleAlert : CircleAlert;
  return (
    <Alert variant={variant}>
      <Icon aria-hidden />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription>
        <p>
          {failure.error.message} The rest of the page still works. Reference {failure.correlationId}.
        </p>
      </AlertDescription>
    </Alert>
  );
}

export function EnvelopeNotes({ couldNotRead, warnings }: { couldNotRead: CouldNotReadItem[]; warnings: Warning[] }) {
  if (!couldNotRead.length && !warnings.length) return null;
  return (
    <div className="flex flex-col gap-1.5">
      {couldNotRead.map(c => (
        <div key={c.kind} className="flex items-center gap-1.5 text-xs text-navy-3">
          <EyeOff className="size-4 shrink-0" aria-hidden />
          <span>
            {c.count} {humanizeKey(c.kind).toLowerCase()}{c.count === 1 ? ' is' : 's are'} hidden by your access.
          </span>
        </div>
      ))}
      {warnings.map((w, i) => (
        <Alert key={i} variant="warning" aria-label="Warning">
          <TriangleAlert aria-hidden />
          <AlertDescription>
            <p>{w.message}</p>
          </AlertDescription>
        </Alert>
      ))}
    </div>
  );
}

// ---------- page header ----------

export function PageHeader(props: { eyebrow?: string; title: ReactNode; subtitle?: ReactNode; meta?: ReactNode; actions?: ReactNode }) {
  return (
    <header className="mb-5 flex flex-wrap items-end justify-between gap-x-6 gap-y-3 narrow:mb-3.5">
      <div className="flex min-w-0 flex-col gap-1">
        {props.eyebrow && <span className="text-xs font-semibold tracking-[0.04em] text-brand-dark uppercase">{props.eyebrow}</span>}
        <h1 className="m-0 text-[28px] leading-9 font-semibold tracking-tight text-navy narrow:text-2xl narrow:leading-[30px]">{props.title}</h1>
        {props.subtitle && <div className="max-w-[760px] text-sm text-navy-2">{props.subtitle}</div>}
        {props.meta && <div className="text-xs text-navy-3">{props.meta}</div>}
      </div>
      {props.actions && <div className="flex flex-wrap items-center gap-2">{props.actions}</div>}
    </header>
  );
}

// "Data as of 22 Nov 2026, 09:00 UTC. Configuration version 7." In the display zone, which it names.
export function DataStamp({ envelope }: { envelope: Envelope<unknown> | undefined }) {
  if (!envelope || !envelope.ok) return null;
  return <>Data as of {fmtDateTime(envelope.asOf)}. Configuration version {envelope.configurationSetVersion}.</>;
}

// ---------- KPI tile ----------

// value is null while the figure is still being read: the tile shows a placeholder, never a number the server did not
// send.
export function KpiTile(props: { label: string; value: number | null; caption: string; attention?: boolean; onClick?: () => void; className?: string }) {
  const loading = props.value === null;
  return (
    <button
      type="button"
      className={cn(
        'relative flex min-w-0 cursor-pointer flex-col gap-0.5 overflow-hidden rounded-xl border bg-card py-3.5 pr-4 pl-[18px] text-left text-card-foreground shadow-xs transition-colors',
        'hover:border-line-strong hover:bg-[#fafafa] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
        props.attention && 'before:absolute before:inset-y-0 before:left-0 before:w-1 before:bg-gold',
        props.className,
      )}
      onClick={props.onClick}
      aria-label={`${props.label}: ${loading ? 'loading' : props.value}. ${props.caption}. Opens the list.`}
      aria-busy={loading || undefined}
    >
      <span className="flex items-center gap-1.5 text-sm font-semibold text-navy-2">
        {props.attention && <TriangleAlert className="size-4 shrink-0 text-gold-strong" aria-hidden />}
        {props.label}
      </span>
      {props.value === null ? (
        <span className="flex h-10 items-center narrow:h-9" aria-hidden>
          <Skeleton className="h-7 w-16" />
        </span>
      ) : (
        <span className="text-[32px] leading-10 font-semibold text-navy tabular-nums narrow:text-[28px] narrow:leading-9">
          {props.value.toLocaleString('en-GB')}
        </span>
      )}
      <span className="text-xs leading-4 text-navy-3">{props.caption}</span>
    </button>
  );
}

// A list that shows one page says so when the server holds more rows than the page asked for (the answer carries a
// skipToken), so a short list is never read as the whole list.
export function MoreRowsNote({ skipToken, what }: { skipToken: string | undefined; what: string }) {
  if (!skipToken) return null;
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center gap-1.5 text-xs text-navy-3">
        <Info className="size-3.5 shrink-0" aria-hidden />
        <span>The product holds more {what} than this list shows. Narrow the range, or ask an AI client for the rest.</span>
      </div>
    </div>
  );
}

// ---------- bar list (one series, horizontal) ----------

export interface BarItem {
  key: string | null;
  label: string;
  count: number;
  muted?: boolean;
}

export function BarList(props: { items: BarItem[]; noun: [string, string]; onDrill?: (item: BarItem) => void; labelWidth?: number; ariaLabel: string }) {
  const max = Math.max(1, ...props.items.map(i => i.count));
  return (
    <ul
      className="m-0 flex list-none flex-col gap-0.5 p-0"
      aria-label={props.ariaLabel}
      style={props.labelWidth ? ({ '--bar-label': `${props.labelWidth}px` } as CSSProperties) : undefined}
    >
      {props.items.map(item => {
        const text = `${item.count} ${item.count === 1 ? props.noun[0] : props.noun[1]}: ${item.label}`;
        const pct = item.count / max;
        return (
          <li key={item.key ?? 'none'}>
            <Tooltip>
              <TooltipTrigger asChild>
                <button
                  type="button"
                  className="grid min-h-[30px] w-full cursor-pointer grid-cols-[var(--bar-label,190px)_minmax(0,1fr)] items-center gap-x-3 rounded-md border-0 bg-transparent px-1.5 text-left text-[length:inherit] text-inherit hover:bg-background focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring narrow:grid-cols-[minmax(0,44%)_minmax(0,1fr)] narrow:gap-x-2"
                  onClick={() => props.onDrill?.(item)}
                >
                  <span className="truncate text-sm text-navy narrow:py-[3px] narrow:leading-[18px] narrow:whitespace-normal">{item.label}</span>
                  <span className="flex h-[22px] min-w-0 items-center gap-2 border-l border-line-strong">
                    <span
                      className={cn('h-3.5 min-w-[3px] shrink-0 rounded-r', item.muted ? 'bg-bar-muted' : 'bg-primary')}
                      style={{ width: `calc((100% - 36px) * ${pct.toFixed(4)})` }}
                      aria-hidden
                    />
                    <span className="text-sm font-semibold text-navy tabular-nums">{item.count}</span>
                  </span>
                </button>
              </TooltipTrigger>
              <TooltipContent side="top" align="start">{text}</TooltipContent>
            </Tooltip>
          </li>
        );
      })}
    </ul>
  );
}

// ---------- in-page switch ----------

// An in-page switch (direction, target stage, view): a tab list with no panels. Manual activation, so the arrow keys
// move the focus and Enter or Space selects. The triggers drop aria-controls, because there are no tab panels for it to
// point to.
export function SwitchTabs<T extends string>(props: { label: string; ariaLabel: string; value: T; onChange: (value: T) => void; options: { value: T; label: ReactNode }[] }) {
  return (
    <div className="flex flex-wrap items-center gap-1">
      <span className="mr-1 text-xs text-navy-3">{props.label}</span>
      <Tabs value={props.value} onValueChange={v => props.onChange(v as T)} activationMode="manual">
        <TabsList aria-label={props.ariaLabel} className="flex-wrap justify-start group-data-[orientation=horizontal]/tabs:h-auto">
          {props.options.map(o => (
            <TabsTrigger
              key={o.value}
              value={o.value}
              aria-controls={undefined}
              className="h-7 flex-none px-3 text-navy-3 hover:text-navy data-[state=active]:bg-card data-[state=active]:text-navy data-[state=active]:ring-1 data-[state=active]:ring-primary"
            >
              {o.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>
    </div>
  );
}

// ---------- small text helpers ----------

export function Muted({ children, className }: { children: ReactNode; className?: string }) {
  return <span className={cn('text-xs text-navy-3', className)}>{children}</span>;
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="py-1 text-sm text-navy-3">{children}</div>;
}
