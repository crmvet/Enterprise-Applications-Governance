#!/usr/bin/env node
// Renders the four harness pages at 1280 px and 390 px with headless Chrome and writes one full-page PNG each.
// No packages: Node 22 or later (global WebSocket and fetch) and an installed Chrome.
//
// It serves the built harness with "vite preview" on 127.0.0.1, starts Chrome with a temporary profile of its own,
// and drives it over the DevTools protocol on 127.0.0.1. Chrome resolves no host except 127.0.0.1, and its
// background services (updates, sync, metrics, safe browsing pings) are off. Run "npm run build" first.
//
// Usage, from Git Bash in src/ui/harness (Chrome on Windows needs absolute Windows paths, so cygpath converts them):
//   node scripts/screenshots.mjs --out "$(cygpath -aw ../screenshots)" \
//     --chrome "$(cygpath -aw '/c/Program Files/Google/Chrome/Application/chrome.exe')"
// Options: --pages overview,app,map,timeline  --widths 1280,390  --query fail=get_change_schedule  --suffix -failure
//          --slices <absolute folder>   also writes each page in 1000 px slices, for a close look at tall pages
// Exit code: 0 when every page rendered with no page error and no sideways scroll, 1 otherwise, 2 for bad usage.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HARNESS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const PORT = 4318;
const BASE = `http://127.0.0.1:${PORT}/`;
const APP_ID = '10000000-0000-4000-8000-000000000001';
const PAGES = {
  overview: { hash: '#/overview', file: 'overview' },
  app: { hash: `#/app/${APP_ID}`, file: 'application-360' },
  map: { hash: `#/map/${APP_ID}`, file: 'dependency-map' },
  timeline: { hash: '#/timeline', file: 'timeline' },
};

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const outDir = opt('--out', null);
const chrome = opt('--chrome', 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe');
const pages = opt('--pages', 'overview,app,map,timeline').split(',').map(s => s.trim()).filter(Boolean);
const widths = opt('--widths', '1280,390').split(',').map(Number);
const query = opt('--query', '');
const suffix = opt('--suffix', '');
const slicesDir = opt('--slices', null);
if (slicesDir && !path.isAbsolute(slicesDir)) {
  console.error('screenshots: --slices needs an absolute folder.');
  process.exit(2);
}
if (slicesDir) fs.mkdirSync(slicesDir, { recursive: true });

if (!outDir || !path.isAbsolute(outDir)) {
  console.error('screenshots: --out needs an absolute folder (use cygpath -aw from Git Bash).');
  process.exit(2);
}
if (pages.some(p => !PAGES[p]) || widths.some(w => !Number.isInteger(w) || w < 320)) {
  console.error('screenshots: unknown page or bad width.');
  process.exit(2);
}
if (!fs.existsSync(path.join(HARNESS, 'dist', 'index.html'))) {
  console.error('screenshots: no dist/index.html. Run "npm run build" first.');
  process.exit(2);
}
if (!fs.existsSync(chrome)) {
  console.error(`screenshots: Chrome not found at ${chrome}`);
  process.exit(2);
}
fs.mkdirSync(outDir, { recursive: true });

const sleep = ms => new Promise(r => setTimeout(r, ms));

// ---- the preview server ----
const preview = spawn(process.execPath, [path.join(HARNESS, 'node_modules', 'vite', 'bin', 'vite.js'), 'preview', '--host', '127.0.0.1', '--port', String(PORT), '--strictPort'], {
  cwd: HARNESS,
  stdio: ['ignore', 'pipe', 'pipe'],
});
let previewLog = '';
preview.stdout.on('data', d => { previewLog += d; });
preview.stderr.on('data', d => { previewLog += d; });

async function waitForServer() {
  for (let i = 0; i < 100; i++) {
    try {
      const r = await fetch(BASE);
      if (r.ok) return;
    } catch { /* not up yet */ }
    await sleep(200);
  }
  throw new Error(`the preview server did not start:\n${previewLog}`);
}

// ---- Chrome ----
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'harness-chrome-'));
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

// ---- a small DevTools protocol client ----
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

async function evaluate(cdp, sessionId, expression) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true }, sessionId);
  return r.result.value;
}

