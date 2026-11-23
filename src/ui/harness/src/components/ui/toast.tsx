// A small toast on the Radix Toast primitive, styled like shadcn/ui. Not sonner: sonner writes a style tag at run time,
// and the web app's Content-Security-Policy aims to allow styles from its own files only.
//
// Use: put <Toaster /> once near the root, then call toast({ title, description }) from anywhere.
import { X } from 'lucide-react';
import { Toast as ToastPrimitive } from 'radix-ui';
import { useSyncExternalStore, type ReactNode } from 'react';
import { cn } from '../../lib/utils';

export interface ToastItem {
  id: number;
  title: ReactNode;
  description?: ReactNode;
}

let items: ToastItem[] = [];
let nextId = 1;
const listeners = new Set<() => void>();

function emit() {
  for (const l of listeners) l();
}

export function toast(input: Omit<ToastItem, 'id'>): number {
  const id = nextId++;
  // Keep the last three, newest last.
  items = [...items, { id, ...input }].slice(-3);
  emit();
  return id;
}

function dismiss(id: number) {
  items = items.filter(t => t.id !== id);
  emit();
}

function subscribe(l: () => void) {
  listeners.add(l);
  return () => listeners.delete(l);
}

export function Toaster({ duration = 6000 }: { duration?: number }) {
  const list = useSyncExternalStore(subscribe, () => items, () => items);
  return (
    <ToastPrimitive.Provider duration={duration} swipeDirection="right">
      {list.map(t => (
        <ToastPrimitive.Root
          key={t.id}
          onOpenChange={open => { if (!open) dismiss(t.id); }}
          className={cn(
            'group pointer-events-auto relative grid w-full grid-cols-[1fr_auto] items-start gap-x-3 gap-y-1 rounded-lg border bg-popover p-4 pr-3 text-popover-foreground shadow-lg',
            'data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:slide-in-from-bottom-2',
            'data-[state=closed]:animate-out data-[state=closed]:fade-out-0',
            'data-[swipe=move]:translate-x-(--radix-toast-swipe-move-x) data-[swipe=cancel]:translate-x-0 data-[swipe=end]:animate-out',
          )}
        >
          <ToastPrimitive.Title className="text-sm font-semibold text-navy">{t.title}</ToastPrimitive.Title>
          <ToastPrimitive.Close
            aria-label="Close"
            className="col-start-2 row-start-1 inline-flex size-6 items-center justify-center rounded-md text-navy-3 hover:bg-accent hover:text-navy focus-visible:outline-2 focus-visible:outline-ring"
          >
            <X className="size-4" aria-hidden />
          </ToastPrimitive.Close>
          {t.description && <ToastPrimitive.Description className="col-span-2 text-sm text-navy-2">{t.description}</ToastPrimitive.Description>}
        </ToastPrimitive.Root>
      ))}
      <ToastPrimitive.Viewport className="fixed right-0 bottom-0 z-[100] m-0 flex w-full max-w-[420px] list-none flex-col gap-2 p-4 outline-none narrow:max-w-none" />
    </ToastPrimitive.Provider>
  );
}
