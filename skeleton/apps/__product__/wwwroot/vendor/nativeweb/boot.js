/* NativeWeb boot.js — the data-nw-* enhancement layer (spec 004 R2-R4)
 * plus request-scoped UX defaults (spec 005 R3/R6): busy states on the
 * submitting control and focus recovery after a swap removes the focused
 * element. Zero work while no request is in flight.
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
 *    data-nw-method, no virtual PUT/PATCH/DELETE. A form with a missing or
 *    empty action gets NO derived transport: htmx resolves an empty path against
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
      if (action) {
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
  // microtask, after the current processing pass. For untouched elements that
  // is a no-op; for snapshot elements boot modified (forms) the attribute-hash
  // mismatch triggers a clean de-init/re-init — listeners are removed before
  // re-adding and load-trigger state is preserved, so no double handlers or
  // duplicate requests.
  document.addEventListener('htmx:beforeProcessNode', function (event) {
    var modified = [];
    sweep(event.target, modified);
    if (modified.length > 0 && window.htmx) {
      queueMicrotask(function () {
        for (var i = 0; i < modified.length; i++) { window.htmx.process(modified[i]); }
      });
    }
  });

  /* Busy states (spec 005 R3): while a request is in flight, the control that
   * submitted it is disabled, marked aria-busy and given the nw-busy class;
   * everything is restored when the request completes, success or failure.
   * htmx:beforeRequest fires AFTER form serialization, so disabling here can
   * never drop the submitter's name/value from the payload. Disabling the
   * form's default button also suppresses implicit (Enter-key) resubmission
   * while in flight. The busy control rides on the issuing element, because
   * htmx:afterRequest is dispatched on that same element in every completion
   * path (load, error, timeout, abort). If the response swapped the control
   * out, restore touches a detached node, which is a harmless no-op. */
  function busyControl(event) {
    var elt = event.detail.elt;
    var trigger = event.detail.requestConfig && event.detail.requestConfig.triggeringEvent;
    if (trigger && trigger.submitter) { return trigger.submitter; }
    if (elt.tagName === 'BUTTON' || (elt.tagName === 'INPUT' && elt.type === 'submit')) {
      return elt;
    }
    if (elt.tagName === 'FORM') {
      return elt.querySelector('button:not([type=button]):not([type=reset]),input[type=submit]');
    }
    return null;
  }

  /* Disabling the focused submitter silently moves focus to <body> the moment
   * the request starts — before any swap. Track that control so (a) restore can
   * hand focus back when the control survives the response, and (b) the swap-
   * focus recovery below can tell "focus was in the target until we disabled
   * it" from "focus was on body all along". */
  var busyFocusControl = null;

  function restoreBusy(elt) {
    var control = elt.nwBusyControl;
    if (!control) { return; }
    delete elt.nwBusyControl;
    control.disabled = false;
    control.removeAttribute('aria-busy');
    control.classList.remove('nw-busy');
    if (control === busyFocusControl) {
      busyFocusControl = null;
      var active = document.activeElement;
      if (control.isConnected && (!active || active === document.body)) {
        try { control.focus({ preventScroll: true }); } catch (e) { control.focus(); }
      }
    }
  }

  document.addEventListener('htmx:beforeRequest', function (event) {
    var control = busyControl(event);
    if (!control || control.disabled) { return; }
    if (document.activeElement === control) { busyFocusControl = control; }
    control.disabled = true;
    control.setAttribute('aria-busy', 'true');
    control.classList.add('nw-busy');
    event.detail.elt.nwBusyControl = control;
    /* A later listener may still cancel this request (htmx reads
     * defaultPrevented after ALL listeners ran), and htmx's cancel path
     * returns without ever firing htmx:afterRequest — so re-check once the
     * dispatch has fully completed and undo the busy state we just applied. */
    queueMicrotask(function () {
      if (event.defaultPrevented) { restoreBusy(event.detail.elt); }
    });
  });

  document.addEventListener('htmx:afterRequest', function (event) {
    restoreBusy(event.detail.elt);
  });

  /* Focus recovery (spec 005 R6): when a swap removes the focused element the
   * browser silently moves focus to <body>, stranding keyboard and screen-
   * reader users at the top of the document. Only that case is handled — focus
   * that survived the swap is never stolen. Preference order inside the new
   * content: an authored [autofocus], else the swap target itself via
   * tabindex="-1" (the same technique the layouts' <main> uses for the skip
   * link). preventScroll keeps an off-screen swap from yanking the viewport. */
  /* Recover only when the swap itself is what removed the focused element.
   * "activeElement is body after the swap" alone is not enough: focus may have
   * been on body all along (user clicked the page background, or a polling /
   * load-triggered swap fired with nothing focused), and recovering then would
   * steal focus. So the outgoing state is recorded per swap at beforeSwap —
   * htmx fires beforeSwap/afterSwap as a sequential pair per swapped region,
   * OOB swaps included, so a single flag pairs up correctly. */
  var focusWasInSwapTarget = false;

  /* The busy layer above may already have moved focus to body by disabling the
   * focused submitter, so "the control we disabled sits inside the target"
   * counts as focus-in-target too — without it a keyboard-submitted form whose
   * error re-render swaps the submitter away would strand focus at the top. */
  document.addEventListener('htmx:beforeSwap', function (event) {
    var active = document.activeElement;
    var target = event.detail.target;
    focusWasInSwapTarget = !!(target && (
      (active && active !== document.body
        && (target === active || (target.contains && target.contains(active))))
      || (busyFocusControl && target.contains && target.contains(busyFocusControl))));
  });

  document.addEventListener('htmx:afterSwap', function (event) {
    /* If the swap detached the tracked busy control, drop the reference now:
     * htmx re-dispatches afterRequest on a connected ancestor when the issuing
     * element was swapped away, so restoreBusy never sees it and the stale
     * node would linger until the next focused submit. */
    if (busyFocusControl && !busyFocusControl.isConnected) { busyFocusControl = null; }
    var swapRemovedFocus = focusWasInSwapTarget;
    focusWasInSwapTarget = false;
    if (!swapRemovedFocus) { return; }
    var active = document.activeElement;
    if (active && active !== document.body) { return; }
    /* For outerHTML swaps detail.target is the OLD, now-detached element —
     * htmx keeps the original eventInfo. The event itself, though, is
     * dispatched on each swapped-IN element, so event.target is the live
     * replacement content. */
    var target = event.detail.target;
    if (!target || !target.isConnected) { target = event.target; }
    if (!target || target.nodeType !== 1 || !target.isConnected) { return; }
    var next = target.querySelector('[autofocus]');
    if (!next) {
      if (!target.hasAttribute('tabindex')) { target.setAttribute('tabindex', '-1'); }
      next = target;
    }
    try { next.focus({ preventScroll: true }); } catch (e) { /* older engines: no options */ next.focus(); }
  });
})();
