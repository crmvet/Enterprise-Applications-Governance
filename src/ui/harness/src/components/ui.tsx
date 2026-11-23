// Shared building blocks for the four pages: panel, status badge, KPI tile, bar list, page header.
import {
  Badge,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Skeleton,
  SkeletonItem,
  Tooltip,
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
} from '@fluentui/react-components';
import {
  CheckmarkCircle12Filled,
  Clock12Regular,
  ErrorCircle12Filled,
  EyeOff16Regular,
  Info12Filled,
  Warning12Filled,
  Warning16Filled,
} from '@fluentui/react-icons';
import { useSyncExternalStore, type CSSProperties, type ReactElement, type ReactNode } from 'react';
import type { CouldNotReadItem, Envelope, Failure, StatusValue, Success, Warning } from '../data/contracts';
import { fmtDateTime, humanizeKey } from '../data/format';
import { toneOfMeaning, type Tone } from '../data/meanings';
import type { ToolState } from '../data/toolClient';
import { palette } from '../theme';

// ---------- layout helpers ----------

const NARROW = '(max-width: 719px)';

function subscribeNarrow(callback: () => void) {
  const mq = window.matchMedia(NARROW);
  mq.addEventListener('change', callback);
  return () => mq.removeEventListener('change', callback);
}

// True below 720 px: phones and narrow panes. Pages switch to lists there.
export function useNarrow(): boolean {
  return useSyncExternalStore(subscribeNarrow, () => window.matchMedia(NARROW).matches, () => false);
}

export const media = {
  narrow: '@media (max-width: 719px)',
  medium: '@media (max-width: 1023px)',
} as const;

// ---------- status badge ----------

const badgeColor: Record<Tone, 'success' | 'warning' | 'danger' | 'brand' | 'informative' | 'subtle'> = {
  success: 'success',
  warning: 'warning',
  danger: 'danger',
  brand: 'brand',
  informative: 'informative',
  subtle: 'subtle',
};

const toneIcon: Partial<Record<Tone, ReactElement>> = {
  success: <CheckmarkCircle12Filled />,
  warning: <Warning12Filled />,
  danger: <ErrorCircle12Filled />,
  informative: <Info12Filled />,
};

export function ToneBadge({ tone, children, icon }: { tone: Tone; children: ReactNode; icon?: ReactElement | null }) {
  // Status colors always come with an icon and a text label, never color alone.
  const shown = icon === null ? undefined : icon ?? toneIcon[tone];
  return (
    <Badge appearance="tint" color={badgeColor[tone]} shape="rounded" size="medium" icon={shown}>
      {children}
    </Badge>
  );
}

export function StatusBadge({ status, tone }: { status: StatusValue; tone?: Tone }) {
  const t = tone ?? toneOfMeaning[status.meaning];
  const icon = status.meaning === 'due' || status.meaning === 'pending' ? <Clock12Regular /> : undefined;
  return <ToneBadge tone={t} icon={icon}>{status.label}</ToneBadge>;
}

// ---------- panel ----------

