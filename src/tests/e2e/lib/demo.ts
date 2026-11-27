// The shared fixtures of the suite: the browser page a scenario plays in, the persona switch, the steps, and the
// helpers that read and act the way a person does. Every spec imports scenario() and the helpers from here.
//
// One step of a presenter script is one test. Its title starts with the step ID (S-03.2), so a failing test names the
// presenter step. A check that fails is recorded and the step goes on (expect.soft); an action that cannot finish
// stops the step. Each step ends with one full-page screenshot, <scenario>-<step>-<width>.png in the run folder's
// screenshots/. No state passes between steps in memory: a step finds what it needs on the
// server by name, so any step or scenario can be played alone.
import fs from 'node:fs';
import path from 'node:path';
import { test as base, expect, type Browser, type BrowserContext, type Page, type TestInfo } from '@playwright/test';
import { apiCall, callLog, logCall, ToolRefused } from './api';
import { Recorder } from './recorder';
import { env, SRC } from './env';
import { captionScript, checkAnswerNow, findChoice, findControl, findField, readText, sheetAnswered, sheetAnswerNow, sheetOpen, writeWatchScript, type FindSpec, type PageWrite } from './page-scripts';
import { browserLogin, displayName, personaKey, PERSONAS, type Persona } from './personas';

export { expect };

const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
export const norm = (s: unknown) => String(s ?? '').replace(/\s+/g, ' ').trim();
const shorten = (s: string, n = 400) => (s.length > n ? `${s.slice(0, n - 3)}...` : s);
const escape = (s: string) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

type Scope = 'dialog' | 'main' | 'page';
type Expected = string | RegExp;

