#!/usr/bin/env node
// Accessibility check with headless Chrome, at 1280 px and 390 px. No packages: Node 22 or later (global WebSocket and
// fetch) and an installed Chrome.
//
// Two targets:
//   the harness (default): serves the built harness (dist/harness, "npm run build" first) with "vite preview" on
//     127.0.0.1 and opens the four pages on their fictional fixtures, also with every tool failing;
//   the web app (--app <url>): the built web app as the local server serves it under /app/ (src/adapters/azure, "Run it
//     locally"), on a store seeded with seed/demo-estate.json after the demo director ran. The URL must be on
//     127.0.0.1, localhost or [::1]. It opens every page of src/app/routes.ts (the list is scripts/a11y-pages.mjs; each
//     record page on a demo record) role by role: signed in as each development persona of --personas (default a
//     requester, an application owner and the platform admin: demo-lena, nadia-web, owen-web), plus the development
//     sign-in once per width with no persona. The records the demo director makes (a claim, a gap check run, a
//     configuration draft) have new IDs in every store, so the check reads them first through the REST API as
//     --lookup-persona (default owen-web).
//     On each page it then opens the main action sheet the persona sees: the first sheet trigger of the page area,
//     where the page header's actions come first. It checks the open sheet the same way, scoped to the sheet, and also
//     that focus moves into the sheet, that the Tab key keeps focus inside it, that Escape closes it, and that focus
//     goes back to the control that opened it. A page where the persona has no action sheet says so; that is not an
//     issue. A page entry with sheetOnly opens only the trigger its pattern names, such as a wave 4 sheet that is not
//     the page's main action. --no-sheets leaves the sheets out.
//
// Chrome resolves no host except 127.0.0.1 and runs with a temporary profile of its own and its background services
// off, as in scripts/screenshots.mjs.
//
// The checks are this script's own, run in the page and on Chrome's accessibility tree, for the WCAG 2.2 AA points a
// script can decide: the page language and title, one main landmark, a level 1 heading and no skipped heading level,
// a name for every image, control, link and tab, no duplicate IDs, ARIA references that resolve, no positive tabindex,
// text contrast of 4.5 to 1 (3 to 1 for large text) against the colors actually behind it, no sideways scrolling at
// 390 px, and a walk with the Tab key: every focus stop shown, and no keyboard trap. Contrast on an image or a gradient
// background is listed as not checked. Not checked by a script: whether the focus outline is easy to see, reading
// order, and how a screen reader announces each part; those need a person.
//
// --axe <absolute file> also runs axe-core from that file (the person running the check provides it; axe-core is
// MPL-2.0, so it is never a dependency of this package) with the WCAG 2.0, 2.1 and 2.2 A and AA rules, and lists its
// violations next to this script's own. It leaves out Radix's focus guards (the hidden, focusable spans marked
// data-radix-focus-guard that an open dialog or popover puts around itself to keep keyboard focus inside), which axe
// would report as aria-hidden-focus: they hand focus on at once and never hold it.
//
// Usage, from Git Bash in src/ui/harness (Chrome on Windows needs absolute Windows paths, so cygpath converts them):
//   node scripts/a11y.mjs --chrome "$(cygpath -aw '/c/Program Files/Google/Chrome/Application/chrome.exe')"
//   node scripts/a11y.mjs --app http://127.0.0.1:5080/app/ --personas demo-lena,nadia-web,owen-web
// Options: --pages <names>  --widths 1280,390  --app-id <application id>  --json
//   web app only: --personas <names> (--persona <name> for one)  --lookup-persona <name>  --no-sheets
//     --clock-days <n>: the page's clock (Date) runs n days ahead, 1 to 30, so a sheet that shows only near an expiry
//     can be checked on a fresh demo store: "Ask again" offers itself in a request's last 48 hours, and the director's
//     requests last five days, so --clock-days 4 with --pages work-ask-again,decision-ask-again. The server's clock is
//     not moved, so only the page's own reading of the time changes; nothing is written.
// Exit code: 0 when no page has an issue, 1 when one has, 2 for bad usage.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { appPages, DEFAULT_PERSONAS, DEMO_IDS, DIRECTOR_RECORDS, LOOKUP_PERSONA } from './a11y-pages.mjs';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = 4320;

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const list = value => value.split(',').map(s => s.trim()).filter(Boolean);
const chrome = opt('--chrome', 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe');
const appUrl = opt('--app', null);
const personas = list(opt('--personas', opt('--persona', DEFAULT_PERSONAS.join(','))));
const lookupPersona = opt('--lookup-persona', LOOKUP_PERSONA);
const openSheets = !args.includes('--no-sheets');
const widths = opt('--widths', '1280,390').split(',').map(Number);
const axeFile = opt('--axe', null);
const asJson = args.includes('--json');
const clockDays = Number(opt('--clock-days', '0'));
const harnessAppId = '10000000-0000-4000-8000-000000000001';
const appId = opt('--app-id', appUrl ? DEMO_IDS.application : harnessAppId);

const HARNESS_PAGES = {
  overview: '#/overview',
  app: `#/app/${appId}`,
  map: `#/map/${appId}`,
  timeline: '#/timeline',
};
// The web app's pages with the demo records; the director's records are filled in once they are read.
const appIds = { ...DEMO_IDS, application: appId, claim: null, gapCheckRun: null, configurationDraft: null };
// The page of each director record, for the note when it could not be read.
const DIRECTOR_PAGE = { claim: 'claim', 'gap-check-run': 'gapCheckRun', 'configuration-draft': 'configurationDraft' };
const APP_PAGE_NAMES = appPages({ ...appIds, claim: 'x', gapCheckRun: 'x', configurationDraft: 'x' }).map(p => p.name);
const pageNames = list(opt('--pages', (appUrl ? APP_PAGE_NAMES : Object.keys(HARNESS_PAGES)).join(',')));

function usage(message) {
  console.error(`a11y: ${message}`);
  process.exit(2);
}

let appBase = null;
if (appUrl) {
  try {
    appBase = new URL(appUrl);
  } catch {
    usage('--app must be a URL such as http://127.0.0.1:5080/app/.');
  }
  if (appBase.protocol !== 'http:' || !['127.0.0.1', 'localhost', '[::1]'].includes(appBase.hostname)) usage('--app must be on http://127.0.0.1, http://localhost or http://[::1].');
  if (!appBase.pathname.endsWith('/')) appBase.pathname += '/';
}
const known = appUrl ? APP_PAGE_NAMES : Object.keys(HARNESS_PAGES);
if (pageNames.some(p => !known.includes(p))) usage(`unknown page; choose from ${known.join(', ')}.`);
const PERSONA = /^[a-z][a-z0-9-]{0,63}$/;
if (appUrl && (!personas.length || !personas.every(p => PERSONA.test(p)) || !PERSONA.test(lookupPersona))) usage('a persona is a development persona name such as nadia-web.');
if (widths.some(w => !Number.isInteger(w) || w < 320)) usage('bad width.');
if (!Number.isInteger(clockDays) || clockDays < 0 || clockDays > 30 || (clockDays && !appUrl)) usage('--clock-days takes 1 to 30, with --app.');
if (axeFile && (!path.isAbsolute(axeFile) || !fs.existsSync(axeFile))) usage('--axe needs an absolute path to an axe-core file that exists.');
if (!appUrl && !fs.existsSync(path.join(HARNESS, 'dist', 'harness', 'index.html'))) usage('no dist/harness/index.html. Run "npm run build" first.');
if (!fs.existsSync(chrome)) usage(`Chrome not found at ${chrome}`);

const sleep = ms => new Promise(r => setTimeout(r, ms));

// ---- the harness preview server (harness target only) ----
let preview = null;
let previewLog = '';
const harnessBase = `http://127.0.0.1:${PORT}/`;
async function startPreview() {
  preview = spawn(process.execPath, [path.join(HARNESS, 'node_modules', 'vite', 'bin', 'vite.js'), 'preview', '--host', '127.0.0.1', '--port', String(PORT), '--strictPort'], {
    cwd: HARNESS,
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  preview.stdout.on('data', d => { previewLog += d; });
  preview.stderr.on('data', d => { previewLog += d; });
  for (let i = 0; i < 100; i++) {
    try {
      const r = await fetch(harnessBase);
      if (r.ok) return;
    } catch { /* not up yet */ }
    await sleep(200);
  }
  throw new Error(`the preview server did not start:\n${previewLog}`);
}

// ---- Chrome and a small DevTools protocol client ----
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'harness-a11y-'));
let browser;

function startChrome() {
  return new Promise((resolve, reject) => {
    browser = spawn(chrome, [
      '--headless=new',
      '--disable-gpu',
      '--no-first-run',
      '--no-default-browser-check',
      '--hide-scrollbars',
      '--force-device-scale-factor=1',
      `--user-data-dir=${profile}`,
      '--remote-debugging-address=127.0.0.1',
      '--remote-debugging-port=0',
      '--disable-background-networking',
      '--disable-component-update',
      '--disable-sync',
      '--disable-extensions',
      '--disable-default-apps',
      '--disable-domain-reliability',
      '--disable-client-side-phishing-detection',
      '--metrics-recording-only',
      '--no-pings',
      '--disable-features=Translate,OptimizationHints,MediaRouter,AutofillServerCommunication',
      '--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1',
      '--window-size=1280,900',
      'about:blank',
    ], { stdio: ['ignore', 'ignore', 'pipe'] });
    let err = '';
    const timer = setTimeout(() => reject(new Error(`Chrome did not open its DevTools port:\n${err}`)), 30000);
    browser.stderr.on('data', d => {
      err += d;
      const m = /DevTools listening on (ws:\/\/\S+)/.exec(err);
      if (m) {
        clearTimeout(timer);
        resolve(m[1]);
      }
    });
    browser.on('error', e => { clearTimeout(timer); reject(e); });
    browser.on('exit', code => { clearTimeout(timer); reject(new Error(`Chrome exited (${code}):\n${err}`)); });
  });
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(url);
    let nextId = 1;
    const waiting = new Map();
    const listeners = new Set();
    ws.onmessage = ev => {
      const msg = JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8'));
      if (msg.id && waiting.has(msg.id)) {
        const { ok, fail } = waiting.get(msg.id);
        waiting.delete(msg.id);
        if (msg.error) fail(new Error(`${msg.error.message} (${msg.error.code})`));
        else ok(msg.result);
      } else if (msg.method) {
        for (const l of listeners) l(msg);
      }
    };
    ws.onerror = () => reject(new Error('cannot connect to Chrome'));
    ws.onopen = () => resolve({
      send(method, params = {}, sessionId) {
        const id = nextId++;
        ws.send(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }));
        return new Promise((ok, fail) => waiting.set(id, { ok, fail }));
      },
      on(fn) { listeners.add(fn); return () => listeners.delete(fn); },
      close() { ws.close(); },
    });
  });
}

