// In-app navigation with the History API: no router package. Links stay real links (open in a new tab, copy the
// address); a plain left click on a link inside the web app becomes an in-app navigation instead of a page load.
import { useEffect, useSyncExternalStore } from 'react';
import { internalTarget } from './routes';

const CHANGE = 'product:navigate';

function subscribe(callback: () => void) {
  window.addEventListener('popstate', callback);
  window.addEventListener(CHANGE, callback);
  return () => {
    window.removeEventListener('popstate', callback);
    window.removeEventListener(CHANGE, callback);
  };
}

const snapshot = () => window.location.pathname + window.location.search;

// The current path and query string.
export function useLocationKey(): string {
  return useSyncExternalStore(subscribe, snapshot, snapshot);
}

export function navigate(target: string, replace = false): void {
  if (target === snapshot()) return;
  if (replace) window.history.replaceState(null, '', target);
  else window.history.pushState(null, '', target);
  window.scrollTo(0, 0);
  window.dispatchEvent(new Event(CHANGE));
}

// Turns plain left clicks on in-app links into navigate(). Modified clicks, other buttons, new-tab targets, downloads
// and links that leave the web app keep the browser's own behavior.
export function useLinkInterception(base: string): void {
  useEffect(() => {
    const onClick = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      const anchor = (event.target as Element | null)?.closest?.('a[href]');
      if (!(anchor instanceof HTMLAnchorElement)) return;
      if ((anchor.target && anchor.target !== '_self') || anchor.hasAttribute('download')) return;
      const target = internalTarget(anchor.href, window.location.origin, base);
      if (target === null) return;
      event.preventDefault();
      navigate(target);
    };
    document.addEventListener('click', onClick);
    return () => document.removeEventListener('click', onClick);
  }, [base]);
}
