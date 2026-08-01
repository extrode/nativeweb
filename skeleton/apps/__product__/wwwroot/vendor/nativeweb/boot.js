/* NativeWeb boot.js — the data-nw-* enhancement layer (spec 004 R2-R4).
 *
 * A single normalization pass, not a framework: elements opting in with any
 * data-nw-* attribute get the matching hx-* attributes so htmx can enhance
 * them. Load with `defer` BEFORE htmx so initial-load normalization runs
 * before htmx processes the document. Convention doc:
 * docs/progressive-enhancement.md in the NativeWeb repo.
 *
 * Rules (binding):
 *  - Opt-in only: elements without a data-nw-* attribute are never touched.
 *  - Never overwrite: an authored hx-* attribute always wins; the matching
 *    data-nw-* value is ignored for that attribute alone.
 *  - Transport comes from the HTML that already works without JavaScript:
 *    a form's method/action, an anchor's href. POST-only convention — no
 *    data-nw-method, no virtual PUT/PATCH/DELETE.
 *  - data-nw-* source attributes are never removed or altered.
 *  - Idempotent: re-running over the same element changes nothing.
 */
(function () {
  'use strict';

  var NAMES = ['target', 'swap', 'trigger', 'confirm'];
  var SELECTOR = '[data-nw-target],[data-nw-swap],[data-nw-trigger],[data-nw-confirm]';
  var TRANSPORTS = ['hx-get', 'hx-post', 'hx-put', 'hx-patch', 'hx-delete'];

  function hasTransport(el) {
    for (var i = 0; i < TRANSPORTS.length; i++) {
      if (el.hasAttribute(TRANSPORTS[i])) { return true; }
    }
    return false;
  }

  function normalize(el) {
    for (var i = 0; i < NAMES.length; i++) {
      var value = el.getAttribute('data-nw-' + NAMES[i]);
      if (value !== null && !el.hasAttribute('hx-' + NAMES[i])) {
        el.setAttribute('hx-' + NAMES[i], value);
      }
    }
    if (hasTransport(el)) { return; }
    if (el.tagName === 'FORM') {
      var method = (el.getAttribute('method') || 'get').toLowerCase();
      el.setAttribute(method === 'post' ? 'hx-post' : 'hx-get', el.getAttribute('action') || '');
    } else if (el.tagName === 'A') {
      var href = el.getAttribute('href');
      if (href !== null) { el.setAttribute('hx-get', href); }
    }
  }

  function sweep(root) {
    if (root.matches && root.matches(SELECTOR)) { normalize(root); }
    if (root.querySelectorAll) {
      var found = root.querySelectorAll(SELECTOR);
      for (var i = 0; i < found.length; i++) { normalize(found[i]); }
    }
  }

  // boot.js is loaded (defer) before htmx, so this listener runs before htmx's
  // own DOMContentLoaded processing of the initial document...
  document.addEventListener('DOMContentLoaded', function () { sweep(document); });

  // ...and swapped-in fragments are normalized per node just before htmx wires
  // them (htmx 2.x fires htmx:beforeProcessNode for every processed element).
  document.addEventListener('htmx:beforeProcessNode', function (event) { sweep(event.target); });
})();
