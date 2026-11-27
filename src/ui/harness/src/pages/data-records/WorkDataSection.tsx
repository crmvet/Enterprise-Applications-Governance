// My work's Data section (rendered in Work.tsx's right column): what the person looks after in the data taxonomy.
//   Data approvals asked of you    data-use, copy, retention and data ownership decisions that ask the person, each with
//                                  the domain, its classification, the rule and the AI flag (list_approvals view mine)
//   Your data domains              the domains the person owns, deputizes or stewards, their own or inherited from a
//                                  domain above, with the gaps in them (find_data_domains ownerObjectId, get_my_queue
//                                  kind data-gap)
//   Your data sent to AI           the applications whose uses send the person's data to AI: one list_data_uses read of
//                                  the uses that reach AI (wave 4), kept to the person's domains on the page
// Wave 4b verify round: the data approvals split as Approvals does: what the person may answer now under the title (and in its
// count), the ones a separation rule keeps them from answering under "Asked of you, but another person answers".
// Each item opens its decision page, its domain record or its use record, where the action lives. The section shows
// nothing for a person with no data part, and its gaps are not counted in the My work tab:
// a gap on a domain is a watch list for its owner, not a request made of the person.
import { ArrowRight, Bot } from 'lucide-react';
import { hrefTo, recordHref } from '../../app/links';
import { Empty, Muted, Panel, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { ClassificationBadge, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import type { DataDomainItem, QueueItem } from '../../data/contracts';
import { plural } from '../../data/format';
import { useRead } from '../../data/reads';
import { useSession } from '../../data/session';
import { dataOf } from '../../data/toolClient';
import { tabHref } from '../app360/nav';
import { splitMine } from '../work-approvals/logic';
import { ApprovalRow, KeptFromAnswering, useNow } from '../work-approvals/parts';
import { aiUsesOf, byPath, checkTitle, fixText, looksLikeId, myDomainCounts, myPartsOn, partsText, queueCheck } from './logic';
import { CheckSeverityBadge } from './parts';
import { rowHref } from './reads';

export function WorkDataSection() {
  const session = useSession();
  const now = useNow();
  const me = session.objectId;
  const canDomains = session.can('find_data_domains');
  const domains = useRead('find_data_domains', me && canDomains ? { ownerObjectId: me, top: 100 } : null);
  const queue = useRead('get_my_queue', canDomains ? { kinds: ['data-gap'], top: 50 } : null);
  const approvals = useRead('list_approvals', session.can('list_approvals') ? { view: 'mine', top: 100 } : null);

  const domainItems = byPath(dataOf(domains)?.items ?? []);
  const gapItems = dataOf(queue)?.items ?? [];
  const dataApprovals = (dataOf(approvals)?.items ?? []).filter(i => !!i.data);
  const withUses = domainItems.filter(d => d.counts.uses > 0);
  // Every open use that reaches AI, in one read (list_data_uses); the person's domains are kept on the page.
  const aiRead = useRead('list_data_uses', domains.status === 'done' && withUses.length > 0 ? { reachesAi: true, top: 100 } : null);
  const aiList = dataOf(aiRead);
  const aiFailure = aiRead.status === 'done' && !aiRead.envelope.ok ? aiRead.envelope : null;

  if (!session.known || !canDomains) return null;
  const failed = [domains, queue].map(s => (s.status === 'done' && !s.envelope.ok ? s.envelope : null)).find(Boolean) ?? null;
  const loaded = domains.status === 'done' && queue.status === 'done';
  if (!loaded) return null;
  if (failed) {
    return (
      <Panel title="Your data domains" id="work-data">
        <PanelFailure failure={failed} />
      </Panel>
    );
  }
  if (!domainItems.length && !gapItems.length && !dataApprovals.length) return null;

  const counts = myDomainCounts(domainItems, me);
  const dataSplit = splitMine(dataApprovals);
  const domainsHref = hrefTo({ page: 'data', mine: true });
  const gapsHref = hrefTo({ page: 'data', tab: 'gaps', mine: true });
  const myKeys = new Set(domainItems.map(d => d.key));
  const aiUses = aiList ? aiUsesOf(aiList.items.filter(u => myKeys.has(u.domain.key))) : [];

  return (
    <>
      {dataApprovals.length > 0 && (
        <Panel
          title="Data approvals asked of you"
          count={dataSplit.answerable.length}
          subtitle="Data use, copy, retention and data ownership decisions. Open one to answer it on its decision page."
          id="work-data-approvals"
        >
          {dataSplit.answerable.length > 0 ? (
            <RowList label="Data approvals asked of you">
              {dataSplit.answerable.map(item => <ApprovalRow key={item.approvalRecordId} item={item} now={now} view="mine" />)}
            </RowList>
          ) : <Empty>No data approval waits for your answer.</Empty>}
          {dataSplit.others.length > 0 && <KeptFromAnswering items={dataSplit.others} now={now} count={dataSplit.others.length} />}
        </Panel>
      )}

      {(domainItems.length > 0 || gapItems.length > 0) && (
        <Panel
          title="Your data domains"
          count={domainItems.length}
          subtitle="The domains you own, deputize or steward, your own and those below them, and what is missing in them."
          id="work-data"
          actions={<TextLink href={domainsHref} className="inline-flex items-center gap-x-1 text-sm font-semibold">On the Data page <ArrowRight className="size-4" aria-hidden /></TextLink>}
        >
          {domainItems.length > 0 && (
            <p className="m-0 text-sm text-navy">
              {counts.own > 0 && <>You own <TextLink href={domainsHref}>{plural(counts.own, 'domain', 'domains')}</TextLink>{counts.inherited > 0 ? <> and answer for <TextLink href={domainsHref}>{plural(counts.inherited, 'domain', 'domains')}</TextLink> below them</> : null}. </>}
              {counts.own === 0 && counts.inherited > 0 && <>You answer for <TextLink href={domainsHref}>{plural(counts.inherited, 'domain', 'domains')}</TextLink> through a domain above. </>}
              {counts.deputy > 0 && <>Deputy on <TextLink href={domainsHref}>{counts.deputy}</TextLink>. </>}
              {counts.steward > 0 && <>Steward of <TextLink href={domainsHref}>{counts.steward}</TextLink>. </>}
              {gapItems.length > 0 && <><TextLink href={gapsHref}>{plural(gapItems.length, 'gap', 'gaps')}</TextLink> to look at.</>}
            </p>
          )}
          {domainItems.length > 0 && (
            <RowList label="Your data domains">
              {domainItems.map(d => <DomainRow key={d.key} item={d} me={me} />)}
            </RowList>
          )}
          {dataOf(domains)?.skipToken && <Muted>You hold more domains than this list shows. The Data page lists them all.</Muted>}
          {gapItems.length > 0 && (
            <div className="flex flex-col gap-1.5">
              <h3 className="m-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">What is missing</h3>
              <RowList label="Data gaps in your domains">
                {gapItems.map(item => <GapRow key={`${item.subjectKind}-${item.subjectId}-${item.title}`} item={item} />)}
              </RowList>
            </div>
          )}
        </Panel>
      )}

      {withUses.length > 0 && (
        <Panel
          title="Your data sent to AI"
          count={aiList ? aiUses.length : undefined}
          subtitle="Applications whose uses send data of your domains to an AI model or agent, with the state of each decision."
          state={aiRead.status === 'loading' ? { status: 'loading' } : undefined}
          skeletonRows={2}
          id="work-data-ai"
        >
          {aiFailure && <PanelFailure failure={aiFailure} />}
          {aiList && (
            <div className="flex flex-col gap-1.5">
              {aiUses.length ? (
                <RowList label="Your data sent to AI">
                  {aiUses.map(u => (
                    <Row
                      key={u.dataUseId}
                      lead={<span className="grid size-8 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden><Bot className="size-4" /></span>}
                      primary={<TextLink href={tabHref(u.businessApplicationId, 'data')}>{u.applicationName}</TextLink>}
                      secondary={<><DomainLink domain={u.domain} />{u.aiServiceAsSeen ? `, to ${u.aiServiceAsSeen}` : ''}</>}
                      tertiary={(() => {
                        const href = rowHref(u.recordUrl, u.domain.key);
                        return href ? <TextLink href={href}>{u.recordNumber ?? 'The use'}</TextLink> : undefined;
                      })()}
                      end={<DataUseStateBadge state={u.state} approvalsGiven={u.approvalsGiven} approvalsNeeded={u.approvalsNeeded} />}
                    />
                  ))}
                </RowList>
              ) : <Empty>No application sends data of your domains to AI.</Empty>}
              {aiList.skipToken && (
                <Muted>{`Read from the first ${aiList.items.length} of ${aiList.totalCount} uses that reach AI. The Uses tab shows the rest.`}</Muted>
              )}
            </div>
          )}
        </Panel>
      )}
    </>
  );
}

function DomainRow({ item, me }: { item: DataDomainItem; me: string | null }) {
  const parts = myPartsOn(item, me);
  const gaps = item.gaps.length;
  return (
    <Row
      primary={<DomainLink domain={{ key: item.key, label: item.label, path: item.path }} />}
      secondary={partsText(parts)}
      badges={(
        <>
          <ClassificationBadge classification={item.classification} domainKey={item.key} />
          {gaps > 0 && <ToneBadge tone="warning">{plural(gaps, 'gap', 'gaps')}</ToneBadge>}
        </>
      )}
      end={item.counts.uses > 0
        ? <TextLink href={hrefTo({ page: 'data', tab: 'uses', domainKey: item.key })} className="text-xs">{plural(item.counts.uses, 'use', 'uses')}</TextLink>
        : <Muted>No uses</Muted>}
      endSub={item.counts.openDecisions > 0 ? `${plural(item.counts.openDecisions, 'open decision', 'open decisions')}` : undefined}
    />
  );
}

function GapRow({ item }: { item: QueueItem }) {
  const { check, rest } = queueCheck(item.title);
  const href = recordHref(item.recordUrl);
  const fix = fixText(item.nextTool);
  return (
    <Row
      primary={href ? <TextLink href={href}>{check ? checkTitle(check) : item.title}</TextLink> : (check ? checkTitle(check) : item.title)}
      secondary={item.why}
      tertiary={[check && !looksLikeId(rest) ? rest : null, fix ? `Fix: ${fix}` : null].filter(Boolean).join('. ') || undefined}
      end={check ? <CheckSeverityBadge check={check} /> : undefined}
    />
  );
}