function waitEvent(cdp, sessionId, method, timeoutMs = 20000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { off(); reject(new Error(`timed out waiting for ${method}`)); }, timeoutMs);
    const off = cdp.on(msg => {
      if (msg.method === method && msg.sessionId === sessionId) {
        clearTimeout(timer);
        off();
        resolve(msg.params);
      }
    });
  });
}

async function evaluate(cdp, sessionId, expression, awaitPromise = false) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise }, sessionId);
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
  return r.result.value;
}

// Ready when no request is open, the fonts are loaded, nothing says it is loading, and that stays so for 500 ms.
// Returns no issues when it is; after about 20 s, the issues that kept it from it, and the page is checked as it is:
// a part that still says it is loading (aria-busy) tells a screen reader user it is loading for good.
const BUSY_PARTS = String.raw`JSON.stringify([...document.querySelectorAll('[aria-busy="true"]')].slice(0, 10).map(el => {
  let s = el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
  const text = (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 60);
  return text ? s + ' "' + text + '"' : s;
}))`;
async function waitReady(cdp, sessionId, inflight) {
  let stable = 0;
  let last = '';
  for (let i = 0; i < 200; i++) {
    const v = await evaluate(cdp, sessionId, `JSON.stringify([document.readyState, document.fonts.status, document.querySelectorAll('[aria-busy="true"]').length, document.body ? document.body.innerText.length : 0])`);
    const state = `${v}|${inflight.size}`;
    const [ready, fonts, busy] = JSON.parse(v);
    if (ready === 'complete' && fonts === 'loaded' && busy === 0 && inflight.size === 0 && state === last) stable++;
    else stable = 0;
    last = state;
    if (stable >= 5) return [];
    await sleep(100);
  }
  const [ready, fonts, busy] = JSON.parse(last.split('|')[0] || '[]');
  const issues = [];
  if (busy > 0) {
    for (const part of JSON.parse(await evaluate(cdp, sessionId, BUSY_PARTS))) {
      issues.push({ rule: 'busy-forever', target: part, detail: `still marked as loading (aria-busy) after 20 s${inflight.size ? '' : ', with no request open'}` });
    }
  }
  if (inflight.size > 0) issues.push({ rule: 'page-settle', target: 'page', detail: `${inflight.size} requests still open after 20 s` });
  if (ready !== 'complete' || fonts !== 'loaded') issues.push({ rule: 'page-settle', target: 'page', detail: `document ${ready}, fonts ${fonts} after 20 s` });
  if (!issues.length) issues.push({ rule: 'page-settle', target: 'page', detail: 'the page kept changing for 20 s' });
  return issues;
}

