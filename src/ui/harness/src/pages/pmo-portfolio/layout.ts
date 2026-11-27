// Layout helpers of the PMO and portfolio pages.
//   useBelowWide()   true below 1024 px: a table of seven columns switches to rows there, as the panels do below 720 px
//                    (useNarrow in components/blocks.tsx). In class names, the same breakpoint is the medium: variant.
import { useSyncExternalStore } from 'react';

const BELOW_WIDE = '(max-width: 1023px)';

function subscribe(callback: () => void) {
  const mq = window.matchMedia(BELOW_WIDE);
  mq.addEventListener('change', callback);
  return () => mq.removeEventListener('change', callback);
}

export function useBelowWide(): boolean {
  return useSyncExternalStore(subscribe, () => window.matchMedia(BELOW_WIDE).matches, () => false);
}
