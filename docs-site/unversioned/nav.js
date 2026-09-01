(function () {
  // ---- group toggle ----
  window.toggleGroup = function (header) {
    var group = header.closest('[data-group]');
    var open  = group.dataset.open === 'true';
    group.dataset.open = open ? 'false' : 'true';
  };

  // ---- sidebar nav (mobile) ----
  window.toggleNav = function () {
    var sidebar  = document.getElementById('jt-sidebar');
    var backdrop = document.getElementById('nav-backdrop');
    if (!sidebar) return;
    var isOpen = sidebar.classList.contains('open');
    sidebar.classList.toggle('open', !isOpen);
    backdrop.classList.toggle('open', !isOpen);
  };

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
    try {
      navigator.clipboard.writeText(text);
      btn.textContent = 'copied!';
      setTimeout(function () { btn.textContent = 'copy'; }, 1800);
    } catch (e) {
      btn.textContent = 'error';
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
    var d = document.documentElement;
    if (t === 'auto') d.removeAttribute('data-theme');
    else d.setAttribute('data-theme', t);
    showTheme(t);
  }

  window.cycleTheme = function () {
    var cur = document.documentElement.getAttribute('data-theme') || 'auto';
    var next = THEMES[(THEMES.indexOf(cur) + 1) % THEMES.length];
    applyTheme(next);
    try {
      if (next === 'auto') localStorage.removeItem('dg-theme');
      else localStorage.setItem('dg-theme', next);
    } catch (e) { /* storage denied; the choice still holds for this page */ }
  };

  // ---- text size ----
  // Bounded at 0.75 and 1.75: below the first the chrome's fixed 46px header
  // stops fitting its own text, above the second the 240px sidebar starts
  // wrapping every label. dir === 0 resets.
  var FS_MIN = 0.75, FS_MAX = 1.75, FS_STEP = 0.125;

  window.fontSize = function (dir) {
    var d = document.documentElement;
    var cur = parseFloat(d.style.getPropertyValue('--fs-scale')) || 1;
    var next = dir === 0 ? 1 : Math.min(FS_MAX, Math.max(FS_MIN, cur + dir * FS_STEP));
    if (next === 1) d.style.removeProperty('--fs-scale');
    else d.style.setProperty('--fs-scale', String(next));
    try {
      if (next === 1) localStorage.removeItem('dg-fs');
      else localStorage.setItem('dg-fs', String(next));
    } catch (e) { /* as above */ }
  };

  // The head script has already applied the saved theme before first paint;
  // this only brings the button's label into line with it.
  showTheme(document.documentElement.getAttribute('data-theme') || 'auto');

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

  // ---- scroll active item into view ----
  var active = document.querySelector('.nav-group-items a.active');
  if (active) active.scrollIntoView({ block: 'nearest' });
})();