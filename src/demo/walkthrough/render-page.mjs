#!/usr/bin/env node
// Renders the walkthrough page from the transcript the walkthrough test writes (README.md in this folder).
// Reads transcript.json and the CSS of the step 2 design page, writes one local, light-only HTML page.
// No packages, no network. Node 20 or later.
//
// Usage: node demo/walkthrough/render-page.mjs [--transcript <file>] [--css-from <html>] [--out <html>] [--check]
//   --check   writes nothing; exit 1 when the page on disk differs from a fresh render
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const SRC = path.resolve(HERE, '..', '..');
const STEP2 = path.resolve(SRC, '..', 'docs', 'design', 'step2');

const args = process.argv.slice(2);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
};
const transcriptPath = path.resolve(opt('--transcript', path.join(HERE, 'transcript.json')));
const cssFrom = path.resolve(opt('--css-from', path.join(STEP2, 'step2-design.html')));
const outPath = path.resolve(opt('--out', path.join(STEP2, 'demo-transcript.html')));
const checkOnly = args.includes('--check');

const transcript = JSON.parse(fs.readFileSync(transcriptPath, 'utf8'));
const designHtml = fs.readFileSync(cssFrom, 'utf8');
const cssMatch = designHtml.match(/<style>([\s\S]*?)<\/style>/);
if (!cssMatch) {
  console.error(`No <style> block in ${cssFrom}`);
  process.exit(2);
}
// The step 2 page's CSS, without its own layout comment (it describes that page), then this page's additions.
const baseCss = cssMatch[1].replace(/^\s*\/\*[\s\S]*?\*\/\s*/, '').trimEnd();

const EN_DASH = String.fromCharCode(0x2013);
const EM_DASH = String.fromCharCode(0x2014);

// ---- helpers ----

const esc = s => String(s)
  .replace(/&/g, '&amp;')
  .replace(/</g, '&lt;')
  .replace(/>/g, '&gt;')
  .replace(/"/g, '&quot;');

// Trims a JSON value for display: long strings, long lists and named members. Every cut is counted for the note.
const MAX_STRING = 150;
const KEEP_STRING = 110;
function trimJson(value, options, notes, depth = 0) {
  const maxItems = options.maxItems ?? 4;
  if (typeof value === 'string') {
    if (value.length > MAX_STRING) {
      notes.strings++;
      return `${value.slice(0, KEEP_STRING)} [trimmed: ${value.length - KEEP_STRING} more characters]`;
    }
    return value;
  }
  if (Array.isArray(value)) {
    const keep = value.slice(0, maxItems).map(v => trimJson(v, options, notes, depth + 1));
    if (value.length > maxItems) {
      notes.lists++;
      keep.push(`[trimmed: ${value.length - maxItems} more items]`);
    }
    return keep;
  }
  if (value && typeof value === 'object') {
    if (depth >= (options.maxDepth ?? 6)) {
      notes.deep++;
      return '[trimmed: nested object]';
    }
    const out = {};
    for (const [k, v] of Object.entries(value)) {
      if ((options.omit || []).includes(k)) {
        notes.omitted.add(k);
        continue;
      }
      if (v === null && options.dropNulls) {
        notes.nulls++;
        continue;
      }
      out[k] = trimJson(v, options, notes, depth + 1);
    }
    return out;
  }
  return value;
}

// Pretty JSON with light syntax colors, escaped for HTML.
function jsonHtml(value) {
  const text = JSON.stringify(value, null, 2);
  return esc(text)
    .replace(/(&quot;(?:[^&]|&(?!quot;))*?&quot;)(\s*:)/g, '<span class="jk">$1</span>$2')
    .replace(/(:\s*)(&quot;(?:[^&]|&(?!quot;))*?&quot;)/g, '$1<span class="js">$2</span>')
    .replace(/(^|\[|,|\s)(true|false|null)(?=\s*[,\]\n}]|$)/gm, '$1<span class="jl">$2</span>')
    .replace(/(:\s*)(-?\d+(?:\.\d+)?)(?=\s*[,\n}])/g, '$1<span class="jn">$2</span>');
}

