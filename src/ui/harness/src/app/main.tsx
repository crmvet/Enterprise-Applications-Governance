// The web app's entry ("npm run build:app", dist/app, served under /app/ on the same origin as the API).
// 1. An address with a sign-in response runs MSAL's redirect bridge and stops (see auth/msalAuth.ts).
// 2. The viewer's own time zone becomes the display zone.
// 3. <base>config.json says which API and which sign-in to use; a bad file shows its problems instead of the app.
// 4. Sign-in: Entra ID through MSAL, or the fictional development persona on a loopback host.
// 5. The shell renders the four pages over the REST API. The fixture client is never part of this build.
import { FluentProvider, MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import { StrictMode, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { hasSignInResponse } from '../auth/bridge';
import { createDevAuth, sessionPersonaStore } from '../auth/devAuth';
import { createApiClient } from '../data/apiClient';
import { configureDisplayZone } from '../data/format';
import { academyTheme } from '../theme';
import { AppShell, type AppAuth } from './AppShell';
import { loadConfig } from './config';
import { normalizeBase } from './routes';

function render(root: Root, children: ReactNode) {
  root.render(
    <StrictMode>
      <FluentProvider theme={academyTheme} style={{ backgroundColor: 'transparent', minHeight: '100vh' }}>
        {children}
      </FluentProvider>
    </StrictMode>,
  );
}

function Problem({ title, lines }: { title: string; lines: string[] }) {
  return (
    <main id="main" style={{ maxWidth: 720, margin: '48px auto', padding: '0 16px' }}>
      <MessageBar intent="error" layout="multiline">
        <MessageBarBody>
          <MessageBarTitle>{title}</MessageBarTitle>
          <ul style={{ margin: '6px 0 0', paddingLeft: 20 }}>
            {lines.map(line => <li key={line}>{line}</li>)}
          </ul>
        </MessageBarBody>
      </MessageBar>
    </main>
  );
}

async function start() {
  const element = document.getElementById('root');
  if (!element) throw new Error('The page has no #root element.');
  const root = createRoot(element);

  if (hasSignInResponse(window.location.hash, window.location.search)) {
    try {
      const { broadcastResponseToMainFrame } = await import('@azure/msal-browser/redirect-bridge');
      await broadcastResponseToMainFrame();
    } catch {
      render(root, <Problem title="The sign-in response could not be read" lines={['Close this tab and sign in again from the web app.']} />);
    }
    return;
  }

  configureDisplayZone('local');
  const base = normalizeBase(import.meta.env.BASE_URL);
  const page = { origin: window.location.origin, hostname: window.location.hostname };
  const loaded = await loadConfig(window.fetch.bind(window), `${base}config.json`, page);
  if (!loaded.ok) {
    render(root, <Problem title="The web app's configuration is not usable" lines={loaded.problems} />);
    return;
  }
  const config = loaded.config;

  let auth: AppAuth;
  try {
    if (config.auth.mode === 'entra') {
      const { createMsalAuth } = await import('../auth/msalAuth');
      auth = await createMsalAuth({
        authority: config.auth.authority,
        clientId: config.auth.clientId,
        apiScope: config.auth.apiScope,
        redirectUri: `${window.location.origin}${base}`,
      });
    } else {
      auth = createDevAuth({ personas: config.development.personas, hostname: window.location.hostname, store: sessionPersonaStore(() => window.sessionStorage) });
    }
  } catch (e) {
    render(root, <Problem title="Sign-in could not start" lines={[e instanceof Error ? e.message : 'Reload the page to try again.']} />);
    return;
  }

  const api = createApiClient({ apiBase: config.apiBase, auth, fetch: window.fetch.bind(window) });
  render(root, <AppShell auth={auth} api={api} base={base} />);
}

void start();
