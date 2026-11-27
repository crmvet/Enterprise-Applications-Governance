// args.mjs: the command line of the demo kit. Options take "--name value" or "--name=value"; a flag is "--name" alone.
// Words that do not start with "--" are the command and its arguments, in order.

export class UsageError extends Error {}

// Options that never take a value. Every other "--name" takes the next word as its value.
const FLAGS = new Set([
  '--help', '--json', '--dry-run', '--native', '--rebuild', '--no-build', '--no-web-build', '--no-director', '--yes',
  '--record', '--read-only', '--keep-store', '--phone', '--settled',
]);

export function parseArgs(argv) {
  const words = [];
  const options = new Map();
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (!a.startsWith('--')) {
      words.push(a);
      continue;
    }
    const eq = a.indexOf('=');
    const name = eq > 0 ? a.slice(0, eq) : a;
    if (eq > 0) {
      options.set(name, a.slice(eq + 1));
    } else if (FLAGS.has(name)) {
      options.set(name, true);
    } else {
      if (i + 1 >= argv.length || argv[i + 1].startsWith('--')) throw new UsageError(name + ' needs a value.');
      options.set(name, argv[++i]);
    }
  }
  return {
    words,
    options,
    has: name => options.has(name),
    flag: name => options.get(name) === true || options.get(name) === 'true',
    text: (name, fallback) => (options.has(name) && options.get(name) !== true ? String(options.get(name)) : fallback),
    number(name, fallback, { min = 1, max = Number.MAX_SAFE_INTEGER } = {}) {
      if (!options.has(name)) return fallback;
      const n = Number(options.get(name));
      if (!Number.isInteger(n) || n < min || n > max) throw new UsageError(name + ' takes a whole number from ' + min + ' to ' + max + '.');
      return n;
    },
    // The options as given, for passing on to the same kit in WSL, except the ones named.
    passOn(except = []) {
      const out = [];
      for (const [name, value] of options) {
        if (except.includes(name)) continue;
        out.push(value === true ? name : name + '=' + value);
      }
      return out;
    },
  };
}

// A port the kit may use: a whole number from 1024 to 65535.
export function port(args, fallback) {
  return args.number('--port', fallback, { min: 1024, max: 65535 });
}
