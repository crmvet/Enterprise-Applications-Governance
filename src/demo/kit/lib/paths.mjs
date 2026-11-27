// paths.mjs: where the kit finds the product's source, and how a Windows path reads inside WSL.
import { existsSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const KIT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const SRC = resolve(KIT, '..', '..');
export const AZURE = join(SRC, 'adapters', 'azure');
export const SERVER_PROJECT = join(AZURE, 'Product.Azure.Server');
export const SERVER_DLL = join(SERVER_PROJECT, 'bin', 'Release', 'net10.0', 'Product.Azure.Server.dll');
export const HARNESS = join(SRC, 'ui', 'harness');
export const WEB_APP = join(HARNESS, 'dist', 'app');
export const SEED = join(AZURE, 'seed');
export const DIRECTOR = join(SEED, 'demo-director.mjs');
export const PERSONA_FILE = join(SEED, 'demo-personas.json');
export const SCENARIO_FILE = join(KIT, 'scenarios.json');
export const E2E = join(SRC, 'tests', 'e2e');

// A Windows path as WSL mounts it: D:\a\b becomes /mnt/d/a/b. Any other path is returned with forward slashes.
export function toWslPath(path) {
  const p = String(path).replace(/\\/g, '/');
  const m = /^([A-Za-z]):\/(.*)$/.exec(p);
  return m ? '/mnt/' + m[1].toLowerCase() + '/' + m[2] : p;
}

// The newest modification time (ms) of the files under the folders, skipping build output and packages. A folder or
// file that does not exist adds nothing.
export function newestTime(paths, { skip = ['bin', 'obj', 'node_modules', 'dist', 'TestResults', '.vs', 'tests'], match = () => true } = {}) {
  let newest = 0;
  const visit = path => {
    let s;
    try {
      s = statSync(path);
    } catch {
      return;
    }
    if (s.isDirectory()) {
      for (const entry of readdirSync(path, { withFileTypes: true })) {
        if (entry.isDirectory() && skip.includes(entry.name)) continue;
        visit(join(path, entry.name));
      }
    } else if (match(path) && s.mtimeMs > newest) {
      newest = s.mtimeMs;
    }
  };
  for (const p of paths) visit(p);
  return newest;
}

// The modification time of one file, or 0 when it is missing.
export function fileTime(path) {
  return existsSync(path) ? statSync(path).mtimeMs : 0;
}
