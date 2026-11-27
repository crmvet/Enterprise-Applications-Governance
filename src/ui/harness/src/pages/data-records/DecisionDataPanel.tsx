// The decision page's data facts (rendered in Decision.tsx for a decision whose subject is data, get_decision data):
// the domain path, the classification, the use or the copy it decides, the governing rule and where it is set, the AI
// and de-identified flags, the state with "1 of 2 approvals", who is asked, and the configuration version the facts are
// read under. Approve and reject stay in the decision page's own answer panel (D-38).
import { ArrowRight } from 'lucide-react';
import type { ReactNode } from 'react';
import { domainHref, recordHrefOf, withParams } from '../../app/links';
import { Muted, Panel, TextLink } from '../../components/blocks';
import { ClassificationBadge, DataOriginBadge, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import type { DecisionDetail } from '../../data/contracts';
import { DATA_ACCESS_TEXT, DATA_USE_STATE_HELP } from '../../data/dataTaxonomy';
import { tabHref } from '../app360/nav';
import { aiText, deidentifiedText } from './logic';
import { ApproverList, GoverningRuleLine, NeverRuleAlert, subhead } from './parts';

const SUBJECT_TITLE: Record<NonNullable<DecisionDetail['data']>['subjectKind'], string> = {
  'data-use': 'The data use it decides',
  'data-mapping': 'The copy it decides',
  'data-domain': 'The data domain it decides about',
  'data-location': 'The data location it decides about',
};

export function DecisionDataPanel({ detail }: { detail: DecisionDetail }) {
  const data = detail.data;
  if (!data) return null;
  const settled = detail.decision.status.meaning !== 'proposed';
  const useHref = data.use ? recordHrefOf('data-use', data.use.dataUseId) : null;
  const mappingHref = data.mapping ? recordHrefOf('data-mapping', data.mapping.dataMappingId) : null;
  const withDomain = (href: string | null) => (href && data.domain ? withParams(href, { domain: data.domain.key }) : href);
  return (
    <Panel
      title={SUBJECT_TITLE[data.subjectKind]}
      subtitle={`Read under configuration version ${data.configurationSetVersion}${settled ? ', as the data stands now' : ''}.`}
      id="decision-data"
    >
      {data.state === 'breaks-rule' && data.domain && (
        <NeverRuleAlert rule={data.governingRule} explanation={null} domainPath={data.domain.path} saved />
      )}
      <dl className="m-0 flex flex-col">
        {data.domain && (
          <DataFact label="Data">
            <DomainLink domain={data.domain} />
            <ClassificationBadge classification={data.classification} domainKey={data.domain.key} />
          </DataFact>
        )}
        {data.use && (
          <DataFact label="The use">
            <span className="flex flex-col gap-0.5">
              <span>
                {useHref ? <TextLink href={withDomain(useHref) ?? '#'}>{data.use.recordNumber ?? 'The use'}</TextLink> : (data.use.recordNumber ?? 'The use')}
                {`: ${DATA_ACCESS_TEXT[data.use.access]}${data.location ? ` at ${data.location.label}` : ''}.`}
              </span>
              {data.use.purpose && <Muted>{data.use.purpose}</Muted>}
              {detail.subject.businessApplicationId && (
                <TextLink href={tabHref(detail.subject.businessApplicationId, 'data')} className="self-start text-xs">
                  {detail.subject.applicationName ? `${detail.subject.applicationName}, its data` : 'The application, its data'}
                </TextLink>
              )}
            </span>
          </DataFact>
        )}
        {data.mapping && (
          <DataFact label="The copy">
            <DataOriginBadge origin={data.mapping.origin} />
            <span>
              {mappingHref ? <TextLink href={withDomain(mappingHref) ?? '#'}>{data.location?.label ?? 'The mapping'}</TextLink> : (data.location?.label ?? 'The mapping')}
              {data.mapping.containers.length ? `, in ${data.mapping.containers.join(', ')}` : ''}
            </span>
          </DataFact>
        )}
        {!data.use && !data.mapping && data.location && (
          <DataFact label="Location">
            <TextLink href={recordHrefOf('data-location', data.location.dataLocationId) ?? '#'}>{data.location.label}</TextLink>
            <Muted>{[data.location.platform.label, data.location.stage?.label].filter(Boolean).join(', ')}</Muted>
          </DataFact>
        )}
        {data.governingRule && (
          <DataFact label="The rule">
            <GoverningRuleLine rule={data.governingRule} />
          </DataFact>
        )}
        {data.reachesAi !== null && <DataFact label="AI">{aiText({ reachesAi: data.reachesAi, aiServiceAsSeen: data.use?.aiServiceAsSeen ?? null })}</DataFact>}
        {data.isDeidentified !== null && <DataFact label="De-identified">{deidentifiedText(data.isDeidentified)}</DataFact>}
        {data.state && (
          <DataFact label="State">
            <DataUseStateBadge state={data.state} approvalsGiven={data.approvalsGiven} approvalsNeeded={data.approvalsNeeded} />
            <Muted>{DATA_USE_STATE_HELP[data.state]}</Muted>
          </DataFact>
        )}
      </dl>
      <div className="flex flex-col gap-1.5">
        <h3 className={subhead}>Who is asked</h3>
        <ApproverList approvers={data.asked} label="Who is asked about this data" empty="The rules ask nobody about this data." />
      </div>
      {data.domain && (
        <TextLink href={domainHref(data.domain.key)} className="inline-flex items-center gap-1 self-start text-sm">
          The domain, its owners and its rules <ArrowRight className="size-3.5" aria-hidden />
        </TextLink>
      )}
    </Panel>
  );
}

function DataFact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[150px_minmax(0,1fr)] items-start gap-x-4 border-t border-line py-2 first:border-t-0 first:pt-0 narrow:grid-cols-1 narrow:gap-y-0.5">
      <dt className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{label}</dt>
      <dd className="m-0 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-navy [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}
