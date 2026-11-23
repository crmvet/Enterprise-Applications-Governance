#!/usr/bin/env node
// Swaps the name placeholders for code names of the product, Enterprise Applications Governance (D-31). The rename is
// its own later step; until it runs, the code keeps the placeholders.
//   Product.   the dotted prefix in names, namespaces, folders and files (for example Product.Engine.Core, Product.sln).
//   <prefix>   the Dataverse publisher prefix, written literally.
// A bare word "Product" or "product" in prose is left alone. Only the dotted form is a placeholder.
// Dry run by default. Nothing changes without --apply.
//
// Usage: node tools/rename-product.mjs --name <PascalName> --prefix <prefix> [--apply] [--root <dir>]
// After --apply: delete bin/ and obj/, then run dotnet restore and node tools/ci.mjs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SRC, walk, isBinary, option } from './lib/files.mjs';

const args = process.argv.slice(2);
const name = option(args, '--name', '');
const prefix = option(args, '--prefix', '');
const apply = args.includes('--apply');
const root = path.resolve(option(args, '--root', SRC));

// The name becomes part of C# namespaces and assembly names.
if (!/^[A-Z][A-Za-z0-9]{1,39}$/.test(name) || name === 'Product') {
  console.error('Give --name as PascalCase letters and digits, 2 to 40 characters, starting with a capital letter.');
  process.exit(2);
}
// Dataverse publisher prefix: 2 to 8 lowercase letters or digits, starting with a letter, not "mscrm".
if (!/^[a-z][a-z0-9]{1,7}$/.test(prefix) || prefix.startsWith('mscrm')) {
  console.error('Give --prefix as 2 to 8 lowercase letters or digits, starting with a letter, not starting with "mscrm".');
  process.exit(2);
}
if (!fs.existsSync(root)) {
  console.error(`No folder: ${root}`);
  process.exit(2);
}

const NAME_RE = /\bProduct(?=\.(?:[A-Z]|sln\b|slnx\b))/g;
const PREFIX_RE = /<prefix>/g;
// NuGet lock files spell project names in lowercase.
const LOCK_NAME_RE = /\bproduct(?=\.[a-z])/g;
const SELF = path.resolve(fileURLToPath(import.meta.url));

let edits = 0;
const changedFiles = [];
const renames = [];

for (const { full, rel } of walk(root)) {
  if (path.resolve(full) === SELF) continue; // keep this script reusable
  const buf = fs.readFileSync(full);
  if (!isBinary(rel, buf)) {
    const text = buf.toString('utf8');
    let count = 0;
    let next = text.replace(NAME_RE, () => { count++; return name; });
    next = next.replace(PREFIX_RE, () => { count++; return prefix; });
    if (path.basename(rel) === 'packages.lock.json') {
      next = next.replace(LOCK_NAME_RE, () => { count++; return name.toLowerCase(); });
    }
    if (count) {
      edits += count;
      changedFiles.push(`  ${rel}: ${count}`);
      if (apply) fs.writeFileSync(full, next);
    }
  }
}

// Folders and files whose names start with "Product.", deepest first so parents move last.
const collect = dir => {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['bin', 'obj', 'node_modules', '.git', '.vs'].includes(e.name)) continue;
    const full = path.join(dir, e.name);
    if (e.isDirectory()) collect(full);
    if (/^Product\./.test(e.name)) renames.push(full);
  }
};
collect(root);
renames.sort((a, b) => b.length - a.length);

console.log(`${apply ? 'Applied' : 'Dry run'}: ${edits} text replacement(s) in ${changedFiles.length} file(s), ${renames.length} rename(s). Root ${root}.`);
for (const line of changedFiles) console.log(line);
for (const full of renames) {
  const target = path.join(path.dirname(full), path.basename(full).replace(/^Product\./, `${name}.`));
  console.log(`  rename ${path.relative(root, full)} -> ${path.relative(root, target)}`);
  if (apply) fs.renameSync(full, target);
}
if (!apply) console.log('Nothing changed. Add --apply to write.');
else console.log('Next: delete bin/ and obj/, then run dotnet restore and node tools/ci.mjs.');
