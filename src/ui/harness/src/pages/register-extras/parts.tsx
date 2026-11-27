// Small parts the register extras share across their pages.
//   <GuidelineMark guideline={ref} />   a guideline chip that fits in a line of text: "G17", linked to the Guidelines page
//                                       at that guideline, its title read out and shown on hover. The full chip with the
//                                       rule text is components/actions.tsx GuidelineChip, for a refusal or a panel.
import { BookOpen } from 'lucide-react';
import { guidelineHref } from '../../app/links';
import type { GuidelineRef } from '../../data/contracts';

export function GuidelineMark({ guideline }: { guideline: GuidelineRef }) {
  return (
    <a
      href={guidelineHref(guideline.number)}
      title={`${guideline.number} ${guideline.title}`}
      aria-label={`Guideline ${guideline.number}: ${guideline.title}. Open the guideline.`}
      className="inline-flex items-center gap-1 rounded-md border border-info-line bg-info-soft px-1.5 py-px text-[11px] leading-4 font-semibold text-brand-dark no-underline hover:border-primary focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
    >
      <BookOpen className="size-3 shrink-0" aria-hidden />
      {guideline.number}
    </a>
  );
}
