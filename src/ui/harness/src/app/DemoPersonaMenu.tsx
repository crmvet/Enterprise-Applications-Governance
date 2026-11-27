// The demo persona switch in the top bar (D-39): "Acting as" with the install's fictional personas and their roles,
// marked Demo. The shell shows it only when config.json lists demo personas, which the host does only in a demo
// install. Choosing a persona reloads every panel as that persona (the shell remounts the page), and every write is
// stamped with the persona and "Web app, demo operator".
import { VenetianMask } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import type { Answer, DemoPersonaUse, Me } from '../data/apiClient';
import { Button } from '../components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../components/ui/dropdown-menu';
import { cn } from '../lib/utils';
import { personaParts, type DemoPersonaSwitch } from './demoPersona';

const YOURSELF = '';

export function DemoPersonaMenu({ personas, me, narrow }: { personas: DemoPersonaSwitch; me: Answer<Me> | null; narrow: boolean }) {
  const current = personas.current();
  const parts = current ? personaParts(current.label) : null;
  // What the server says the last /api/me ran as. Null while it is read or when it failed.
  const applied: DemoPersonaUse | null | undefined = me?.ok ? me.value.demoPersona : undefined;
  const mismatch = me?.ok === true && (applied?.name ?? null) !== (current?.name ?? null);

  // The menu closes first and the switch runs after: the switch replaces the page, and the menu's focus trap must have
  // given the page back by then.
  const [open, setOpen] = useState(false);
  const pending = useRef<string | null | undefined>(undefined);
  useEffect(() => {
    if (open || pending.current === undefined) return;
    const next = pending.current;
    pending.current = undefined;
    const timer = window.setTimeout(() => personas.choose(next), 50);
    return () => window.clearTimeout(timer);
  }, [open, personas]);

  const firstName = parts ? parts.name.split(/\s+/)[0] : 'yourself';
  const triggerText = narrow ? firstName : `Acting as ${parts ? parts.name : 'yourself'}`;

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          className={cn(
            'max-w-[260px] gap-1.5 border-warning-line bg-warning-soft pr-2.5 pl-1.5 text-navy hover:bg-warning-soft hover:text-navy narrow:max-w-[150px]',
            mismatch && 'border-danger-line',
          )}
          aria-label={`Demo persona switch. Acting as ${current ? current.label : 'yourself'}.${mismatch ? ' The server did not apply this persona.' : ''}`}
        >
          <span className="inline-flex items-center gap-1 rounded-sm bg-warning px-1 py-px text-[11px] leading-4 font-semibold tracking-[0.04em] text-white uppercase" aria-hidden>
            <VenetianMask className="size-3" />
            Demo
          </span>
          <span className="truncate">{triggerText}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" collisionPadding={8} className="w-80 max-w-[calc(100vw-16px)] text-navy">
        <DropdownMenuLabel className="flex flex-col gap-1 pb-2">
          <span className="text-sm font-semibold">Demo: act as a persona</span>
          <span className="text-xs leading-relaxed font-normal text-navy-3">
            This demo install lets the operator act as any fictional persona. The engine checks every request as the
            persona, and every change is stamped with the persona and "demo operator".
          </span>
        </DropdownMenuLabel>
        {mismatch && (
          <p className="mx-2 mb-2 rounded-md border border-danger-line bg-danger-soft px-2 py-1.5 text-xs leading-relaxed text-navy">
            {applied
              ? `The server ran the last request as ${applied.label}.`
              : 'The server did not apply the persona: it runs requests as you. The switch works only in a demo install, signed in to the web app.'}
          </p>
        )}
        <DropdownMenuSeparator />
        <DropdownMenuRadioGroup
          value={current?.name ?? YOURSELF}
          onValueChange={value => {
            const next = value === YOURSELF ? null : value;
            if (next === (current?.name ?? null)) return;
            pending.current = next;
            setOpen(false);
          }}
        >
          <DropdownMenuRadioItem value={YOURSELF} className="items-start">
            <span className="flex flex-col">
              <span className="font-medium">Yourself</span>
              <span className="text-xs text-navy-3">Your own roles, from your groups</span>
            </span>
          </DropdownMenuRadioItem>
          {personas.personas.map(p => {
            const { name, role } = personaParts(p.label);
            return (
              <DropdownMenuRadioItem key={p.name} value={p.name} className="items-start">
                <span className="flex flex-col">
                  <span className="font-medium">{name}</span>
                  {role && <span className="text-xs text-navy-3">{role}</span>}
                </span>
              </DropdownMenuRadioItem>
            );
          })}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
