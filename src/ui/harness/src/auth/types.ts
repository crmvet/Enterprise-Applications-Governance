// What the API client needs from a sign-in: headers for a call to the API base, and a way to start sign-in again.
// Two implementations: Entra ID through MSAL (msalAuth.ts, production) and the fictional development persona
// (devAuth.ts, local runs on a loopback host only).

export interface SignedInUser {
  name: string | null;
  username: string | null;
}

export interface AuthSession {
  readonly mode: 'entra' | 'development';
  // The headers for one call to the API base. Throws SignInRequired when there is no usable sign-in; a sign-in may
  // already have started. Throws SignInUnavailable when the sign-in service could not be reached.
  headers(): Promise<Record<string, string>>;
  // The API answered 401 not_signed_in: sign in again.
  onUnauthorized(): void;
  // The API answered step_up_required (D-38): sign the person in again now, with a login prompt, and with the claims
  // challenge when the error carried one. The person answers again once they are back.
  stepUp(claimsChallenge: string | null): void;
  user(): SignedInUser | null;
  signOut(): Promise<void>;
  // Calls back when the sign-in state changes (a persona chosen, a sign-in problem). Returns the unsubscribe function.
  subscribe(callback: () => void): () => void;
  // A sign-in problem to show on the page, or null.
  problem(): string | null;
}

export class SignInRequired extends Error {
  constructor(message = 'Sign in to see this section.') {
    super(message);
    this.name = 'SignInRequired';
  }
}

export class SignInUnavailable extends Error {
  constructor(message = 'The sign-in service did not answer. Check the connection and try again.') {
    super(message);
    this.name = 'SignInUnavailable';
  }
}

// A small listener list for subscribe().
export function listeners() {
  const set = new Set<() => void>();
  return {
    subscribe(callback: () => void) {
      set.add(callback);
      return () => { set.delete(callback); };
    },
    notify() {
      for (const callback of [...set]) callback();
    },
  };
}