// Ready when the page made its tool calls, none is pending, fonts are loaded, and that stays so for 400 ms.
async function waitReady(cdp, sessionId) {
  let stable = 0;
  let last = '';
  for (let i = 0; i < 200; i++) {
    const v = await evaluate(cdp, sessionId, 'JSON.stringify([window.__harnessCalls || 0, window.__harnessPending || 0, document.fonts.status])');
    const [calls, pending, fonts] = JSON.parse(v);
    if (calls > 0 && pending === 0 && fonts === 'loaded' && v === last) stable++;
    else stable = 0;
    last = v;
    if (stable >= 4) return;
    await sleep(100);
  }
  throw new Error('the page did not settle');
}

let failed = false;
try {
  await waitForServer();
  const wsUrl = await startChrome();
  const cdp = await connect(wsUrl);
  const { targetId } = await cdp.send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  await cdp.send('Page.enable', {}, sessionId);
  await cdp.send('Runtime.enable', {}, sessionId);

  const pageErrors = [];
  cdp.on(msg => {
    if (msg.sessionId !== sessionId) return;
    if (msg.method === 'Runtime.exceptionThrown') pageErrors.push(msg.params.exceptionDetails.exception?.description ?? msg.params.exceptionDetails.text);
    if (msg.method === 'Runtime.consoleAPICalled' && (msg.params.type === 'error' || msg.params.type === 'warning')) {
      pageErrors.push(`console.${msg.params.type}: ${msg.params.args.map(a => a.value ?? a.description ?? '').join(' ')}`);
    }
  });

  for (const width of widths) {
    for (const page of pages) {
      const { hash, file } = PAGES[page];
      const url = `${BASE}${query ? `?${query}` : ''}${hash}`;
      const target = path.join(outDir, `${file}-${width}${suffix}.png`);
      pageErrors.length = 0;
      await cdp.send('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
      await cdp.send('Page.navigate', { url: 'about:blank' }, sessionId);
      const loaded = waitEvent(cdp, sessionId, 'Page.loadEventFired');
      await cdp.send('Page.navigate', { url }, sessionId);
      await loaded;
      await waitReady(cdp, sessionId);

      let size = JSON.parse(await evaluate(cdp, sessionId, 'JSON.stringify({ h: Math.ceil(document.documentElement.scrollHeight), sw: document.documentElement.scrollWidth, iw: window.innerWidth })'));
      await cdp.send('Emulation.setDeviceMetricsOverride', { width, height: size.h, deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
      await sleep(300);
      size = JSON.parse(await evaluate(cdp, sessionId, 'JSON.stringify({ h: Math.ceil(document.documentElement.scrollHeight), sw: document.documentElement.scrollWidth, iw: window.innerWidth })'));
      await cdp.send('Emulation.setDeviceMetricsOverride', { width, height: size.h, deviceScaleFactor: 1, mobile: width < 600 }, sessionId);
      await sleep(150);

      const shot = await cdp.send('Page.captureScreenshot', { format: 'png', fromSurface: true }, sessionId);
      fs.writeFileSync(target, Buffer.from(shot.data, 'base64'));
      if (slicesDir) {
        for (let y = 0, i = 1; y < size.h; y += 1000, i++) {
          const clip = { x: 0, y, width, height: Math.min(1000, size.h - y), scale: 1 };
          const part = await cdp.send('Page.captureScreenshot', { format: 'png', fromSurface: true, clip }, sessionId);
          fs.writeFileSync(path.join(slicesDir, `${file}-${width}${suffix}-part${i}.png`), Buffer.from(part.data, 'base64'));
        }
      }
      const sideways = size.sw > size.iw;
      const problems = [...(sideways ? [`scrolls sideways: ${size.sw} px content in ${size.iw} px`] : []), ...pageErrors];
      if (problems.length) failed = true;
      console.log(`${problems.length ? 'FAIL' : 'ok  '}  ${path.basename(target)}  ${width} x ${size.h}${problems.length ? `\n      ${problems.join('\n      ')}` : ''}`);
    }
  }
  await cdp.send('Browser.close').catch(() => {});
  cdp.close();
} catch (e) {
  failed = true;
  console.error(`screenshots: ${e.message}`);
} finally {
  preview.kill();
  if (browser && browser.exitCode === null) browser.kill();
  for (let i = 0; i < 20; i++) {
    try { fs.rmSync(profile, { recursive: true, force: true }); break; } catch { await sleep(250); }
  }
}
process.exit(failed ? 1 : 0);
