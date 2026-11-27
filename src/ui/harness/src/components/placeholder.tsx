// A page that is routed and in the menu but not built yet: its header, what it will hold, and links to the pages that
// already show part of it. The wave 2 and wave 3 page files start from it and replace it as they are built
// (docs/architecture/screens.md and docs/architecture/data-taxonomy.md).
// tabs: a page whose tabs live in the address (PageTabs) shows them between the header and the panel, so the tab
// addresses work before the page is built.
import type { ReactNode } from 'react';
import { PageHeader, Panel, TextLink } from './blocks';

export function PagePlaceholder({ eyebrow, title, subtitle, holds, links, tabs }: {
  eyebrow: string;
  title: string;
  subtitle?: ReactNode;
  // What the page will hold, one short line each.
  holds: string[];
  // Pages that already show part of it.
  links?: { label: string; href: string }[];
  tabs?: ReactNode;
}) {
  return (
    <>
      <PageHeader eyebrow={eyebrow} title={title} subtitle={subtitle} />
      {tabs}
      <Panel title="This page is being built">
        <p className="m-0 max-w-[720px] text-sm leading-relaxed text-navy-2">It will hold:</p>
        <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm text-navy-2">
          {holds.map(h => <li key={h}>{h}</li>)}
        </ul>
        {links && links.length > 0 && (
          <p className="m-0 flex flex-wrap gap-x-4 gap-y-1 text-sm">
            {links.map(l => <TextLink key={l.href} href={l.href}>{l.label}</TextLink>)}
          </p>
        )}
      </Panel>
    </>
  );
}
