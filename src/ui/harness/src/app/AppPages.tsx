// The web app's own views beside the four pages:
// - the applications list a drill-down opens (find_applications with the drill's filter), which leads to Application 360;
// - a plain panel for a record link of another entity, which these pages do not show;
// - a page for an address that does not exist.
import { ArrowLeft, TriangleAlert } from 'lucide-react';
import { useState } from 'react';
import type { FoundApplications } from '../data/contracts';
import { humanizeKey, plural } from '../data/format';
import { dataOf, useTool } from '../data/toolClient';
import { useHost, type ApplicationFilter } from '../harness';
import { Row, RowList } from '../components/rows';
import { DataStamp, Empty, PageHeader, Panel, TextLink } from '../components/blocks';
import { Button } from '../components/ui/button';

const PAGE_SIZE = 50;

const plain = 'm-0 max-w-[720px] text-sm leading-relaxed text-navy-2';
const mono = 'font-mono text-xs [overflow-wrap:anywhere] text-navy';

function describe(filter: ApplicationFilter, first: FoundApplications | null): string {
  const parts: string[] = [];
  const name = (keys: string[] | undefined, noun: string, fromItem: (k: string) => string | undefined) => {
    for (const k of keys ?? []) parts.push(`${noun}: ${fromItem(k) ?? humanizeKey(k)}`);
  };
  const items = first?.items ?? [];
  name(filter.lifecycleStageKeys, 'Stage', k => items.find(i => i.stage?.key === k)?.stage?.label);
  name(filter.tierKeys, 'Tier', k => items.find(i => i.tier?.key === k)?.tier?.label);
  name(filter.applicationKindKeys, 'Kind', k => items.find(i => i.kind.key === k)?.kind.label);
  return parts.length ? `Registered applications. ${parts.join('. ')}.` : 'Every registered application you can see.';
}

export function ApplicationsList({ filter }: { filter: ApplicationFilter }) {
  const host = useHost();
  // Each "Show more" adds the next page, read with the skip token the previous page returned.
  const [skipTokens, setSkipTokens] = useState<string[]>([]);
  const first = useTool('find_applications', { ...filter, top: PAGE_SIZE });
  const firstData = dataOf(first);

  return (
    <>
      <PageHeader
        eyebrow="Applications"
        title="Applications"
        subtitle={describe(filter, firstData)}
        meta={<DataStamp envelope={first.status === 'done' ? first.envelope : undefined} />}
        actions={(
          <Button variant="outline" asChild>
            <a href={host.href({ page: 'overview' })}>
              <ArrowLeft aria-hidden />
              Overview
            </a>
          </Button>
        )}
      />
      <Panel title="Matching applications" count={firstData?.totalCount} state={first} skeletonRows={6}>
        {data => (
          <>
            {data.items.length ? <ApplicationRows data={data} /> : <Empty>No registered application matches.</Empty>}
            {skipTokens.map((token, i) => (
              <MorePage key={token} filter={filter} token={token} last={i === skipTokens.length - 1} onNext={next => setSkipTokens(t => (t.includes(next) ? t : [...t, next]))} />
            ))}
            {!skipTokens.length && data.skipToken && (
              <Button className="self-start" variant="outline" onClick={() => setSkipTokens([data.skipToken!])}>
                Show more
              </Button>
            )}
            <span className="text-sm text-navy-2">{plural(data.totalCount, 'application matches', 'applications match')} in all.</span>
          </>
        )}
      </Panel>
    </>
  );
}

function MorePage({ filter, token, last, onNext }: { filter: ApplicationFilter; token: string; last: boolean; onNext: (token: string) => void }) {
  const state = useTool('find_applications', { ...filter, top: PAGE_SIZE, skipToken: token });
  const data = dataOf(state);
  if (state.status === 'loading') return <Empty>Loading more.</Empty>;
  if (!state.envelope.ok) return <Empty>{state.envelope.error.message} Reference {state.envelope.correlationId}.</Empty>;
  return (
    <>
      {data && <ApplicationRows data={data} />}
      {last && data?.skipToken && (
        <Button className="self-start" variant="outline" onClick={() => onNext(data.skipToken!)}>
          Show more
        </Button>
      )}
    </>
  );
}

function ApplicationRows({ data }: { data: FoundApplications }) {
  const host = useHost();
  return (
    <RowList label="Applications">
      {data.items.map(a => (
        <Row
          key={a.businessApplicationId}
          primary={<TextLink href={host.href({ page: 'app', id: a.businessApplicationId })}>{a.name}</TextLink>}
          secondary={[a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label ?? 'No stage yet'].join('. ')}
          tertiary={a.businessOwner
            ? `Business owner ${a.businessOwner.displayLabel}`
            : (
              <span className="inline-flex items-center gap-1 font-semibold text-gold-strong">
                <TriangleAlert className="size-3.5 shrink-0" aria-hidden />
                No business owner
              </span>
            )}
          end={<TextLink className="text-sm" href={host.href({ page: 'map', id: a.businessApplicationId })}>Dependency map</TextLink>}
        />
      ))}
    </RowList>
  );
}

export function OtherRecord({ entity, id }: { entity: string; id: string }) {
  const host = useHost();
  return (
    <>
      <PageHeader eyebrow="Record" title={humanizeKey(entity)} />
      <Panel title="Not shown here">
        <p className={plain}>
          The web app shows applications, their dependencies and the timeline. A record of this kind is not shown here.
          Your AI client can read it with the product's tools; give it this ID.
        </p>
        <span className={mono}>{id}</span>
        <TextLink className="self-start text-sm" href={host.href({ page: 'overview' })}>Go to the overview</TextLink>
      </Panel>
    </>
  );
}

export function NotFound() {
  const host = useHost();
  return (
    <>
      <PageHeader eyebrow="Not found" title="This page does not exist" />
      <Panel title="Where to go">
        <p className={plain}>The address does not match a page of the web app. Check the link, or start from the overview.</p>
        <TextLink className="self-start text-sm" href={host.href({ page: 'overview' })}>Go to the overview</TextLink>
      </Panel>
    </>
  );
}
