// Shared file helpers for the src/ tools.
// No packages. Node 20 or later.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// src/ is the parent of tools/.
export const SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

// Folders that hold build output, packages or local tool state. Never source.
// dist is the page harness build (ui/harness/dist): bundled third-party code, rebuilt by "npm run build".
export const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', 'dist', '.vs', '.vscode', '.idea', 'TestResults', '.git']);

// Extensions that are always binary, even if the first bytes look like text.
const BINARY_EXT = /\.(dll|exe|pdb|nupkg|snupkg|snk|zip|png|jpe?g|gif|webp|ico|pdf|vsdx|xlsx|docx|pptx|woff2?|ttf|otf)$/i;

// Yields every file under root as { full, rel }, with rel using forward slashes.
// Skips SKIP_DIRS, plus any extra relative folder paths in skipRel.
export function* walk(root, skipRel = []) {
  const skip = new Set(skipRel.map(p => p.replace(/\\/g, '/').replace(/\/$/, '')));
  // Depth first, in ordinal name order, so output is stable on every machine.
  const stack = [root];
  while (stack.length) {
    const dir = stack.pop();
    const entries = fs.readdirSync(dir, { withFileTypes: true })
      .sort((a, b) => (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
    const subdirs = [];
    for (const e of entries) {
      const full = path.join(dir, e.name);
      const rel = path.relative(root, full).split(path.sep).join('/');
      if (e.isDirectory()) {
        if (!SKIP_DIRS.has(e.name) && !skip.has(rel)) subdirs.push(full);
      } else if (e.isFile()) {
        yield { full, rel };
      }
    }
    for (let i = subdirs.length - 1; i >= 0; i--) stack.push(subdirs[i]);
  }
}

// True when a file should be treated as binary.
export function isBinary(name, buf) {
  if (BINARY_EXT.test(name)) return true;
  return buf.subarray(0, 8000).includes(0);
}

// Turns a glob into a RegExp. Supports **/, **, * and ?. Paths use forward slashes.
export function globToRegExp(glob) {
  let re = '';
  for (let i = 0; i < glob.length; i++) {
    const c = glob[i];
    if (c === '*' && glob[i + 1] === '*' && glob[i + 2] === '/') { re += '(?:.*/)?'; i += 2; }
    else if (c === '*' && glob[i + 1] === '*') { re += '.*'; i++; }
    else if (c === '*') re += '[^/]*';
    else if (c === '?') re += '[^/]';
    else if ('.+^${}()|[]\\'.includes(c)) re += '\\' + c;
    else re += c;
  }
  return new RegExp('^' + re + '$');
}

// Reads the value after a flag, for example --root <dir>.
export function option(args, name, fallback) {
  const i = args.indexOf(name);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : fallback;
}
