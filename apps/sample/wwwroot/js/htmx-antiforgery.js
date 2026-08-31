// Copies the antiforgery token from the layout's <meta> onto every htmx request
// as a header, so a swap POST passes the same validation a form POST does.
//
// A form submitted by htmx sends its own hidden field, so this is belt and
// braces for that path -- it matters for hx-post on something that is not a
// form (a button, a link), where there is no hidden input to send. The
// skeleton's layout inlines this same logic under a CSP nonce; apps/sample has
// no nonce-minting middleware, so it is a served file instead. Same reasoning
// as theme.js.
//
// htmx 4 reshaped the event payload: the request lives at
// event.detail.ctx.request, and headers is a plain object on it. In htmx 2 this
// was event.detail.headers. Anything porting this back to a 2.x app has to
// change that line.
document.addEventListener('htmx:config:request', function (event) {
  var meta = document.querySelector('meta[name="request-verification-token"]');
  if (meta) {
    event.detail.ctx.request.headers['RequestVerificationToken'] = meta.content;
  }
});