// ---- the checks that run in the page ----
// A function of the scope: 'page' checks the whole page; 'sheet' checks the open sheet, the element the script marked
// with data-a11y-scope, and leaves out the points that belong to the page (language, title, main landmark, level 1
// heading). Returns { issues: [{ rule, target, detail }], unchecked: n, heading: the level 1 heading or the sheet's
// name }.
const PAGE_CHECKS = String.raw`((scope) => {
  const issues = [];
  let unchecked = 0;
  const root = scope === 'sheet' ? document.querySelector('[data-a11y-scope]') : document.body;
  if (!root) return JSON.stringify({ issues: [{ rule: 'sheet-open', target: 'page', detail: 'the sheet closed before it was checked' }], unchecked: 0, heading: '' });
  const describe = el => {
    if (!el || el.nodeType !== 1) return String(el);
    let s = el.tagName.toLowerCase();
    if (el.id) s += '#' + el.id;
    const role = el.getAttribute('role');
    if (role) s += '[role=' + role + ']';
    const text = (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 50);
    return text ? s + ' "' + text + '"' : s;
  };
  const add = (rule, el, detail) => issues.push({ rule, target: describe(el), detail: detail || '' });
  const shown = el => {
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
      const cs = getComputedStyle(e);
      if (cs.display === 'none' || cs.visibility === 'hidden' || cs.visibility === 'collapse') return false;
    }
    const r = el.getBoundingClientRect();
    return r.width > 1 && r.height > 1;
  };
  const hiddenFromAll = el => !!el.closest('[aria-hidden="true"], [hidden], [inert]');

  const sheetName = () => {
    const by = (root.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean).map(id => document.getElementById(id)).filter(Boolean);
    return (by.map(e => e.textContent || '').join(' ') || root.getAttribute('aria-label') || '').trim().replace(/\s+/g, ' ').slice(0, 80);
  };
  if (scope === 'page') {
    const lang = (document.documentElement.getAttribute('lang') || '').trim();
    if (!lang) add('html-lang', document.documentElement, 'the page names no language');
    if (!document.title.trim()) add('document-title', document.documentElement, 'the page has no title');
    const mains = [...document.querySelectorAll('main, [role="main"]')].filter(shown);
    if (mains.length !== 1) add('one-main', document.body, mains.length + ' main landmarks shown');
  } else if (!sheetName()) {
    add('sheet-name', root, 'the sheet has no title that names it');
  }

  const headings = [...root.querySelectorAll('h1, h2, h3, h4, h5, h6, [role="heading"]')].filter(h => shown(h) && !hiddenFromAll(h));
  const level = h => h.getAttribute('aria-level') ? Number(h.getAttribute('aria-level')) : Number(h.tagName.slice(1)) || 2;
  if (scope === 'page' && !headings.some(h => level(h) === 1)) add('page-has-heading-one', document.body, 'no level 1 heading');
  let previous = 0;
  for (const h of headings) {
    const l = level(h);
    if (previous && l > previous + 1) add('heading-order', h, 'level ' + previous + ' then level ' + l);
    previous = l;
    if (!(h.textContent || '').trim() && !(h.getAttribute('aria-label') || '').trim()) add('empty-heading', h);
  }

  for (const img of root.querySelectorAll('img')) {
    if (!img.hasAttribute('alt') && !hiddenFromAll(img)) add('image-alt', img, 'an image with no alt attribute');
  }
  for (const svg of root.querySelectorAll('svg[role="img"]')) {
    if (!hiddenFromAll(svg) && !(svg.getAttribute('aria-label') || '').trim() && !svg.getAttribute('aria-labelledby') && !svg.querySelector('title')) add('svg-img-alt', svg, 'an image role with no name');
  }

  // IDs are unique in the whole document; with a sheet open, only the IDs the sheet uses are reported.
  const ids = new Map();
  for (const el of document.querySelectorAll('[id]')) ids.set(el.id, (ids.get(el.id) || 0) + 1);
  for (const [id, n] of ids) {
    if (n < 2) continue;
    const all = [...document.querySelectorAll('[id]')].filter(e => e.id === id);
    if (scope === 'page' || all.some(e => root.contains(e))) add('duplicate-id', all.find(e => root.contains(e)) || all[0], n + ' elements share the id ' + id);
  }
  for (const attr of ['aria-labelledby', 'aria-describedby', 'aria-controls', 'aria-owns', 'aria-activedescendant']) {
    for (const el of root.querySelectorAll('[' + attr + ']')) {
      for (const ref of (el.getAttribute(attr) || '').split(/\s+/).filter(Boolean)) {
        if (!document.getElementById(ref)) add('aria-valid-reference', el, attr + ' names ' + ref + ', which is not on the page');
      }
    }
  }
  for (const el of root.querySelectorAll('[tabindex]')) {
    if (Number(el.getAttribute('tabindex')) > 0) add('tabindex', el, 'tabindex ' + el.getAttribute('tabindex'));
  }

  // Text contrast against the colors behind the text, from the elements actually stacked at that point.
  const parse = value => {
    const m = /^rgba?\(([^)]+)\)$/.exec(value.trim());
    if (!m) return null;
    const p = m[1].split(/[\s,\/]+/).filter(Boolean).map(x => x.endsWith('%') ? Number(x.slice(0, -1)) / 100 : Number(x));
    if (p.length < 3 || p.some(Number.isNaN)) return null;
    return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
  };
  const over = (top, below) => {
    const a = top.a + below.a * (1 - top.a);
    if (a === 0) return { r: 0, g: 0, b: 0, a: 0 };
    const mix = k => (top[k] * top.a + below[k] * below.a * (1 - top.a)) / a;
    return { r: mix('r'), g: mix('g'), b: mix('b'), a };
  };
  const luminance = c => {
    const ch = v => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4); };
    return 0.2126 * ch(c.r) + 0.7152 * ch(c.g) + 0.0722 * ch(c.b);
  };
  const ratio = (a, b) => { const x = luminance(a); const y = luminance(b); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
  const hex = c => '#' + [c.r, c.g, c.b].map(v => Math.round(v).toString(16).padStart(2, '0')).join('');
  const seen = new Set();
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    if (!node.textContent.trim()) continue;
    const el = node.parentElement;
    if (!el || seen.has(el)) continue;
    seen.add(el);
    if (!shown(el) || hiddenFromAll(el) || el.closest('[disabled], [aria-disabled="true"], script, style, noscript')) continue;
    const range = document.createRange();
    range.selectNodeContents(node);
    const rect = [...range.getClientRects()].find(r => r.width > 1 && r.height > 1);
    if (!rect) continue;
    const cs = getComputedStyle(el);
    if (Number(cs.opacity) === 0) continue;
    const x = Math.min(window.innerWidth - 1, Math.max(0, rect.left + Math.min(rect.width / 2, 4)));
    const y = Math.min(window.innerHeight - 1, Math.max(0, rect.top + rect.height / 2));
    const stack = document.elementsFromPoint(x, y);
    let start = stack.indexOf(el);
    if (start < 0) start = stack.findIndex(e => e.contains(el));
    if (start < 0) start = stack.findIndex(e => el.contains(e) || e.contains(el));
    if (start < 0) { unchecked++; continue; }
    let background = { r: 255, g: 255, b: 255, a: 0 };
    let unknown = false;
    const layers = [];
    for (const e of stack.slice(start)) {
      const s = getComputedStyle(e);
      if (s.backgroundImage && s.backgroundImage !== 'none') { unknown = true; break; }
      const c = parse(s.backgroundColor);
      if (!c) { unknown = true; break; }
      const opacity = Number(s.opacity);
      if (c.a > 0) layers.push({ ...c, a: c.a * (Number.isNaN(opacity) ? 1 : opacity) });
      if (c.a >= 1 && opacity >= 1) break;
    }
    if (unknown) { unchecked++; continue; }
    background = { r: 255, g: 255, b: 255, a: 1 };
    for (const layer of layers.reverse()) background = over(layer, background);
    const fg = parse(cs.color);
    if (!fg) { unchecked++; continue; }
    let opacity = 1;
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) opacity *= Number(getComputedStyle(e).opacity) || 1;
    const text = over({ ...fg, a: fg.a * opacity }, background);
    const size = parseFloat(cs.fontSize);
    const bold = Number(cs.fontWeight) >= 700;
    const large = size >= 24 || (bold && size >= 18.66);
    const needed = large ? 3 : 4.5;
    const got = ratio(text, background);
    if (got + 0.005 < needed) add('color-contrast', el, got.toFixed(2) + ' to 1 (' + hex(text) + ' on ' + hex(background) + ', ' + size + ' px' + (bold ? ' bold' : '') + '), needs ' + needed);
  }

  if (window.innerWidth < 600 && document.documentElement.scrollWidth > window.innerWidth) {
    add('reflow', document.documentElement, document.documentElement.scrollWidth + ' px wide content in ' + window.innerWidth + ' px');
  }
  if (scope === 'sheet') {
    // A sheet that runs past the window, or a part of it that scrolls sideways (a table or code may: WCAG 1.4.10).
    const r = root.getBoundingClientRect();
    if (r.right > window.innerWidth + 1 || r.left < -1) add('reflow', root, 'the sheet runs past the window: ' + Math.round(r.left) + ' to ' + Math.round(r.right) + ' px in ' + window.innerWidth + ' px');
    for (const el of [root, ...root.querySelectorAll('*')]) {
      if (!shown(el) || hiddenFromAll(el)) continue;
      const cs = getComputedStyle(el);
      if (el.scrollWidth > el.clientWidth + 1 && (cs.overflowX === 'auto' || cs.overflowX === 'scroll') && !el.matches('pre, code, textarea, table, [role="table"], [role="grid"]') && !el.querySelector('table, pre')) {
        add('reflow', el, 'scrolls sideways inside the sheet: ' + el.scrollWidth + ' px of content in ' + el.clientWidth + ' px');
      }
    }
    return JSON.stringify({ issues, unchecked, heading: sheetName() });
  }
  const h1 = headings.find(h => level(h) === 1);
  return JSON.stringify({ issues, unchecked, heading: h1 ? h1.innerText.trim().replace(/\s+/g, ' ').slice(0, 80) : '' });
})`;
const pageChecks = scope => `(${PAGE_CHECKS})(${JSON.stringify(scope)})`;

