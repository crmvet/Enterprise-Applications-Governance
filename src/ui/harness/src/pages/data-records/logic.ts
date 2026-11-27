// The data records' logic with no React in it, tested in logic.test.ts: how a use or a mapping is found when no tool
// reads it by ID, the Application 360 summary line, the person's part on a domain, who a data decision asks in words,
// the configuration versions that touched a domain, the findings a page shows as banners, and the retention state of
// a retiring application's uses. Every state and every rule comes from the engine; these functions only put its answer
// into words or pick rows out of a read.
import type {
  ConfigurationChanges,
  DataApprover,
  DataCheckCode,
  DataDomainDetail,
  DataDomainItem,
  DataFinding,
  DataLocationItem,
  DataMappingItem,
  DataUseItem,
  DecisionLogItem,
  DependencyImpact,
  LabelledKey,
  RulesExplained,
} from '../../data/contracts';
import { DATA_CHECKS, DATA_USE_STATE_HELP, dataCheckOf } from '../../data/dataTaxonomy';

export const KEY_PATTERN = /^[a-z][a-z0-9-]{0,63}$/;

// True for a row ID, which a page never shows a person (a queue title falls back to the subject's ID).
export function looksLikeId(text: string): boolean {
  return /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(text.trim());
}

export const sameId = (a: string | null | undefined, b: string | null | undefined): boolean =>
  !!a && !!b && a.toLowerCase() === b.toLowerCase();

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

// ---------------------------------------------------------------------------------------------------------------------
// Finding a use or a mapping by its ID
// ---------------------------------------------------------------------------------------------------------------------

// No tool reads one use or one mapping by its ID. get_data_domain lists the open uses and mappings of a domain, so a
// record page needs the domain. A link from these pages carries it (?domain=<key>). Without it, explain_rules (aspect
// data) traces the row's domain chain from the top down: one data-domain step per level, then the classification's
// default, then the step that governs, which names a level already traced. The row's domain is the last level traced
// for the first time.
export function domainKeyOfTrace(explained: RulesExplained | null | undefined): string | null {
  const aspect = explained?.aspects.find(a => a.aspect === 'data');
  if (!aspect) return null;
  const seen = new Set<string>();
  let last: string | null = null;
  for (const step of aspect.trace) {
    if (step.entity !== 'data-domain' || !step.key || seen.has(step.key)) continue;
    seen.add(step.key);
    last = step.key;
  }
  return last;
}

// ---------------------------------------------------------------------------------------------------------------------
// Application 360: the summary line
// ---------------------------------------------------------------------------------------------------------------------

export interface DataSummary {
  // Distinct domains read only, and distinct domains written (write or read and write), as the engine counts them in
  // get_application_card's own summary.
  reads: number;
  writes: number;
  highest: LabelledKey | null;
  // The latest sensitivity answer, and true when a data check (DC-09) says it is below the data.
  sensitivity: string | null;
  belowData: boolean;
  // Set when there is no open use: "holds no business data" or "declares no data use yet".
  none: string | null;
}

export function dataSummary(input: {
  uses: DataUseItem[];
  highest: LabelledKey | null;
  sensitivity: string | null;
  findings: DataFinding[];
  holdsNoBusinessData: boolean | null;
}): DataSummary {
  const open = input.uses.filter(u => !u.endedOn);
  const domains = (pick: (u: DataUseItem) => boolean) => new Set(open.filter(pick).map(u => u.domain.key)).size;
  return {
    reads: domains(u => u.access === 'read'),
    writes: domains(u => u.access === 'write' || u.access === 'read-write'),
    highest: input.highest,
    sensitivity: input.sensitivity,
    belowData: input.findings.some(f => f.check === 'DC-09'),
    none: open.length ? null : input.holdsNoBusinessData ? 'holds no business data' : 'declares no data use yet',
  };
}

// "Reads 3 domains, writes 1. Highest class: Restricted. Sensitivity answer: Internal (below the data)."
export function dataSummaryText(s: DataSummary, applicationName: string): string {
  const parts: string[] = [];
  parts.push(s.none ? `${applicationName} ${s.none}.` : `Reads ${count(s.reads, 'domain', 'domains')}, writes ${s.writes}.`);
  if (s.highest) parts.push(`Highest class: ${s.highest.label}.`);
  if (s.sensitivity) parts.push(`Sensitivity answer: ${s.sensitivity}${s.belowData ? ' (below the data)' : ''}.`);
  else if (s.belowData) parts.push('The sensitivity answer is below the data.');
  return parts.join(' ');
}

