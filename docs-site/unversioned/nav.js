(function () {
  var d = document.documentElement;

  function store(key, value) {
    try {
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch (e) { /* storage denied; the choice still holds for this page */ }
  }

  // ---- group toggle ----
  window.toggleGroup = function (header) {
    var group = header.closest('[data-group]');
    var open  = group.dataset.open === 'true';
    group.dataset.open = open ? 'false' : 'true';
  };

  // ---- sidebar ----
  // Two different things at two widths. Under 760px the sidebar is a drawer over the
  // page; above it, a column that can be collapsed to give the article the width.
  function isDrawer() { return window.matchMedia('(max-width: 760px)').matches; }

  window.toggleNav = function () {
    var sidebar  = document.getElementById('jt-sidebar');
    var backdrop = document.getElementById('nav-backdrop');
    if (!sidebar) return;
    if (isDrawer()) {
      var isOpen = sidebar.classList.contains('open');
      sidebar.classList.toggle('open', !isOpen);
      backdrop.classList.toggle('open', !isOpen);
      return;
    }
    var hidden = d.getAttribute('data-nav') === 'hidden';
    if (hidden) d.removeAttribute('data-nav'); else d.setAttribute('data-nav', 'hidden');
    store('dg-nav', hidden ? null : 'hidden');
  };

  // ---- sidebar width ----
  // A drag on the sidebar's right edge. Bounded: below 180px the labels wrap on every
  // word, above 480px the article is the thing being squeezed.
  var NAV_MIN = 180, NAV_MAX = 480;
  var resizer = document.getElementById('nav-resizer');
  if (resizer) {
    var dragging = false;
    resizer.addEventListener('pointerdown', function (e) {
      if (isDrawer()) return;
      dragging = true;
      resizer.setPointerCapture(e.pointerId);
      d.classList.add('nav-resizing');
      e.preventDefault();
    });
    resizer.addEventListener('pointermove', function (e) {
      if (!dragging) return;
      var w = Math.min(NAV_MAX, Math.max(NAV_MIN, Math.round(e.clientX)));
      d.style.setProperty('--nav-w', w + 'px');
    });
    function endDrag() {
      if (!dragging) return;
      dragging = false;
      d.classList.remove('nav-resizing');
      var w = parseInt(d.style.getPropertyValue('--nav-w'), 10);
      store('dg-nav-w', w ? String(w) : null);
    }
    resizer.addEventListener('pointerup', endDrag);
    resizer.addEventListener('pointercancel', endDrag);
    resizer.addEventListener('dblclick', function () {
      d.style.removeProperty('--nav-w');
      store('dg-nav-w', null);
    });
  }

  // ---- nav filter ----
  window.filterNav = function (q) {
    q = q.toLowerCase().trim();
    document.querySelectorAll('.nav-group').forEach(function (group) {
      var items   = group.querySelectorAll('.nav-group-items a');
      var anyVis  = false;
      items.forEach(function (a) {
        var match = !q || a.textContent.toLowerCase().includes(q);
        a.classList.toggle('nav-hidden', !match);
        if (match) anyVis = true;
      });
      group.classList.toggle('nav-hidden', !anyVis);
      if (q && anyVis) group.dataset.open = 'true';
    });
  };

  // ---- copy code ----
  window.copyCode = function (btn) {
    var pre = btn.closest('.code-block').querySelector('pre');
    if (!pre) return;
    var text = pre.innerText || pre.textContent;
    function done(label) {
      btn.textContent = label;
      btn.classList.add('copied');
      setTimeout(function () { btn.textContent = 'copy'; btn.classList.remove('copied'); }, 1800);
    }
    try {
      navigator.clipboard.writeText(text).then(function () { done('✓ copied'); },
                                               function () { done('error'); });
    } catch (e) {
      done('error');
    }
  };

  // ---- colour scheme ----
  // Three states, not two. "auto" is a real choice and the default one: it
  // follows the OS, which is what most readers actually want, and a two-state
  // toggle gives no way back to it once touched.
  var THEMES = ['auto', 'light', 'dark'];

  function showTheme(t) {
    var btn = document.getElementById('theme-btn');
    if (btn) btn.textContent = t;
  }

  function applyTheme(t) {
    if (t === 'auto') d.removeAttribute('data-theme');
    else d.setAttribute('data-theme', t);
    showTheme(t);
  }

  window.cycleTheme = function () {
    var cur = d.getAttribute('data-theme') || 'auto';
    var next = THEMES[(THEMES.indexOf(cur) + 1) % THEMES.length];
    applyTheme(next);
    store('dg-theme', next === 'auto' ? null : next);
  };

  // ---- text size ----
  // Bounded at 0.75 and 1.75: below the first the chrome's fixed 46px header
  // stops fitting its own text, above the second the 240px sidebar starts
  // wrapping every label. dir === 0 resets.
  var FS_MIN = 0.75, FS_MAX = 1.75, FS_STEP = 0.125;

  window.fontSize = function (dir) {
    var cur = parseFloat(d.style.getPropertyValue('--fs-scale')) || 1;
    var next = dir === 0 ? 1 : Math.min(FS_MAX, Math.max(FS_MIN, cur + dir * FS_STEP));
    if (next === 1) d.style.removeProperty('--fs-scale');
    else d.style.setProperty('--fs-scale', String(next));
    store('dg-fs', next === 1 ? null : String(next));
  };

  // The head script has already applied the saved theme before first paint;
  // this only brings the button's label into line with it.
  showTheme(d.getAttribute('data-theme') || 'auto');

  // ---- on this page: hide / show ----
  // Two layouts, two meanings. Beside the article (wide) the toggle hides the rail to a
  // tab, and that choice is remembered. Above the article (narrow) the rail starts folded
  // to its title row and the toggle unfolds it for this page only.
  window.toggleToc = function () {
    if (window.matchMedia('(max-width: 1100px)').matches) {
      if (d.hasAttribute('data-toc-narrow')) d.removeAttribute('data-toc-narrow');
      else d.setAttribute('data-toc-narrow', 'open');
      return;
    }
    var hidden = d.getAttribute('data-toc') === 'hidden';
    if (hidden) d.removeAttribute('data-toc'); else d.setAttribute('data-toc', 'hidden');
    store('dg-toc', hidden ? null : 'hidden');
  };

  // ---- table of contents: mark the section being read ----
  var tocLinks = document.querySelectorAll('.toc a');
  if (tocLinks.length && 'IntersectionObserver' in window) {
    var byId = {};
    tocLinks.forEach(function (a) { byId[a.getAttribute('href').slice(1)] = a; });

    var seen = [];
    var obs = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        var id = e.target.id;
        var i = seen.indexOf(id);
        if (e.isIntersecting && i < 0) seen.push(id);
        if (!e.isIntersecting && i >= 0) seen.splice(i, 1);
      });
      tocLinks.forEach(function (a) { a.classList.remove('toc-active'); });
      // The topmost heading currently on screen, in document order -- not the
      // most recent callback, whose order depends on scroll direction.
      var ids = Object.keys(byId).filter(function (id) { return seen.indexOf(id) >= 0; });
      if (ids.length) byId[ids[0]].classList.add('toc-active');
    }, { rootMargin: '0px 0px -70% 0px' });

    Object.keys(byId).forEach(function (id) {
      var h = document.getElementById(id);
      if (h) obs.observe(h);
    });
  }

  // ---- search ----
  // The index is a script, not JSON: fetch() of a sibling file fails under file://,
  // and these sites are opened from disk. Injected once, on the first search.
  var overlay = document.getElementById('search-overlay');
  var input   = document.getElementById('search-input');
  var results = document.getElementById('search-results');
  var index   = null, indexState = 'idle', selected = -1, hits = [];

  function loadIndex() {
    if (indexState !== 'idle') return;
    indexState = 'loading';
    var s = document.createElement('script');
    s.src = 'search-index.js';
    s.onload = function () {
      index = window.__docsIndex || [];
      indexState = 'ready';
      if (input) runSearch(input.value);
    };
    s.onerror = function () {
      indexState = 'failed';
      if (results) results.innerHTML = '<div class="search-empty">the search index did not load</div>';
    };
    document.head.appendChild(s);
  }

  window.openSearch = function () {
    if (!overlay) return;
    overlay.hidden = false;
    loadIndex();
    input.focus();
    input.select();
  };

  window.closeSearch = function () {
    if (overlay) overlay.hidden = true;
  };

  function esc(s) {
    return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  // Every term must appear somewhere on the page; a title hit outranks a heading hit
  // outranks a body hit, and the snippet is cut around the first body hit.
  function score(page, terms) {
    var title = page.t.toLowerCase(), body = page.b.toLowerCase();
    var total = 0, heading = null, first = -1;
    for (var i = 0; i < terms.length; i++) {
      var t = terms[i], s = 0;
      if (title.indexOf(t) >= 0) s += 10;
      for (var j = 0; j < page.h.length; j++) {
        if (page.h[j].t.toLowerCase().indexOf(t) >= 0) { s += 5; if (!heading) heading = page.h[j]; break; }
      }
      var at = body.indexOf(t);
      if (at >= 0) { s += 1; if (first < 0 || at < first) first = at; }
      if (s === 0) return null;
      total += s;
    }
    return { page: page, score: total, heading: heading, at: first };
  }

  function snippet(body, at, terms) {
    if (at < 0) return esc(body.slice(0, 140));
    var start = Math.max(0, at - 60), end = Math.min(body.length, at + 100);
    var text = (start > 0 ? '…' : '') + body.slice(start, end) + (end < body.length ? '…' : '');
    var out = esc(text);
    terms.forEach(function (t) {
      var re = new RegExp(t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'ig');
      out = out.replace(re, function (m) { return '<mark>' + m + '</mark>'; });
    });
    return out;
  }

  function render() {
    if (!results) return;
    if (!hits.length) {
      results.innerHTML = input.value.trim()
        ? '<div class="search-empty">nothing matches</div>'
        : '';
      return;
    }
    var terms = input.value.toLowerCase().split(/\s+/).filter(Boolean);
    results.innerHTML = hits.map(function (h, i) {
      var href = h.page.u + (h.heading ? '#' + h.heading.i : '');
      var where = h.heading ? esc(h.heading.t) : '';
      return '<a class="search-hit' + (i === selected ? ' selected' : '') + '" href="' + href + '" role="option">' +
             '<div class="search-hit-title">' + esc(h.page.t) +
             (where ? '<span class="search-hit-where">' + where + '</span>' : '') +
             '<span class="search-hit-section">' + esc(h.page.s) + '</span></div>' +
             '<div class="search-hit-snippet">' + snippet(h.page.b, h.at, terms) + '</div></a>';
    }).join('');
    var sel = results.querySelector('.selected');
    if (sel) sel.scrollIntoView({ block: 'nearest' });
  }

  window.runSearch = function (q) {
    if (indexState !== 'ready') { loadIndex(); return; }
    var terms = q.toLowerCase().split(/\s+/).filter(Boolean);
    hits = [];
    if (terms.length) {
      index.forEach(function (p) { var s = score(p, terms); if (s) hits.push(s); });
      hits.sort(function (a, b) { return b.score - a.score; });
      hits = hits.slice(0, 20);
    }
    selected = hits.length ? 0 : -1;
    render();
  };

  function searchKey(e) {
    if (e.key === 'ArrowDown') { if (hits.length) selected = (selected + 1) % hits.length; render(); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { if (hits.length) selected = (selected - 1 + hits.length) % hits.length; render(); e.preventDefault(); }
    else if (e.key === 'Enter') {
      var a = results.querySelector('.selected');
      if (a) window.location.href = a.getAttribute('href');
      e.preventDefault();
    }
    else if (e.key === 'Escape') { closeSearch(); e.preventDefault(); }
  }

  if (input) input.addEventListener('keydown', searchKey);
  if (overlay) overlay.addEventListener('click', function (e) { if (e.target === overlay) closeSearch(); });

  // ---- keyboard shortcuts ----
  var help = document.getElementById('kbd-help');
  window.toggleHelp = function () { if (help) help.hidden = !help.hidden; };
  if (help) help.addEventListener('click', function (e) { if (e.target === help) toggleHelp(); });

  function typing(e) {
    var t = e.target;
    return t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable);
  }

  function follow(sel) {
    var a = document.querySelector(sel);
    if (a) window.location.href = a.getAttribute('href');
  }

  document.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'k') {
      openSearch(); e.preventDefault(); return;
    }
    if (e.key === 'Escape') {
      if (overlay && !overlay.hidden) { closeSearch(); return; }
      if (help && !help.hidden) { toggleHelp(); return; }
      var sidebar = document.getElementById('jt-sidebar');
      if (sidebar && sidebar.classList.contains('open')) toggleNav();
      return;
    }
    if (typing(e) || e.ctrlKey || e.metaKey || e.altKey) return;
    switch (e.key) {
      case '/': openSearch(); break;
      case '[': follow('.pn-prev'); break;
      case ']': follow('.pn-next'); break;
      case 'b': toggleNav(); break;
      case 'o': toggleToc(); break;
      case 't': cycleTheme(); break;
      case '-': fontSize(-1); break;
      case '=': case '+': fontSize(1); break;
      case '0': fontSize(0); break;
      case '?': toggleHelp(); break;
      default: return;
    }
    e.preventDefault();
  });

  // ---- scroll active item into view ----
  var active = document.querySelector('.nav-group-items a.active');
  if (active) active.scrollIntoView({ block: 'nearest' });
})();