// Controls, links, tabs and images on Chrome's accessibility tree that have no name. With a sheet open, only those in
// the sheet (the page behind it is hidden from the tree while it is open).
const NAMED_ROLES = new Set(['button', 'link', 'textbox', 'searchbox', 'combobox', 'listbox', 'checkbox', 'radio', 'switch', 'tab', 'menuitem', 'menuitemcheckbox', 'menuitemradio', 'slider', 'spinbutton', 'option', 'treeitem', 'image', 'img', 'progressbar', 'meter', 'dialog', 'alertdialog']);
async function inSheet(cdp, sessionId, backendNodeId) {
  try {
    const { object } = await cdp.send('DOM.resolveNode', { backendNodeId }, sessionId);
    const r = await cdp.send('Runtime.callFunctionOn', {
      objectId: object.objectId,
      functionDeclaration: 'function () { const e = this.nodeType === 1 ? this : this.parentElement; return !!(e && e.closest("[data-a11y-scope]")); }',
      returnByValue: true,
    }, sessionId);
    await cdp.send('Runtime.releaseObject', { objectId: object.objectId }, sessionId).catch(() => {});
    return r.result.value === true;
  } catch {
    return true;
  }
}
async function namelessNodes(cdp, sessionId, scope = 'page') {
  const { nodes } = await cdp.send('Accessibility.getFullAXTree', {}, sessionId);
  const found = [];
  for (const node of nodes) {
    const role = node.role?.value;
    if (node.ignored || !NAMED_ROLES.has(role)) continue;
    const name = (node.name?.value ?? '').trim();
    if (name) continue;
    if (scope === 'sheet' && node.backendDOMNodeId && !await inSheet(cdp, sessionId, node.backendDOMNodeId)) continue;
    let target = role;
    if (node.backendDOMNodeId) {
      try {
        const { node: dom } = await cdp.send('DOM.describeNode', { backendNodeId: node.backendDOMNodeId }, sessionId);
        const attrs = dom.attributes ?? [];
        const attr = k => { const i = attrs.indexOf(k); return i >= 0 ? attrs[i + 1] : null; };
        target = `${dom.localName}${attr('id') ? `#${attr('id')}` : ''}[role=${role}]${attr('class') ? ` .${attr('class').split(/\s+/).slice(0, 2).join('.')}` : ''}`;
      } catch { /* keep the role */ }
    }
    found.push({ rule: 'accessible-name', target, detail: `a ${role} with no accessible name` });
  }
  return found;
}

