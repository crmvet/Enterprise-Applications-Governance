// The sign-in page, shown until there is a signed-in person.
// Production: one button that starts the Entra ID sign-in (a full-page redirect).
// Development: a choice of fictional personas, sent as X-Product-Dev-Identity to the local server.
import {
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Radio,
  RadioGroup,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { PersonArrowRight20Regular } from '@fluentui/react-icons';
import { useEffect, useId, useState } from 'react';
import type { DevAuth } from '../auth/devAuth';
import type { MsalAuth } from '../auth/msalAuth';
import { palette } from '../theme';
import { media } from '../components/ui';

const useStyles = makeStyles({
  wrap: { maxWidth: '560px', margin: '48px auto', display: 'flex', flexDirection: 'column', rowGap: '16px', [media.narrow]: { margin: '24px auto' } },
  card: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${palette.line}`,
    borderRadius: '10px',
    padding: '24px',
    display: 'flex',
    flexDirection: 'column',
    rowGap: '14px',
    [media.narrow]: { padding: '16px' },
  },
  h1: { margin: 0, fontSize: '24px', lineHeight: '32px', fontWeight: tokens.fontWeightSemibold, color: palette.navy },
  text: { margin: 0, fontSize: tokens.fontSizeBase300, color: palette.navy2 },
  small: { margin: 0, fontSize: tokens.fontSizeBase200, color: palette.navy3 },
  legend: { fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold, color: palette.navy, padding: 0, marginBottom: '4px' },
  fieldset: { border: 'none', margin: 0, padding: 0 },
  action: { alignSelf: 'flex-start' },
});

function Problem({ text }: { text: string | null }) {
  if (!text) return null;
  return (
    <MessageBar intent="warning" layout="multiline">
      <MessageBarBody>
        <MessageBarTitle>Sign-in needs another try</MessageBarTitle>
        {text}
      </MessageBarBody>
    </MessageBar>
  );
}

export function EntraSignIn({ auth }: { auth: MsalAuth }) {
  const s = useStyles();
  const [busy, setBusy] = useState(false);
  useEffect(() => { document.title = 'Sign in | Product'; }, []);
  return (
    <main className={s.wrap} id="main">
      <Problem text={auth.problem()} />
      <section className={s.card} aria-labelledby="sign-in-title">
        <h1 className={s.h1} id="sign-in-title">Sign in</h1>
        <p className={s.text}>
          Sign in with your work account to see the application register: the portfolio, each application, its
          dependencies, and planned changes and deployments.
        </p>
        <p className={s.small}>
          The pages are read-only. Your organization's groups decide which product roles you hold and what you can see.
        </p>
        <Button
          className={s.action}
          appearance="primary"
          icon={<PersonArrowRight20Regular />}
          disabled={busy}
          onClick={() => { setBusy(true); void auth.signIn(); }}
        >
          {busy ? 'Opening the sign-in page' : 'Sign in'}
        </Button>
      </section>
    </main>
  );
}

export function DevSignIn({ auth }: { auth: DevAuth }) {
  const s = useStyles();
  const legendId = useId();
  const [choice, setChoice] = useState<string>(auth.personas[0]?.name ?? '');
  useEffect(() => { document.title = 'Development sign-in | Product'; }, []);
  return (
    <main className={s.wrap} id="main">
      <Problem text={auth.problem()} />
      <section className={s.card} aria-labelledby="sign-in-title">
        <h1 className={s.h1} id="sign-in-title">Development sign-in</h1>
        <p className={s.text}>
          A local run with no tenant. Choose a fictional persona; the local server signs each request in as that persona,
          with the groups and claims an Entra ID token would carry.
        </p>
        <fieldset className={s.fieldset}>
          <legend className={s.legend} id={legendId}>Persona</legend>
          <RadioGroup aria-labelledby={legendId} value={choice} onChange={(_, d) => setChoice(d.value)}>
            {auth.personas.map(p => <Radio key={p.name} value={p.name} label={`${p.label} (${p.name})`} />)}
          </RadioGroup>
        </fieldset>
        <Button className={s.action} appearance="primary" icon={<PersonArrowRight20Regular />} disabled={!choice} onClick={() => auth.choose(choice)}>
          Continue as this persona
        </Button>
        <p className={s.small}>
          Works only on 127.0.0.1 or localhost, against the local server started in the Development environment with
          Product:DevIdentity:Enabled=true. The Azure deployment has no development sign-in.
        </p>
      </section>
    </main>
  );
}