// The scripts quote a heading and the line under it as one sentence ("You are asked. As Platform admin."), and the
// page shows them as two blocks with no stop between. A quoted string also matches with the punctuation left out.
// A figure and its words may sit in two blocks with no space between ("14applications"), so a digit followed by a letter
// reads as two words.
export const loose = (s: unknown) => norm(s).replace(/[.,;:!?"\u201c\u201d]/g, '').replace(/(\d)([A-Za-z])/g, '$1 $2').replace(/\s+/g, ' ');

// The seconds the request limit asks to wait, when the text is its answer ("Too many requests just now ... Try again in
// 15 seconds."), or 0.
export function limitWait(text: string): number {
  if (!/Too many requests just now/.test(text)) return 0;
  return Math.min(60, Number(/Try again in ([0-9]+) seconds?/.exec(text)?.[1] ?? 15) || 15);
}

// The write tools of the web app (ui/harness/src/data/contracts.ts, WRITE_TOOLS), read once from its source so the list
// cannot drift. A step declares the ones it performs (Demo.writes); tools/check-actions.mjs holds every write tool the
// web app calls to a declaring step.
const WRITE_TOOLS = new Set<string>(
  [...(/export const WRITE_TOOLS[^=]*=\s*\[([\s\S]*?)\]/.exec(readContracts())?.[1] ?? '').matchAll(/'([a-z_]+)'/g)].map(m => m[1]),
);
function readContracts(): string {
  try {
    return fs.readFileSync(path.join(SRC, 'ui', 'harness', 'src', 'data', 'contracts.ts'), 'utf8');
  } catch {
    return '';
  }
}

// The answer a sheet gives when it closed itself after the write (lib/page-scripts.ts, sheetAnswerNow).
const CLOSED = /^The sheet closed after the write/;

// True when the request body asks for a check only (validateOnly: "Check first"), so nothing was written.
export function sentAsCheck(body: string | null): boolean {
  try {
    return JSON.parse(body ?? '{}')?.validateOnly === true;
  } catch {
    return false;
  }
}

// The text shows the expected words: a pattern, or a quoted string with or without its punctuation.
export const shows = (text: string, expected: Expected) =>
  expected instanceof RegExp ? expected.test(text) : text.includes(norm(expected)) || loose(text).includes(loose(expected));

// The part of the page's text where the expected words would be: around the first word of them the page shows, so a
// failure shows the nearest text, not only the page's start.
function around(text: string, expected: Expected): string {
  const source = expected instanceof RegExp ? expected.source.replace(/\\./g, ' ') : expected;
  const words = source.split(/[^A-Za-z0-9-]+/).filter(w => w.length >= 4);
  for (const w of words) {
    const i = text.indexOf(w);
    if (i >= 0) return `...${text.slice(Math.max(0, i - 200), i + 700)}...`;
  }
  return shorten(text, 900);
}

// ---------------------------------------------------------------------------------------------------------------
// The page a worker plays in. One browser context per worker, kept across the steps of a scenario, so "demo play"
// records one video per scenario and the steps flow like a person's session. A failed step restarts the worker, and
// the next step starts in a fresh context.
// ---------------------------------------------------------------------------------------------------------------

export interface Shared {
  context: BrowserContext;
  page: Page;
  inflight: Set<unknown>;
  errors: string[];
  attached: boolean;
  // The persona the page acts as now, for the request count.
  persona: Persona | null;
  // The scenarios this worker played, for the video's name.
  scenarios: Set<string>;
  firstStep: string;
  // demo play --record: the video of this worker, marked at the end of each step.
  recorder: Recorder | null;
  // The writes the page sent this step (POST /api/<tool>, write tools only), as the page's own fetch saw them
  // (writeWatchScript): the tool, the side sheet that was open (data-action-site), whether it was a check only
  // (validateOnly), and whether the server answered it ok. A write counts as performed only when it was sent for real and
  // answered ok (wave 4b review).
  posted: PageWrite[];
}

type WorkerFixtures = { width: number; height: number; shared: Shared };
type TestFixtures = { demo: Demo };

export const test = base.extend<TestFixtures, WorkerFixtures>({
  width: [1280, { scope: 'worker', option: true }],
  height: [900, { scope: 'worker', option: true }],
  shared: [
    async ({ playwright, headless, channel, launchOptions, width, height }, use) => {
      let browser: Browser;
      let context: BrowserContext;
      let page: Page;
      const attached = env.mode === 'demo';
      if (attached) {
        // The operator's Chrome, signed in to the web app: its tab's session storage holds the sign-in.
        browser = await playwright.chromium.connectOverCDP(`http://127.0.0.1:${env.cdpPort}`);
        context = browser.contexts()[0];
        const tab = context?.pages().find(p => p.url().startsWith(env.base.toString()));
        if (!context || !tab) throw new Error(`No tab of the attached Chrome shows ${env.base}: sign in to the web app there first.`);
        page = tab;
        await page.setViewportSize({ width, height });
      } else {
        browser = await playwright.chromium.launch({
          ...launchOptions,
          headless,
          ...(env.chrome ? { executablePath: env.chrome } : { channel: channel ?? 'chrome' }),
          slowMo: env.slowMo || launchOptions?.slowMo,
          // Resolve no host but the web app's, as the old story script did.
          args: [...(launchOptions?.args ?? []), `--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE ${env.base.hostname.replace(/^\[|\]$/g, '')}`],
        });
        context = await browser.newContext({
          viewport: { width, height },
          deviceScaleFactor: 1,
          locale: 'en-US',
        });
        page = await context.newPage();
      }
      if (env.captions) await context.addInitScript(captionScript);
      // Every write the page sends, with the sheet it came from and the server's answer (lib/page-scripts.ts).
      const posted: PageWrite[] = [];
      await context.exposeBinding('__e2eWrite', (_source, w: PageWrite) => {
        if (w && WRITE_TOOLS.has(w.tool)) posted.push(w);
      }).catch(() => {});
      await context.addInitScript(writeWatchScript);
      // demo play --record: one video per worker, which is one scenario in play mode (lib/recorder.ts).
      const videos = path.join(env.runDir, 'videos');
      const recorder = env.record ? new Recorder(path.join(videos, `recording-${process.pid}-${width}.avi`), width, height) : null;
      const shared: Shared = { context, page, inflight: new Set(), errors: [], attached, persona: null, scenarios: new Set(), firstStep: '', recorder, posted };
      await recorder?.start(context, page);
      page.on('request', r => {
        shared.inflight.add(r);
        const pathname = new URL(r.url()).pathname;
        if (shared.persona && pathname.startsWith('/api/')) logCall(shared.persona);
      });
      page.on('requestfinished', r => shared.inflight.delete(r));
      page.on('requestfailed', r => shared.inflight.delete(r));
      page.on('pageerror', e => shared.errors.push(e.message));
      // No trace of the operator's own Chrome (security review): a trace's network log keeps request headers,
      // and the web app's /api calls carry the operator's bearer token.
      if (!attached) await context.tracing.start({ screenshots: true, snapshots: true }).catch(() => {});
      await use(shared);
      const video = await recorder?.stop().catch(() => null);
      if (video) {
        const played = [...shared.scenarios];
        const name = played.length > 1 ? `${played[0]}-to-${played[played.length - 1]}` : played[0] ?? 'recording';
        // A failed step restarts the worker, so a scenario may come in parts: each part keeps its first step's number.
        let target = path.join(videos, `${name}-${width}.avi`);
        if (fs.existsSync(target)) target = path.join(videos, `${name}-from-${shared.firstStep.replace('.', '-')}-${width}.avi`);
        fs.renameSync(video, target);
      }
      if (!attached) await context.tracing.stop().catch(() => {});
      if (attached) {
        // The operator's tab acts as the operator again.
        await page.evaluate(k => { try { sessionStorage.removeItem(k); sessionStorage.removeItem('e2e.caption'); } catch { /* blocked storage */ } }, personaKey()).catch(() => {});
        await browser.close().catch(() => {});
      } else {
        await context.close().catch(() => {});
        await browser.close().catch(() => {});
      }
    },
    { scope: 'worker' },
  ],
  demo: async ({ shared, width }, use, testInfo) => {
    await use(new Demo(shared, testInfo, width));
  },
});

// ---------------------------------------------------------------------------------------------------------------
// Scenarios and steps
// ---------------------------------------------------------------------------------------------------------------

// One scenario of src/docs/demo/scenarios.md: a describe block whose tests are its steps, in order. Tests of a file run
// in order on one worker (playwright.config.ts), and a failed step does not skip the next ones.
//
// A step is a Playwright test called in the spec itself, so the report and a failure point at the spec's line:
// step('S-03.2 Who is asked, and who only advises', async ({ demo }) => { ... }). The title starts with the step ID.
// Before each step the hooks set its caption and trace; after it, even when it failed, its screenshot.
export function scenario(id: string, title: string, body: (step: typeof test) => void): void {
  test.describe(`${id} ${title}`, () => {
    test.beforeEach(async ({ demo }, testInfo) => {
      const stepId = testInfo.title.split(' ')[0];
      if (!stepId.startsWith(`${id}.`)) throw new Error(`The test "${testInfo.title}" does not start with a step ID of ${id}.`);
      testInfo.annotations.push({ type: 'step', description: stepId });
      await demo.begin(stepId, testInfo.title.slice(stepId.length + 1));
    });
    test.afterEach(async ({ demo }) => {
      await demo.finish();
    });
    body(test);
  });
}

// ---------------------------------------------------------------------------------------------------------------
// The helpers of one step
// ---------------------------------------------------------------------------------------------------------------

export interface SheetOptions {
  open?: FindSpec | string;
  fill?: () => Promise<void>;
  preview?: string;
  previewShows?: Expected[];
  submit: string;
  confirm?: string;
  what?: string;
  expect?: 'done' | 'refused' | 'problem' | null;
  answerShows?: Expected[];
  close?: boolean;
  // The sheet closes itself after the write (the page moves on, such as a withdrawn record that is no longer listed), so
  // its answer cannot be read: the step checks the page after it instead. Without it, a sheet that closed with no answer
  // and no answerShows is a failed check (wave 4b QA).
  closes?: true;
}

export class Demo {
  readonly page: Page;
  readonly readOnly = env.readOnly;
  readonly mode = env.mode;
  readonly aiSeeded = env.aiSeeded;
  readonly phone: boolean;
  persona: Persona | null = null;
  stepId = '';
  private stepTitle = '';
  // The writes this step performs in the web app (Demo.writes): a tool, or a tool at one side sheet ("tool@site"), and
  // whether the step skipped a write on purpose.
  private declared: string[] = [];
  private skippedWrite = false;

  constructor(private shared: Shared, private info: TestInfo, readonly width: number) {
    this.page = shared.page;
    this.phone = width < 1024;
  }

  // ---- the step's frame ----

  async begin(stepId: string, title: string) {
    this.stepId = stepId;
    this.stepTitle = title;
    this.shared.scenarios.add(stepId.split('.')[0]);
    if (!this.shared.firstStep) this.shared.firstStep = stepId;
    this.shared.errors.length = 0;
    this.shared.posted.length = 0;
    this.declared = [];
    this.skippedWrite = false;
    if (!this.shared.attached) await this.shared.context.tracing.startChunk({ title: stepId }).catch(() => {});
    await this.caption();
  }

  async finish() {
    for (const e of this.shared.errors) this.check(false, 'the page threw no error', e);
    this.checkWrites();
    try {
      await this.shot();
    } catch (e) {
      this.check(false, 'the step screenshot was taken', (e as Error).message);
    }
    // demo play: the step's end stays on screen long enough to read (QA: a step with nothing to click went by in about
    // 3 seconds, too quick for a video), with its caption.
    if (env.play && env.holdMs > 0) await sleep(env.holdMs);
    this.shared.recorder?.mark();
    if (this.shared.attached) return;
    const failed = this.info.errors.length > 0;
    const trace = path.join(this.info.outputDir, 'trace.zip');
    await this.shared.context.tracing.stopChunk(failed ? { path: trace } : undefined).catch(() => {});
    if (failed && fs.existsSync(trace)) await this.info.attach('trace', { path: trace, contentType: 'application/zip' });
  }

  // The step's screenshot: <scenario>-<step>-<width>.png, such as S-03-2-1280.png.
  async shot(label = '') {
    const [scenarioId, n] = this.stepId.split('.');
    const file = path.join(env.shotsDir, `${scenarioId}-${n}${label ? `-${label}` : ''}-${this.width}.png`);
    fs.mkdirSync(env.shotsDir, { recursive: true });
    await this.settle(5000);
    // The caption bar of demo play stays out of the pictures, except while a video records: hiding it there made the
    // caption blink off at the end of every step in the video (QA). While a video records, the picture is the viewport
    // only: a full-page capture resizes the page, and the video would show it.
    const hideCaption = env.captions && !env.record;
    if (hideCaption) await this.page.evaluate(() => { const bar = document.getElementById('e2e-caption'); if (bar) bar.style.visibility = 'hidden'; }).catch(() => {});
    await this.page.screenshot({ path: file, fullPage: !env.record, animations: 'disabled' });
    if (hideCaption) await this.page.evaluate(() => { const bar = document.getElementById('e2e-caption'); if (bar) bar.style.visibility = 'visible'; }).catch(() => {});
    await this.info.attach(path.basename(file), { path: file, contentType: 'image/png' });
    this.info.annotations.push({ type: 'screenshot', description: path.relative(env.runDir, file).split(path.sep).join('/') });
    return file;
  }

  // ---- the writes a step performs ----

  // Declares the writes this step performs in the web app, for real (not "Check first") and answered ok: a write tool
  // ('record_document_link'), or a tool at one side sheet ('record_document_link@pages/app360/sheets#DocumentCurrentSheet',
  // the sheet's data-action-site). At the end of the step each one must have been performed, unless the run is read
  // only. tools/check-actions.mjs reads these declarations: every side sheet of the web app has a step that declares it
  // (wave 4b QA).
  writes(...writes: string[]) {
    for (const w of writes) {
      const tool = w.split('@')[0];
      if (!WRITE_TOOLS.has(tool)) this.check(false, `"${tool}" is a write tool of the web app`, [...WRITE_TOOLS].join(', '));
      if (!this.declared.includes(w)) this.declared.push(w);
    }
  }

  // The declared writes were performed; the summary lists every tool and sheet the page performed, and the writes the
  // server refused.
  private checkWrites() {
    const done = this.shared.posted.filter(p => !p.check && p.ok);
    const real = [...new Set(done.map(p => p.tool))];
    const sites = [...new Set(done.map(p => `${p.tool}@${p.site ?? 'no sheet'}`))];
    const refused = this.shared.posted.filter(p => !p.check && !p.ok).map(p => `${p.tool}@${p.site ?? 'no sheet'}`);
    if (real.length) this.info.annotations.push({ type: 'performed', description: real.join(', ') });
    if (sites.length) this.info.annotations.push({ type: 'performed site', description: sites.join(', ') });
    if (refused.length) this.info.annotations.push({ type: 'refused write', description: refused.join(', ') });
    if (this.readOnly || this.skippedWrite || !this.declared.length || this.info.errors.length) return;
    for (const w of this.declared) {
      const hit = w.includes('@') ? sites.includes(w) : real.includes(w);
      this.check(hit, `the step performed ${w} in the web app, for real`, `performed: ${sites.join(', ') || 'nothing'}`);
    }
  }

  // ---- the persona switch ----

  // The persona the page signs in as from the next page load: the same storage key the web app uses, written before
  // its scripts run (the development sign-in on a local run, the demo persona switch on a Demo install).
  async as(persona: Persona) {
    this.persona = persona;
    this.shared.persona = persona;
    const key = personaKey();
    const value = browserLogin(persona);
    await this.shared.context.addInitScript(([k, v]) => { try { sessionStorage.setItem(k, v); } catch { /* blocked storage */ } }, [key, value]);
    await this.caption();
  }

  async caption() {
    if (!env.captions) return;
    const who = this.persona ? `${displayName(this.persona)}, ${PERSONAS[this.persona].role}` : '';
    const text = [this.stepId, this.stepTitle, who].filter(Boolean).join('  |  ');
    await this.shared.context.addInitScript(t => { try { sessionStorage.setItem('e2e.caption', t); } catch { /* blocked storage */ } }, text);
    await this.page
      .evaluate(t => {
        try {
          sessionStorage.setItem('e2e.caption', t);
        } catch { /* blocked storage */ }
        (window as unknown as { __e2eCaption?: () => void }).__e2eCaption?.();
      }, text)
      .catch(() => {});
  }

  // ---- navigation and reading ----

  // Ready when no request is open, nothing says it is loading, and that stays so for 400 ms.
  async settle(maxMs = 20000) {
    let stable = 0;
    let last = '';
    const until = Date.now() + maxMs;
    while (Date.now() < until) {
      let v = 'navigating';
      try {
        v = await this.page.evaluate(() => JSON.stringify([document.readyState, document.querySelectorAll('[aria-busy="true"]').length, document.body ? document.body.innerText.length : 0]));
      } catch {
        v = 'navigating';
      }
      const state = `${v}|${this.shared.inflight.size}`;
      const ready = v !== 'navigating' && JSON.parse(v)[0] === 'complete' && JSON.parse(v)[1] === 0 && this.shared.inflight.size === 0;
      stable = ready && state === last ? stable + 1 : 0;
      last = state;
      if (stable >= 4) return;
      await sleep(100);
    }
  }

  // Opens a page of the web app as the persona (the current one unless named), and checks the persona is signed in.
  async open(relative: string, persona: Persona | null = null) {
    if (persona && persona !== this.persona) await this.as(persona);
    if (!this.persona) throw new Error('No persona chosen: call demo.as(persona) first.');
    await this.pace();
    const url = new URL(relative, env.base).toString();
    this.shared.inflight.clear();
    await this.page.goto(url, { waitUntil: 'domcontentloaded' });
    await sleep(150);
    await this.settle();
    for (let attempt = 0; attempt < 3; attempt++) {
      const wait = limitWait(await this.text('main').catch(() => ''));
      if (!wait) break;
      await this.waitForLimit(wait);
      await this.page.reload({ waitUntil: 'domcontentloaded' });
      await this.settle();
    }
    await this.signedIn();
    return url;
  }

  // The page stores the persona and does not ask for a sign-in; on a wide screen the top bar names the person.
  async signedIn() {
    const want = browserLogin(this.persona!);
    const stored = await this.page.evaluate(k => { try { return sessionStorage.getItem(k); } catch { return null; } }, personaKey()).catch(() => null);
    this.check(stored === want, `the page acts as ${displayName(this.persona!)}`, `stored persona ${stored}`);
    if (!this.phone) await this.see(displayName(this.persona!), { what: `the top bar names ${displayName(this.persona!)}`, timeout: 8000 });
  }

  // The server answered the request limit (Product:Limits): the suite reads faster than a person, so it waits as the
  // page says and goes on. Each wait is a note in the summary.
  async waitForLimit(seconds: number) {
    this.note(`The server's request limit asked to wait ${seconds} seconds (${this.url()}).`);
    await sleep((seconds + 1) * 1000);
  }

  // The suite reads faster than a person, and one page can send 60 REST calls (the portfolio review pack). Before a
  // page load the suite keeps the persona below 200 calls in the last minute, under the server's default limit of 300,
  // by waiting as a person would. Each wait is a note in the summary.
  async pace(limit = 200) {
    const list = callLog.get(this.persona!) ?? [];
    const now = Date.now();
    const recent = list.filter(t => t > now - 60000);
    if (recent.length <= limit) return;
    const wait = recent[recent.length - limit - 1] + 60000 - now + 500;
    this.note(`Paced: ${recent.length} REST calls as ${displayName(this.persona!)} in the last minute, so the step waited ${Math.ceil(wait / 1000)} seconds.`);
    await sleep(wait);
  }

  async back() {
    await this.page.goBack({ waitUntil: 'domcontentloaded' }).catch(() => {});
    await sleep(200);
    await this.settle();
  }

  url() {
    return this.page.url();
  }

  async text(scope: Scope = 'page') {
    return this.page.evaluate(readText, scope);
  }

  // ---- checks: recorded, and the step goes on ----

  check(condition: unknown, what: string, detail = '') {
    expect.soft(!!condition, `${this.stepId}: ${what}${detail ? `\n    ${shorten(norm(detail), 1200)}` : ''}`).toBe(true);
    return !!condition;
  }

  // Waits for text (or a pattern) to show. Records a failure, with what the page shows, when it does not.
  async see(expected: Expected, o: { scope?: Scope; what?: string; timeout?: number } = {}): Promise<RegExpExecArray | string[] | null> {
    const scope = o.scope ?? 'page';
    const until = Date.now() + (o.timeout ?? 10000);
    let text = '';
    for (;;) {
      text = await this.text(scope).catch(() => '');
      const hit = expected instanceof RegExp ? expected.exec(text) : text.includes(norm(expected)) || loose(text).includes(loose(expected)) ? [norm(expected)] : null;
      if (hit) {
        this.check(true, o.what ?? '');
        return hit;
      }
      if (Date.now() > until) break;
      await sleep(300);
    }
    const shown = scope === 'page' ? await this.text('main').catch(() => text) : text;
    this.check(false, o.what ?? `the page shows ${expected instanceof RegExp ? expected : `"${expected}"`}`, `${this.url()}: ${around(shown, expected)}`);
    return null;
  }

  // Each of the texts shows (one check each).
  async seeAll(expected: Expected[], o: { scope?: Scope; what?: string; timeout?: number } = {}) {
    for (const e of expected) await this.see(e, { ...o, what: o.what ? `${o.what}: ${e}` : undefined });
  }

  async notSee(unexpected: Expected, o: { scope?: Scope; what?: string } = {}) {
    const text = await this.text(o.scope ?? 'page');
    const hit = unexpected instanceof RegExp ? unexpected.test(text) : text.includes(norm(unexpected));
    return this.check(!hit, o.what ?? `the page does not show "${unexpected}"`, this.url());
  }

  // A number the page shows after a label, such as "Owner gaps 2".
  async number(label: string, o: { scope?: Scope; what?: string; timeout?: number } = {}): Promise<number | null> {
    const m = await this.see(new RegExp(`${escape(label)} ?(\\d+)`), { ...o, what: o.what ?? `the page shows a number for "${label}"` });
    return m ? Number(m[1]) : null;
  }

  // The number after a label is the one expected.
  async count(label: string, expected: number, o: { scope?: Scope; what?: string; timeout?: number } = {}) {
    // A wide screen reads "Data domain 25", a phone "Data domain (25)".
    const re = new RegExp(`${escape(label)} ?\\(?${expected}(?!\\d)`);
    return this.see(re, { ...o, what: o.what ?? `"${label}" reads ${expected}` });
  }

  // The address matches.
  async at(pattern: RegExp, what: string) {
    await expect.poll(() => this.url(), { timeout: 5000 }).toMatch(pattern).catch(() => {});
    return this.check(pattern.test(this.url()), what, this.url());
  }

  // ---- what the run allows ----

  // An action a person takes, with what it checks after. Skipped in read-only mode. When it cannot finish, the step
  // stops with what was being done.
  async act<T>(what: string, fn: () => Promise<T>): Promise<T | null> {
    if (this.readOnly) {
      this.info.annotations.push({ type: 'skipped write', description: what });
      this.skippedWrite = true;
      return null;
    }
    try {
      return await fn();
    } catch (e) {
      await this.closeSheet().catch(() => {});
      throw new Error(`${this.stepId}: ${what}: the action could not finish: ${(e as Error).message} (at ${this.url()})`);
    }
  }

  // Claude Code's calls on the MCP endpoint: a local run only, and never in read-only mode.
  async mcp<T>(what: string, fn: () => Promise<T>): Promise<T | null> {
    if (!env.mcp || this.readOnly) {
      this.info.annotations.push({ type: 'skipped', description: `${what} (Claude Code: ${this.readOnly ? 'read only' : 'not on a Demo install'})` });
      return null;
    }
    return fn();
  }

  // MCP reads: a local run only; a read writes nothing, so read-only mode keeps them.
  async mcpRead<T>(what: string, fn: () => Promise<T>): Promise<T | null> {
    if (!env.mcp) {
      this.info.annotations.push({ type: 'skipped', description: `${what} (Claude Code: not on a Demo install)` });
      return null;
    }
    return fn();
  }

  // True when the demo director did Claude Code's work for Priya (a local run); otherwise the check is skipped.
  seeded(what: string) {
    if (this.aiSeeded) return true;
    this.info.annotations.push({ type: 'skipped', description: `${what} (Claude Code's work is live on a Demo install, not seeded)` });
    return false;
  }

  note(what: string) {
    this.info.annotations.push({ type: 'note', description: what });
  }

  // A write the step leaves out on purpose, with the reason: the summary lists it, and the declared writes are not
  // checked for this step.
  skip(what: string) {
    this.info.annotations.push({ type: 'skipped write', description: what });
    this.skippedWrite = true;
  }

  // A state an earlier step makes is not on the store. Where it cannot exist (a demo install, a read-only run, a store
  // without Claude Code's seeded work) the write is skipped, with the reason; on a local writing run it must be there, so
  // the step fails (wave 4b QA and review).
  missing(what: string) {
    if (this.mode === 'demo' || this.readOnly || !this.aiSeeded) this.skip(what);
    else this.check(false, `${what} (a local writing run must have it)`);
  }

  // A check that reads the REST API. A failed read is a failed check.
  async api<T>(what: string, fn: () => Promise<T>): Promise<T | null> {
    try {
      return await fn();
    } catch (e) {
      this.check(false, what, (e as Error).message);
      return null;
    }
  }

  // ---- acting the way a person does ----

  private async pick(spec: FindSpec, timeout = 10000) {
    let found = await this.page.evaluate(findControl, spec);
    const until = Date.now() + timeout;
    while ((found.error || found.disabled) && Date.now() < until) {
      await sleep(250);
      found = await this.page.evaluate(findControl, spec);
    }
    if (found.error) throw new Error(`cannot click: ${found.error}`);
    if (found.disabled) throw new Error(`cannot click "${spec.text}": it is disabled`);
    return this.page.locator('[data-e2e-pick="1"]').first();
  }

  // Clicks a control a person reads by its text: scrolled into view, a real click at its middle.
  async click(spec: FindSpec | string, timeout = 10000) {
    const s = typeof spec === 'string' ? { text: spec } : spec;
    const target = await this.pick(s, timeout);
    try {
      await target.click({ timeout: 5000 });
    } catch {
      // Something sits over the control (a sticky bar): click it in the page.
      await target.evaluate(el => (el as HTMLElement).click());
    }
    await sleep(200);
    await this.settle();
  }

  // True when the control shows now.
  async has(spec: FindSpec | string) {
    const s = typeof spec === 'string' ? { text: spec } : spec;
    const found = await this.page.evaluate(findControl, s);
    return !found.error;
  }

  // Types into the field a label names, in the open sheet or the page.
  async fill(label: string, value: string, scope: 'dialog' | 'page' = 'page') {
    let ok = await this.page.evaluate(findField, { label, scope });
    const until = Date.now() + 5000;
    while (ok !== 'ok' && Date.now() < until) {
      await sleep(200);
      ok = await this.page.evaluate(findField, { label, scope });
    }
    if (ok !== 'ok') throw new Error(`cannot type: ${ok}`);
    await this.page.locator('[data-e2e-field="1"]').first().fill(value);
    await sleep(150);
  }

  // Picks the radio button or checkbox whose label starts with the text (leaves it on when it already is).
  async choose(text: string, scope: 'dialog' | 'page' = 'page', timeout = 8000) {
    let found = await this.page.evaluate(findChoice, { text, scope });
    const until = Date.now() + timeout;
    while (found.error && Date.now() < until) {
      await sleep(250);
      found = await this.page.evaluate(findChoice, { text, scope });
    }
    if (found.error) throw new Error(`cannot choose: ${found.error}`);
    if (!found.checked) {
      const target = this.page.locator('[data-e2e-choice="1"]').first();
      try {
        await target.click({ timeout: 5000 });
      } catch {
        await target.evaluate(el => (el as HTMLElement).click());
      }
    }
    await sleep(150);
    await this.settle();
  }

  // Picks an option in a select (a combobox) showing the given text.
  async select(comboText: string, option: string) {
    await this.click({ text: comboText, role: 'combobox', prefix: true });
    await this.click({ text: option, role: 'option' });
  }

  // Finds a person in a people picker by name and picks them.
  async person(label: string, name: string, scope: 'dialog' | 'page' = 'page') {
    await this.fill(label, name.split(' ')[0], scope);
    await this.click({ text: name, prefix: true, scope, near: label });
  }

  // Sets a date or date-and-time field a label names.
  async setValue(label: string, value: string, scope: 'dialog' | 'page' = 'page') {
    const ok = await this.page.evaluate(findField, { label, scope });
    if (ok !== 'ok') throw new Error(`cannot set: ${ok}`);
    await this.page.locator('[data-e2e-field="1"]').first().fill(value);
    await sleep(150);
  }

  async key(key: string) {
    await this.page.keyboard.press(key);
    await sleep(200);
  }

  // ---- side sheets ----

  // Opens a side sheet with its trigger, fills it, runs its check first when asked, submits it, confirms it, and
  // answers the engine's answer: { kind: 'done' | 'refused' | 'problem' | 'none', text }.
  async sheet(o: SheetOptions) {
    const what = o.what ?? o.submit;
    if (o.open) await this.click(o.open);
    await this.waitSheet();
    if (o.fill) await o.fill();
    if (o.preview) {
      // "Check first": every rule runs and nothing is written; the sheet shows "Checked: nothing was written yet".
      await this.click({ text: o.preview, scope: 'dialog' });
      const until = Date.now() + 15000;
      let checked = '';
      while (Date.now() < until && !checked) {
        checked = await this.page.evaluate(checkAnswerNow);
        if (!checked) await sleep(200);
      }
      this.check(/^Checked/.test(checked), `${what}: "${o.preview}" answers and writes nothing`, checked || 'no answer');
      for (const want of o.previewShows ?? []) {
        const hit = shows(checked, want);
        this.check(hit, `${what}: the check says ${want}`, checked);
      }
    }
    let answer = { kind: 'none', text: '' };
    for (let attempt = 0; attempt < 3; attempt++) {
      await this.click({ text: o.submit, scope: 'dialog' });
      if (o.confirm) {
        // A confirm step comes for an act that cannot be undone; some sheets have none and answer at once.
        const until = Date.now() + 5000;
        while (Date.now() < until) {
          if (await this.page.evaluate(sheetAnswered)) break;
          const found = await this.page.evaluate(findControl, { text: o.confirm, scope: 'dialog' as const });
          if (!found.error && !found.disabled && /Back/.test(await this.text('dialog'))) {
            await this.click({ text: o.confirm, scope: 'dialog' }, 2000);
            break;
          }
          await sleep(200);
        }
      }
      answer = await this.sheetAnswer();
      // The request limit: "Too many requests just now ... Nothing was written." A person waits and sends it again.
      const wait = limitWait(answer.text);
      if (!wait) break;
      await this.waitForLimit(wait);
    }
    const want = o.expect === undefined ? 'done' : o.expect;
    this.info.annotations.push({ type: 'answer', description: `${what}: ${shorten(norm(answer.text), 600)}` });
    if (want) this.check(answer.kind === want, `${what}: the engine's answer is ${want}`, `${answer.kind}: ${answer.text}`);
    // A sheet that closed with no answer proves nothing by itself: the step says it closes and checks the page after it.
    if (want === 'done' && CLOSED.test(answer.text) && !o.closes) this.check(false, `${what}: the sheet shows the engine's answer before it closes (or the step says closes: true and checks the page)`, answer.text);
    for (const w of o.answerShows ?? []) {
      const hit = shows(answer.text, w);
      this.check(hit, `${what}: the answer says ${w instanceof RegExp ? w : `"${w}"`}`, answer.text);
    }
    if (o.close !== false) await this.closeSheet();
    return answer;
  }

  async waitSheet(timeout = 8000) {
    const until = Date.now() + timeout;
    while (Date.now() < until) {
      if (await this.page.evaluate(sheetOpen)) return;
      await sleep(150);
    }
    throw new Error('the side sheet did not open');
  }

  async sheetAnswer(timeout = 20000) {
    const until = Date.now() + timeout;
    while (Date.now() < until) {
      const a = await this.page.evaluate(sheetAnswerNow);
      if (a) return a;
      await sleep(200);
    }
    return { kind: 'none', text: await this.text('dialog') };
  }

  async closeSheet() {
    for (let i = 0; i < 3; i++) {
      if (!(await this.page.evaluate(sheetOpen))) break;
      await this.page.keyboard.press('Escape');
      await sleep(300);
    }
    await this.settle();
  }

  // Answers the approval request on the decision page that is open: Approve (or Reject) with a typed reason. With
  // preview, "Check my answer" first.
  async answer(reason: string, o: { answer?: 'approve' | 'reject'; near?: string; what?: string; answerShows?: Expected[]; preview?: boolean; previewShows?: Expected[]; expect?: 'done' | 'refused' | null } = {}) {
    const verb = o.answer === 'reject' ? 'Reject' : 'Approve';
    return this.sheet({
      open: { text: verb, scope: 'main', near: o.near ?? 'Your answer is asked' },
      fill: () => this.fill('Reason', reason, 'dialog'),
      ...(o.preview ? { preview: 'Check my answer', previewShows: o.previewShows } : {}),
      submit: verb,
      confirm: verb,
      what: o.what ?? `${this.persona ? displayName(this.persona) : 'The person'} answers ${verb.toLowerCase()}`,
      answerShows: o.answerShows,
      expect: o.expect,
    });
  }

  // Opens the approval card with this statement on Approvals, Waiting for me, and lands on its decision page.
  async openCard(statement: string) {
    await this.open('approvals?view=mine');
    await this.click({ text: 'Answer', near: statement, scope: 'main' });
    await this.at(/\/records\/decision\//, `the card "${shorten(statement, 60)}" opens its decision page`);
  }

  // Proposes a decision in a sheet, then sends it for approval the way the page offers next: the follow-up sheet
  // "Send it for approval now?" (Send for approval), the page's notice "Proposed. Nobody is asked until it is
  // submitted." (Submit for approval), or the decision page's own Submit. via 'page' (wave 4b QA): Cancel in the
  // follow-up, then the page's own "Submit for approval" (the Owners banner, a review's step 2).
  async proposeAndSubmit(o: SheetOptions & { what: string; sendShows?: Expected[]; via?: 'follow-up' | 'page' }) {
    const answer = await this.sheet({ ...o, submit: o.submit ?? 'Propose', close: false, expect: null });
    // The follow-up sheet "Send it for approval now?" replaces the proposal sheet once the proposal is saved, sometimes
    // with the proposal's answer on top and sometimes without it.
    const followUp = answer.kind === 'none' && (await this.has({ text: 'Send for approval', scope: 'dialog' }));
    this.check(answer.kind === 'done' || followUp, `${o.what}: the engine's answer is done`, `${answer.kind}: ${answer.text}`);
    if (answer.kind !== 'done' && !followUp) {
      await this.closeSheet();
      throw new Error(`the proposal was not saved: ${answer.text}`);
    }
    // The follow-up may replace the proposal sheet a moment after the write.
    let route: 'follow-up' | 'notice' | 'decision' | null = null;
    const until = Date.now() + 4000;
    while (!route && Date.now() < until) {
      if (await this.has({ text: 'Send for approval', scope: 'dialog' })) route = 'follow-up';
      else if (await this.has({ text: 'Submit for approval', scope: 'main' })) route = 'notice';
      else if (/not submitted yet/.test(await this.text('main')) && (await this.has({ text: 'Submit', scope: 'main', near: 'not submitted yet' }))) route = 'notice';
      else if (/\/records\/decision\//.test(this.url())) route = 'decision';
      else await sleep(250);
    }
    if (route === 'follow-up' && o.via === 'page') {
      await this.click({ text: 'Cancel', scope: 'dialog' });
      await this.closeSheet();
      await this.click({ text: 'Submit for approval', scope: 'main' });
      await this.waitSheet();
      const sent = await this.send('Submit');
      await this.sendShows(o, sent);
      await this.closeSheet();
      return sent;
    }
    if (route === 'follow-up') {
      const sent = await this.send('Send for approval');
      await this.sendShows(o, sent);
      await this.closeSheet();
      return sent;
    }
    if (route === 'notice') {
      await this.closeSheet();
      if (await this.has({ text: 'Submit for approval', scope: 'main' })) await this.click({ text: 'Submit for approval', scope: 'main' });
      else await this.click({ text: 'Submit', scope: 'main', near: 'not submitted yet' });
      await this.waitSheet();
      const sent = await this.send('Submit');
      await this.sendShows(o, sent);
      await this.closeSheet();
      return sent;
    }
    if (!/\/records\/decision\//.test(this.url()) && (await this.has({ text: 'Open the record', role: 'link', scope: 'dialog' }))) {
      await this.click({ text: 'Open the record', role: 'link', scope: 'dialog' });
    }
    await this.closeSheet();
    this.check(/\/records\/decision\//.test(this.url()), `${o.what}: the proposal opens on its decision page`, this.url());
    return this.sheet({ open: { text: 'Submit', scope: 'main' }, submit: 'Submit', confirm: 'Submit', what: `${o.what}: submit it`, answerShows: o.sendShows });
  }

  // Clicks the send button of the open sheet, confirms when the sheet asks, and waits for the engine's answer to the
  // send ("Submitted. ..."), not the proposal's answer the follow-up repeats on top.
  private async send(label: string) {
    await this.click({ text: label, scope: 'dialog' });
    const until = Date.now() + 20000;
    while (Date.now() < until) {
      if (!(await this.page.evaluate(sheetOpen))) return { kind: 'done', text: 'The sheet closed after the send.' };
      if (/Back/.test(await this.text('dialog'))) {
        for (const confirm of [label, 'Submit']) {
          if (await this.has({ text: confirm, scope: 'dialog' })) {
            await this.click({ text: confirm, scope: 'dialog' }, 2000);
            break;
          }
        }
      }
      const now = await this.page.evaluate(sheetAnswerNow);
      if (now && (now.kind === 'refused' || /Submitted/.test(now.text))) return now;
      await sleep(250);
    }
    return { kind: 'none', text: await this.text('dialog') };
  }

  private async sendShows(o: { what: string; sendShows?: Expected[] }, sent: { kind: string; text: string }) {
    this.info.annotations.push({ type: 'answer', description: `${o.what}: sent: ${shorten(norm(sent.text), 600)}` });
    this.check(sent.kind === 'done', `${o.what}: sending it is done`, `${sent.kind}: ${sent.text}`);
    for (const w of o.sendShows ?? []) {
      const hit = shows(sent.text, w);
      this.check(hit, `${o.what}: sending it says ${w instanceof RegExp ? w : `"${w}"`}`, sent.text);
    }
  }

  // The engine's state of the decision whose page is open (or the given ID), read as the current persona.
  async decisionIs(meaning: string, what: string, decisionId: string | null = null) {
    return this.api(what, async () => {
      const id = decisionId ?? /\/records\/decision\/([0-9a-f-]{36})/.exec(this.url())?.[1];
      if (!id) throw new Error(`no decision page is open (${this.url()})`);
      const r = await apiCall(this.persona!, 'get_decision', { decisionId: id });
      if (!r.body?.ok) throw new ToolRefused('get_decision', r.status, r.body?.error);
      const status = r.body.data.decision?.status?.meaning;
      const warnings = (r.body.warnings ?? []).map(w => w.message).join(' ');
      this.check(status === meaning, what, `status ${status}${warnings ? `; the engine says: ${warnings}` : ''}`);
      return r.body.data;
    });
  }
}
