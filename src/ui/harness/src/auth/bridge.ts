// True when the address carries a sign-in response from Entra ID: a state parameter with a code or an error, in the
// fragment or the query string. The web app never uses these names itself, so such an address is always a response
// for MSAL's redirect bridge (see msalAuth.ts).
export function hasSignInResponse(hash: string, search: string): boolean {
  for (const part of [hash.replace(/^#/, ''), search.replace(/^\?/, '')]) {
    if (!part) continue;
    const params = new URLSearchParams(part);
    if (params.has('state') && (params.has('code') || params.has('error'))) return true;
  }
  return false;
}
