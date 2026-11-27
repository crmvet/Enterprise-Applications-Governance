// The write call sites of the web app, read from its source without a browser or a package (wave 4b QA,
// fix round 1). Shared by tools/check-actions.mjs and its tests.
//
// A site is one side sheet a person submits: an <ActionSheet tool="..."> in src/ui/harness/src. Its key is the file's
// path under src without the extension, then "#", then the top-level component it sits in, such as
// "pages/app360/sheets#DocumentCurrentSheet". The sheet carries the key in its site attribute (data-action-site on the
// page), and lib/demo.ts reads it when a step submits the sheet.
import fs from 'node:fs';
import path from 'node:path';

// The write tools of the web app, from WRITE_TOOLS in src/ui/harness/src/data/contracts.ts, as lib/demo.ts reads them.
export function writeTools(harnessSrc) {
  const text = fs.readFileSync(path.join(harnessSrc, 'data', 'contracts.ts'), 'utf8');
  const block = /export const WRITE_TOOLS[^=]*=\s*\[([\s\S]*?)\]/.exec(text)?.[1] ?? '';
  return new Set([...block.matchAll(/'([a-z_]+)'/g)].map(m => m[1]));
}

// The source with its comments blanked out (same length, so positions stay): a line that starts with //, /* or *, and a
// JSX comment {/* ... */}. A string is never touched, so text such as "the person's day" cannot confuse it.
export function withoutComments(text) {
  const blank = s => s.replace(/[^\n]/g, ' ');
  return text
    .replace(/\{\/\*[\s\S]*?\*\/\}/g, blank)
    .replace(/^[ \t]*\/\*[\s\S]*?\*\//gm, blank)
    .replace(/^[ \t]*(?:\/\/|\*).*$/gm, blank);
}

// The end of a JSX opening tag that starts at index at ("<ActionSheet"): the ">" outside braces and strings.
export function openingTagEnd(text, at) {
  let depth = 0;
  let quote = null;
  for (let i = at + 1; i < text.length; i++) {
    const c = text[i];
    if (quote) {
      if (c === '\\') { i++; continue; }
      if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'" || c === '`') quote = c;
    else if (c === '{') depth++;
    else if (c === '}') depth--;
    else if (c === '>' && depth === 0) return i;
  }
  return -1;
}

// The top-level component a position sits in: the nearest "function Name(" that starts a line, Name capitalized.
export function componentAt(text, at) {
  const before = text.slice(0, at);
  const all = [...before.matchAll(/^(?:export\s+)?(?:default\s+)?function\s+([A-Z][A-Za-z0-9_]*)\s*[<(]/gm)];
  return all.length ? all[all.length - 1][1] : null;
}

// Every .ts and .tsx file under a folder, tests left out.
export function sourceFiles(root) {
  const out = [];
  const walk = dir => {
    for (const name of fs.readdirSync(dir).sort()) {
      const file = path.join(dir, name);
      if (fs.statSync(file).isDirectory()) walk(file);
      else if (/\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name)) out.push(file);
    }
  };
  walk(root);
  return out;
}

// What the web app's source says about its writes:
//   sites     one entry per <ActionSheet>: { key, tool, site (the attribute as written, or null), file, line }
//   calls     every useWriteAction('<tool>') outside components/actions.tsx: { tool, file, line } (the checks a sheet runs
//             first, such as the configuration builder's own "Check first")
//   problems  a tool or a site not written as a string literal, outside the generic forwarder in components/actions.tsx
export function scanWebApp(harnessSrc) {
  const sites = [];
  const calls = [];
  const problems = [];
  for (const file of sourceFiles(harnessSrc)) {
    const raw = fs.readFileSync(file, 'utf8');
    const text = withoutComments(raw);
    const rel = path.relative(harnessSrc, file).split(path.sep).join('/');
    const forwarder = rel === 'components/actions.tsx';
    const lineOf = i => text.slice(0, i).split('\n').length;
    for (const m of text.matchAll(/<ActionSheet(?=[\s>])/g)) {
      const end = openingTagEnd(text, m.index);
      const tag = end < 0 ? text.slice(m.index) : text.slice(m.index, end);
      const tool = /\btool=(?:"([a-z_]+)"|\{\s*['"]([a-z_]+)['"]\s*\})/.exec(tag);
      const site = /\bsite=(?:"([^"]*)"|\{\s*['"]([^'"]*)['"]\s*\})/.exec(tag);
      // A site written as an expression, such as site={where}: named in the message, so the fix is plain.
      const siteExpression = site ? null : /\bsite=\{([^}]*)\}/.exec(tag);
      const component = componentAt(text, m.index);
      const entry = {
        key: `${rel.replace(/\.tsx?$/, '')}#${component ?? '?'}`,
        tool: tool ? tool[1] ?? tool[2] : null,
        site: site ? site[1] ?? site[2] : null,
        siteExpression: siteExpression ? siteExpression[1].trim() : null,
        file: rel,
        line: lineOf(m.index),
      };
      if (!tool) problems.push(`${rel}:${entry.line}: an ActionSheet whose tool is not a string literal`);
      if (!component) problems.push(`${rel}:${entry.line}: an ActionSheet outside a top-level component`);
      sites.push(entry);
    }
    for (const m of text.matchAll(/useWriteAction\(\s*(?:['"]([a-z_]+)['"]|([^,)]+))/g)) {
      if (m[1]) calls.push({ tool: m[1], file: rel, line: lineOf(m.index) });
      else if (!forwarder) problems.push(`${rel}:${lineOf(m.index)}: useWriteAction with a tool that is not a string literal (${m[2].trim()})`);
    }
    for (const m of text.matchAll(/\btool=\{(?!\s*['"][a-z_]+['"]\s*\})([^}]*)\}/g)) {
      if (!forwarder) problems.push(`${rel}:${lineOf(m.index)}: a tool that is not a string literal (tool={${m[1].trim()}})`);
    }
  }
  return { sites, calls, problems };
}
