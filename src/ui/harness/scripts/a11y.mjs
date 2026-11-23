#!/usr/bin/env node
// Accessibility check of the four pages with headless Chrome, at 1280 px and 390 px. No packages: Node 22 or later
// (global WebSocket and fetch) and an installed Chrome.
//
// Two targets:
//   the harness (default): serves the built harness (dist/harness, "npm run build" first) with "vite preview" on
//     127.0.0.1 and opens the four pages on their fictional fixtures, also with every tool failing;
//   the web app (--app <url>): the built web app as the local server serves it under /app/ (src/adapters/azure, "Run it
//     locally"), signed in with a fictional development persona (--persona, default nadia-web). The URL must be on
//     127.0.0.1, localhost or [::1]. It adds the sign-in page, the applications list and a page that does not exist.
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
//   node scripts/a11y.mjs --app http://127.0.0.1:5080/app/ --persona nadia-web
// Options: --pages overview,app,map,timeline  --widths 1280,390  --app-id <application id>  --json
// Exit code: 0 when no page has an issue, 1 when one has, 2 for bad usage.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = 4320;

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const chrome = opt('--chrome', 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe');
const appUrl = opt('--app', null);
const persona = opt('--persona', 'nadia-web');
const widths = opt('--widths', '1280,390').split(',').map(Number);
const axeFile = opt('--axe', null);
const asJson = args.includes('--json');
const harnessAppId = '10000000-0000-4000-8000-000000000001';
const appId = opt('--app-id', appUrl ? '00000000-0000-4000-8000-000000000511' : harnessAppId);

const HARNESS_PAGES = {
  overview: '#/overview',
  app: `#/app/${appId}`,
  map: `#/map/${appId}`,
  timeline: '#/timeline',
};
const APP_PAGES = {
  overview: '',
  app: `records/business-application/${appId}`,
  map: `records/business-application/${appId}/map`,
  timeline: 'timeline',
  applications: 'applications?stage=build',
  missing: 'no-such-page',
  'sign-in': '',
};
const pageNames = opt('--pages', appUrl ? Object.keys(APP_PAGES).join(',') : Object.keys(HARNESS_PAGES).join(',')).split(',').map(s => s.trim()).filter(Boolean);

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
const PAGES = appUrl ? APP_PAGES : HARNESS_PAGES;
if (pageNames.some(p => !(p in PAGES))) usage(`unknown page; choose from ${Object.keys(PAGES).join(', ')}.`);
if (widths.some(w => !Number.isInteger(w) || w < 320)) usage('bad width.');
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
async function waitReady(cdp, sessionId, inflight) {
  let stable = 0;
  let last = '';
  for (let i = 0; i < 300; i++) {
    const v = await evaluate(cdp, sessionId, `JSON.stringify([document.readyState, document.fonts.status, document.querySelectorAll('[aria-busy="true"]').length, document.body ? document.body.innerText.length : 0])`);
    const state = `${v}|${inflight.size}`;
    const [ready, fonts, busy] = JSON.parse(v);
    if (ready === 'complete' && fonts === 'loaded' && busy === 0 && inflight.size === 0 && state === last) stable++;
    else stable = 0;
    last = state;
    if (stable >= 5) return;
    await sleep(100);
  }
  throw new Error('the page did not settle');
}

// ---- the checks that run in the page ----
// Returns { issues: [{ rule, target, detail }], unchecked: n, heading: the level 1 heading }.
const PAGE_CHECKS = String.raw`(() => {
  const issues = [];
  let unchecked = 0;
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

  const lang = (document.documentElement.getAttribute('lang') || '').trim();
  if (!lang) add('html-lang', document.documentElement, 'the page names no language');
  if (!document.title.trim()) add('document-title', document.documentElement, 'the page has no title');

  const mains = [...document.querySelectorAll('main, [role="main"]')].filter(shown);
  if (mains.length !== 1) add('one-main', document.body, mains.length + ' main landmarks shown');

  const headings = [...document.querySelectorAll('h1, h2, h3, h4, h5, h6, [role="heading"]')].filter(h => shown(h) && !hiddenFromAll(h));
  const level = h => h.getAttribute('aria-level') ? Number(h.getAttribute('aria-level')) : Number(h.tagName.slice(1)) || 2;
  if (!headings.some(h => level(h) === 1)) add('page-has-heading-one', document.body, 'no level 1 heading');
  let previous = 0;
  for (const h of headings) {
    const l = level(h);
    if (previous && l > previous + 1) add('heading-order', h, 'level ' + previous + ' then level ' + l);
    previous = l;
    if (!(h.textContent || '').trim() && !(h.getAttribute('aria-label') || '').trim()) add('empty-heading', h);
  }

  for (const img of document.querySelectorAll('img')) {
    if (!img.hasAttribute('alt') && !hiddenFromAll(img)) add('image-alt', img, 'an image with no alt attribute');
  }
  for (const svg of document.querySelectorAll('svg[role="img"]')) {
    if (!hiddenFromAll(svg) && !(svg.getAttribute('aria-label') || '').trim() && !svg.getAttribute('aria-labelledby') && !svg.querySelector('title')) add('svg-img-alt', svg, 'an image role with no name');
  }

  const ids = new Map();
  for (const el of document.querySelectorAll('[id]')) ids.set(el.id, (ids.get(el.id) || 0) + 1);
  for (const [id, n] of ids) if (n > 1) add('duplicate-id', document.getElementById(id), n + ' elements share the id ' + id);
  for (const attr of ['aria-labelledby', 'aria-describedby', 'aria-controls', 'aria-owns', 'aria-activedescendant']) {
    for (const el of document.querySelectorAll('[' + attr + ']')) {
      for (const ref of (el.getAttribute(attr) || '').split(/\s+/).filter(Boolean)) {
        if (!document.getElementById(ref)) add('aria-valid-reference', el, attr + ' names ' + ref + ', which is not on the page');
      }
    }
  }
  for (const el of document.querySelectorAll('[tabindex]')) {
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
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
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
  const h1 = headings.find(h => level(h) === 1);
  return JSON.stringify({ issues, unchecked, heading: h1 ? h1.innerText.trim().replace(/\s+/g, ' ').slice(0, 80) : '' });
})()`;

// Controls, links, tabs and images on Chrome's accessibility tree that have no name.
const NAMED_ROLES = new Set(['button', 'link', 'textbox', 'searchbox', 'combobox', 'listbox', 'checkbox', 'radio', 'switch', 'tab', 'menuitem', 'menuitemcheckbox', 'menuitemradio', 'slider', 'spinbutton', 'option', 'treeitem', 'image', 'img', 'progressbar', 'meter', 'dialog', 'alertdialog']);
async function namelessNodes(cdp, sessionId) {
  const { nodes } = await cdp.send('Accessibility.getFullAXTree', {}, sessionId);
  const found = [];
  for (const node of nodes) {
    const role = node.role?.value;
    if (node.ignored || !NAMED_ROLES.has(role)) continue;
    const name = (node.name?.value ?? '').trim();
    if (name) continue;
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
// keyboard trap. Radix's focus guards hand focus on at once and are not counted as stops.
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
  return JSON.stringify({ stop: e.__a11yStop, label, shown: !hidden && r.width >= 1 && r.height >= 1 });
})()`;
async function keyboardWalk(cdp, sessionId, maxPresses = 150) {
  await evaluate(cdp, sessionId, 'window.__a11yStops = 0; if (document.activeElement && document.activeElement.blur) document.activeElement.blur(); window.scrollTo(0, 0); true');
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
    else if (state.stop === first) { wrapped = true; break; }
    if (!state.shown) issues.push({ rule: 'focus-hidden', target: state.label, detail: 'the Tab key puts focus on an element that is not shown' });
    stops.push(state);
    const n = stops.length;
    if (n >= 3 && stops[n - 1].stop === stops[n - 2].stop && stops[n - 2].stop === stops[n - 3].stop) {
      issues.push({ rule: 'keyboard-trap', target: state.label, detail: 'three Tab presses left focus on this element' });
      break;
    }
  }
  if (!wrapped && stops.length >= 30 && new Set(stops.slice(-30).map(s => s.stop)).size <= 5) {
    issues.push({ rule: 'keyboard-trap', target: stops[stops.length - 1].label, detail: `after ${maxPresses} Tab presses focus circles ${new Set(stops.slice(-30).map(s => s.stop)).size} elements` });
  }
  return { issues, stops: new Set(stops.map(s => s.stop)).size };
}

async function runAxe(cdp, sessionId, source) {
  const loaded = await evaluate(cdp, sessionId, 'typeof window.axe');
  if (loaded !== 'object') await evaluate(cdp, sessionId, source);
  const text = await evaluate(cdp, sessionId, `axe.run({ include: [['body']], exclude: [['[data-radix-focus-guard]']] }, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] }, resultTypes: ['violations'] }).then(r => JSON.stringify(r.violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.map(n => ({ target: n.target.join(' '), summary: (n.failureSummary || '').replace(/\\s+/g, ' ').slice(0, 240) })) }))))`, true);
  const out = [];
  for (const v of JSON.parse(text)) {
    for (const n of v.nodes) out.push({ rule: `axe:${v.id}`, target: n.target, detail: `${v.impact ?? ''} ${v.help}. ${n.summary}`.trim() });
  }
  return out;
}

function urlOf(name) {
  if (!appUrl) return `${harnessBase}${PAGES[name]}`;
  return new URL(PAGES[name], appBase).toString();
}

const report = [];
let failed = false;
try {
  if (!appUrl) await startPreview();
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

  const variants = appUrl ? [''] : ['', 'fail=get_portfolio_summary,get_application_card,get_change_schedule,get_dependency_impact,get_application_owners'];
  for (const width of widths) {
    for (const name of pageNames) {
      for (const variant of variants) {
        if (appUrl) await setPersona(name === 'sign-in' ? null : persona);
        let url = urlOf(name);
        if (variant) url = url.replace('#', `?${variant}#`);
        const label = `${name}${variant ? ' (tools failing)' : ''} at ${width} px`;
        pageErrors.length = 0;
        inflight.clear();
        await cdp.send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
        await cdp.send('Page.navigate', { url: 'about:blank' }, sessionId);
        const loaded = waitEvent(cdp, sessionId, 'Page.loadEventFired');
        await cdp.send('Page.navigate', { url }, sessionId);
        await loaded;
        await waitReady(cdp, sessionId, inflight);

        // The whole page in the viewport, so the contrast check sees every text at its place.
        const height = await evaluate(cdp, sessionId, 'Math.ceil(document.documentElement.scrollHeight)');
        await cdp.send('Emulation.setDeviceMetricsOverride', { width, height: Math.max(900, height), deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
        await sleep(250);

        const { issues, unchecked, heading } = JSON.parse(await evaluate(cdp, sessionId, PAGE_CHECKS));
        issues.push(...await namelessNodes(cdp, sessionId));
        if (axeSource) issues.push(...await runAxe(cdp, sessionId, axeSource));
        const keyboard = await keyboardWalk(cdp, sessionId);
        issues.push(...keyboard.issues);
        for (const e of pageErrors) issues.push({ rule: 'page-error', target: 'page', detail: e });
        if (issues.length) failed = true;
        report.push({ page: label, url, heading, issues, contrastNotChecked: unchecked, tabStops: keyboard.stops });
        if (!asJson) {
          console.log(`${issues.length ? 'FAIL' : 'ok  '}  ${label}  "${heading}"  ${keyboard.stops} tab stops${unchecked ? `  (${unchecked} text contrast not checked: image or gradient background)` : ''}`);
          for (const i of issues) console.log(`      ${i.rule}  ${i.target}  ${i.detail}`);
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
if (asJson) console.log(JSON.stringify({ axe: Boolean(axeFile), pages: report }, null, 2));
process.exit(failed ? 1 : 0);