function noteText(notes, extra) {
  const parts = [];
  if (notes.omitted.size) parts.push(`left out: ${[...notes.omitted].map(k => `<code class="k">${esc(k)}</code>`).join(', ')}`);
  if (notes.strings) parts.push(`${notes.strings} long text field${notes.strings > 1 ? 's' : ''} cut`);
  if (notes.lists) parts.push(`${notes.lists} list${notes.lists > 1 ? 's' : ''} shortened`);
  if (notes.deep) parts.push(`${notes.deep} nested object${notes.deep > 1 ? 's' : ''} folded`);
  if (notes.nulls) parts.push(`${notes.nulls} empty field${notes.nulls > 1 ? 's' : ''} hidden`);
  if (extra) parts.push(extra);
  return parts.length ? `Trimmed for reading: ${parts.join('; ')}.` : 'Shown in full.';
}

const newNotes = () => ({ strings: 0, lists: 0, deep: 0, nulls: 0, omitted: new Set() });

// What each kind of exchange shows: the request as sent and the answer, trimmed.
const ENVELOPE_OMIT = ['engineStamp', 'untrustedFields'];
function views(x) {
  const ch = x.channel;
  if (ch === 'mcp') {
    const req = { headers: x.request.headers, body: x.request.body };
    const body = x.response.body;
    const envelope = body && body.result && body.result.structuredContent;
    let res;
    let resExtra = '';
    if (envelope) {
      res = { httpStatus: x.response.httpStatus, 'result.isError': body.result.isError, 'result.structuredContent': envelope };
      resExtra = 'result.content repeats the envelope as text and is not shown';
    } else {
      res = { httpStatus: x.response.httpStatus, body };
    }
    const isList = x.entryPoint === 'tools/list';
    return {
      req,
      reqOptions: { maxItems: 6 },
      res,
      resOptions: { omit: ENVELOPE_OMIT, maxItems: isList ? 8 : 4, dropNulls: true },
      resExtra: isList ? `${body.result.tools.length} tools in all; the transcript keeps each tool's name, annotations and schema size` : resExtra,
    };
  }
  if (ch === 'seed') {
    return { req: x.request, reqOptions: { maxItems: 4 }, res: null };
  }
  // engine and record-endpoint: the caller, the body, then the runtime outcome.
  const r = x.response;
  const res = {
    outcome: r.outcome,
    ...(r.mcpError ? { mcpError: r.mcpError } : {}),
    reasons: r.reasons,
    // A record step answers with its flags in the payload, so they come before the writes.
    ...(x.channel === 'record-endpoint' ? { payload: r.payload } : {}),
    writes: (r.writes || []).map(w => ({ entity: w.entity, operation: w.operation, rowId: w.rowId, effect: w.effect })),
    ...(x.channel === 'record-endpoint' ? {} : { payload: r.payload }),
    rowId: r.rowId,
    created: r.created,
    configurationVersion: r.configurationVersion,
  };
  return {
    req: x.request,
    reqOptions: { maxItems: 6, omit: ['groups'] },
    res,
    resOptions: { maxItems: 5, dropNulls: true, omit: [] },
    resExtra: [x.response.mcpError ? "mcpError is the fixed code the MCP server's own map gives this refusal" : '', Array.isArray(x.response.writes) && x.response.writes.length ? 'each write shows its entity, operation and effect; its columns are in the transcript' : ''].filter(Boolean).join('; '),
  };
}

const CHANNEL = {
  mcp: { label: 'MCP', cls: 'blue', title: 'A JSON-RPC request to the MCP server core, as Claude Code sends it' },
  engine: { label: 'Engine', cls: 'eng', title: "A write at the engine's table door, not through MCP" },
  'record-endpoint': { label: 'Record endpoint', cls: 'eng', title: 'The engine entry the record endpoint host calls' },
  seed: { label: 'Runner seed', cls: 'open', title: 'Test setup written straight into the in-memory store' },
};

function chips(outcome) {
  const out = [];
  if (outcome.ok === true) out.push('<span class="pill ok">ok</span>');
  if (outcome.ok === false) out.push('<span class="pill no">refused</span>');
  if (outcome.errorCode) out.push(`<span class="pill no">${esc(outcome.errorCode)}</span>`);
  for (const code of outcome.reasonCodes || []) out.push(`<code class="rc">${esc(code)}</code>`);
  return out.join(' ');
}

