// One list row that reads well at 1280 px and at 390 px: a leading visual, a primary line, muted detail lines,
// and an end column for a badge. Lists replace tables in the panels, so nothing scrolls sideways on a phone.
import { Link, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { Open12Regular } from '@fluentui/react-icons';
import type { ReactNode } from 'react';
import { palette } from '../theme';
import { media } from './ui';

const useStyles = makeStyles({
  list: { listStyle: 'none', margin: 0, padding: 0 },
  row: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr) auto',
    columnGap: '12px',
    alignItems: 'start',
    padding: '10px 0',
    borderTop: `1px solid ${palette.line}`,
    ':first-child': { borderTop: 'none', paddingTop: '2px' },
    ':last-child': { paddingBottom: '2px' },
  },
  withLead: { gridTemplateColumns: '32px minmax(0, 1fr) auto' },
  // A wide end (two or more badges) moves under the text below 720 px, so the text keeps the full width.
  wideEnd: {
    [media.narrow]: { gridTemplateColumns: 'minmax(0, 1fr)', rowGap: '6px' },
  },
  wideEndLead: {
    [media.narrow]: { gridTemplateColumns: '32px minmax(0, 1fr)', rowGap: '6px' },
  },
  endBelow: {
    [media.narrow]: { gridColumn: '-2 / -1', alignItems: 'flex-start', textAlign: 'left', maxWidth: 'none' },
  },
  endBelowBadges: { [media.narrow]: { justifyContent: 'flex-start' } },
  badges: { display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: '4px' },
  lead: { paddingTop: '1px' },
  body: { display: 'flex', flexDirection: 'column', rowGap: '2px', minWidth: 0 },
  primary: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: palette.navy,
    lineHeight: tokens.lineHeightBase300,
    overflowWrap: 'anywhere',
  },
  secondary: { fontSize: tokens.fontSizeBase200, color: palette.navy3, lineHeight: tokens.lineHeightBase200 },
  end: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    rowGap: '4px',
    textAlign: 'right',
    [media.narrow]: { maxWidth: '44vw' },
  },
  endBadges: { display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: '4px' },
  extLink: { display: 'inline-flex', alignItems: 'center', columnGap: '3px' },
});

export function RowList({ children, label }: { children: ReactNode; label?: string }) {
  const s = useStyles();
  return <ul className={s.list} aria-label={label}>{children}</ul>;
}

export interface RowProps {
  lead?: ReactNode;
  primary: ReactNode;
  secondary?: ReactNode;
  tertiary?: ReactNode;
  end?: ReactNode;
  endSub?: ReactNode;
  // Badges that belong to the text, such as deployment flags. They wrap under the detail lines.
  badges?: ReactNode;
  wideEnd?: boolean;
}

export function Row(props: RowProps) {
  const s = useStyles();
  return (
    <li className={mergeClasses(s.row, props.lead !== undefined && s.withLead, props.wideEnd && (props.lead !== undefined ? s.wideEndLead : s.wideEnd))}>
      {props.lead !== undefined && <div className={s.lead}>{props.lead}</div>}
      <div className={s.body}>
        <span className={s.primary}>{props.primary}</span>
        {props.secondary && <span className={s.secondary}>{props.secondary}</span>}
        {props.tertiary && <span className={s.secondary}>{props.tertiary}</span>}
        {props.badges && <div className={s.badges}>{props.badges}</div>}
      </div>
      {(props.end || props.endSub) && (
        <div className={mergeClasses(s.end, props.wideEnd && s.endBelow)}>
          {props.end && <div className={mergeClasses(s.endBadges, props.wideEnd && s.endBelowBadges)}>{props.end}</div>}
          {props.endSub && <span className={s.secondary}>{props.endSub}</span>}
        </div>
      )}
    </li>
  );
}

// A link out of the product (a wiki page, a run, a repository). URLs are https only (the schemas say so).
export function ExternalLink({ href, children }: { href: string; children: ReactNode }) {
  const s = useStyles();
  return (
    <Link href={href} target="_blank" rel="noopener noreferrer" className={s.extLink}>
      {children}
      <Open12Regular aria-label="opens in a new tab" />
    </Link>
  );
}
