// Functions that run inside the page (Playwright serializes each one, so none may use anything outside its own body).
// They find what a person reads: a control by its visible text, the text of the open side sheet, of main or of the
// page. Ported from the wave 3 story script (ui/harness/scripts/story-e2e.mjs, now retired).

export interface FindSpec {
  text: string;
  // prefix: the control's text starts with text. contains: the text is anywhere in it. Neither: the text is all of it.
  prefix?: boolean;
  contains?: boolean;
  // Among controls with that text, the one closest to this text: the smallest block that holds both (a card, a row).
  near?: string;
  // dialog: the open side sheet (the last one); main; page (the default).
  scope?: 'dialog' | 'main' | 'page';
  // button (the default: buttons, links, tabs, options, menu items, summaries), link, tab, option, combobox.
  role?: 'button' | 'link' | 'tab' | 'option' | 'combobox';
}

export interface FindResult {
  error?: string;
  disabled?: boolean;
  text?: string;
}

// Marks the control with data-e2e-pick="1" for the locator that clicks it.
export function findControl(spec: FindSpec): FindResult {
  const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();
  const shown = (el: Element) => {
    for (let e: Element | null = el; e && e.nodeType === 1; e = e.parentElement) {
      const cs = getComputedStyle(e);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
    }
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  for (const old of Array.from(document.querySelectorAll('[data-e2e-pick]'))) old.removeAttribute('data-e2e-pick');
  const dialogs = Array.from(document.querySelectorAll('[role="dialog"], [role="alertdialog"]')).filter(shown);
  const root = spec.scope === 'dialog' ? dialogs[dialogs.length - 1] : spec.scope === 'main' ? document.querySelector('main') : document.body;
  if (!root) return { error: spec.scope === 'dialog' ? 'no side sheet is open' : 'no main landmark' };
  const selectors: Record<string, string> = {
    button: 'button:not([role="combobox"]), a[href], [role="button"], [role="tab"], [role="option"], [role="menuitem"], [role="menuitemradio"], summary',
    link: 'a[href]',
    tab: '[role="tab"]',
    option: '[role="option"]',
    combobox: '[role="combobox"]',
  };
  const textOf = (el: Element) => norm((el as HTMLElement).innerText || el.getAttribute('aria-label') || (el as HTMLInputElement).value || '');
  const want = norm(spec.text);
  const matches = (el: Element) => {
    const t = textOf(el);
    const label = norm(el.getAttribute('aria-label'));
    if (spec.contains) return t.includes(want) || label.includes(want);
    return spec.prefix ? t.startsWith(want) || label.startsWith(want) : t === want || label === want;
  };
  let found = Array.from(root.querySelectorAll(selectors[spec.role || 'button'])).filter(el => shown(el) && matches(el));
  if (!found.length) return { error: `no control "${spec.text}"${spec.near ? ` near "${spec.near}"` : ''}` };
  if (spec.near) {
    const anchor = norm(spec.near);
    const score = (el: Element) => {
      for (let e = el.parentElement; e && e !== document.documentElement; e = e.parentElement) {
        const t = norm((e as HTMLElement).innerText);
        if (t.includes(anchor)) return t.length;
      }
      return Infinity;
    };
    const scored = found.map(el => ({ el, s: score(el) })).filter(x => x.s < Infinity).sort((a, b) => a.s - b.s);
    if (!scored.length) return { error: `no control "${spec.text}" near "${spec.near}"` };
    found = [scored[0].el];
  }
  const el = found[0];
  el.setAttribute('data-e2e-pick', '1');
  return { disabled: (el as HTMLButtonElement).disabled === true || el.getAttribute('aria-disabled') === 'true', text: textOf(el).slice(0, 80) };
}

// The text a person sees, spaces collapsed: in the open side sheet, in main, or the whole page.
export function readText(scope: 'dialog' | 'main' | 'page'): string {
  const dialogs = Array.from(document.querySelectorAll('[role="dialog"], [role="alertdialog"]')).filter(d => d.getBoundingClientRect().width > 0);
  const root = scope === 'dialog' ? dialogs[dialogs.length - 1] : scope === 'main' ? document.querySelector('main') : document.body;
  return root ? ((root as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim() : '';
}

// Marks the input or text area a label names (the label's text before "Required" or "Optional"), in the open sheet or
// the page. A label may name a group (a search with its results): the search inside it is marked.
export function findField(arg: { label: string; scope: 'dialog' | 'page' }): string {
  const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();
  for (const old of Array.from(document.querySelectorAll('[data-e2e-field]'))) old.removeAttribute('data-e2e-field');
  const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
  const root = arg.scope === 'dialog' ? dialogs[dialogs.length - 1] : document.body;
  if (!root) return 'no side sheet is open';
  const want = norm(arg.label);
  const labels = Array.from(root.querySelectorAll('label')).filter(
    l => l.getBoundingClientRect().width > 0 && norm(l.innerText).replace(/ ?Required$/, '').replace(/ ?Optional$/, '') === want,
  );
  for (const l of labels) {
    let c: Element | null = (l.htmlFor && document.getElementById(l.htmlFor)) || l.querySelector('input, textarea');
    if (c && !/^(INPUT|TEXTAREA)$/.test(c.tagName)) c = c.querySelector('input[type="search"], input[type="text"], input:not([type]), textarea');
    if (!c && l.parentElement) c = l.parentElement.querySelector('input[type="search"], input[type="text"], textarea');
    if (c && /^(INPUT|TEXTAREA)$/.test(c.tagName)) {
      c.setAttribute('data-e2e-field', '1');
      return 'ok';
    }
  }
  // A search box named by its aria-label or placeholder, with no label element.
  const byName = Array.from(root.querySelectorAll('input, textarea')).find(
    i => i.getBoundingClientRect().width > 0 && (norm(i.getAttribute('aria-label')) === want || norm(i.getAttribute('placeholder')) === want),
  );
  if (byName) {
    byName.setAttribute('data-e2e-field', '1');
    return 'ok';
  }
  return `no field labeled "${want}"`;
}

// Marks the radio button or checkbox whose label starts with the text. Answers whether it is already on.
export function findChoice(arg: { text: string; scope: 'dialog' | 'page' }): { error?: string; checked?: boolean } {
  const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();
  for (const old of Array.from(document.querySelectorAll('[data-e2e-choice]'))) old.removeAttribute('data-e2e-choice');
  const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
  const root = arg.scope === 'dialog' ? dialogs[dialogs.length - 1] : document.body;
  if (!root) return { error: 'no side sheet is open' };
  const want = norm(arg.text);
  const inputs = Array.from(root.querySelectorAll('input[type="radio"], input[type="checkbox"], [role="radio"], [role="checkbox"]'));
  const labelOf = (input: Element) => {
    const label = input.closest('label') || (input.id && document.querySelector(`label[for="${input.id}"]`));
    return norm(label ? (label as HTMLElement).innerText : input.getAttribute('aria-label'));
  };
  // A label that is exactly the text wins over one that only starts with it ("No" before "None of these").
  const found = inputs.find(i => labelOf(i) === want) ?? inputs.find(i => labelOf(i).startsWith(want));
  if (!found) return { error: `no choice "${want}"` };
  found.setAttribute('data-e2e-choice', '1');
  const checked = (found as HTMLInputElement).checked === true || found.getAttribute('aria-checked') === 'true' || found.getAttribute('data-state') === 'checked';
  return { checked };
}

// The engine's answer in the open sheet: the success panel (role status, "Done"), a refusal (role alert) or the
// page's own form check ("Check the form"). A sheet that opened its new record has closed itself.
export function sheetAnswerNow(): { kind: string; text: string } | null {
  const dialogs = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
  const d = dialogs[dialogs.length - 1];
  if (!d) return { kind: 'done', text: `The sheet closed after the write, and the page moved on to ${location.pathname}` };
  const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();
  const ok = d.querySelector('[role="status"]') as HTMLElement | null;
  if (ok && /Done/.test(ok.innerText)) return { kind: 'done', text: norm(ok.innerText) };
  const alerts = Array.from(d.querySelectorAll('[role="alert"]')).map(a => norm((a as HTMLElement).innerText));
  const refused = alerts.find(a => !a.startsWith('Check the form'));
  if (refused) return { kind: 'refused', text: refused };
  if (alerts.length) return { kind: 'problem', text: alerts[0] };
  // A proposal sheet that saved hands over to its follow-up, which may not repeat the proposal's answer.
  if (/Send it for approval now\?/.test((d as HTMLElement).innerText)) return { kind: 'done', text: 'The proposal is saved; the follow-up "Send it for approval now?" is open.' };
  return null;
}

// The "Check first" answer in the open sheet: the status or alert that starts with "Check".
export function checkAnswerNow(): string {
  const d = Array.from(document.querySelectorAll('[role="dialog"]')).filter(x => x.getBoundingClientRect().width > 0).pop();
  if (!d) return '';
  const s = Array.from(d.querySelectorAll('[role="status"], [role="alert"]'))
    .map(x => (x as HTMLElement).innerText.replace(/\s+/g, ' ').trim())
    .find(x => /^Check/.test(x));
  return s || '';
}

// True when the open sheet already shows an answer that is not a check.
export function sheetAnswered(): boolean {
  const d = Array.from(document.querySelectorAll('[role="dialog"]')).filter(x => x.getBoundingClientRect().width > 0).pop();
  return !!d && Array.from(d.querySelectorAll('[role="status"], [role="alert"]')).some(x => !/^Check/.test((x as HTMLElement).innerText.trim()));
}

export function sheetOpen(): boolean {
  return Array.from(document.querySelectorAll('[role="dialog"]')).some(d => d.getBoundingClientRect().width > 0);
}

// The caption bar of "demo play": the persona and the step, kept in session storage so it survives a navigation.
// The caption bar never covers a side sheet's buttons (QA: in a recorded video it hid the sheet's final Approve). With
// no sheet open it runs along the foot of the page, and the page gets room under its last line. With a side sheet open
// it moves to the left of the sheet; when the sheet fills the width (a phone), it moves to the top.
export function captionScript(): void {
  const place = (bar: HTMLElement) => {
    const sheets = Array.from(document.querySelectorAll('[role="dialog"]')).filter(d => d.getBoundingClientRect().width > 0);
    const sheet = sheets[sheets.length - 1];
    const left = sheet ? sheet.getBoundingClientRect().left : 0;
    if (sheet && left < 240) {
      bar.style.top = '0';
      bar.style.bottom = 'auto';
      bar.style.right = '0';
    } else {
      bar.style.top = 'auto';
      bar.style.bottom = '0';
      bar.style.right = sheet ? `${Math.max(0, window.innerWidth - left)}px` : '0';
    }
  };
  const paint = () => {
    let text = '';
    try {
      text = sessionStorage.getItem('e2e.caption') || '';
    } catch {
      text = '';
    }
    if (!text || !document.body) return;
    let bar = document.getElementById('e2e-caption');
    if (!bar) {
      bar = document.createElement('div');
      bar.id = 'e2e-caption';
      bar.setAttribute('aria-hidden', 'true');
      bar.style.cssText = 'position:fixed;left:0;right:0;bottom:0;z-index:2147483647;pointer-events:none;'
        + 'background:#eef3fb;color:#0b2a4a;border-top:2px solid #0b2a4a;font:600 15px/1.4 "Segoe UI",system-ui,sans-serif;'
        + 'padding:8px 16px;box-shadow:0 -2px 8px rgba(11,42,74,.15)';
      document.body.appendChild(bar);
      document.body.style.paddingBottom = '48px';
      let queued = false;
      const own = bar;
      new MutationObserver(records => {
        if (queued || records.every(r => r.target === own || own.contains(r.target))) return;
        queued = true;
        requestAnimationFrame(() => {
          queued = false;
          const b = document.getElementById('e2e-caption');
          if (b) place(b);
        });
      }).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['role', 'data-state', 'style', 'class'] });
      window.addEventListener('resize', () => place(bar as HTMLElement));
    }
    bar.textContent = text;
    place(bar);
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', paint);
  else paint();
  (window as unknown as { __e2eCaption: () => void }).__e2eCaption = paint;
}

// One write the page sent: the tool, the side sheet open when it was sent (its data-action-site), whether it was a check
// only (validateOnly), and whether the server answered it ok (200 and ok true).
export interface PageWrite {
  tool: string;
  site: string | null;
  check: boolean;
  ok: boolean;
}

// Runs in the page before its own scripts: wraps fetch, so every POST /api/<tool> is reported to the suite (the binding
// __e2eWrite, lib/demo.ts) with the sheet it came from and the server's answer. The web app keeps window.fetch when it
// starts, so it uses the wrapped one. Nothing about the request or the answer changes.
export function writeWatchScript(): void {
  const w = window as unknown as { fetch: typeof fetch; __e2eWrite?: (x: unknown) => Promise<void>; __e2eWatched?: boolean };
  if (w.__e2eWatched) return;
  w.__e2eWatched = true;
  const own = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    let tool: string | null = null;
    let method = 'GET';
    try {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;
      method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase();
      tool = /\/api\/([a-z_]+)$/.exec(new URL(url, location.href).pathname)?.[1] ?? null;
    } catch {
      tool = null;
    }
    if (!tool || method !== 'POST') return own(input, init);
    const sheets = Array.from(document.querySelectorAll('[role="dialog"][data-action-site]')).filter(d => d.getBoundingClientRect().width > 0);
    const site = sheets.length ? sheets[sheets.length - 1].getAttribute('data-action-site') : null;
    let check = false;
    try {
      check = typeof init?.body === 'string' && JSON.parse(init.body)?.validateOnly === true;
    } catch {
      check = false;
    }
    const report = (ok: boolean) => { try { void w.__e2eWrite?.({ tool, site, check, ok }); } catch { /* no suite */ } };
    try {
      const res = await own(input, init);
      let ok = false;
      try {
        ok = res.status === 200 && (await res.clone().json())?.ok === true;
      } catch {
        ok = false;
      }
      report(ok);
      return res;
    } catch (e) {
      report(false);
      throw e;
    }
  };
}
