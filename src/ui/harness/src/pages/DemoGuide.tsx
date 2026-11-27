// Demo guide (screen "Demo guide (Demo installs only)", priority 1): the demo scenarios of src/docs/demo/scenarios.md,
// S-01 to S-22, each with its presenter steps by step ID. Each scenario names the persona to act as and opens the exact
// page, with the persona switch one click away. S-01 to S-16 are the 15-minute story, S-17 to S-22 the deep dive.
//   /app/demo-guide?step=<n>   (also /app/demo). The shell renders it only in a demo install (config.json demo true);
//                              anywhere else the address is "page not found", and the menu never lists it. step is the
//                              page's own parameter: the scenario whose page the operator opened last (its place in the
//                              guide, 1 to 22), so Back returns to it.
//   ?fresh=expired|soon        the freshness panel shows only the story requests that expired, or expire within 48 hours
// Reads: useInstall() (app/install.ts) for the persona switch (D-39: personas.current(), personas.choose(name)), or on a
// local run the development sign-in (devPersonas), find_applications and list_portfolios (the IDs of the applications and
// the portfolio the story names), list_approvals (the approval requests open now, to show the demo director has run),
// get_demo_freshness (tool 102: the story requests that expired or expire within 48 hours).
// Writes: none. The demo director (src/adapters/azure/seed/demo-director.mjs) sets up the story's starting state, and the
// demo kit's refresh (src/demo/kit) renews the story requests before a meeting.
//
// Design choices (D-41, 2026-11-24), beyond those in guide-log-docs/demoStory.ts:
// - A step's link is one click: it switches to the link's persona (when the install lists that persona and it is not
//   the one in use) and opens the page. A modified click (a new tab) opens the page without switching. Before it leaves,
//   the guide writes the step into its own address, so Back lands on the guide with that step marked and the next one
//   offered.
// - The persona panel lists every persona of the install, so the operator can also switch without leaving the guide.
// - Reset is not a button: no endpoint resets the demo store or runs the demo director from the web app. The guide
//   shows the command the operator runs, and a live count of open approval requests that tells whether it ran.
// Wave 4 choices (D-41, 2026-11-25):
// - Freshness is a panel of its own beside the setup: a demo that meets an expired request fails in front of people, so
//   the guide reads get_demo_freshness on every visit and names each request that lapsed or lapses within 48 hours, each
//   linking to its decision page, where its proposer or a platform admin can also ask again. The counts filter the list
//   (?fresh=), so each number opens its rows.
// - The refresh command is exact for this install (demo-admin/logic.ts refreshCommand): the demo kit's file run with
//   Node from the repository root, with the operator's token on a demo install and none on a local run. The kit has no
//   installed command, so the page names the file.
// - On a local run (the development sign-in, no demo personas listed) the persona links switch the development
//   identity. Its personas are named demo-<name> or <name>-web, so each story persona is matched to the install's own
//   (storyPersonaName). "Act as yourself" is hidden there: the development sign-in always acts as a persona.
import { ArrowRight, Bot, CircleCheck, ClipboardCopy, Hourglass, Info, RefreshCw, UserRound, VenetianMask } from 'lucide-react';
import { useMemo, useState, type MouseEvent } from 'react';
import { hrefTo, recordHref, useQueryParam, withParams } from '../app/links';
import { navigate } from '../app/navigation';
import { personaParts, type DemoPersonaSwitch } from '../app/demoPersona';
import { useInstall } from '../app/install';
import { Empty, LinkButton, Muted, PageHeader, Panel, PanelFailure, TextLink, ToneBadge } from '../components/blocks';
import { Row, RowList } from '../components/rows';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Badge } from '../components/ui/badge';
import { Button } from '../components/ui/button';
import type { DemoFreshness, FoundApplications } from '../data/contracts';
import { fmtDateTime, plural } from '../data/format';
import { useRead } from '../data/reads';
import { dataOf, useTool } from '../data/toolClient';
import { cn } from '../lib/utils';
import { useParamNav } from './admin/parts';
import {
  askedWords,
  directorCommand,
  expiryWords,
  freshnessFilterOf,
  freshnessMatches,
  refreshCommand,
  storyPersonaName,
  type FreshnessFilter,
} from './demo-admin/logic';
import {
  COVERAGE_NOTES,
  coverageOf,
  DEMO_MINUTES,
  lookupOf,
  personaOf,
  resolveLink,
  soonLabel,
  stepOf,
  STORY_STEPS,
  type StoryLink,
  type StoryLookup,
  type StoryPersona,
  type StoryStep,
} from './guide-log-docs/demoStory';

