// Pieces the data records, Application 360's Data panel, My work's Data section and the decision page share: who a
// data decision asks, the governing rule with where it is set, the never-rule alert in the engine's words, one finding,
// one use, and the page frame of a record that is still being found. Built on the shared blocks and the data chips, so a
// state or a rule reads the same everywhere (data/dataTaxonomy.ts).
import { Bot, Lock, ShieldOff, TriangleAlert, UserRoundX, Users } from 'lucide-react';
import type { ReactNode } from 'react';
import { hrefTo, recordHref } from '../../app/links';
import { Muted, PageHeader, Panel, PanelFailure, TextLink, ToneBadge } from '../../components/blocks';
import { DataRuleChip, DataSeverityBadge, DataUseStateBadge, DomainLink } from '../../components/dataChips';
import { Row, RowList } from '../../components/rows';
import { AiMark } from '../../components/stamps';
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert';
import type { DataApprover, DataFinding, DataGoverningRule, DataUseItem, Failure, WriteToolName } from '../../data/contracts';
import { DATA_ACCESS_TEXT, DATA_CHECKS, governingRuleText, ruleSourceText } from '../../data/dataTaxonomy';
import { fmtDate } from '../../data/format';
import { useSession } from '../../data/session';
import { approverWords, checkTitle, fixText } from './logic';

export const subhead = 'm-0 text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase';

// True when the person may call the write tool here: the web app's own entry, and the Tool Policy gives it. The engine
// still checks the write.
export function useCanWrite(): (tool: WriteToolName) => boolean {
  const session = useSession();
  return tool => session.isPerson && session.can(tool);
}

// ---------- a record still being found ----------

// The frame of a record page while its row is read, when the read fails, or when no read lists the row any more.
export function RecordFrame({ eyebrow, title, state, gone, back }: {
  eyebrow: string;
  title: string;
  state: 'loading' | Failure | 'gone';
  gone?: ReactNode;
  back: { label: string; href: string };
}) {
  return (
    <>
      <PageHeader eyebrow={eyebrow} title={title} />
      {state === 'loading' ? (
        <Panel title="Reading the record" state={{ status: 'loading' }} skeletonRows={4}>{() => null}</Panel>
      ) : state === 'gone' ? (
        <Alert variant="info">
          <AlertTitle>This record is no longer listed</AlertTitle>
          <AlertDescription><div className="m-0 text-sm">{gone}</div></AlertDescription>
        </Alert>
      ) : (
        <PanelFailure failure={state} />
      )}
      <p className="mt-4 text-sm text-navy-2"><TextLink href={back.href}>{back.label}</TextLink></p>
    </>
  );
}

// ---------- who a data decision asks ----------

export function ApproverList({ approvers, label, empty }: { approvers: DataApprover[]; label: string; empty?: string }) {
  if (!approvers.length) return <Muted>{empty ?? 'Nobody needs to approve it.'}</Muted>;
  return (
    <RowList label={label}>
      {approvers.map((a, i) => {
        const w = approverWords(a);
        return (
          <Row
            key={`${a.kind}-${a.role?.key ?? a.ownerRole?.key ?? 'x'}-${a.principal?.objectId ?? i}`}
            lead={(
              <span className={`grid size-8 place-items-center rounded-full ${w.noOneToAsk ? 'bg-gold-soft text-gold-strong' : 'bg-secondary text-navy-2'}`} aria-hidden>
                {w.noOneToAsk ? <UserRoundX className="size-4" /> : a.principal ? <span className="text-xs font-semibold">{initials(w.who)}</span> : <Users className="size-4" />}
              </span>
            )}
            primary={w.who}
            secondary={w.part}
            end={w.noOneToAsk
              ? <ToneBadge tone="danger">No one to ask</ToneBadge>
              : w.isConsultation ? <ToneBadge tone="informative" icon={null}>Consulted, advice only</ToneBadge> : undefined}
          />
        );
      })}
    </RowList>
  );
}

function initials(text: string): string {
  const words = text.replace(/[^A-Za-z ]/g, ' ').split(' ').filter(Boolean);
  return ((words[0]?.[0] ?? '') + (words[1]?.[0] ?? '')).toUpperCase() || '?';
}

// ---------- the governing rule ----------

// The rule that decides a use or a copy, as a chip and a sentence: "The AI use rule is Never, set on People / Payroll."
// sentence false leaves the sentence out, where the engine's own explanation follows and says it.
export function GoverningRuleLine({ rule, sentence = true }: { rule: DataGoverningRule; sentence?: boolean }) {
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      <DataRuleChip name={rule.name} rule={rule} />
      <span className="text-sm font-normal text-navy-2">{sentence ? governingRuleText(rule) : `${ruleSourceText(rule).charAt(0).toUpperCase()}${ruleSourceText(rule).slice(1)}.`}</span>
    </span>
  );
}