// Walks the page with the Tab key from the top, as a keyboard user does. Every stop must be shown, and focus must move
// on: three presses on one element, or a long walk that never gets back to the start and circles a few elements, is a
// keyboard trap. Radix's focus guards hand focus on at once and are not counted as stops. In an open sheet the walk
// starts inside it, and every stop must be in the sheet: a modal sheet keeps focus until it closes. A date or time field
// takes one press per part (month, day, year, hours, minutes, AM or PM) and stays the focused element meanwhile.
const FOCUS_STATE = String.raw`(() => {
  const e = document.activeElement;
  if (!e || e === document.body || e === document.documentElement) return JSON.stringify(null);
  if (e.hasAttribute('data-radix-focus-guard')) return JSON.stringify({ guard: true });
  const r = e.getBoundingClientRect();
  let hidden = false;
  for (let p = e; p && p.nodeType === 1; p = p.parentElement) {
    const cs = getComputedStyle(p);
    if (cs.display === 'none' || cs.visibility === 'hidden') hidden = true;
  }
  let label = e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') + (e.getAttribute('role') ? '[role=' + e.getAttribute('role') + ']' : '');
  const text = (e.getAttribute('aria-label') || e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40);
  if (text) label += ' "' + text + '"';
  if (!e.__a11yStop) e.__a11yStop = ++window.__a11yStops;
  const parts = e.tagName === 'INPUT' && /^(date|time|datetime-local|month|week)$/.test(e.type) ? 7 : 1;
  return JSON.stringify({ stop: e.__a11yStop, label, shown: !hidden && r.width >= 1 && r.height >= 1, inSheet: !!e.closest('[data-a11y-scope]'), parts });
})()`;
async function keyboardWalk(cdp, sessionId, scope = 'page', maxPresses = 150) {
  await evaluate(cdp, sessionId, scope === 'sheet'
    ? 'window.__a11yStops = 0; true'
    : 'window.__a11yStops = 0; if (document.activeElement && document.activeElement.blur) document.activeElement.blur(); window.scrollTo(0, 0); true');
  const issues = [];
  const stops = [];
  let first = null;
  let wrapped = false;
  for (let i = 0; i < maxPresses; i++) {
    for (const type of ['keyDown', 'keyUp']) {
      await cdp.send('Input.dispatchKeyEvent', { type, key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9, nativeVirtualKeyCode: 9 }, sessionId);
    }
    const state = JSON.parse(await evaluate(cdp, sessionId, FOCUS_STATE));
    if (state === null) {
      if (stops.length) { wrapped = true; break; }
      continue;
    }
    if (state.guard) continue;
    if (first === null) first = state.stop;
    // Back at the first stop after others: the walk went round. (A date field stays the first stop for its parts.)
    else if (state.stop === first && stops[stops.length - 1].stop !== first) { wrapped = true; break; }
    if (!state.shown) issues.push({ rule: 'focus-hidden', target: state.label, detail: 'the Tab key puts focus on an element that is not shown' });
    if (scope === 'sheet' && !state.inSheet) {
      issues.push({ rule: 'sheet-focus-escapes', target: state.label, detail: 'the Tab key moves focus out of the open sheet' });
      break;
    }
    stops.push(state);
    let same = 0;
    for (let k = stops.length - 1; k >= 0 && stops[k].stop === state.stop; k--) same++;
    if (same >= state.parts + 2) {
      issues.push({ rule: 'keyboard-trap', target: state.label, detail: `${same} Tab presses left focus on this element` });
      break;
    }
  }
  if (!wrapped && stops.length >= 30 && new Set(stops.slice(-30).map(s => s.stop)).size <= 5) {
    issues.push({ rule: 'keyboard-trap', target: stops[stops.length - 1].label, detail: `after ${maxPresses} Tab presses focus circles ${new Set(stops.slice(-30).map(s => s.stop)).size} elements` });
  }
  return { issues, stops: new Set(stops.map(s => s.stop)).size };
}

