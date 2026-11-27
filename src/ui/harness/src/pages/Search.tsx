// The global search (/app/search?q=<text>), opened from the box in the top bar.
//   A record number (DEC-0123, CHG-0007, IR-0108, REV-0015, APP-0042, CFG-0003, DU-0007, in any spelling): find_records. When
//   exactly one record has the number, the page opens it at once, in place of the search in the browser history.
//   Words: applications by name, purpose or number (find_applications, every registration status), intake requests by
//   title, need or number (list_intake_requests), and people by name (find_people, two letters or more).
// Each result opens its own page. The reads follow the person's read scope, as every list does.
import { useEffect } from 'react';
import { hrefTo, recordHref } from '../app/links';
import { navigate } from '../app/navigation';
import { normalizeBase, type RouteOf } from '../app/routes';
import { parseSearch, type SearchQuery } from '../app/shellLogic';
import { SearchBox } from '../app/TopBar';
import { DataStamp, Empty, Muted, PageHeader, Panel, StatusBadge, TextLink } from '../components/blocks';
import { personNote } from '../components/people';
import { Row, RowList } from '../components/rows';
import { AiMark } from '../components/stamps';
import { humanizeKey } from '../data/format';
import { useRead } from '../data/reads';

const TOP = 10;

const ENTITY_TEXT: Record<string, string> = {
  'business-application': 'Application',
  'intake-request': 'Intake request',
  decision: 'Decision',
  'change-request': 'Change request',
  review: 'Review',
  'configuration-draft': 'Configuration draft',
  'data-use': 'Data use',
};

export function SearchPage({ route }: { route: RouteOf<'search'> }) {
  const query = parseSearch(route.text ?? '');
  const base = normalizeBase(import.meta.env.BASE_URL);
  return (
    <>
      <PageHeader
        eyebrow="Search"
        title={query.text ? `Results for "${query.text}"` : 'Search'}
        subtitle="Applications by name or number, people by name, and any record by its number, such as DEC-0123 or CHG-0007."
      />
      <SearchBox base={base} initial={query.text} autoFocus={!query.text} className="mb-5 max-w-[560px]" />
      {!query.text ? (
        <Panel title="What you can search">
          <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm text-navy-2">
            <li>An application by its name, its purpose or its number (APP-0042).</li>
            <li>A person by name, such as "pri pa" for Priya Patel.</li>
            <li>A record by its number: an intake request (IR-0108), a decision (DEC-0145), a change (CHG-0007), a review (REV-0015).</li>
          </ul>
        </Panel>
      ) : query.recordNumber ? (
        <RecordNumberResults query={query} />
      ) : (
        <WordResults query={query} />
      )}
    </>
  );
}

function RecordNumberResults({ query }: { query: SearchQuery }) {
  const number = query.recordNumber!.number;
  const state = useRead('find_records', { recordNumber: number });
  const items = state.status === 'done' && state.envelope.ok ? state.envelope.data.items : null;
  const only = items && items.length === 1 ? recordHref(items[0].recordUrl) : null;
  useEffect(() => {
    if (only) navigate(only, true);
  }, [only]);
  return (
    <Panel title={`Records numbered ${number}`} state={state} skeletonRows={2}>
      {d => (d.items.length ? (
        <RowList label="Records">
          {d.items.map(r => {
            const href = recordHref(r.recordUrl);
            return (
              <Row
                key={`${r.entity}-${r.recordId}`}
                primary={href ? <TextLink href={href}>{r.recordNumber}: {r.label}</TextLink> : `${r.recordNumber}: ${r.label}`}
                secondary={ENTITY_TEXT[r.entity] ?? humanizeKey(r.entity)}
                end={r.status ? <StatusBadge status={r.status} /> : undefined}
              />
            );
          })}
        </RowList>
      ) : <Empty>No record you may read has the number {number}.</Empty>)}
    </Panel>
  );
}

function WordResults({ query }: { query: SearchQuery }) {
  const apps = useRead('find_applications', { text: query.text, registrationMeanings: ['registered', 'draft', 'archived'], top: TOP });
  const intakes = useRead('list_intake_requests', { view: 'all', text: query.text, top: TOP });
  const people = useRead('find_people', query.people ? { text: query.text, top: TOP } : null);
  const more = (shown: number, total: number) => (total > shown ? <Muted>The first {shown} of {total}. Type more to narrow it.</Muted> : null);
  return (
    <div className="grid grid-cols-2 items-start gap-4 medium:grid-cols-1 narrow:gap-3">
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        <Panel
          title="Applications"
          state={apps}
          count={apps.status === 'done' && apps.envelope.ok ? apps.envelope.data.totalCount : undefined}
          actions={<TextLink className="text-sm" href={hrefTo({ page: 'applications', filter: { text: query.text } })}>Open in the register</TextLink>}
        >
          {d => (d.items.length ? (
            <>
              <RowList label="Applications">
                {d.items.map(a => (
                  <Row
                    key={a.businessApplicationId}
                    primary={<TextLink href={hrefTo({ page: 'app', id: a.businessApplicationId })}>{a.name}</TextLink>}
                    secondary={[a.kind.label, a.tier?.label ?? 'No tier yet', a.stage?.label ?? 'No stage yet'].join('. ')}
                    badges={<AiMark isAiCreated={a.isAiCreated} performedBy={a.performedBy} />}
                  />
                ))}
              </RowList>
              {more(d.items.length, d.totalCount)}
            </>
          ) : <Empty>No application matches.</Empty>)}
        </Panel>
        <Panel
          title="Intake requests"
          state={intakes}
          count={intakes.status === 'done' && intakes.envelope.ok ? intakes.envelope.data.totalCount : undefined}
          actions={<TextLink className="text-sm" href={hrefTo({ page: 'intake', view: 'all', text: query.text })}>Open in intake</TextLink>}
        >
          {d => (d.items.length ? (
            <>
              <RowList label="Intake requests">
                {d.items.map(i => {
                  const href = recordHref(i.recordUrl);
                  const title = i.recordNumber ? `${i.recordNumber}: ${i.title}` : i.title;
                  return (
                    <Row
                      key={i.intakeRequestId}
                      primary={href ? <TextLink href={href}>{title}</TextLink> : title}
                      secondary={i.requester ? `Requested by ${i.requester.displayLabel}` : undefined}
                      end={<StatusBadge status={i.status} />}
                      badges={<AiMark isAiCreated={i.isAiCreated} performedBy={i.performedBy} />}
                    />
                  );
                })}
              </RowList>
              {more(d.items.length, d.totalCount)}
            </>
          ) : <Empty>No intake request matches.</Empty>)}
        </Panel>
      </div>
      <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
        {query.people ? (
          <Panel title="People" state={people} count={people.status === 'done' && people.envelope.ok ? people.envelope.data.totalCount : undefined}>
            {d => (d.items.length ? (
              <>
                <RowList label="People">
                  {d.items.map(p => (
                    <Row
                      key={p.objectId}
                      primary={<TextLink href={hrefTo({ page: 'person', objectId: p.objectId })}>{p.displayLabel}</TextLink>}
                      secondary={personNote(p) ?? undefined}
                    />
                  ))}
                </RowList>
                {more(d.items.length, d.totalCount)}
              </>
            ) : <Empty>No person the product knows matches.</Empty>)}
          </Panel>
        ) : (
          <Panel title="People">
            <Empty>Type two letters or more of a name to find a person.</Empty>
          </Panel>
        )}
        <Muted><DataStamp envelope={apps.status === 'done' ? apps.envelope : undefined} /></Muted>
      </div>
    </div>
  );
}
