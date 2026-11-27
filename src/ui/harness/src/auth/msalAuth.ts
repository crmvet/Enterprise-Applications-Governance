// Entra ID sign-in for the web app, with MSAL for the browser (@azure/msal-browser, MIT).
//
// The web app is a public client (a single-page app registration). It signs the person in with the authorization code
// flow and PKCE through a full-page redirect, and asks for one scope: the product API's api.access. MSAL keeps its
// cache in sessionStorage, so the sign-in lasts for the tab and leaves nothing behind when the tab closes.
// Each API call asks acquireTokenSilent first and falls back to acquireTokenRedirect. The access token goes only to
// the configured API base (apiClient.ts); the API validates it, and it never forwards it: calls the API makes to
// Microsoft Graph or the Power Platform API use a new token from the on-behalf-of exchange.
//
// The redirect URI is the web app's base address, <origin>/app/. MSAL 5 expects the redirect URI to run its redirect
// bridge (broadcastResponseToMainFrame); src/app/main.tsx does that before anything else when the address carries a
// sign-in response, and the bridge hands the response back to the page that started the sign-in.
import {
  BrowserCacheLocation,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser';
import { listeners, SignInRequired, SignInUnavailable, type AuthSession, type SignedInUser } from './types';

export interface EntraSettings {
  authority: string;
  clientId: string;
  apiScope: string;
  // <origin>/app/, registered as a single-page application redirect URI on the web app registration.
  redirectUri: string;
}

const REDIRECT_AT_KEY = 'product.signInRedirectAt';
// A second redirect within this time after coming back from one is a loop; the page shows the problem instead.
const LOOP_MS = 60_000;

const errorCode = (e: unknown): string => (e && typeof e === 'object' && 'errorCode' in e ? String((e as { errorCode: unknown }).errorCode) : '');
const NETWORK = new Set(['no_network_connectivity', 'network_error', 'endpoints_resolution_error']);

function readNumber(key: string): number {
  try {
    return Number(window.sessionStorage.getItem(key) ?? '0') || 0;
  } catch {
    return 0;
  }
}

function writeNumber(key: string, value: number): void {
  try {
    window.sessionStorage.setItem(key, String(value));
  } catch {
    // Without storage the loop guard falls back to this page load only.
  }
}

export interface MsalAuth extends AuthSession {
  readonly mode: 'entra';
  signIn(): Promise<void>;
  account(): AccountInfo | null;
}

export async function createMsalAuth(settings: EntraSettings): Promise<MsalAuth> {
  const scopes = [settings.apiScope];
  const pca = new PublicClientApplication({
    auth: {
      clientId: settings.clientId,
      authority: settings.authority,
      redirectUri: settings.redirectUri,
      postLogoutRedirectUri: settings.redirectUri,
    },
    cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
  });
  await pca.initialize();

  const events = listeners();
  let problem: string | null = null;
  let redirecting = false;

  // Picks up a sign-in response the redirect bridge stored for this page.
  let returned = false;
  try {
    const result = await pca.handleRedirectPromise();
    if (result?.account) {
      pca.setActiveAccount(result.account);
      returned = true;
    }
  } catch (e) {
    problem = `Sign-in did not complete (${errorCode(e) || 'unknown error'}). Sign in again.`;
  }
  if (!pca.getActiveAccount()) {
    const accounts = pca.getAllAccounts();
    if (accounts.length) pca.setActiveAccount(accounts[0]);
  }
  if (!returned) writeNumber(REDIRECT_AT_KEY, 0);

  const redirect = (account: AccountInfo | null) => {
    if (redirecting) return;
    const last = readNumber(REDIRECT_AT_KEY);
    if (last && Date.now() - last < LOOP_MS) {
      problem = 'Sign-in came back without a token for the product API. Check that the web app registration may ask for the API scope, then sign in again.';
      events.notify();
      return;
    }
    redirecting = true;
    writeNumber(REDIRECT_AT_KEY, Date.now());
    const request = { scopes, redirectStartPage: window.location.href };
    const started = account ? pca.acquireTokenRedirect({ ...request, account }) : pca.loginRedirect(request);
    started.catch(e => {
      redirecting = false;
      problem = `Sign-in could not start (${errorCode(e) || 'unknown error'}).`;
      events.notify();
    });
  };

  return {
    mode: 'entra',
    account: () => pca.getActiveAccount(),
    async signIn() {
      writeNumber(REDIRECT_AT_KEY, 0);
      problem = null;
      redirect(null);
    },
    async headers() {
      const account = pca.getActiveAccount();
      if (!account) throw new SignInRequired();
      try {
        const result = await pca.acquireTokenSilent({ scopes, account });
        return { Authorization: `Bearer ${result.accessToken}` };
      } catch (e) {
        const code = errorCode(e);
        if (NETWORK.has(code)) throw new SignInUnavailable();
        if (code !== 'interaction_in_progress') redirect(account);
        throw new SignInRequired('Signing you in again.');
      }
    },
    onUnauthorized() {
      redirect(pca.getActiveAccount());
    },
    stepUp(claimsChallenge) {
      // A fresh sign-in for an approval answer (D-38): always a login prompt, so auth_time is new; with the
      // authentication context's claims challenge when the API sent one (base64 JSON).
      const account = pca.getActiveAccount();
      let claims: string | undefined;
      try {
        claims = claimsChallenge ? window.atob(claimsChallenge) : undefined;
      } catch {
        claims = undefined;
      }
      writeNumber(REDIRECT_AT_KEY, 0);
      pca.acquireTokenRedirect({ scopes, prompt: 'login', redirectStartPage: window.location.href, ...(account ? { account } : {}), ...(claims ? { claims } : {}) }).catch(e => {
        problem = `Sign-in could not start (${errorCode(e) || 'unknown error'}).`;
        events.notify();
      });
    },
    user(): SignedInUser | null {
      const account = pca.getActiveAccount();
      return account ? { name: account.name ?? null, username: account.username || null } : null;
    },
    async signOut() {
      writeNumber(REDIRECT_AT_KEY, 0);
      await pca.logoutRedirect({ account: pca.getActiveAccount() ?? undefined, postLogoutRedirectUri: settings.redirectUri });
    },
    subscribe: events.subscribe,
    problem: () => problem,
  };
}
