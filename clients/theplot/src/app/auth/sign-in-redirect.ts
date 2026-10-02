/** /signin is a BFF endpoint, not an Angular route — full-page navigation. */
export function redirectToSignIn(
  returnUrl = window.location.pathname + window.location.search + window.location.hash,
): void {
  window.location.assign(`/signin?returnUrl=${encodeURIComponent(returnUrl)}`);
}