async function runAxe(cdp, sessionId, source, scope = 'page') {
  const loaded = await evaluate(cdp, sessionId, 'typeof window.axe');
  if (loaded !== 'object') await evaluate(cdp, sessionId, source);
  const include = scope === 'sheet' ? '[data-a11y-scope]' : 'body';
  const text = await evaluate(cdp, sessionId, `axe.run({ include: [['${include}']], exclude: [['[data-radix-focus-guard]']] }, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }, resultTypes: ['violations'] }).then(r => JSON.stringify(r.violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.map(n => ({ target: n.target.join(' '), summary: (n.failureSummary || '').replace(/\\s+/g, ' ').slice(0, 240) })) }))))`, true);
  const out = [];
  for (const v of JSON.parse(text)) {
    for (const n of v.nodes) out.push({ rule: `axe:${v.id}`, target: n.target, detail: `${v.impact ?? ''} ${v.help}. ${n.summary}`.trim() });
  }
  return out;
}


// ---- the web app's records and personas ----
const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

// The first value of a field anywhere in a tool's answer.
function firstField(value, field) {
  if (Array.isArray(value)) {
    for (const v of value) {
      const found = firstField(v, field);
      if (found) return found;
    }
  } else if (value && typeof value === 'object') {
    if (typeof value[field] === 'string' && GUID.test(value[field])) return value[field].toLowerCase();
    for (const v of Object.values(value)) {
      const found = firstField(v, field);
      if (found) return found;
    }
  }
  return null;
}

// Reads one record of each kind the demo director makes, through the REST API as the lookup persona, into appIds.
// Returns what went wrong for each record it could not read.
async function readDirectorRecords() {
  const problems = {};
  for (const [key, { tool, input, field }] of Object.entries(DIRECTOR_RECORDS)) {
    try {
      const r = await fetch(new URL(`/api/${tool}`, appBase), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Product-Dev-Identity': lookupPersona },
        body: JSON.stringify(input),
        redirect: 'error',
        signal: AbortSignal.timeout(30000),
      });
      const body = await r.json().catch(() => null);
      appIds[key] = firstField(body?.data, field);
      if (!appIds[key]) {
        problems[key] = body?.error
          ? `${tool} as ${lookupPersona} answered ${body.error.code}: ${body.error.message}`
          : `${tool} as ${lookupPersona} (HTTP ${r.status}) listed no record. Did the demo director run?`;
      }
    } catch (e) {
      problems[key] = `${tool} as ${lookupPersona}: ${e.message}`;
    }
  }
  return problems;
}

// The development personas the web app offers (config.json), so a persona it does not offer stops the check instead
// of checking the sign-in page under every name.
async function offeredPersonas() {
  const r = await fetch(new URL('config.json', appBase), { redirect: 'error', signal: AbortSignal.timeout(30000) });
  const config = await r.json();
  return (config?.development?.personas ?? []).map(p => p.name);
}

// ---- the main action sheet (web app target) ----
// The sheet triggers of the page area the persona sees, in page order, marked so the script can click them. Radix marks
// a dialog trigger with aria-haspopup="dialog"; the page area has no popover. A trigger whose label matches the page's
// sheet pattern comes first; with only set (the page's sheetOnly), the other triggers are left out.
const FIND_TRIGGERS = String.raw`((prefer, only) => {
  const shown = el => {
    const r = el.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) return false;
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
      const cs = getComputedStyle(e);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
    }
    return true;
  };
  const label = el => (el.getAttribute('aria-label') || el.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 60);
  const all = [...document.querySelectorAll('main [aria-haspopup="dialog"]')]
    .filter(el => shown(el) && !el.disabled && el.getAttribute('aria-disabled') !== 'true' && !el.closest('[inert], [aria-hidden="true"]'));
  const re = prefer ? new RegExp(prefer, 'i') : null;
  const ordered = re ? [...all.filter(el => re.test(label(el))), ...(only ? [] : all.filter(el => !re.test(label(el))))] : all;
  for (const e of document.querySelectorAll('[data-a11y-candidate]')) e.removeAttribute('data-a11y-candidate');
  return JSON.stringify(ordered.slice(0, 6).map((el, i) => { el.setAttribute('data-a11y-candidate', String(i)); return label(el); }));
})`;
const clickCandidate = i => `(() => {
  const el = document.querySelector('[data-a11y-candidate="${i}"]');
  if (!el) return false;
  for (const e of document.querySelectorAll('[data-a11y-trigger]')) e.removeAttribute('data-a11y-trigger');
  el.setAttribute('data-a11y-trigger', '');
  el.scrollIntoView({ block: 'center' });
  el.focus();
  el.click();
  return true;
})()`;
// The open sheet, marked as the scope of the sheet checks: whether focus is in it, and how much taller the window must
// be so the part of it that scrolls shows everything at once.
const OPEN_SHEET = String.raw`(() => {
  const open = [...document.querySelectorAll('[role="dialog"], [role="alertdialog"]')]
    .filter(d => d.getAttribute('data-state') !== 'closed' && d.getAttribute('data-slot') !== 'popover-content' && d.getBoundingClientRect().width > 1);
  const d = open[open.length - 1];
  if (!d) return JSON.stringify(null);
  for (const e of document.querySelectorAll('[data-a11y-scope]')) if (e !== d) e.removeAttribute('data-a11y-scope');
  d.setAttribute('data-a11y-scope', '');
  const a = document.activeElement;
  let more = 0;
  for (const el of [d, ...d.querySelectorAll('*')]) {
    const cs = getComputedStyle(el);
    if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 1) more = Math.max(more, el.scrollHeight - el.clientHeight);
  }
  return JSON.stringify({ focusInside: !!a && d.contains(a), more });
})()`;
const SHEET_CLOSED = `(() => { const d = document.querySelector('[data-a11y-scope]'); return !d || d.getAttribute('data-state') === 'closed'; })()`;
const FOCUS_BACK = String.raw`(() => {
  const t = document.querySelector('[data-a11y-trigger]');
  const a = document.activeElement;
  const text = a ? (a.getAttribute('aria-label') || a.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 40) : '';
  const now = !a || a === document.body ? 'the page body' : a.tagName.toLowerCase() + (text ? ' "' + text + '"' : '');
  return JSON.stringify({ trigger: !!t, back: !!t && !!a && (a === t || t.contains(a)), now });
})()`;

