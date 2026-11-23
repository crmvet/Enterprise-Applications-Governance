// The sign-in page, shown until there is a signed-in person.
// Production: one button that starts the Entra ID sign-in (a full-page redirect).
// Development: a choice of fictional personas, sent as X-Product-Dev-Identity to the local server.
import { TriangleAlert, UserRoundCheck } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import type { DevAuth } from '../auth/devAuth';
import type { MsalAuth } from '../auth/msalAuth';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';
import { Button } from '../components/ui/button';

// The page keeps a 16 px gutter on a phone; the card itself is at most 560 px wide.
const wrap = 'mx-auto my-12 flex w-full max-w-[592px] flex-col gap-4 px-4 narrow:my-6';
const card = 'flex flex-col gap-3.5 rounded-xl border bg-card p-6 text-card-foreground shadow-xs narrow:p-4';
const h1 = 'm-0 text-2xl leading-8 font-semibold tracking-tight text-navy';
const text = 'm-0 text-sm leading-relaxed text-navy-2';
const small = 'm-0 text-xs leading-relaxed text-navy-3';

function Problem({ text }: { text: string | null }) {
  if (!text) return null;
  return (
    <Alert variant="warning">
      <TriangleAlert aria-hidden />
      <AlertTitle>Sign-in needs another try</AlertTitle>
      <AlertDescription>
        <p>{text}</p>
      </AlertDescription>
    </Alert>
  );
}

export function EntraSignIn({ auth }: { auth: MsalAuth }) {
  const [busy, setBusy] = useState(false);
  useEffect(() => { document.title = 'Sign in | Product'; }, []);
  return (
    <main className={wrap} id="main">
      <Problem text={auth.problem()} />
      <section className={card} aria-labelledby="sign-in-title">
        <h1 className={h1} id="sign-in-title">Sign in</h1>
        <p className={text}>
          Sign in with your work account to see the application register: the portfolio, each application, its
          dependencies, and planned changes and deployments.
        </p>
        <p className={small}>
          The pages are read-only. Your organization's groups decide which product roles you hold and what you can see.
        </p>
        <Button className="self-start" disabled={busy} onClick={() => { setBusy(true); void auth.signIn(); }}>
          <UserRoundCheck aria-hidden />
          {busy ? 'Opening the sign-in page' : 'Sign in'}
        </Button>
      </section>
    </main>
  );
}

export function DevSignIn({ auth }: { auth: DevAuth }) {
  const legendId = useId();
  const groupName = useId();
  const [choice, setChoice] = useState<string>(auth.personas[0]?.name ?? '');
  useEffect(() => { document.title = 'Development sign-in | Product'; }, []);
  return (
    <main className={wrap} id="main">
      <Problem text={auth.problem()} />
      <section className={card} aria-labelledby="sign-in-title">
        <h1 className={h1} id="sign-in-title">Development sign-in</h1>
        <p className={text}>
          A local run with no tenant. Choose a fictional persona; the local server signs each request in as that persona,
          with the groups and claims an Entra ID token would carry.
        </p>
        <fieldset className="m-0 border-0 p-0">
          <legend className="mb-1.5 p-0 text-sm font-semibold text-navy" id={legendId}>Persona</legend>
          {/* Native radio buttons: one tab stop for the group, arrow keys move the choice. */}
          <div className="flex flex-col gap-0.5" role="radiogroup" aria-labelledby={legendId}>
            {auth.personas.map(p => (
              <label key={p.name} className="flex min-h-8 cursor-pointer items-center gap-2.5 rounded-md px-1 text-sm text-navy hover:bg-muted">
                <input
                  type="radio"
                  name={groupName}
                  value={p.name}
                  checked={choice === p.name}
                  onChange={() => setChoice(p.name)}
                  className="size-4 shrink-0 cursor-pointer appearance-none rounded-full border border-input bg-card shadow-xs outline-none checked:border-primary checked:bg-primary checked:shadow-[inset_0_0_0_3px_var(--card)] focus-visible:ring-[3px] focus-visible:ring-ring/50"
                />
                {`${p.label} (${p.name})`}
              </label>
            ))}
          </div>
        </fieldset>
        <Button className="self-start" disabled={!choice} onClick={() => auth.choose(choice)}>
          <UserRoundCheck aria-hidden />
          Continue as this persona
        </Button>
        <p className={small}>
          Works only on 127.0.0.1 or localhost, against the local server started in the Development environment with
          Product:DevIdentity:Enabled=true. The Azure deployment has no development sign-in.
        </p>
      </section>
    </main>
  );
}
