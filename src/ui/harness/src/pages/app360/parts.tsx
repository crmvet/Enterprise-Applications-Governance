// Small badges Application 360, the dependency map and the timeline share. Application360.tsx re-exports them under
// their old names, so the pages that import them from there keep working.
import { useState, type ReactNode } from 'react';
import type { LabelledKey } from '../../data/contracts';
import { ToneBadge } from '../../components/blocks';
import { Badge } from '../../components/ui/badge';
import { cn } from '../../lib/utils';
import { criticalityTone } from './logic';

export { criticalityTone };

export function CriticalityBadge({ criticality }: { criticality: LabelledKey | null }) {
  return (
    <ToneBadge tone={criticalityTone(criticality?.key)} icon={null}>
      {criticality ? `${criticality.label} criticality` : 'Criticality not set'}
    </ToneBadge>
  );
}

// An outlined badge: red line and text for an emergency change, navy for the others.
export function ChangeTypeBadge({ type }: { type: LabelledKey }) {
  return (
    <Badge variant="outline" className={type.key === 'emergency' ? 'border-danger-line text-danger' : 'border-line-strong text-navy-2'}>
      {type.label}
    </Badge>
  );
}

// Keeps an action mounted once the person used it, even when the data read again after its write no longer offers it
// (a requirement now met, a decision now proposed, a review now done), so the engine's answer in its sheet stays in view
// until the person closes the sheet. When it no longer applies it is hidden, not removed.
export function KeepMounted({ show, children, className }: { show: boolean; children: ReactNode; className?: string }) {
  const [kept, setKept] = useState(false);
  if (!show && !kept) return null;
  return <span className={cn(show ? className ?? 'contents' : 'hidden')} onClickCapture={() => setKept(true)}>{children}</span>;
}
