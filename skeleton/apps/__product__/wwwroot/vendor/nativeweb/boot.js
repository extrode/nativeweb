/* NativeWeb boot.js — the data-nw-* enhancement layer (spec 004 R2-R4)
 * plus request-scoped UX defaults (spec 005 R3/R6): busy states on the
 * submitting control and focus recovery after a swap removes the focused
 * element. Zero work while no request is in flight.
 *
 * A single normalization pass, not a framework: elements opting in with any
 * data-nw-* attribute get the matching hx-* attributes so htmx can enhance
 * them. Load with `defer` BEFORE htmx so initial-load normalization runs
 * before htmx processes the document. Convention doc:
 * docs/01-architecture/progressive-enhancement.md in the NativeWeb repo.
 *
 * Targets htmx 4.0.0 (spec 012). Every htmx lifecycle event carries its
 * payload on `event.detail.ctx`; the flat `detail.elt` / `detail.target` /
 * `detail.headers` / `detail.requestConfig` of htmx 2 do not exist. The
 * mapping used below is `ctx.sourceElement` for the issuing element,
 * `ctx.target` for the resolved swap target, and `ctx.request.submitter` for
 * the control that submitted a form.
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

  function normalize(el) {
    for (var i = 0; i < NAMES.length; i++) {
      var value = el.getAttribute('data-nw-' + NAMES[i]);
      if (value !== null && !el.hasAttribute('hx-' + NAMES[i])) {
        el.setAttribute('hx-' + NAMES[i], value);
      }
    }
    if (hasTransport(el)) { return; }
    if (el.tagName === 'FORM') {
      var action = el.getAttribute('action');
      if (action) {
        var method = (el.getAttribute('method') || 'get').toLowerCase();
        el.setAttribute(method === 'post' ? 'hx-post' : 'hx-get', action);
      }
    } else if (el.tagName === 'A') {
      var href = el.getAttribute('href');
      if (href !== null) {
        el.setAttribute('hx-get', href);
      }
    }
  }

  function sweep(root) {
    if (root.matches && root.matches(SELECTOR)) { normalize(root); }
    if (root.querySelectorAll) {
      var found = root.querySelectorAll(SELECTOR);
      for (var i = 0; i < found.length; i++) { normalize(found[i]); }
    }
  }

  // boot.js is loaded (defer) before htmx, so this listener registers first and
  // FIFO ordering runs the initial sweep before htmx processes the document.
  // (Holds for the vendored htmx 4.0.0, whose ready gate is still
  // `readyState === "loading"` -> DOMContentLoaded — re-verify on any bump.)
  document.addEventListener('DOMContentLoaded', function () { sweep(document); });

  // Swapped-in fragments. htmx 4 dispatches htmx:before:process on the subtree
  // root with NO detail, so the root is read off event.target.
  //
  // Under htmx 2 this listener also had to re-submit whatever it modified via a
  // queued htmx.process(): htmx built its list of elements to initialize BEFORE
  // firing the equivalent event, so a swapped-in *descendant* whose only hooks
  // were data-nw-* attributes was invisible to that pass (the B3-review HIGH
  // finding). htmx 4 dispatches this event and only then queries the subtree, so
  // attributes added here are picked up by the very pass that follows and the
  // re-submission is dead weight. Removed on measurement, not on reading:
  // Normalization_SwappedInDescendantAnchor_GetsHtmxWired passes without it, and
  // still fails when this sweep is removed, so it does exercise the path.
  document.addEventListener('htmx:before:process', function (event) {
    sweep(event.target);
  });

  /* Busy states (spec 005 R3): while a request is in flight, the control that
   * submitted it is disabled, marked aria-busy and given the nw-busy class;
   * everything is restored when the request completes, success or failure.
   * htmx:before:request fires AFTER form serialization, so disabling here can
   * never drop the submitter's name/value from the payload. Disabling the
   * form's default button also suppresses implicit (Enter-key) resubmission
   * while in flight. The busy control rides on the request's own ctx object,
   * which is the same object at before:request and at finally:request. It does
   * NOT ride on ctx.sourceElement, and that is a fix rather than a preference:
   * htmx reassigns ctx.sourceElement to the swap target while swapping, so the
   * property names a different element by the time the request finishes.
   * Measured on the signin form, whose data-nw-target is `closest .auth-board`:
   * sourceElement is FORM.auth-card at before:request and DIV.auth-board at
   * finally:request. Reading the busy reference back off it therefore looked at
   * an element that had never been given one, and a submit control outside the
   * swapped region stayed disabled forever. Everything inside the region is
   * replaced by the response anyway, which is why this hid for so long.
   *
   * Delegated from document on purpose. htmx's own trigger ends
   * `isConnected ? element : document`, so a source the swap detached has its
   * completion event re-dispatched on document — an at-target listener bound to
   * that element would never hear it. Delegation is what makes the detached
   * case work, not what misses it.
   *
   * htmx 4 puts the submitter on ctx.request.submitter before the request
   * lifecycle starts, which replaces htmx 2's reach through
   * detail.requestConfig.triggeringEvent.submitter. */
  function busyControl(event) {
    var ctx = event.detail.ctx;
    var elt = ctx.sourceElement;
    if (ctx.request && ctx.request.submitter) { return ctx.request.submitter; }
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

  function restoreBusy(ctx) {
    var control = ctx && ctx.nwBusyControl;
    if (!control) { return; }
    delete ctx.nwBusyControl;
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

  document.addEventListener('htmx:before:request', function (event) {
    var control = busyControl(event);
    if (!control || control.disabled) { return; }
    if (document.activeElement === control) { busyFocusControl = control; }
    control.disabled = true;
    control.setAttribute('aria-busy', 'true');
    control.classList.add('nw-busy');
    event.detail.ctx.nwBusyControl = control;
  });

  /* Restore on htmx:finally:request, NOT htmx:after:request. In htmx 4 the
   * after: event fires only once the response body has been read, and it is
   * cancelable — a network error, a timeout, an abort or a cancelled
   * before:request all skip it. finally:request sits in the request's actual
   * finally block, so it fires on every completion path including those four.
   * Restoring on after: would leave the submit control disabled forever after a
   * dropped connection. (htmx 2's afterRequest did cover every path, which is
   * why this is a behaviour change and not a rename.) */
  document.addEventListener('htmx:finally:request', function (event) {
    restoreBusy(event.detail.ctx);
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
   * steal focus. So the outgoing state is recorded per swap at before:swap —
   * htmx fires before:swap/after:swap as a sequential pair per swapped region,
   * OOB swaps included, so a single flag pairs up correctly. */
  var focusWasInSwapTarget = false;

  /* The busy layer above may already have moved focus to body by disabling the
   * focused submitter, so "the control we disabled sits inside the target"
   * counts as focus-in-target too — without it a keyboard-submitted form whose
   * error re-render swaps the submitter away would strand focus at the top. */
  document.addEventListener('htmx:before:swap', function (event) {
    var active = document.activeElement;
    var target = event.detail.ctx.target;
    focusWasInSwapTarget = !!(target && (
      (active && active !== document.body
        && (target === active || (target.contains && target.contains(active))))
      || (busyFocusControl && target.contains && target.contains(busyFocusControl))));
  });

  document.addEventListener('htmx:after:swap', function (event) {
    /* If the swap detached the tracked busy control, drop the reference now, so
     * a stale node does not linger until the next focused submit. */
    if (busyFocusControl && !busyFocusControl.isConnected) { busyFocusControl = null; }
    var swapRemovedFocus = focusWasInSwapTarget;
    focusWasInSwapTarget = false;
    if (!swapRemovedFocus) { return; }
    var active = document.activeElement;
    if (active && active !== document.body) { return; }
    /* htmx 4 dispatches this on ctx.sourceElement, not on the swapped-in
     * content — so event.target is the element that ISSUED the request, which
     * is the wrong node to focus and may not even be in the swapped region.
     * The live content is reached through ctx.target instead. (htmx 2
     * dispatched per swapped-in element, which is why the old event.target
     * fallback is gone rather than renamed.) */
    var target = event.detail.ctx.target;
    if (!target || target.nodeType !== 1 || !target.isConnected) { return; }
    var next = target.querySelector('[autofocus]');
    if (!next) {
      if (!target.hasAttribute('tabindex')) { target.setAttribute('tabindex', '-1'); }
      next = target;
    }
    try { next.focus({ preventScroll: true }); } catch (e) { /* older engines: no options */ next.focus(); }
  });
})();
