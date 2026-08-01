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
 *    data-nw-method, no virtual PUT/PATCH/DELETE. A form with no explicit
 *    action gets NO derived transport: htmx resolves an empty path against
 *    the base URL and merges query params where native submission replaces
 *    them, so the two paths would diverge.
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

  /* Returns true when normalization added at least one hx-* attribute. */
  function normalize(el) {
    var changed = false;
    for (var i = 0; i < NAMES.length; i++) {
      var value = el.getAttribute('data-nw-' + NAMES[i]);
      if (value !== null && !el.hasAttribute('hx-' + NAMES[i])) {
        el.setAttribute('hx-' + NAMES[i], value);
        changed = true;
      }
    }
    if (hasTransport(el)) { return changed; }
    if (el.tagName === 'FORM') {
      var action = el.getAttribute('action');
      if (action !== null) {
        var method = (el.getAttribute('method') || 'get').toLowerCase();
        el.setAttribute(method === 'post' ? 'hx-post' : 'hx-get', action);
        changed = true;
      }
    } else if (el.tagName === 'A') {
      var href = el.getAttribute('href');
      if (href !== null) {
        el.setAttribute('hx-get', href);
        changed = true;
      }
    }
    return changed;
  }

  function sweep(root, modified) {
    if (root.matches && root.matches(SELECTOR) && normalize(root) && modified) {
      modified.push(root);
    }
    if (root.querySelectorAll) {
      var found = root.querySelectorAll(SELECTOR);
      for (var i = 0; i < found.length; i++) {
        if (normalize(found[i]) && modified) { modified.push(found[i]); }
      }
    }
  }

  // boot.js is loaded (defer) before htmx, so this listener registers first and
  // FIFO ordering runs the initial sweep before htmx processes the document.
  // (Holds for the vendored htmx 2.0.10, whose ready gate also waits for
  // DOMContentLoaded unless readyState === "complete" — re-verify on any bump.)
  document.addEventListener('DOMContentLoaded', function () { sweep(document, null); });

  // Swapped-in fragments: htmx builds its list of elements to initialize BEFORE
  // firing htmx:beforeProcessNode, so an element selectable only through the
  // hx-* attributes this sweep just added (e.g. a descendant <a data-nw-target>)
  // is not in that snapshot. Forms and submit buttons are — htmx selects them
  // unconditionally and reads their attributes after this event — but anything
  // else that gained an attribute is re-submitted to htmx.process() on a
  // microtask, after the current processing pass; htmx's per-element init hash
  // makes the second pass a no-op for everything the first one already wired.
  document.addEventListener('htmx:beforeProcessNode', function (event) {
    var modified = [];
    sweep(event.target, modified);
    if (modified.length > 0 && window.htmx) {
      queueMicrotask(function () {
        for (var i = 0; i < modified.length; i++) { window.htmx.process(modified[i]); }
      });
    }
  });
})();