const STEP = /^[0-9]{1,2}$/;
const stepId = (n: number) => `demo-step-${n}`;

// The persona as the install names it, or the story's own label when the install has no persona of that name.
interface PersonaView {
  // The story's name for the persona (seed/demo-personas.json).
  name: string;
  // The install's persona that plays it: the same name on a demo install, demo-<name> or <name>-web on a local run.
  installName: string | null;
  label: string;
  person: string;
  role: string | null;
  known: boolean;
}

function personaView(p: StoryPersona, personas: DemoPersonaSwitch | null): PersonaView {
  const installName = personas ? storyPersonaName(p.name, personas.personas) : null;
  const listed = installName ? personas?.personas.find(x => x.name === installName) ?? null : null;
  const label = listed?.label ?? p.label;
  const parts = personaParts(label);
  return { name: p.name, installName, label, person: parts.name, role: parts.role, known: !!listed && !!personas?.available };
}

const firstName = (person: string) => person.split(/\s+/)[0];

async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

function CopyButton({ text, label }: { text: string; label: string }) {
  const [done, setDone] = useState<'copied' | 'failed' | null>(null);
  return (
    <span className="inline-flex items-center gap-2">
      <Button type="button" variant="outline" size="sm" onClick={() => { void copyText(text).then(ok => setDone(ok ? 'copied' : 'failed')); }} aria-label={label}>
        <ClipboardCopy aria-hidden />
        Copy
      </Button>
      <span aria-live="polite" className="text-xs text-navy-3">
        {done === 'copied' ? 'Copied.' : done === 'failed' ? 'The browser did not allow copying. Select the text instead.' : ''}
      </span>
    </span>
  );
}

function CommandBlock({ title, note, command, copyLabel }: { title: string; note: string; command: string; copyLabel: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">{title}</span>
      <p className="m-0 text-xs leading-relaxed text-navy-2">{note}</p>
      <code className="block rounded-md border border-line bg-background px-3 py-2 font-mono text-xs [overflow-wrap:anywhere] text-navy">{command}</code>
      <CopyButton text={command} label={copyLabel} />
    </div>
  );
}

const origin = () => (typeof window === 'undefined' ? 'https://<this install>' : window.location.origin);

