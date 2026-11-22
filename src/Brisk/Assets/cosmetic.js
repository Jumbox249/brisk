// Cosmetic filtering: hides elements that EasyList marks as ads (the empty boxes left behind
// after the network blocker stops the ad itself). Runs in the top frame of every page.
(() => {
  if (window.top !== window || !window.chrome || !window.chrome.webview) return;
  const wv = window.chrome.webview;

  const sheet = new CSSStyleSheet();
  const attach = () => {
    if (!document.adoptedStyleSheets.includes(sheet)) {
      document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
    }
  };
  attach();

  const hidden = new Set();
  const hide = (selectors) => {
    for (const sel of selectors) {
      if (hidden.has(sel)) continue;
      hidden.add(sel);
      try { sheet.insertRule(`${sel}{display:none!important}`, sheet.cssRules.length); } catch { /* invalid selector */ }
    }
    attach();
  };

  let exceptions = [];
  let generic = false;
  const seenClasses = new Set();
  const seenIds = new Set();
  let newClasses = [];
  let newIds = [];
  let timer = 0;

  const collect = (el) => {
    if (el.id && !seenIds.has(el.id)) { seenIds.add(el.id); newIds.push(el.id); }
    for (const c of el.classList || []) {
      if (!seenClasses.has(c)) { seenClasses.add(c); newClasses.push(c); }
    }
  };

  const flush = () => {
    timer = 0;
    if (!newClasses.length && !newIds.length) return;
    wv.postMessage({ type: 'brisk-classes', classes: newClasses, ids: newIds, exceptions });
    newClasses = [];
    newIds = [];
  };
  const schedule = () => { if (!timer) timer = setTimeout(flush, 100); };

  const scan = (root) => {
    if (root.nodeType !== 1) return;
    collect(root);
    for (const el of root.querySelectorAll('[id],[class]')) collect(el);
    schedule();
  };

  const startGeneric = () => {
    if (document.documentElement) scan(document.documentElement);
    new MutationObserver((records) => {
      for (const r of records) {
        if (r.type === 'attributes') collect(r.target);
        else for (const n of r.addedNodes) scan(n);
      }
      schedule();
    }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'id'] });
  };

  wv.addEventListener('message', (e) => {
    const m = e.data;
    if (!m || typeof m !== 'object') return;
    if (m.type === 'brisk-init') {
      const r = m.r || {};
      exceptions = r.exceptions || [];
      hide(r.hide_selectors || []);
      if (!r.generichide && !generic) {
        generic = true;
        if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', startGeneric, { once: true });
        else startGeneric();
      }
    } else if (m.type === 'brisk-hide') {
      hide(m.selectors || []);
    }
  });

  wv.postMessage({ type: 'brisk-init', url: location.href });
})();