const usePanelStyles = makeStyles({
  panel: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${palette.line}`,
    borderRadius: '10px',
    padding: '18px 20px 16px',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    rowGap: '12px',
    [media.narrow]: { padding: '14px 14px 12px', borderRadius: '8px' },
  },
  head: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '10px',
    rowGap: '6px',
    flexWrap: 'wrap',
    minHeight: '28px',
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
  },
  count: {
    display: 'inline-block',
    marginLeft: '8px',
    verticalAlign: '1px',
    lineHeight: '18px',
    fontSize: tokens.fontSizeBase200,
    color: palette.navy3,
    fontWeight: tokens.fontWeightSemibold,
    backgroundColor: palette.lightGrey,
    borderRadius: '10px',
    padding: '1px 8px',
  },
  actions: { marginLeft: 'auto', display: 'flex', columnGap: '8px', alignItems: 'center' },
  subtitle: { marginTop: '-8px', fontSize: tokens.fontSizeBase200, color: palette.navy3 },
  notes: { display: 'flex', flexDirection: 'column', rowGap: '6px' },
  hidden: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '6px',
    fontSize: tokens.fontSizeBase200,
    color: palette.navy3,
  },
  skeleton: { display: 'flex', flexDirection: 'column', rowGap: '10px', paddingTop: '4px' },
});

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
  const s = usePanelStyles();
  const { state } = props;
  let body: ReactNode;
  let notes: ReactNode = null;
  if (!state) {
    body = typeof props.children === 'function' ? null : props.children;
  } else if (state.status === 'loading') {
    body = (
      <Skeleton aria-label={`Loading ${props.title}`} className={s.skeleton}>
        {Array.from({ length: props.skeletonRows ?? 3 }, (_, i) => <SkeletonItem key={i} size={16} />)}
      </Skeleton>
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
    <section className={mergeClasses(s.panel, props.className)} id={props.id} aria-labelledby={headingId}>
      <div className={s.head}>
        <h2 className={s.title} id={headingId}>
          {props.title}
          {props.count !== undefined && <span className={s.count}>{props.count}</span>}
        </h2>
        {props.actions && <div className={s.actions}>{props.actions}</div>}
      </div>
      {props.subtitle && <div className={s.subtitle}>{props.subtitle}</div>}
      {body}
      {notes}
    </section>
  );
}

export function PanelFailure({ failure }: { failure: Failure }) {
  const code = failure.error.code;
  const intent = code === 'not_found' ? 'info' : code === 'not_authorized' || code === 'not_signed_in' || code === 'rate_limited' ? 'warning' : 'error';
  const title =
    code === 'not_found' ? 'Nothing to show'
      : code === 'not_authorized' ? 'You cannot see this section'
        : code === 'not_signed_in' ? 'Sign in to see this section'
          : code === 'rate_limited' ? 'Too many requests just now'
            : 'This section could not load';
  return (
    <MessageBar intent={intent} layout="multiline">
      <MessageBarBody>
        <MessageBarTitle>{title}</MessageBarTitle>
        {failure.error.message} The rest of the page still works. Reference {failure.correlationId}.
      </MessageBarBody>
    </MessageBar>
  );
}

export function EnvelopeNotes({ couldNotRead, warnings }: { couldNotRead: CouldNotReadItem[]; warnings: Warning[] }) {
  const s = usePanelStyles();
  if (!couldNotRead.length && !warnings.length) return null;
  return (
    <div className={s.notes}>
      {couldNotRead.map(c => (
        <div key={c.kind} className={s.hidden}>
          <EyeOff16Regular aria-hidden />
          <span>
            {c.count} {humanizeKey(c.kind).toLowerCase()}{c.count === 1 ? ' is' : 's are'} hidden by your access.
          </span>
        </div>
      ))}
      {warnings.map((w, i) => (
        <MessageBar key={i} intent="warning" layout="multiline" aria-labelledby={undefined} aria-label="Warning">
          <MessageBarBody>{w.message}</MessageBarBody>
        </MessageBar>
      ))}
    </div>
  );
}

// ---------- page header ----------

const useHeaderStyles = makeStyles({
  header: {
    display: 'flex',
    alignItems: 'flex-end',
    justifyContent: 'space-between',
    columnGap: '24px',
    rowGap: '12px',
    flexWrap: 'wrap',
    marginBottom: '20px',
    [media.narrow]: { marginBottom: '14px' },
  },
  titles: { display: 'flex', flexDirection: 'column', rowGap: '4px', minWidth: 0 },
  eyebrow: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.blueDark,
    letterSpacing: '0.04em',
    textTransform: 'uppercase',
  },
  h1: {
    margin: 0,
    fontSize: '28px',
    lineHeight: '36px',
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    [media.narrow]: { fontSize: '24px', lineHeight: '30px' },
  },
  subtitle: { fontSize: tokens.fontSizeBase300, color: palette.navy2, maxWidth: '760px' },
  meta: { fontSize: tokens.fontSizeBase200, color: palette.navy3 },
  actions: { display: 'flex', columnGap: '8px', rowGap: '8px', flexWrap: 'wrap', alignItems: 'center' },
});

export function PageHeader(props: { eyebrow?: string; title: ReactNode; subtitle?: ReactNode; meta?: ReactNode; actions?: ReactNode }) {
  const s = useHeaderStyles();
  return (
    <header className={s.header}>
      <div className={s.titles}>
        {props.eyebrow && <span className={s.eyebrow}>{props.eyebrow}</span>}
        <h1 className={s.h1}>{props.title}</h1>
        {props.subtitle && <div className={s.subtitle}>{props.subtitle}</div>}
        {props.meta && <div className={s.meta}>{props.meta}</div>}
      </div>
      {props.actions && <div className={s.actions}>{props.actions}</div>}
    </header>
  );
}

// "Data as of 22 Nov 2026, 09:00 UTC. Configuration version 7." In the display zone, which it names.
export function DataStamp({ envelope }: { envelope: Envelope<unknown> | undefined }) {
  if (!envelope || !envelope.ok) return null;
  return <>Data as of {fmtDateTime(envelope.asOf)}. Configuration version {envelope.configurationSetVersion}.</>;
}

// ---------- KPI tile ----------

const useKpiStyles = makeStyles({
  tile: {
    appearance: 'none',
    margin: 0,
    fontFamily: 'inherit',
    fontSize: 'inherit',
    lineHeight: 'inherit',
    color: 'inherit',
    textAlign: 'left',
    textDecoration: 'none',
    boxSizing: 'border-box',
    cursor: 'pointer',
    display: 'flex',
    flexDirection: 'column',
    rowGap: '2px',
    padding: '14px 16px 14px 18px',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${palette.line}`,
    borderRadius: '10px',
    position: 'relative',
    overflow: 'hidden',
    minWidth: 0,
    ':hover': { ...shorthands.borderColor(palette.lineStrong), backgroundColor: '#fbfcfe' },
    ':focus-visible': { outline: `2px solid ${palette.blue}`, outlineOffset: '2px' },
  },
  attention: {
    '::before': {
      content: '""',
      position: 'absolute',
      left: 0,
      top: 0,
      bottom: 0,
      width: '4px',
      backgroundColor: palette.gold,
    },
  },
  label: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '6px',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy2,
  },
  icon: { color: palette.goldDark, flexShrink: 0 },
  value: {
    fontSize: '32px',
    lineHeight: '40px',
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    fontVariantNumeric: 'tabular-nums',
    [media.narrow]: { fontSize: '28px', lineHeight: '36px' },
  },
  caption: { fontSize: tokens.fontSizeBase200, color: palette.navy3, lineHeight: tokens.lineHeightBase200 },
  pending: { display: 'flex', alignItems: 'center', height: '40px', [media.narrow]: { height: '36px' } },
  pendingBar: { width: '64px' },
});