export function DemoGuide() {
  const { personas, devPersonas } = useInstall();
  // A demo install's persona switch (D-39); on a local run with no demo personas listed, the development sign-in.
  const switcher = personas?.available ? personas : devPersonas?.available ? devPersonas : null;
  const local = !!switcher && switcher === devPersonas;
  const current = switcher?.current() ?? null;
  const lastStep = stepOf(useQueryParam('step', STEP));
  const fresh = freshnessFilterOf(useQueryParam('fresh', /^(expired|soon)$/));

  // The story names applications and a portfolio; the install's own register gives their IDs.
  const apps = useTool('find_applications', { top: 100 });
  const portfolios = useRead('list_portfolios', { top: 100, includeInactive: true });
  const appRows = (dataOf(apps) as FoundApplications | null)?.items;
  const portfolioRows = dataOf(portfolios)?.items;
  const reading = apps.status === 'loading' || portfolios.status === 'loading';
  const lookup = useMemo<StoryLookup | null>(() => {
    if (!appRows && !portfolioRows) return null;
    return lookupOf(
      (appRows ?? []).map(a => ({ name: a.name, id: a.businessApplicationId })),
      (portfolioRows ?? []).map(p => ({ name: p.name, id: p.portfolioId })),
    );
  }, [appRows, portfolioRows]);

  const markStep = (n: number) => navigate(withParams(hrefTo({ page: 'demo-guide' }), { step: String(n), fresh }), true);
  const showStep = (n: number) => {
    markStep(n);
    const el = document.getElementById(stepId(n));
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    const heading = document.getElementById(`${stepId(n)}-title`);
    heading?.focus({ preventScroll: true });
  };

  // One click: the persona, then the page. The step goes into the guide's address first, so Back returns to it.
  const open = (step: StoryStep, link: StoryLink, href: string, event: MouseEvent<HTMLAnchorElement>) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    markStep(step.number);
    navigate(href);
    const name = switcher ? storyPersonaName(personaOf(step, link), switcher.personas) : null;
    if (switcher && name && current?.name !== name) switcher.choose(name);
  };

  const last = lastStep ? STORY_STEPS.find(s => s.number === lastStep) ?? null : null;
  const next = lastStep ? STORY_STEPS.find(s => s.number === lastStep + 1) ?? null : null;
  const story = STORY_STEPS.filter(s => s.time !== null);
  const deepDive = STORY_STEPS.filter(s => s.time === null);
  const span = (list: StoryStep[]) => (list.length ? `${list[0].id} to ${list[list.length - 1].id}` : '');

  return (
    <>
      <PageHeader
        eyebrow="Demo"
        title="Demo guide"
        subtitle={`Govern AI development of your apps, on fictional data: the ${DEMO_MINUTES}-minute story (${span(story)})${deepDive.length ? ` and the deep-dive scenarios (${span(deepDive)})` : ''}. Each scenario names the persona to act as, and each link opens the exact page.`}
        meta={local ? 'A local run with the development sign-in. Every name and record here is fictional.' : 'A demo install only. Every name and record here is fictional.'}
      />

      {lastStep && (
        <div className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-info-line bg-info-soft px-3 py-2.5 text-sm text-navy-2" role="status">
          <CircleCheck className="size-4 shrink-0 text-info" aria-hidden />
          <span>
            You last opened {last ? last.id : `scenario ${lastStep}`}.{' '}
            {next ? <>Next: {next.id}, {next.title}.</> : 'That was the last scenario.'}
          </span>
          {next && (
            <Button size="sm" variant="outline" onClick={() => showStep(next.number)}>
              Go to {next.id}
              <ArrowRight aria-hidden />
            </Button>
          )}
        </div>
      )}

      <div className="mb-4 grid grid-cols-[minmax(0,7fr)_minmax(0,5fr)] items-start gap-4 medium:grid-cols-1 narrow:mb-3 narrow:gap-3">
        <PersonaPanel switcher={switcher} local={local} />
        <div className="flex min-w-0 flex-col gap-4 narrow:gap-3">
          <FreshnessPanel filter={fresh} />
          <SetupPanel />
        </div>
      </div>

      {reading && <Muted className="mb-2 block">Finding the story's applications in this install's register.</Muted>}
      <ol className="m-0 mb-4 flex list-none flex-col gap-4 p-0 narrow:gap-3" aria-label="The scenarios, step by step">
        {STORY_STEPS.map(step => (
          <StepCard key={step.number} step={step} lookup={lookup} switcher={switcher} currentName={current?.name ?? null} marked={lastStep === step.number} onOpen={open} />
        ))}
      </ol>

      <CoveragePanel onStep={showStep} />
    </>
  );
}

// ---------- the persona in use ----------