// The findings Application 360 shows as banners (DC-09 sensitivity below the data, DC-10 undeclared data, DC-11 mapping
// missing), retention (DC-13), and the rest.
export function splitFindings(findings: DataFinding[]): Record<'dc09' | 'dc10' | 'dc11' | 'dc13' | 'other', DataFinding[]> {
  const out: Record<'dc09' | 'dc10' | 'dc11' | 'dc13' | 'other', DataFinding[]> = { dc09: [], dc10: [], dc11: [], dc13: [], other: [] };
  for (const f of findings) {
    if (f.check === 'DC-09') out.dc09.push(f);
    else if (f.check === 'DC-10') out.dc10.push(f);
    else if (f.check === 'DC-11') out.dc11.push(f);
    else if (f.check === 'DC-13') out.dc13.push(f);
    else out.other.push(f);
  }
  return out;
}

// The data decisions in a decision log: data use, data retention, and any decision about a use, a mapping, a domain or
// a location. Open ones first.
export function dataDecisionsOf(items: DecisionLogItem[]): DecisionLogItem[] {
  const list = items.filter(d => d.status.meaning !== 'withdrawn'
    && (d.decisionType.key === 'data-use' || d.decisionType.key === 'data-retention' || d.subjectKind.startsWith('data-')));
  return [...list.filter(d => d.status.meaning === 'proposed'), ...list.filter(d => d.status.meaning !== 'proposed')];
}

// The open uses that send data to AI.
export function aiUsesOf(uses: DataUseItem[]): DataUseItem[] {
  return uses.filter(u => u.reachesAi && !u.endedOn);
}

// The locations an application's uses reach, each with the domains it reaches there.
export function locationsOfUses(uses: DataUseItem[]): { location: NonNullable<DataUseItem['location']>; domains: DataUseItem['domain'][] }[] {
  const byId = new Map<string, { location: NonNullable<DataUseItem['location']>; domains: DataUseItem['domain'][] }>();
  for (const u of uses) {
    if (!u.location || u.endedOn) continue;
    const key = u.location.dataLocationId.toLowerCase();
    const entry = byId.get(key) ?? { location: u.location, domains: [] };
    if (!entry.domains.some(d => d.key === u.domain.key)) entry.domains.push(u.domain);
    byId.set(key, entry);
  }
  return [...byId.values()].sort((a, b) => a.location.label.localeCompare(b.location.label));
}

// ---------------------------------------------------------------------------------------------------------------------
// Retention when an application retires (P09, DC-13)
// ---------------------------------------------------------------------------------------------------------------------

export type RetentionState = 'decided' | 'waiting' | 'open';

export interface RetentionRow {
  use: DataUseItem;
  state: RetentionState;
  decision: DecisionLogItem | null;
}

// Each open use with its data-retention decision: the proposed one first, else the latest accepted one, else open.
export function retentionRows(uses: DataUseItem[], decisions: DecisionLogItem[]): RetentionRow[] {
  const retention = decisions.filter(d => d.decisionType.key === 'data-retention' && d.subjectKind === 'data-use');
  return uses.filter(u => !u.endedOn).map(use => {
    const mine = retention.filter(d => sameId(d.subjectId, use.dataUseId));
    const waiting = mine.find(d => d.status.meaning === 'proposed') ?? null;
    if (waiting) return { use, state: 'waiting' as const, decision: waiting };
    const decided = mine.find(d => d.status.meaning === 'accepted') ?? null;
    return decided ? { use, state: 'decided' as const, decision: decided } : { use, state: 'open' as const, decision: null };
  });
}

// ---------------------------------------------------------------------------------------------------------------------
// My work: the person's part on a domain
// ---------------------------------------------------------------------------------------------------------------------

export type DomainPart = 'owner' | 'deputy' | 'steward';

export interface MyPart {
  part: DomainPart;
  // The domain above it is set on; null when it is set on the domain itself.
  inheritedFrom: LabelledKey | null;
}

export function myPartsOn(item: DataDomainItem, objectId: string | null): MyPart[] {
  if (!objectId) return [];
  const parts: MyPart[] = [];
  if (item.owner && sameId(item.owner.principal.objectId, objectId)) parts.push({ part: 'owner', inheritedFrom: item.owner.inheritedFrom });
  // The deputy stands in for the owner, so it comes from where the owner is set.
  if (item.deputy && sameId(item.deputy.objectId, objectId)) parts.push({ part: 'deputy', inheritedFrom: item.owner?.inheritedFrom ?? null });
  if (item.steward && sameId(item.steward.principal.objectId, objectId)) parts.push({ part: 'steward', inheritedFrom: item.steward.inheritedFrom });
  return parts;
}