// value is null while the figure is still being read: the tile shows a placeholder, never a number the server did not
// send.
export function KpiTile(props: { label: string; value: number | null; caption: string; attention?: boolean; onClick?: () => void; className?: string }) {
  const s = useKpiStyles();
  const loading = props.value === null;
  return (
    <button
      type="button"
      className={mergeClasses(s.tile, props.attention && s.attention, props.className)}
      onClick={props.onClick}
      aria-label={`${props.label}: ${loading ? 'loading' : props.value}. ${props.caption}. Opens the list.`}
      aria-busy={loading || undefined}
    >
      <span className={s.label}>
        {props.attention && <Warning16Filled className={s.icon} aria-hidden />}
        {props.label}
      </span>
      {props.value === null ? (
        <Skeleton className={s.pending} aria-hidden>
          <SkeletonItem size={28} className={s.pendingBar} />
        </Skeleton>
      ) : (
        <span className={s.value}>{props.value.toLocaleString('en-GB')}</span>
      )}
      <span className={s.caption}>{props.caption}</span>
    </button>
  );
}

// A list that shows one page says so when the server holds more rows than the page asked for (the answer carries a
// skipToken), so a short list is never read as the whole list.
export function MoreRowsNote({ skipToken, what }: { skipToken: string | undefined; what: string }) {
  const s = usePanelStyles();
  if (!skipToken) return null;
  return (
    <div className={s.notes}>
      <div className={s.hidden}>
        <Info12Filled aria-hidden />
        <span>The product holds more {what} than this list shows. Narrow the range, or ask an AI client for the rest.</span>
      </div>
    </div>
  );
}