// A use the rule in force does not allow, in the engine's own words (the check's explanation, or the refusal), with the
// rule and where it is set. Saving such a use is refused for a person and an AI client alike (G-25).
export function NeverRuleAlert({ rule, explanation, domainPath, saved }: {
  rule: DataGoverningRule | null;
  explanation: string | null;
  domainPath: string;
  // True for a use already on record (a later rule change left it breaking); false for one not saved yet.
  saved: boolean;
}) {
  return (
    <Alert variant="error" role="alert">
      <Lock aria-hidden />
      <AlertTitle>{saved ? 'This use breaks a rule' : 'This use would be refused'}</AlertTitle>
      <AlertDescription>
        <div className="flex flex-col gap-1.5">
          {explanation && <p className="m-0">{explanation}</p>}
          {rule && (explanation
            ? <span className="flex"><GoverningRuleLine rule={rule} sentence={false} /></span>
            : <p className="m-0 text-sm">{governingRuleText(rule)} The data is {domainPath}.</p>)}
          <p className="m-0 text-xs text-navy-2">
            {saved
              ? 'No decision can allow it. End the use, or use de-identified data where the rule allows that.'
              : 'Saving it is refused for a person and an AI client alike. Nothing is written.'}
          </p>
        </div>
      </AlertDescription>
    </Alert>
  );
}

// ---------- one finding ----------

export function FindingRow({ finding, showDomain = true }: { finding: DataFinding; showDomain?: boolean }) {
  const href = recordHref(finding.recordUrl);
  const fix = fixText(finding.fixTool);
  return (
    <Row
      lead={<span className="grid size-8 place-items-center rounded-full bg-gold-soft text-gold-strong" aria-hidden><TriangleAlert className="size-4" /></span>}
      primary={href ? <TextLink href={href}>{checkTitle(finding.check)}</TextLink> : checkTitle(finding.check)}
      secondary={finding.message}
      tertiary={[
        showDomain && finding.domain ? finding.domain.path : null,
        finding.applicationName,
        fix ? `Fix: ${fix}` : null,
      ].filter(Boolean).join('. ') || undefined}
      end={<DataSeverityBadge severity={finding.severity} />}
    />
  );
}

// The design's own severity for a check, for a queue item that carries no severity.
export function CheckSeverityBadge({ check }: { check: keyof typeof DATA_CHECKS }) {
  return <DataSeverityBadge severity={DATA_CHECKS[check].severity} />;
}

// ---------- one use ----------

export function UseFlags({ use }: { use: Pick<DataUseItem, 'reachesAi' | 'aiServiceAsSeen' | 'isDeidentified' | 'isAiCreated' | 'performedBy'> }) {
  return (
    <>
      {use.reachesAi && (
        <ToneBadge tone="warning" icon={<Bot aria-hidden />}>
          {use.aiServiceAsSeen ? `To AI: ${use.aiServiceAsSeen}` : 'Sends to AI'}
        </ToneBadge>
      )}
      {use.isDeidentified && <ToneBadge tone="informative" icon={<ShieldOff aria-hidden />}>De-identified</ToneBadge>}
      <AiMark isAiCreated={use.isAiCreated} performedBy={use.performedBy} />
    </>
  );
}

// One use in a list: the domain (or the application), what it does, where, its state, and the actions the page passes.
export function UseRow({ use, href, show, actions }: {
  use: DataUseItem;
  href: string | null;
  show: 'domain' | 'application';
  actions?: ReactNode;
}) {
  const what = `${DATA_ACCESS_TEXT[use.access]}${use.location ? ` at ${use.location.label}` : ''}`;
  const number = use.recordNumber ?? 'Data use';
  return (
    <Row
      primary={show === 'domain'
        ? <DomainLink domain={use.domain} />
        : <TextLink href={hrefTo({ page: 'app', id: use.businessApplicationId })}>{use.applicationName}</TextLink>}
      secondary={(
        <>
          {href ? <TextLink href={href}>{number}</TextLink> : number}
          {`: ${what}.`}
        </>
      )}
      tertiary={[use.purpose, use.endedOn ? `Ended ${fmtDate(use.endedOn)}` : null].filter(Boolean).join('. ') || undefined}
      badges={<UseFlags use={use} />}
      end={<DataUseStateBadge state={use.state} approvalsGiven={use.approvalsGiven} approvalsNeeded={use.approvalsNeeded} />}
      endSub={actions ? <span className="mt-1 flex flex-wrap justify-end gap-1.5 narrow:justify-start">{actions}</span> : undefined}
      wideEnd
    />
  );
}