function exchangeHtml(x) {
  const v = views(x);
  const ch = CHANNEL[x.channel] || CHANNEL.engine;
  const reqNotes = newNotes();
  const reqShown = trimJson(v.req, v.reqOptions, reqNotes);
  let resBlock = '';
  if (v.res) {
    const resNotes = newNotes();
    const resShown = trimJson(v.res, v.resOptions, resNotes);
    resBlock = `
          <div class="io">
            <p class="label">${x.channel === 'mcp' ? 'Response' : 'Answer'}</p>
            <pre class="code"><code>${jsonHtml(resShown)}</code></pre>
            <p class="trim">${noteText(resNotes, v.resExtra)}</p>
          </div>`;
  }
  return `
        <div class="xch" id="x${x.n}">
          <div class="xch-head">
            <span class="pill ${ch.cls}" title="${esc(ch.title)}">${esc(ch.label)}</span>
            <code class="ep">${esc(x.entryPoint)}</code>
            <span class="meta">${esc(x.actor)}, ${esc(x.at.replace('T', ' ').replace('Z', ' UTC'))}</span>
            <span class="xch-out">${chips(x.outcome)}</span>
          </div>
          <div class="io-grid${v.res ? '' : ' one'}">
          <div class="io">
            <p class="label">${x.channel === 'seed' ? 'Rows the runner writes' : 'Request'}</p>
            <pre class="code"><code>${jsonHtml(reqShown)}</code></pre>
            <p class="trim">${noteText(reqNotes, '')}</p>
          </div>${resBlock}
          </div>
        </div>`;
}