// ---------- bar list (one series, horizontal) ----------

const useBarStyles = makeStyles({
  list: { listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', rowGap: '2px' },
  row: {
    appearance: 'none',
    margin: 0,
    fontFamily: 'inherit',
    fontSize: 'inherit',
    lineHeight: 'inherit',
    color: 'inherit',
    textAlign: 'left',
    textDecoration: 'none',
    boxSizing: 'border-box',
    display: 'grid',
    gridTemplateColumns: 'var(--bar-label, 190px) minmax(0, 1fr)',
    alignItems: 'center',
    columnGap: '12px',
    width: '100%',
    minHeight: '30px',
    padding: '0 6px',
    border: 'none',
    backgroundColor: 'transparent',
    borderRadius: '6px',
    cursor: 'pointer',
    ':hover': { backgroundColor: palette.lightGrey },
    ':focus-visible': { outline: `2px solid ${palette.blue}`, outlineOffset: '1px' },
    [media.narrow]: { gridTemplateColumns: 'minmax(0, 44%) minmax(0, 1fr)', columnGap: '8px' },
  },
  label: {
    fontSize: tokens.fontSizeBase300,
    color: palette.navy,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    [media.narrow]: { whiteSpace: 'normal', lineHeight: '18px', padding: '3px 0' },
  },
  track: {
    display: 'flex',
    alignItems: 'center',
    columnGap: '8px',
    height: '22px',
    borderLeft: `1px solid ${palette.lineStrong}`,
    minWidth: 0,
  },
  bar: {
    height: '14px',
    minWidth: '3px',
    borderRadius: '0 4px 4px 0',
    backgroundColor: palette.blue,
    flexShrink: 0,
  },
  barMuted: { backgroundColor: palette.muted },
  value: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    fontVariantNumeric: 'tabular-nums',
  },
});

export interface BarItem {
  key: string | null;
  label: string;
  count: number;
  muted?: boolean;
}

export function BarList(props: { items: BarItem[]; noun: [string, string]; onDrill?: (item: BarItem) => void; labelWidth?: number; ariaLabel: string }) {
  const s = useBarStyles();
  const max = Math.max(1, ...props.items.map(i => i.count));
  return (
    <ul className={s.list} aria-label={props.ariaLabel} style={props.labelWidth ? ({ '--bar-label': `${props.labelWidth}px` } as CSSProperties) : undefined}>
      {props.items.map(item => {
        const text = `${item.count} ${item.count === 1 ? props.noun[0] : props.noun[1]}: ${item.label}`;
        const pct = item.count / max;
        return (
          <li key={item.key ?? 'none'}>
            <Tooltip content={text} relationship="description" withArrow positioning="above-start">
              <button type="button" className={s.row} onClick={() => props.onDrill?.(item)}>
                <span className={s.label}>{item.label}</span>
                <span className={s.track}>
                  <span
                    className={mergeClasses(s.bar, item.muted && s.barMuted)}
                    style={{ width: `calc((100% - 36px) * ${pct.toFixed(4)})` }}
                    aria-hidden
                  />
                  <span className={s.value}>{item.count}</span>
                </span>
              </button>
            </Tooltip>
          </li>
        );
      })}
    </ul>
  );
}

// ---------- small text helpers ----------

const useTextStyles = makeStyles({
  muted: { color: palette.navy3, fontSize: tokens.fontSizeBase200 },
  empty: { color: palette.navy3, fontSize: tokens.fontSizeBase300, padding: '4px 0' },
});

export function Muted({ children, className }: { children: ReactNode; className?: string }) {
  const s = useTextStyles();
  return <span className={mergeClasses(s.muted, className)}>{children}</span>;
}

export function Empty({ children }: { children: ReactNode }) {
  const s = useTextStyles();
  return <div className={s.empty}>{children}</div>;
}
