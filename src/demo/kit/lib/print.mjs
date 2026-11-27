// print.mjs: plain text tables and lines for the kit's output.

// A table with a header row. Every cell is text; a column is as wide as its widest cell, up to max.
export function table(headers, rows, { max = 60 } = {}) {
  const cut = s => {
    const t = String(s ?? '');
    return t.length > max ? t.slice(0, max - 3) + '...' : t;
  };
  const cells = [headers, ...rows].map(r => r.map(cut));
  const widths = headers.map((_, i) => Math.max(...cells.map(r => (r[i] ?? '').length)));
  const line = r => '  ' + r.map((c, i) => (i === r.length - 1 ? c : c.padEnd(widths[i]))).join('  ');
  return [line(cells[0]), line(widths.map(w => '-'.repeat(w))), ...cells.slice(1).map(line)].join('\n');
}

export function count(n, one, many) {
  return n + ' ' + (n === 1 ? one : many);
}