const PART_TEXT: Record<DomainPart, string> = { owner: 'Data owner', deputy: 'Deputy data owner', steward: 'Data steward' };

// "Data owner", "Deputy data owner, from People", or the fallback for a domain the search found through a level above.
export function partsText(parts: MyPart[]): string {
  if (!parts.length) return 'Yours through a domain above it';
  return parts.map(p => `${PART_TEXT[p.part]}${p.inheritedFrom ? `, from ${p.inheritedFrom.label}` : ''}`).join('; ');
}

// The person's domains in path order, those set on the domain itself counted apart from the inherited ones.
export function myDomainCounts(items: DataDomainItem[], objectId: string | null): { own: number; inherited: number; deputy: number; steward: number } {
  let own = 0;
  let inherited = 0;
  let deputy = 0;
  let steward = 0;
  for (const item of items) {
    const parts = myPartsOn(item, objectId);
    if (parts.some(p => p.part === 'owner' && !p.inheritedFrom)) own += 1;
    else if (parts.some(p => p.part === 'owner')) inherited += 1;
    if (parts.some(p => p.part === 'deputy')) deputy += 1;
    if (parts.some(p => p.part === 'steward')) steward += 1;
  }
  return { own, inherited, deputy, steward };
}

export function byPath<T extends { path: string }>(items: T[]): T[] {
  return [...items].sort((a, b) => a.path.localeCompare(b.path));
}

// The check of a queue item of kind data-gap, from its title ("DC-05: People / Payroll"), and the rest of the title.
export function queueCheck(title: string): { check: DataCheckCode | null; rest: string } {
  const match = /^(DC-\d{2}):\s*(.*)$/.exec(title);
  if (!match) return { check: null, rest: title };
  return { check: dataCheckOf(match[1]), rest: match[2] };
}

export function checkTitle(check: string): string {
  const code = dataCheckOf(check);
  return code ? DATA_CHECKS[code].title : 'Data check';
}