// Inline markup in narrative text: `code` becomes a code chip. Everything else is escaped.
const inline = s => esc(s).replace(/`([^`]+)`/g, '<code class="rc">$1</code>');

function stepHtml(step, exchanges) {
  const xs = step.exchanges.map(i => exchanges[i]);
  const main = new Set(step.main || step.exchanges);
  const shown = xs.filter(x => main.has(x.n));
  const folded = xs.filter(x => !main.has(x.n));
  const tools = [...new Set(xs.map(x => x.entryPoint))];
  return `
    <section class="step${step.safety ? ' safety' : ''}" id="s${esc(step.id)}" aria-labelledby="h-s${esc(step.id)}">
      <div class="step-head">
        <p class="eyebrow">Step ${esc(step.id)} of ${transcript.steps.length}${step.safety ? ' <span class="pill gold">The safety moment</span>' : ''}</p>
        <h2 id="h-s${esc(step.id)}">${inline(step.title)}</h2>
      </div>
      <div class="said">
        <p class="label">${esc(step.who)}</p>
        <p class="quote">${inline(step.asked)}</p>
      </div>
      <p class="small"><b>Called:</b> ${tools.map(t => `<code class="ep">${esc(t)}</code>`).join(' ')}</p>
      <div class="decided">
        <p class="label">What the engine decided, and why</p>
        <p>${inline(step.decided)}</p>
      </div>
      ${(step.notes || []).map(n => `<p class="aside">${inline(n)}</p>`).join('\n      ')}
      <p class="label">The main calls, request and answer</p>
      <div class="xch-list">${shown.map(exchangeHtml).join('')}
      </div>${folded.length ? `
      <details class="xchs">
        <summary>The other ${folded.length === 1 ? 'call' : `${folded.length} calls`} of this step: ${[...new Set(folded.map(x => x.entryPoint))].map(t => esc(t)).join(', ')}</summary>
        <div class="xch-list">${folded.map(exchangeHtml).join('')}
        </div>
      </details>` : ''}
    </section>`;
}

const extraCss = `
/* Walkthrough page: step cards, the safety card, light code boxes. Light only, navy text. */
.pill.ok { background: #e3f4ea; color: var(--ink); border: 1px solid #a9d8bb; }
.pill.no { background: #fde9e7; color: var(--ink); border: 1px solid #f0b4ad; }
.pill.eng { background: var(--sunk); color: var(--ink); border: 1px solid var(--rule-strong); }
code.rc, code.ep { font-family: var(--font-mono); font-size: 12.5px; color: var(--ink); background: var(--sunk); border: 1px solid var(--rule); border-radius: 4px; padding: 0 5px; overflow-wrap: anywhere; }
code.ep { background: var(--paper); }
.cast td code { font-family: var(--font-mono); font-size: 12.5px; }
.legend { list-style: none; padding: 0; margin: 0; display: grid; gap: 6px; font-size: 14.5px; color: var(--ink-2); }
.legend li { display: flex; flex-wrap: wrap; gap: 4px 10px; align-items: baseline; }
.step { background: var(--paper); border: 1px solid var(--rule); border-radius: 10px; padding: 20px 22px; gap: 14px; box-shadow: var(--shadow); }
.step.safety { border: 2px solid var(--gold); background: #fffdf5; }
.step-head { display: grid; gap: 6px; }
.said { background: var(--blue-soft); border-radius: 8px; padding: 12px 16px; display: grid; gap: 4px; }
.said .quote { font-size: 17px; color: var(--ink); }
.said .quote::before { content: "\\201C"; }
.said .quote::after { content: "\\201D"; }
.decided { border-left: 4px solid var(--blue); padding: 4px 0 4px 14px; display: grid; gap: 4px; max-width: 100ch; }
.step.safety .decided { border-left-color: var(--gold); }
.decided p:not(.label) { font-size: 15.5px; color: var(--ink); }
.aside { font-size: 14px; color: var(--ink-2); background: var(--gold-soft); border: 1px solid var(--gold); border-radius: 8px; padding: 8px 12px; max-width: 100ch; }
details.xchs { border-top: 1px solid var(--rule); padding-top: 10px; }
details.xchs > summary { cursor: pointer; font-weight: 600; font-size: 14.5px; color: var(--link); }
.xch-list { display: grid; gap: 18px; }
details.xchs .xch-list { margin-top: 12px; }
.xch { display: grid; gap: 8px; min-width: 0; }
.xch-head { display: flex; flex-wrap: wrap; align-items: baseline; gap: 6px 10px; }
.xch-out { display: inline-flex; flex-wrap: wrap; gap: 4px 6px; align-items: baseline; }
.io-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: 10px; }
@media (min-width: 980px) { .io-grid { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); } .io-grid.one { grid-template-columns: minmax(0, 1fr); } }
.io { display: grid; gap: 4px; min-width: 0; align-content: start; }
pre.code { margin: 0; overflow: auto; max-height: 460px; white-space: pre-wrap; overflow-wrap: anywhere; background: #f7f9fc; border: 1px solid var(--rule); border-radius: 6px; padding: 10px 12px; font-family: var(--font-mono); font-size: 12.5px; line-height: 1.5; color: var(--ink); }
pre.code .jk { color: #0a2a4a; font-weight: 500; }
pre.code .js { color: #0b5d8a; }
pre.code .jn, pre.code .jl { color: #8a5300; }
.trim { font-size: 12.5px; color: var(--muted); }
.toc { list-style: none; padding: 0; margin: 0; display: grid; grid-template-columns: minmax(0, 1fr); gap: 6px; }
@media (min-width: 760px) { .toc { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
.toc a { text-decoration: none; }
.toc a:hover { text-decoration: underline; }
.toc .n { font-family: var(--font-mono); font-size: 12px; color: var(--muted); margin-right: 6px; }
@media (max-width: 520px) { .step { padding: 16px; } }
`;

// ---- page ----

const exchanges = transcript.exchanges;
const people = transcript.people.map(p => `
          <tr><th scope="row">${esc(p.name)}</th><td data-label="Role">${esc(p.role)}</td><td data-label="How">${inline(p.how)}</td></tr>`).join('');
const toc = transcript.steps.map(s => `<li><a href="#s${esc(s.id)}"><span class="n">${esc(s.id)}</span>${inline(s.title)}</a>${s.safety ? ' <span class="pill gold">safety</span>' : ''}</li>`).join('\n          ');
const legend = Object.entries(CHANNEL).map(([, c]) => `<li><span class="pill ${c.cls}">${esc(c.label)}</span><span>${esc(c.title)}.</span></li>`).join('\n          ');

const page = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>${esc(transcript.title)}</title>
<meta name="description" content="${esc(transcript.description)}">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Archivo:wght@600;700;800&family=IBM+Plex+Sans:wght@400;500;600&family=IBM+Plex+Mono:wght@400;500&display=swap">
<style>
/* Generated by src/demo/walkthrough/render-page.mjs from src/demo/walkthrough/transcript.json. Do not edit by hand.
   The base styles are the step 2 design page's (step2-design.html); the additions are at the end. */
${baseCss}
${extraCss.trim()}
</style>
</head>
<body id="top">

<header class="topbar">
  <div class="bar-inner gutter">
    <a class="brand" href="#top">Product walkthrough</a>
    <nav class="tabs-nav" aria-label="Sections">
      <ul class="tabs">
        <li><a href="#about">About</a></li>
${transcript.steps.map(s => `        <li><a${s.safety ? ' class="go"' : ''} href="#s${esc(s.id)}">Step ${esc(s.id)}</a></li>`).join('\n')}
        <li><a href="#files">Files</a></li>
        <li><a href="step2-design.html">Step 2 design</a></li>
      </ul>
    </nav>
  </div>
</header>

<main class="page gutter">

  <div class="head">
    <p class="eyebrow">${esc(transcript.eyebrow)}</p>
    <h1>${esc(transcript.title)}</h1>
    <p class="lede">${inline(transcript.intro)}</p>
    <div class="status-row">
      <span class="pill open">Design, not a product</span>
      <span class="pill blue">Fictional people, apps and teams</span>
      <span class="pill blue">Balanced preset, version ${esc(transcript.configuration.version)}</span>
      <a class="pill gold" href="#s4">Step 4: AI never approves</a>
    </div>
  </div>

  <section id="about" aria-labelledby="h-about">
    <div class="area-head">
      <p class="eyebrow">How to read it</p>
      <h2 id="h-about">Eight steps, every call recorded</h2>
      <div class="prose">
${transcript.about.map(p => `        <p>${inline(p)}</p>`).join('\n')}
      </div>
    </div>
    <ul class="legend" aria-label="Kinds of call">
          ${legend}
    </ul>
    <div class="tbl">
      <table class="cast stack-sm">
        <thead><tr><th scope="col">Person or workload</th><th scope="col">Role in the story</th><th scope="col">How the role is held</th></tr></thead>
        <tbody>${people}
        </tbody>
      </table>
    </div>
    <ol class="toc" aria-label="Steps">
          ${toc}
    </ol>
  </section>
${transcript.steps.map(s => stepHtml(s, exchanges)).join('\n')}

  <section id="files" aria-labelledby="h-files">
    <div class="area-head">
      <p class="eyebrow">Where this comes from</p>
      <h2 id="h-files">Files and how to run it again</h2>
    </div>
    <div class="prose">
${transcript.files.map(p => `      <p>${inline(p)}</p>`).join('\n')}
    </div>
    <p class="foot">Generated from the transcript of ${exchanges.length} recorded exchanges, configuration content hash <code class="k">${esc(transcript.configuration.contentHash)}</code>. Every name, ID and URL on this page is fictional.</p>
  </section>

</main>
</body>
</html>
`;

if (page.includes(EN_DASH) || page.includes(EM_DASH)) {
  console.error('The page holds an en or em dash; fix the transcript or the template.');
  process.exit(1);
}

if (checkOnly) {
  const onDisk = fs.existsSync(outPath) ? fs.readFileSync(outPath, 'utf8') : '';
  if (onDisk !== page) {
    console.error(`${path.relative(process.cwd(), outPath)} is out of date. Run node demo/walkthrough/render-page.mjs.`);
    process.exit(1);
  }
  console.log(`${path.relative(process.cwd(), outPath)} is current (${exchanges.length} exchanges).`);
} else {
  fs.writeFileSync(outPath, page);
  console.log(`Wrote ${outPath} (${exchanges.length} exchanges, ${transcript.steps.length} steps).`);
}
