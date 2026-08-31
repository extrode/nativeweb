// Sets data-theme before first paint, so a dark-mode visitor never sees a white
// flash. Loaded WITHOUT defer, from the document head, for that reason: deferred
// it would run after the stylesheets have already painted the default theme.
//
// A file rather than an inline <script>. The skeleton's layout inlines the same
// logic with a CSP nonce (templates/layout/page.html), which apps/sample has no
// middleware to mint -- and an inline script with no nonce is the thing a
// content-security policy is supposed to refuse. A served file needs no
// exception.
(function () {
  try {
    var stored = localStorage.getItem('theme');
    var theme = stored === 'light' || stored === 'dark'
      ? stored
      : (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    document.documentElement.setAttribute('data-theme', theme);
  } catch (e) {
    // localStorage throws in a partitioned or cookie-blocked context. The
    // stylesheet's :root defaults to light, so failing here is a theme choice
    // not applied, not a broken page.
  }
})();
