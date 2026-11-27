// The data mapping record (/app/records/data-mapping/<id>): one domain's data at one location (O26). Shows the domain
// and its classification, the location, the containers, the origin, what it was copied from, and for a copy outside
// production its copy state with the copy rule and where it is set, and the copy decision.
// Reads: get_data_domain (mappings, decisions; the mapping is found in its domain, see reads.ts useRowInDomain).
// Writes, each in the shared side sheet: change or end the mapping (DataMappingSheet, save_data_mapping), propose the
// copy decision (DataDecisionSheet), withdraw a draft (withdraw_record). Approve and reject stay on the decision page.
import { ArrowRight, Pencil, ShieldCheck, Square } from 'lucide-react';
import { hrefTo, recordHrefOf, useQueryParam } from '../../app/links';
import { DataStamp, Empty, PageHeader, Panel, StatusBadge, TextLink } from '../../components/blocks';
import { ClassificationBadge, DataOriginBadge, DataRuleChip, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { AiMark, StampLine } from '../../components/stamps';
import { Button } from '../../components/ui/button';
import type { DataDomainDetail, DataMappingItem, Success } from '../../data/contracts';
import { DATA_ORIGIN_TEXT, ruleSourceText } from '../../data/dataTaxonomy';
import { fmtDate, fmtDateTime } from '../../data/format';
import { Fact, Facts } from '../changes-records/parts';
import { DataDecisionSheet, DataMappingSheet } from '../data/sheets';
import { Sticky, WithdrawSheet } from '../work-approvals/parts';
import { copyStateHelp, decisionsAbout, isDraftRow, KEY_PATTERN, needsDecision, sameId } from './logic';
import { RecordFrame, subhead, useCanWrite } from './parts';
import { rowHref, useRowInDomain } from './reads';

const grid = 'grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:gap-3';
const stack = 'flex min-w-0 flex-col gap-4 narrow:gap-3';

export function DataMappingRecord({ dataMappingId }: { dataMappingId: string }) {
  const hint = useQueryParam('domain', KEY_PATTERN);
  const lookup = useRowInDomain<DataMappingItem>({
    entity: 'data-mapping',
    id: dataMappingId,
    hint,
    sections: ['mappings', 'decisions'],
    pick: d => d.mappings,
    idOf: m => m.dataMappingId,
  });
  if (lookup.status !== 'found') {
    return (
      <RecordFrame
        eyebrow="Data mapping"
        title="Data mapping"
        state={lookup.status === 'failed' ? lookup.failure : lookup.status}
        back={{ label: 'The mapping on the Data page', href: hrefTo({ page: 'data', tab: 'mapping' }) }}
        gone={lookup.status === 'gone' && (
          <>
            The product lists open mappings only, and this one has ended or was withdrawn.
            {lookup.domainKey && <> Its data is <TextLink href={hrefTo({ page: 'data-domain', key: lookup.domainKey })}>{lookup.detail?.data.domain.path ?? 'its domain'}</TextLink>; the domain lists where its data sits now.</>}
          </>
        )}
      />
    );
  }
  return <MappingBody mapping={lookup.item} detail={lookup.detail} />;
}

function MappingBody({ mapping, detail }: { mapping: DataMappingItem; detail: Success<DataDomainDetail> }) {
  const canWrite = useCanWrite();
  const domain = detail.data.domain;
  const decisions = decisionsAbout(detail.data.decisions, 'data-mapping', mapping.dataMappingId);
  const open = decisions.find(d => d.status.meaning === 'proposed') ?? null;
  const draft = isDraftRow(mapping);
  const ended = !!mapping.endedOn;
  const copy = mapping.copyState !== null;
  const source = mapping.copiedFromMappingId ? (detail.data.mappings ?? []).find(m => sameId(m.dataMappingId, mapping.copiedFromMappingId)) ?? null : null;
  const otherHere = (detail.data.mappings ?? []).filter(m => !sameId(m.dataMappingId, mapping.dataMappingId) && sameId(m.location.dataLocationId, mapping.location.dataLocationId));
  const canEdit = canWrite('save_data_mapping') && !ended;
  const canPropose = canWrite('save_decision_proposal') && copy && !draft && !ended && !open && needsDecision(mapping.copyState);
  const canWithdraw = canWrite('withdraw_record') && draft;
  const title = `${domain.label} at ${mapping.location.label}`;

  return (
    <>
      <PageHeader
        eyebrow="Data mapping"
        title={title}
        status={(
          <span className="flex flex-wrap items-center gap-1.5">
            <StatusBadge status={mapping.status} />
            <DataOriginBadge origin={mapping.origin} />
            {mapping.copyState && <DataUseStateBadge state={mapping.copyState} approvalsNeeded={mapping.approvalsNeeded} />}
          </span>
        )}
        subtitle={`${DATA_ORIGIN_TEXT[mapping.origin]} of ${domain.path}${mapping.containers.length ? `, in ${mapping.containers.join(', ')}` : ''}.`}
        meta={(
          <span className="flex flex-col gap-0.5">
            <AiMark isAiCreated={mapping.isAiCreated} performedBy={mapping.performedBy} className="self-start" />
            <StampLine performedBy={mapping.performedBy} />
            <span><DataStamp envelope={detail} /></span>
          </span>
        )}
        actions={(
          <>
            <Sticky show={canEdit && draft}>
              <DataMappingSheet mode="confirm" mapping={mapping} trigger={<Button size="sm"><ShieldCheck aria-hidden />Check and confirm</Button>} />
            </Sticky>
            <Sticky show={canEdit && !draft}>
              <DataMappingSheet mode="edit" mapping={mapping} trigger={<Button size="sm" variant="outline"><Pencil aria-hidden />Change</Button>} />
            </Sticky>
            <Sticky show={canEdit && !draft}>
              <DataMappingSheet mode="end" mapping={mapping} trigger={<Button size="sm" variant="outline"><Square aria-hidden />End the mapping</Button>} />
            </Sticky>
            <Sticky show={canPropose}>
              <DataDecisionSheet subjectKind="data-mapping" subjectId={mapping.dataMappingId} label={`Copy of ${domain.path} at ${mapping.location.label}`} trigger={<Button size="sm" variant="outline">Propose the copy decision</Button>} />
            </Sticky>
            <Sticky show={canWithdraw}>
              <WithdrawSheet subjectKind="data-mapping" subjectId={mapping.dataMappingId} title={title} />
            </Sticky>
          </>
        )}
      />

      <div className={grid}>
        <div className={stack}>
          <Panel title="The mapping">
            <Facts>
              <Fact label="Data" sub={domain.level?.label}><DomainLink domain={mapping.domain} /></Fact>
              <Fact label="Classification"><ClassificationBadge classification={domain.classification} domainKey={domain.key} /></Fact>
              <Fact label="Location" sub={[mapping.location.platform.label, mapping.location.stage?.label].filter(Boolean).join(', ')}>
                <TextLink href={recordHrefOf('data-location', mapping.location.dataLocationId) ?? '#'}>{mapping.location.label}</TextLink>
              </Fact>
              <Fact label="Origin">{DATA_ORIGIN_TEXT[mapping.origin]}</Fact>
              <Fact label="Containers">{mapping.containers.length ? mapping.containers.join(', ') : 'The whole location'}</Fact>
              {mapping.copiedFromMappingId && (
                <Fact label="Copied from">
                  {source
                    ? <TextLink href={rowHref(source.recordUrl, domain.key) ?? '#'}>{source.location.label}</TextLink>
                    : <TextLink href={recordHrefOf('data-mapping', mapping.copiedFromMappingId) ?? '#'}>The source mapping</TextLink>}
                </Fact>
              )}
              <Fact label="Confirmed">{mapping.confirmedOn ? fmtDate(mapping.confirmedOn) : draft ? 'Not yet: a draft' : 'Not recorded'}</Fact>
              <Fact label="Started">{fmtDate(mapping.startOn, 'Not recorded')}</Fact>
              {mapping.endedOn && <Fact label="Ended">{fmtDate(mapping.endedOn)}</Fact>}
            </Facts>
          </Panel>

          {copy && mapping.copyState && (
            <Panel title="Copy outside production" subtitle="A copy of the data outside production follows the domain's copy rule.">
              <div className="flex flex-col gap-2">
                <span className="flex flex-wrap items-center gap-2">
                  <DataUseStateBadge state={mapping.copyState} approvalsNeeded={mapping.approvalsNeeded} />
                  <span className="text-sm text-navy-2">{copyStateHelp(mapping.copyState)}</span>
                </span>
                <h3 className={subhead}>The copy rule</h3>
                <span className="flex flex-wrap items-center gap-1.5">
                  <DataRuleChip name="copy" rule={domain.rules.copy} />
                  <span className="text-sm text-navy-2">{`${ruleSourceText(domain.rules.copy).charAt(0).toUpperCase()}${ruleSourceText(domain.rules.copy).slice(1)}.`}</span>
                </span>
              </div>
            </Panel>
          )}
        </div>

        <div className={stack}>
          <Panel title={copy ? 'The copy decision' : 'Decisions about this mapping'} count={decisions.length} subtitle="Open first. Approve or reject on the decision page.">
            {decisions.length ? (
              <RowList label="Decisions about this mapping">
                {decisions.map(d => (
                  <Row
                    key={d.decisionId}
                    primary={<TextLink href={hrefTo({ page: 'decision', id: d.decisionId })}>{d.statement || d.decisionType.label}</TextLink>}
                    secondary={[d.recordNumber, `${d.decisionType.label} decision`, d.outcome ? `Outcome: ${d.outcome.label}` : null].filter(Boolean).join('. ')}
                    tertiary={d.decidedOn ? `Decided ${fmtDateTime(d.decidedOn)}` : undefined}
                    end={<StatusBadge status={d.status} />}
                  />
                ))}
              </RowList>
            ) : (
              <Empty>{copy && needsDecision(mapping.copyState) ? 'No copy decision yet. The copy rule asks for one.' : 'No decision is recorded about this mapping.'}</Empty>
            )}
          </Panel>
          <Panel title="Other data at this location" count={otherHere.length}>
            {otherHere.length ? (
              <RowList label="Other mappings of this domain at the location">
                {otherHere.map(m => (
                  <Row
                    key={m.dataMappingId}
                    primary={<TextLink href={rowHref(m.recordUrl, domain.key) ?? '#'}>{m.containers.length ? m.containers.join(', ') : 'The whole location'}</TextLink>}
                    end={<DataOriginBadge origin={m.origin} short />}
                  />
                ))}
              </RowList>
            ) : <Empty>No other mapping of {domain.label} at this location.</Empty>}
            <TextLink href={recordHrefOf('data-location', mapping.location.dataLocationId) ?? '#'} className="inline-flex items-center gap-1 self-start text-sm">
              Everything at {mapping.location.label} <ArrowRight className="size-3.5" aria-hidden />
            </TextLink>
          </Panel>
        </div>
      </div>
    </>
  );
}
