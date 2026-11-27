// Small pieces the Owners, person and Reviews pages share: a person's avatar and link, the leave and directory badges,
// a filter chip, the "approvals on their roles" figure and a scroll-to-panel helper for figures that drill on the page.
// Wave 4 (the teams group): PersonLink sends a group to its team page (/app/teams/<objectId>), which shows its members,
// roles, applications and what waits on it; the group's person page (its owner rows, ended ones and Hand over) is one
// link away from there.
import { UsersRound, X } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, teamHref } from '../../app/links';
import { TextLink, ToneBadge } from '../../components/blocks';
import { Avatar, AvatarFallback } from '../../components/ui/avatar';
import type { DirectoryState, IsoDate, PrincipalRef } from '../../data/contracts';
import { fmtDate } from '../../data/format';
import { cn } from '../../lib/utils';
import { leaveReason, type LeaveReason } from './model';

function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (!words.length) return '?';
  const first = Array.from(words[0])[0] ?? '';
  const last = words.length > 1 ? Array.from(words[words.length - 1])[0] ?? '' : '';
  return (first + last).toUpperCase();
}

// Theme tints only; the color carries no meaning, the same person keeps the same tint.
const tints = ['bg-info-soft text-info', 'bg-gold-soft text-gold-strong', 'bg-success-soft text-success', 'bg-subtle-soft text-navy-2'];

function tint(name: string): string {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
  return tints[Math.abs(h) % tints.length];
}

export function PersonAvatar({ principal, className }: { principal: PrincipalRef; className?: string }) {
  if (principal.principalKind === 'group') {
    return (
      <Avatar aria-hidden className={className}>
        <AvatarFallback className="bg-primary text-primary-foreground"><UsersRound className="size-4" aria-hidden /></AvatarFallback>
      </Avatar>
    );
  }
  const name = principal.displayLabel || principal.objectId;
  return (
    <Avatar aria-hidden className={className}>
      <AvatarFallback className={cn('text-xs font-semibold', tint(name))}>{initials(name)}</AvatarFallback>
    </Avatar>
  );
}

// A person's name as a link to their person page. A group links to its team page, which shows what it owns and more.
export function PersonLink({ principal, className }: { principal: PrincipalRef; className?: string }) {
  const href = principal.principalKind === 'group' ? teamHref(principal.objectId) : hrefTo({ page: 'person', objectId: principal.objectId.toLowerCase() });
  return (
    <TextLink className={className} href={href}>
      {principal.displayLabel || (principal.principalKind === 'group' ? 'A group' : 'A person')}
    </TextLink>
  );
}

export function leaveText(reason: LeaveReason, leavesOn: IsoDate | null, today: IsoDate): string {
  if (reason === 'disabled') return 'Account disabled in Entra';
  if (reason === 'deleted') return 'Account deleted in Entra';
  if (!leavesOn) return 'Leaving';
  return leavesOn < today ? `Left ${fmtDate(leavesOn)}` : `Leaves ${fmtDate(leavesOn)}`;
}

// The leave or directory state of a holder, as a badge with an icon and words. Nothing for a holder who is staying.
export function LeaveBadge({ holder, today }: { holder: { directoryState: DirectoryState | null; leavesOn: IsoDate | null }; today: IsoDate }) {
  const reason = leaveReason(holder);
  if (!reason) return null;
  return (
    <>
      <ToneBadge tone={reason === 'leaves' ? 'warning' : 'danger'}>{leaveText(reason, holder.leavesOn, today)}</ToneBadge>
      {reason !== 'leaves' && holder.leavesOn && <ToneBadge tone="warning">{leaveText('leaves', holder.leavesOn, today)}</ToneBadge>}
    </>
  );
}

export function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="inline-flex max-w-full items-center gap-1 rounded-md border border-info-line bg-info-soft py-0.5 pr-1 pl-2 text-xs font-semibold text-brand-dark">
      <span className="truncate">{label}</span>
      <button
        type="button"
        onClick={onRemove}
        aria-label={`Remove the filter ${label}`}
        className="inline-flex size-4 shrink-0 cursor-pointer items-center justify-center rounded-sm border-0 bg-transparent p-0 text-brand-dark hover:bg-card focus-visible:outline-2 focus-visible:outline-ring"
      >
        <X className="size-3" aria-hidden />
      </button>
    </span>
  );
}

// Moves to a panel on the same page and puts the keyboard focus on its heading, for figures that drill to rows below.
export function scrollToPanel(id: string): void {
  const panel = document.getElementById(id);
  if (!panel) return;
  panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
  const heading = document.getElementById(`${id}-title`);
  if (heading) {
    heading.setAttribute('tabindex', '-1');
    heading.focus({ preventScroll: true });
  }
}

// A label and value pair for a record's facts.
export function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex min-w-0 flex-col gap-0.5">
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 text-sm text-navy [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

export const factsClass = 'm-0 grid grid-cols-3 gap-x-6 gap-y-4 medium:grid-cols-2 narrow:grid-cols-1 narrow:gap-y-3';
export const facts2Class = 'm-0 grid grid-cols-2 gap-x-6 gap-y-4 narrow:grid-cols-1 narrow:gap-y-3';

// A row of figures: five across, three below 1024 px, two below 720 px (as on the overview).
export const kpiRowClass = 'mb-4 grid grid-cols-5 gap-3 medium:not-narrow:grid-cols-3 narrow:grid-cols-2 narrow:gap-2.5';
export const kpiRowClass3 = 'mb-4 grid grid-cols-3 gap-3 narrow:grid-cols-2 narrow:gap-2.5';
export const kpiRowClass4 = 'mb-4 grid grid-cols-4 gap-3 medium:not-narrow:grid-cols-2 narrow:grid-cols-2 narrow:gap-2.5';

// Two columns side by side, one below 1024 px.
export const twoColumnClass = 'mb-4 grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3';
