#!/usr/bin/env node
// Dash check: no en dash (U+2013) and no em dash (U+2014) in any text file under src/.
// Code, comments, docs and JSON all count. Build output and packages are skipped.
//
// Usage: node tools/dash-check.mjs [--root <dir>]
// Exit code: 0 clean, 1 dashes found, 2 usage error.
import fs from 'node:fs';
import path from 'node:path';
import { SRC, walk, isBinary, option } from './lib/files.mjs';

const EN_DASH = String.fromCharCode(0x2013);
const EM_DASH = String.fromCharCode(0x2014);

const root = path.resolve(option(process.argv.slice(2), '--root', SRC));
if (!fs.existsSync(root)) {
  console.error(`Dash check: no folder ${root}`);
  process.exit(2);
}

let files = 0;
let en = 0;
let em = 0;
const hits = [];

for (const { full, rel } of walk(root)) {
  const buf = fs.readFileSync(full);
  if (isBinary(rel, buf)) continue;
  files++;
  const lines = buf.toString('utf8').split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    let lineEn = 0;
    let lineEm = 0;
    for (const ch of lines[i]) {
      if (ch === EN_DASH) lineEn++;
      else if (ch === EM_DASH) lineEm++;
    }
    if (lineEn || lineEm) {
      en += lineEn;
      em += lineEm;
      hits.push(`  ${rel}:${i + 1}  en ${lineEn}, em ${lineEm}`);
    }
  }
}

if (!hits.length) {
  console.log(`Dash check: clean. ${files} text files, 0 en dashes, 0 em dashes, root ${root}.`);
  process.exit(0);
}
console.log(`Dash check: ${en} en dash(es) and ${em} em dash(es) in ${hits.length} line(s), root ${root}.`);
for (const h of hits.slice(0, 200)) console.log(h);
if (hits.length > 200) console.log(`  ... and ${hits.length - 200} more lines`);
process.exit(1);