const report = [];
let failed = false;
let badUsage = false;
try {
  if (!appUrl) await startPreview();
  let problems = {};
  if (appUrl) {
    const offered = await offeredPersonas();
    const missing = [...personas, lookupPersona].filter(p => !offered.includes(p));
    if (missing.length) {
      badUsage = true;
      throw new Error(`the web app does not offer the development persona ${missing.join(', ')}. It offers ${offered.join(', ') || 'none'}.`);
    }
    problems = await readDirectorRecords();
  }
  const wsUrl = await startChrome();
  const cdp = await connect(wsUrl);
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  await cdp.send('Page.enable', {}, sessionId);
  await cdp.send('Runtime.enable', {}, sessionId);
  await cdp.send('Network.enable', {}, sessionId);
  await cdp.send('DOM.enable', {}, sessionId);
  await cdp.send('Accessibility.enable', {}, sessionId);
  const axeSource = axeFile ? fs.readFileSync(axeFile, 'utf8') : null;
  // --clock-days: the page's Date runs that many days ahead, before any of its scripts.
  if (clockDays) {
    await cdp.send('Page.addScriptToEvaluateOnNewDocument', {
      source: `(() => {
        const shift = ${clockDays * 86400000};
        const Real = Date;
        const realNow = Real.now.bind(Real);
        class Shifted extends Real {
          constructor(...a) { if (a.length) super(...a); else super(realNow() + shift); }
          static now() { return realNow() + shift; }
        }
        globalThis.Date = Shifted;
      })();`,
    }, sessionId);
  }

  const inflight = new Set();
  const pageErrors = [];
  cdp.on(msg => {
    if (msg.sessionId !== sessionId) return;
    if (msg.method === 'Network.requestWillBeSent') inflight.add(msg.params.requestId);
    if (msg.method === 'Network.loadingFinished' || msg.method === 'Network.loadingFailed') inflight.delete(msg.params.requestId);
    if (msg.method === 'Runtime.exceptionThrown') pageErrors.push(msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text);
  });

  // The web app signs in with the persona before its scripts run; the sign-in page is checked without one.
  let personaScript = null;
  const setPersona = async name => {
    if (personaScript) await cdp.send('Page.removeScriptToEvaluateOnNewDocument', { identifier: personaScript }, sessionId);
    personaScript = null;
    const source = name === null
      ? `try { sessionStorage.removeItem('product.devPersona'); } catch (e) {}`
      : `try { sessionStorage.setItem('product.devPersona', ${JSON.stringify(name)}); } catch (e) {}`;
    personaScript = (await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source }, sessionId)).identifier;
  };
  const viewport = (width, height) => cdp.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
  const pressKey = async (key, code, keyCode) => {
    for (const type of ['keyDown', 'keyUp']) {
      await cdp.send('Input.dispatchKeyEvent', { type, key, code, windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode }, sessionId);
    }
  };
  // Waits until no animation that ends is still running, such as the sheet sliding in.
  const animationsDone = async () => {
    for (let i = 0; i < 40; i++) {
      const running = await evaluate(cdp, sessionId, `document.getAnimations().filter(a => a.playState === 'running' && a.effect && a.effect.getTiming().iterations !== Infinity).length`);
      if (!running) return;
      await sleep(100);
    }
  };
  // Every check of one scope: the page, or the open sheet.
  const inspect = async scope => {
    const { issues, unchecked, heading } = JSON.parse(await evaluate(cdp, sessionId, pageChecks(scope)));
    issues.push(...await namelessNodes(cdp, sessionId, scope));
    if (axeSource) issues.push(...await runAxe(cdp, sessionId, axeSource, scope));
    const keyboard = await keyboardWalk(cdp, sessionId, scope);
    issues.push(...keyboard.issues);
    return { issues, unchecked, heading, stops: keyboard.stops };
  };
  // Opens the main action sheet: the first trigger that opens one. Returns its label, or null with the labels tried.
  const openSheet = async (prefer, only = false) => {
    const labels = JSON.parse(await evaluate(cdp, sessionId, `(${FIND_TRIGGERS})(${JSON.stringify(prefer ?? null)}, ${only ? 'true' : 'false'})`));
    for (let i = 0; i < labels.length; i++) {
      if (!await evaluate(cdp, sessionId, clickCandidate(i))) continue;
      let state = null;
      for (let t = 0; t < 30 && !state; t++) {
        await sleep(100);
        state = JSON.parse(await evaluate(cdp, sessionId, OPEN_SHEET));
      }
      if (!state) continue;
      await animationsDone();
      const settle = await waitReady(cdp, sessionId, inflight);
      const now = JSON.parse(await evaluate(cdp, sessionId, OPEN_SHEET));
      return { label: labels[i], focusInside: state.focusInside, more: now ? now.more : 0, tried: labels, settle };
    }
    return { label: null, tried: labels };
  };

  const record = entry => {
    if (entry.issues.length) failed = true;
    report.push(entry);
    if (asJson) return;
    const where = `${entry.page}${entry.state === 'sheet' ? ` sheet "${entry.sheet}"` : ''}${entry.variant ? ' (tools failing)' : ''} at ${entry.width} px${entry.persona ? ` as ${entry.persona}` : ''}`;
    const notes = [
      entry.contrastNotChecked ? `${entry.contrastNotChecked} text contrast not checked: image or gradient background` : null,
      entry.note,
    ].filter(Boolean);
    console.log(`${entry.issues.length ? 'FAIL' : 'ok  '}  ${where}  "${entry.heading}"  ${entry.tabStops} tab stops${notes.length ? `  (${notes.join('; ')})` : ''}`);
    for (const i of entry.issues) console.log(`      ${i.rule}  ${i.target}  ${i.detail}`);
  };

  const pages = appUrl
    ? appPages(appIds).filter(p => pageNames.includes(p.name))
    : pageNames.map(name => ({ name, path: HARNESS_PAGES[name], sheet: false }));
  const variants = appUrl ? [''] : ['', 'fail=get_portfolio_summary,get_application_card,get_change_schedule,get_dependency_impact,get_application_owners'];
  const runs = appUrl ? personas : [null];
  for (const [n, runPersona] of runs.entries()) {
    for (const width of widths) {
      for (const page of pages) {
        // The development sign-in is checked signed out, once per width.
        if (appUrl && page.persona === false && n > 0) continue;
        const persona = appUrl && page.persona !== false ? runPersona : null;
        if (page.path === null) {
          if (n === 0 && width === widths[0]) {
            record({
              page: page.name, persona: null, width, state: 'page', url: null, heading: '', contrastNotChecked: 0, tabStops: 0,
              issues: [{ rule: 'no-demo-record', target: 'page', detail: `not checked: ${problems[DIRECTOR_PAGE[page.name]] ?? 'no record to open'}` }],
            });
          }
          continue;
        }
        for (const variant of variants) {
          if (appUrl) await setPersona(persona);
          let url = appUrl ? new URL(page.path, appBase).toString() : `${harnessBase}${page.path}`;
          if (variant) url = url.replace('#', `?${variant}#`);
          pageErrors.length = 0;
          inflight.clear();
          await viewport(width, 900);
          await cdp.send('Page.navigate', { url: 'about:blank' }, sessionId);
          const loaded = waitEvent(cdp, sessionId, 'Page.loadEventFired');
          await cdp.send('Page.navigate', { url }, sessionId);
          await loaded;
          const settle = await waitReady(cdp, sessionId, inflight);

          // The whole page in the viewport, so the contrast check sees every text at its place.
          const height = Math.max(900, await evaluate(cdp, sessionId, 'Math.ceil(document.documentElement.scrollHeight)'));
          await viewport(width, height);
          await sleep(250);

          const base = { page: page.name, persona, width, url, variant: variant || undefined };
          const seen = await inspect('page');
          seen.issues.unshift(...settle);
          for (const e of pageErrors) seen.issues.push({ rule: 'page-error', target: 'page', detail: e });
          const entry = { ...base, state: 'page', heading: seen.heading, issues: seen.issues, contrastNotChecked: seen.unchecked, tabStops: seen.stops };
          if (!appUrl || !openSheets || page.sheet === false || persona === null) {
            record(entry);
            continue;
          }

          // The main action sheet, as this persona sees it.
          const opened = await openSheet(page.sheet, page.sheetOnly === true && typeof page.sheet === 'string');
          if (!opened.label) {
            entry.note = page.sheetOnly ? `no sheet matching /${page.sheet}/ for this persona` : 'no action sheet for this persona';
            record(entry);
            continue;
          }
          entry.note = `action sheet: "${opened.label}"`;
          record(entry);
          pageErrors.length = 0;
          if (opened.more > 0) {
            await viewport(width, Math.min(height + opened.more + 40, 16000));
            await sleep(250);
            await animationsDone();
          }
          const sheet = await inspect('sheet');
          sheet.issues.unshift(...opened.settle);
          if (!opened.focusInside) sheet.issues.unshift({ rule: 'sheet-focus', target: `sheet "${sheet.heading}"`, detail: 'focus did not move into the sheet when it opened' });
          // Escape closes the sheet, and focus goes back to the control that opened it.
          await pressKey('Escape', 'Escape', 27);
          let closed = false;
          for (let t = 0; t < 30 && !closed; t++) {
            await sleep(100);
            closed = await evaluate(cdp, sessionId, SHEET_CLOSED);
          }
          if (!closed) {
            sheet.issues.push({ rule: 'sheet-escape', target: `sheet "${sheet.heading}"`, detail: 'Escape does not close the sheet' });
          } else {
            await animationsDone();
            await sleep(150);
            const back = JSON.parse(await evaluate(cdp, sessionId, FOCUS_BACK));
            if (!back.back) {
              sheet.issues.push({
                rule: 'sheet-focus-return',
                target: `button "${opened.label}"`,
                detail: back.trigger ? `when the sheet closed, focus went to ${back.now}, not to the control that opened it` : 'the control that opened the sheet is gone after it closed',
              });
            }
          }
          for (const e of pageErrors) sheet.issues.push({ rule: 'page-error', target: 'sheet', detail: e });
          record({ ...base, state: 'sheet', sheet: opened.label, heading: sheet.heading, issues: sheet.issues, contrastNotChecked: sheet.unchecked, tabStops: sheet.stops });
        }
      }
    }
  }
  await cdp.send('Browser.close').catch(() => {});
  cdp.close();
} catch (e) {
  failed = true;
  console.error(`a11y: ${e.message}`);
} finally {
  if (preview) preview.kill();
  if (browser && browser.exitCode === null) browser.kill();
  for (let i = 0; i < 20; i++) {
    try { fs.rmSync(profile, { recursive: true, force: true }); break; } catch { await sleep(250); }
  }
}
if (asJson) console.log(JSON.stringify({ axe: Boolean(axeFile), personas: appUrl ? personas : [], clockDays: clockDays || undefined, pages: report }, null, 2));
process.exit(badUsage ? 2 : failed ? 1 : 0);