// What fixes a finding, in words, from the tool the check names.
export function fixText(tool: string | null | undefined): string | null {
  switch (tool) {
    case 'propose_owner_change':
      return 'Propose an owner';
    case 'save_data_use':
      return 'Declare, change or end the use';
    case 'save_decision_proposal':
      return 'Propose a decision';
    case 'submit_record':
      return 'Send it for decision';
    case 'save_data_mapping':
      return 'Map the data';
    case 'save_data_location':
      return 'Update the location';
    case 'answer_tier_questions':
      return 'Answer the tier questions again';
    case 'move_data_references':
      return 'Move the rows to another domain';
    case 'propose_configuration_change':
      return 'Propose a configuration change';
    default:
      return null;
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Who a data decision asks
// ---------------------------------------------------------------------------------------------------------------------

export interface ApproverWords {
  // The person asked by name, or the role whose members answer, or who is missing.
  who: string;
  // The part they answer for: "Data owner, from People", "Security reviewer".
  part: string;
  noOneToAsk: boolean;
  isConsultation: boolean;
}

export function approverWords(a: DataApprover): ApproverWords {
  const name = a.principal ? a.principal.displayLabel || a.principal.objectId : null;
  if (a.kind === 'role') {
    const role = a.role?.label ?? 'A product role';
    return { who: name ?? `Any ${role.toLowerCase()}`, part: role, noOneToAsk: a.noOneToAsk, isConsultation: a.isConsultation };
  }
  const role = a.ownerRole?.label ?? 'Owner';
  const where = a.inheritedFrom ? `, from ${a.inheritedFrom.label}` : '';
  const who = name ?? (a.noOneToAsk ? `No ${role.toLowerCase()} to ask` : `The ${role.toLowerCase()}`);
  return { who, part: `${role}${where}`, noOneToAsk: a.noOneToAsk, isConsultation: a.isConsultation };
}

// "Hana Lindqvist (data owner) and any security reviewer" for a sentence.
export function approversSentence(approvers: DataApprover[]): string {
  const counting = approvers.filter(a => !a.isConsultation);
  if (!counting.length) return 'Nobody needs to approve it.';
  const names = counting.map(a => {
    const role = (a.kind === 'role' ? a.role?.label ?? 'a product role' : a.ownerRole?.label ?? 'owner').toLowerCase();
    const name = a.principal ? a.principal.displayLabel || a.principal.objectId : null;
    if (name) return `${name} (${role}${a.inheritedFrom ? `, from ${a.inheritedFrom.label}` : ''})`;
    if (a.kind === 'role') return `any ${role}`;
    return a.noOneToAsk ? `no ${role} to ask` : `the ${role}`;
  });
  const list = names.length === 1 ? names[0] : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
  return `${list.charAt(0).toUpperCase()}${list.slice(1)}.`;
}

// ---------------------------------------------------------------------------------------------------------------------
// The domain record
// ---------------------------------------------------------------------------------------------------------------------

type OwnersOf = NonNullable<DataDomainDetail['owners']>;

// The holder set on this level itself (not inherited), for one owner role, or null.
export function ownHolder(owners: OwnersOf | null | undefined, ownerRoleKey: string): OwnersOf[number] | null {
  const row = owners?.find(o => o.ownerRole.key === ownerRoleKey) ?? null;
  return row && row.holder && !row.inheritedFrom ? row : null;
}

// The level that answers for the domain, for one owner role: where its holder is set, or null when no level holds it.
export function answeringLevel(owners: OwnersOf | null | undefined, ownerRoleKey: string, domainKey: string): string | null {
  const row = owners?.find(o => o.ownerRole.key === ownerRoleKey);
  if (!row || !row.holder) return null;
  return row.inheritedFrom?.key ?? domainKey;
}

// The configuration versions whose change summary names the domain ("Changed Data Domain `payroll`: aiUseRule."),
// newest first, with the lines that name it.
export function versionsTouching(versions: ConfigurationChanges['versions'], domainKey: string): { version: ConfigurationChanges['versions'][number]; lines: string[] }[] {
  const mark = `Data Domain \`${domainKey}\``;
  return versions
    .map(version => ({ version, lines: version.changeSummary.filter(l => l.includes(mark)) }))
    .filter(v => v.lines.length > 0)
    .sort((a, b) => b.version.version - a.version.version);
}

// A change summary line in plain words: the entity and key marks dropped ("Changed Data Domain `payroll`: aiUseRule."
// reads "Changed this domain: AI use rule.").
export function versionLineText(line: string, domainKey: string): string {
  const fields: Record<string, string> = {
    aiUseRule: 'AI use rule',
    useRule: 'use rule',
    copyToTestRule: 'copy rule',
    classificationKey: 'classification',
    parentKey: 'parent',
    levelKey: 'level',
    retentionClassKey: 'retention class',
    label: 'name',
    description: 'description',
    examples: 'examples',
    sortOrder: 'order',
    ownerRequired: 'owner required',
    declarable: 'declarable',
  };
  return line
    .replace(`Data Domain \`${domainKey}\``, 'this domain')
    .replace(/: (.+)\.$/, (_, list: string) => `: ${list.split(', ').map(f => fields[f] ?? f).join(', ')}.`);
}

// The open decisions first, then the settled ones, newest first as the read gives them.
export function openFirst<T extends { status: { meaning: string } }>(items: T[]): T[] {
  return [...items.filter(d => d.status.meaning === 'proposed'), ...items.filter(d => d.status.meaning !== 'proposed')];
}

// The mappings of a domain grouped by location, locations by name.
export function mappingsByLocation(mappings: DataMappingItem[]): { location: DataMappingItem['location']; mappings: DataMappingItem[] }[] {
  const byId = new Map<string, { location: DataMappingItem['location']; mappings: DataMappingItem[] }>();
  for (const m of mappings) {
    const key = m.location.dataLocationId.toLowerCase();
    const entry = byId.get(key) ?? { location: m.location, mappings: [] };
    entry.mappings.push(m);
    byId.set(key, entry);
  }
  return [...byId.values()].sort((a, b) => a.location.label.localeCompare(b.location.label));
}

// The decisions about one row (a use or a mapping) from a domain's decision list.
export function decisionsAbout(decisions: DataDomainDetail['decisions'], subjectKind: 'data-use' | 'data-mapping', id: string): NonNullable<DataDomainDetail['decisions']> {
  return openFirst((decisions ?? []).filter(d => d.subjectKind === subjectKind && sameId(d.subjectId, id)));
}

// ---------------------------------------------------------------------------------------------------------------------
// The location record
// ---------------------------------------------------------------------------------------------------------------------

export type LocationFlag = keyof DataLocationItem['flags'];

export const LOCATION_FLAG_TEXT: Record<LocationFlag, string> = {
  draft: 'Draft',
  stale: 'Not seen lately',
  unmapped: 'No data mapped',
  noTechnicalOwner: 'No technical owner',
  copyOutsideProduction: 'Copy outside production',
};

// A location's stage in words: "Test", "Production", "Live (production)", or whether it is production when no stage is set.
export function stageText(stage: { label: string } | null, isProduction: boolean): string {
  if (!stage) return isProduction ? 'Production' : 'No stage set';
  return isProduction && !/production/i.test(stage.label) ? `${stage.label} (production)` : stage.label;
}

export function locationFlags(flags: DataLocationItem['flags']): LocationFlag[] {
  return (Object.keys(LOCATION_FLAG_TEXT) as LocationFlag[]).filter(k => flags[k]);
}

// ---------------------------------------------------------------------------------------------------------------------
// The use record
// ---------------------------------------------------------------------------------------------------------------------

// The use's AI line: "Sends data to AI: Azure OpenAI, as seen", "Sends data to AI", or "No AI".
export function aiText(use: Pick<DataUseItem, 'reachesAi' | 'aiServiceAsSeen'>): string {
  if (!use.reachesAi) return 'No AI';
  return use.aiServiceAsSeen ? `Sends data to AI: ${use.aiServiceAsSeen}, as seen` : 'Sends data to AI';
}

export function deidentifiedText(isDeidentified: boolean | null | undefined): string {
  return isDeidentified ? 'De-identified data only' : 'Not de-identified';
}

// True for a use or a mapping an AI client drafted that no person has confirmed yet.
export function isDraftRow(row: { status: { meaning: string } }): boolean {
  return row.status.meaning === 'draft';
}

// A copy's state in words: the shared help for a use, said of a copy.
export function copyStateHelp(state: DataUseItem['state']): string {
  return DATA_USE_STATE_HELP[state].replace(/data-use decision/g, 'copy decision').replace(/this use/g, 'this copy').replace(/End the use/g, 'End the copy');
}

// The states in which a person may propose a decision: one is needed, or the rule tightened since it was allowed.
export function needsDecision(state: DataUseItem['state'] | null | undefined): boolean {
  return state === 'needed' || state === 're-decide';
}

// ---------------------------------------------------------------------------------------------------------------------
// "What depends on it?" from a component or a data location (get_dependency_impact, wave 4)
// ---------------------------------------------------------------------------------------------------------------------

type ImpactNodeLike = DependencyImpact['nodes'][number];

// How a node joined the walk, in words. parentName is the application one hop nearer the start (or the start itself).
export function viaText(node: Pick<ImpactNodeLike, 'via' | 'dependencyTypeKey' | 'depth'>, parentName: string, typeLabel: (key: string) => string): string {
  switch (node.via) {
    case 'part-of': return `Built from ${parentName}`;
    case 'ai-component': return `Uses ${parentName} as its AI component`;
    case 'data-use': return `Has a data use at ${parentName}`;
    case 'hosts-location': return `${parentName} is this application's location`;
    default: return node.dependencyTypeKey ? `${typeLabel(node.dependencyTypeKey)} ${parentName}` : `Depends on ${parentName}`;
  }
}

export interface ImpactGroup {
  depth: number;
  title: string;
  nodes: { node: ImpactNodeLike; parentName: string | null }[];
}

// The walk's nodes by hop: the first hop joined the start directly; each later node names the application before it on
// its path. Applications by name within a hop.
export function impactGroups(impact: Pick<DependencyImpact, 'nodes'>): ImpactGroup[] {
  const names = new Map(impact.nodes.map(n => [n.businessApplicationId.toLowerCase(), n.name]));
  const byDepth = new Map<number, ImpactGroup['nodes']>();
  for (const node of impact.nodes) {
    const parentId = node.path.length >= 2 ? node.path[node.path.length - 2] : null;
    const parentName = node.depth > 1 && parentId ? names.get(parentId.toLowerCase()) ?? null : null;
    const list = byDepth.get(node.depth) ?? [];
    list.push({ node, parentName });
    byDepth.set(node.depth, list);
  }
  return [...byDepth.entries()]
    .sort(([a], [b]) => a - b)
    .map(([depth, nodes]) => ({
      depth,
      title: depth === 1 ? 'Directly' : `${depth} hops away`,
      nodes: nodes.sort((a, b) => a.node.name.localeCompare(b.node.name)),
    }));
}