function PersonaPanel({ switcher, local }: { switcher: DemoPersonaSwitch | null; local: boolean }) {
  const current = switcher?.current() ?? null;
  const parts = current ? personaParts(current.label) : null;
  if (!switcher) {
    return (
      <Panel title="Acting as">
        <Alert variant="warning">
          <Info aria-hidden />
          <AlertTitle>No persona switch in this install</AlertTitle>
          <AlertDescription>
            <p className="m-0">
              This demo install lists no demo personas, so the guide cannot switch the person you act as. Each link still
              opens its page. Sign in as the persona a step names, or ask the person who runs the install to list the
              demo personas.
            </p>
          </AlertDescription>
        </Alert>
      </Panel>
    );
  }
  return (
    <Panel
      title="Acting as"
      subtitle={local
        ? 'A local run: the development sign-in acts as the persona you choose, and the engine checks and stamps every request as that persona.'
        : 'The engine checks every request as the persona, and stamps every change with the persona and demo operator.'}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span className="inline-flex items-center gap-1.5 rounded-md bg-warning-soft px-2 py-1 text-sm font-semibold text-navy">
          <VenetianMask className="size-4 text-warning" aria-hidden />
          {parts ? parts.name : 'Yourself'}
        </span>
        {parts?.role && <Muted>{parts.role}</Muted>}
        {/* The development sign-in always acts as someone, so a local run has no "yourself" to go back to. */}
        {current && !local && (
          <Button size="sm" variant="ghost" onClick={() => switcher.choose(null)}>Act as yourself</Button>
        )}
      </div>
      <ul className="m-0 grid list-none grid-cols-2 gap-1.5 p-0 narrow:grid-cols-1" aria-label={local ? 'Development personas' : 'Demo personas'}>
        {switcher.personas.map(p => {
          const { name, role } = personaParts(p.label);
          const active = current?.name === p.name;
          return (
            <li key={p.name}>
              <button
                type="button"
                aria-pressed={active}
                onClick={() => { if (!active) switcher.choose(p.name); }}
                className={cn(
                  'flex w-full cursor-pointer items-center gap-2 rounded-md border px-2.5 py-1.5 text-left focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring',
                  active ? 'border-primary bg-info-soft' : 'border-line bg-card hover:border-primary',
                )}
              >
                <UserRound className="size-4 shrink-0 text-navy-3" aria-hidden />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-sm font-semibold text-navy">{name}</span>
                  {role && <span className="truncate text-xs text-navy-3">{role}</span>}
                </span>
                {active && <Badge variant="brand">Acting now</Badge>}
              </button>
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}

// ---------- is the demo fresh? ----------

type FreshItem = DemoFreshness['items'][number];

// The story's approval requests that expired, or expire within 48 hours (get_demo_freshness), and the command that
// renews them. A request asked again already is not listed: the tool leaves it out.
function FreshnessPanel({ filter }: { filter: FreshnessFilter | undefined }) {
  const state = useRead('get_demo_freshness', {});
  const go = useParamNav();
  const set = (f: FreshnessFilter) => go({ fresh: filter === f ? undefined : f });
  const command = refreshCommand(origin());
  const title = 'Is the demo fresh?';
  if (state.status === 'loading') {
    return <Panel title={title} id="demo-freshness" state={state} skeletonRows={2}>{() => null}</Panel>;
  }
  if (!state.envelope.ok) {
    return (
      <Panel title={title} id="demo-freshness">
        {state.envelope.error.code === 'not_found'
          ? <Muted>This install does not check the story's approval requests: the check runs on a demo install, or on a local run that holds the demo estate.</Muted>
          : <PanelFailure failure={state.envelope} />}
      </Panel>
    );
  }
  const d = state.envelope.data;
  const shown = d.items.filter(i => freshnessMatches(i.state, filter));
  return (
    <Panel title={title} id="demo-freshness" subtitle={`Checked ${fmtDateTime(state.envelope.asOf)}: the story's approval requests, against the next ${plural(d.withinHours, 'hour', 'hours')}.`}>
      {!d.needsRefresh ? (
        <p className="m-0 flex items-start gap-1.5 text-sm text-navy">
          <CircleCheck className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
          <span>{`Fresh: every story approval request has more than ${plural(d.withinHours, 'hour', 'hours')} left. Nothing to refresh before the demo.`}</span>
        </p>
      ) : (
        <>
          <Alert variant="warning">
            <Hourglass aria-hidden />
            <AlertTitle>Refresh before the demo</AlertTitle>
            <AlertDescription>
              <p className="m-0">
                {d.counts.expired > 0 && d.counts.expiresSoon > 0
                  ? `${plural(d.counts.expired, 'story request has', 'story requests have')} expired, and ${plural(d.counts.expiresSoon, 'expires', 'expire')} within ${plural(d.withinHours, 'hour', 'hours')}.`
                  : d.counts.expired > 0
                    ? `${plural(d.counts.expired, 'story request has', 'story requests have')} expired.`
                    : `${plural(d.counts.expiresSoon, 'story request expires', 'story requests expire')} within ${plural(d.withinHours, 'hour', 'hours')}.`}
                {' '}A persona who opens one would find nobody asked. Run the refresh below, then read this page again.
              </p>
            </AlertDescription>
          </Alert>
          <ul className="m-0 flex list-none flex-wrap gap-x-4 gap-y-1 p-0 text-sm" aria-label="Story requests by state">
            <li className={cn(filter === 'expired' && 'font-semibold underline')}>
              <FreshFigure value={d.counts.expired} label="expired" onOpen={() => set('expired')} pressed={filter === 'expired'} />
            </li>
            <li className={cn(filter === 'soon' && 'font-semibold underline')}>
              <FreshFigure value={d.counts.expiresSoon} label={soonLabel(d.counts.expiresSoon, d.withinHours)} onOpen={() => set('soon')} pressed={filter === 'soon'} />
            </li>
          </ul>
          {shown.length === 0 ? <Empty>No story request is in this state.</Empty> : (
            <RowList label="Story requests to refresh">
              {shown.map(i => <FreshRow key={i.approvalRecordId} item={i} />)}
            </RowList>
          )}
          {filter && <LinkButton className="self-start text-xs" onClick={() => go({ fresh: undefined })}>Show every story request to refresh</LinkButton>}
          <CommandBlock
            title="Refresh the demo"
            note="From the root of the product's repository, shortly before the demo. It asks each of these requests again, with the demo director's own steps, and leaves every fresh request as it is, so running it twice is safe. The web app cannot run it for you. One request at a time also works: its proposer or a platform admin opens it and chooses Ask again."
            command={command}
            copyLabel="Copy the demo refresh command"
          />
        </>
      )}
    </Panel>
  );
}

function FreshFigure({ value, label, onOpen, pressed }: { value: number; label: string; onOpen: () => void; pressed: boolean }) {
  if (value === 0) return <span className="tabular-nums text-navy-2">{`0 ${label}`}</span>;
  return (
    <LinkButton onClick={onOpen} aria-pressed={pressed} className="tabular-nums text-gold-strong" aria-label={`${value} ${label}. ${pressed ? 'Shows every request again.' : 'Shows only these.'}`}>
      {`${value} ${label}`}
    </LinkButton>
  );
}

function FreshRow({ item: i }: { item: FreshItem }) {
  const href = recordHref(i.decision.recordUrl) ?? hrefTo({ page: 'decision', id: i.decision.decisionId });
  const title = `${i.decision.recordNumber ? `${i.decision.recordNumber}: ` : ''}${i.decision.statement || 'A decision without a statement'}`;
  return (
    <Row
      lead={<span className="grid size-8 place-items-center rounded-full bg-secondary text-navy-2" aria-hidden><RefreshCw className="size-4" /></span>}
      primary={<TextLink href={href}>{title}</TextLink>}
      secondary={`${i.decision.decisionType.label} decision${i.application ? ` about ${i.application.name}` : ''}. Asks ${askedWords(i.request)}.`}
      tertiary={i.request.expiresOn ? `${i.state === 'expired' ? 'Expired' : 'Expires'} ${fmtDateTime(i.request.expiresOn)}` : undefined}
      end={<ToneBadge tone={i.state === 'expired' ? 'danger' : 'warning'} icon={<Hourglass aria-hidden />}>{expiryWords(i.hoursLeft, i.state)}</ToneBadge>}
    />
  );
}

// ---------- before the demo ----------

function SetupPanel() {
  const open = useRead('list_approvals', { view: 'open', top: 1 });
  const count = dataOf(open)?.totalCount ?? null;
  const command = directorCommand(origin());
  return (
    <Panel title="Before you start">
      <ul className="m-0 flex list-disc flex-col gap-1 pl-5 text-sm text-navy-2">
        <li>The top bar shows the "Demo, fictional data" badge.</li>
        <li>Claude Code is open beside the browser and connected to this install as Priya Castell.</li>
        <li>
          The demo director has made the open work live: approvals waiting for each persona, drafts, scored demand, a
          finished gap check with its claims, a registered target, documents to confirm, a completed review and a closed
          change. It never answers an approval: the people in the demo do that.
        </li>
        <li>
          Claude Code's earlier work for Priya on AI activity comes from a local run of the director, or from a demo
          install whose director ran with the operator's Claude Code token. Otherwise AI activity shows what Claude
          Code does live in S-02, S-07 and S-21, and the estate's AI answer and AI-only code approvals.
        </li>
      </ul>
      <div className="flex flex-wrap items-center gap-2 text-sm text-navy">
        {open.status === 'loading' ? <Muted>Reading the open approval requests.</Muted>
          : count === null ? <Muted>The open approval requests could not be read as this persona.</Muted>
            : count > 0 ? (
              <TextLink href={hrefTo({ page: 'approvals', view: 'open' })} aria-label={`${count} approval ${count === 1 ? 'request is' : 'requests are'} open now. Opens the list.`}>
                <ToneBadge tone="success">{`${count} approval ${count === 1 ? 'request is' : 'requests are'} open now`}</ToneBadge>
              </TextLink>
            )
              : <ToneBadge tone="warning">No approval request is open: run the demo director</ToneBadge>}
      </div>
      <CommandBlock
        title="Run the demo director"
        note="From src/adapters/azure in the product's repository, on a freshly seeded store, shortly before the demo: approval requests expire. It creates only what is missing, so running it again is safe. To start the story fresh, seed the store again first. The web app cannot do either for you."
        command={command}
        copyLabel="Copy the demo director command"
      />
    </Panel>
  );
}

// ---------- one step ----------

function StepCard(props: {
  step: StoryStep;
  lookup: StoryLookup | null;
  switcher: DemoPersonaSwitch | null;
  currentName: string | null;
  marked: boolean;
  onOpen: (step: StoryStep, link: StoryLink, href: string, event: MouseEvent<HTMLAnchorElement>) => void;
}) {
  const { step, switcher } = props;
  const people = step.personas.map(p => personaView(p, switcher));
  const titleId = `${stepId(step.number)}-title`;
  return (
    <li
      id={stepId(step.number)}
      aria-labelledby={titleId}
      className={cn(
        'flex scroll-mt-28 flex-col gap-3 rounded-xl border bg-card px-5 pt-[18px] pb-4 shadow-xs narrow:rounded-lg narrow:px-3.5 narrow:pt-3.5 narrow:pb-3',
        props.marked && 'border-primary ring-1 ring-primary',
      )}
    >
      <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
        <span className="rounded-full bg-navy px-2 py-px font-mono text-xs font-semibold text-white">{step.id}</span>
        <span className="text-xs font-medium text-navy-3 tabular-nums">{step.time ?? 'Deep dive'}</span>
        <h2 id={titleId} tabIndex={-1} className="m-0 text-base leading-6 font-semibold text-navy outline-none">{step.title}</h2>
        {props.marked && <Badge variant="brand">Opened last</Badge>}
      </div>

      <div className="flex flex-wrap items-center gap-1.5" aria-label="Personas in this step">
        {people.map((p, i) => (
          <span key={p.name} className="inline-flex items-center gap-1.5 text-sm text-navy">
            {i > 0 && <span className="text-xs text-navy-3">then</span>}
            <span className="inline-flex items-center gap-1 rounded-md border border-line bg-background px-2 py-0.5">
              <VenetianMask className="size-3.5 text-warning" aria-hidden />
              <span className="font-semibold">{p.person}</span>
              {p.role && <span className="text-xs text-navy-3">{p.role}</span>}
            </span>
            {p.installName && props.currentName === p.installName ? <Badge variant="brand">Acting now</Badge>
              : p.known && switcher && p.installName ? (
                <Button size="xs" variant="ghost" onClick={() => switcher.choose(p.installName)} aria-label={`Act as ${p.label}`}>Act as {firstName(p.person)}</Button>
              ) : switcher ? <Muted>{`This install has no persona named ${p.name}.`}</Muted> : null}
          </span>
        ))}
      </div>

      {step.aiClient && (
        <div className="flex flex-col gap-1.5 rounded-md border border-info-line bg-info-soft px-3 py-2.5">
          <span className="flex items-center gap-1.5 text-sm font-semibold text-navy">
            <Bot className="size-4 text-info" aria-hidden />
            In Claude Code, type
          </span>
          <q className="text-sm text-navy">{step.aiClient.prompt}</q>
          <span className="text-xs leading-relaxed text-navy-2">{step.aiClient.note}</span>
          <CopyButton text={step.aiClient.prompt} label="Copy the prompt for Claude Code" />
        </div>
      )}

      <ul className="m-0 flex list-none flex-col gap-1 p-0 text-sm leading-relaxed text-navy-2" aria-label={`The steps of ${step.id}`}>
        {step.points.map(p => (
          <li key={p.id} className="grid grid-cols-[64px_minmax(0,1fr)] gap-x-2 narrow:grid-cols-[56px_minmax(0,1fr)]">
            <span className="font-mono text-xs leading-relaxed font-semibold text-navy">{p.id}</span>
            <span>{p.text}</span>
          </li>
        ))}
      </ul>

      {step.notBuilt && (
        <p className="m-0 flex items-start gap-1.5 text-xs leading-relaxed text-navy-2">
          <Info className="mt-px size-3.5 shrink-0 text-info" aria-hidden />
          <span>{step.notBuilt}</span>
        </p>
      )}

      <div className="flex flex-col gap-1.5">
        <span className="text-xs font-semibold tracking-[0.04em] text-navy-3 uppercase">Go there</span>
        <ul className="m-0 flex list-none flex-wrap gap-2 p-0">
          {step.links.map(link => {
            const { route, missing, params } = resolveLink(link, props.lookup);
            const href = params ? withParams(hrefTo(route), params) : hrefTo(route);
            const persona = people.find(p => p.name === personaOf(step, link)) ?? people[0];
            const switches = !!switcher && persona.known && props.currentName !== persona.installName;
            return (
              <li key={link.label} className="flex max-w-full flex-col gap-0.5">
                <a
                  href={href}
                  onClick={e => props.onOpen(step, link, href, e)}
                  aria-label={switches ? `Act as ${persona.person} and open ${link.label}` : `Open ${link.label}`}
                  className="inline-flex min-h-8 items-center gap-1.5 rounded-md border border-line bg-card px-3 py-1 text-sm font-medium text-primary no-underline shadow-xs hover:border-primary hover:text-brand-dark focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                >
                  {link.label}
                  {switches && <span className="text-xs font-normal text-navy-3">as {firstName(persona.person)}</span>}
                  <ArrowRight className="size-3.5 shrink-0" aria-hidden />
                </a>
                {missing && props.lookup && <Muted>{`${missing} is not in the register you can see, so this opens a search instead.`}</Muted>}
              </li>
            );
          })}
        </ul>
      </div>

      <div className="flex flex-wrap items-center gap-1" aria-label="Use cases shown">
        <span className="mr-1 text-xs text-navy-3">Shows</span>
        {step.useCases.map(u => <Badge key={u} variant="subtle" className="font-mono text-[11px]">{u}</Badge>)}
      </div>
    </li>
  );
}

// ---------- coverage ----------

function CoveragePanel({ onStep }: { onStep: (n: number) => void }) {
  const rows = coverageOf(STORY_STEPS);
  return (
    <Panel title="Coverage" count={rows.length} subtitle="Which use case each scenario shows">
      {rows.length ? (
        <>
          <dl className="m-0 grid grid-cols-3 gap-x-6 gap-y-1.5 medium:grid-cols-2 narrow:grid-cols-1" aria-label="Use cases and the steps that show them">
            {rows.map(r => (
              <div key={r.useCase} className="grid grid-cols-[116px_minmax(0,1fr)] items-baseline gap-x-2 border-b border-line py-1">
                <dt className="font-mono text-xs text-navy">{r.useCase}</dt>
                <dd className="m-0 flex flex-wrap gap-x-3 gap-y-0.5">
                  {r.steps.map(n => (
                    <LinkButton key={n} className="text-sm" onClick={() => onStep(n)} aria-label={`Go to ${STORY_STEPS[n - 1].id}, ${STORY_STEPS[n - 1].title}`}>
                      {STORY_STEPS[n - 1].id}
                    </LinkButton>
                  ))}
                </dd>
              </div>
            ))}
          </dl>
          <dl className="m-0 flex flex-col gap-1" aria-label="Use cases the whole run shows, or does not show">
            {COVERAGE_NOTES.map(n => (
              <div key={n.useCase} className="grid grid-cols-[116px_minmax(0,1fr)] items-baseline gap-x-2">
                <dt className="font-mono text-xs text-navy">{n.useCase}</dt>
                <dd className="m-0 text-sm text-navy-2">{n.text}</dd>
              </div>
            ))}
          </dl>
        </>
      ) : <Empty>No use case is listed.</Empty>}
    </Panel>
  );
}
