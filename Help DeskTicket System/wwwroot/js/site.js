(() => {
  const t = k => (window.i18n && window.i18n[k]) || k;
  // ---- dark mode
  const toggle = document.getElementById('themeToggle');
  const setIcon = () => { if (toggle) toggle.innerHTML = document.documentElement.dataset.bsTheme === 'dark' ? '<i class="bi bi-sun"></i>' : '<i class="bi bi-moon-stars"></i>'; };
  setIcon();
  toggle?.addEventListener('click', () => {
    const next = document.documentElement.dataset.bsTheme === 'dark' ? 'light' : 'dark';
    document.documentElement.dataset.bsTheme = next;
    try { localStorage.setItem('theme', next); } catch { }
    setIcon();
  });

  // ---- confirmation prompts (data-confirm on buttons or forms; text is already translated)
  document.addEventListener("click", e => {
    const el = e.target.closest("button[data-confirm], a[data-confirm]");
    if (el && !confirm(el.dataset.confirm)) { e.preventDefault(); e.stopImmediatePropagation(); }
  }, true);
  document.addEventListener("submit", e => {
    const f = e.target;
    if (f.dataset && f.dataset.confirm && !e.submitter?.dataset.confirm && !confirm(f.dataset.confirm)) e.preventDefault();
  }, true);

  // ---- "/" focuses search
  document.addEventListener('keydown', e => {
    if (e.key === '/' && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement?.tagName)) {
      const s = document.getElementById('globalSearch');
      if (s) { e.preventDefault(); s.focus(); }
    }
  });

  // ---- clickable ticket rows
  document.querySelectorAll('tr[data-href]').forEach(tr => tr.addEventListener('click', e => {
    if (e.target.closest('a,button,input,label,select')) return;
    location.href = tr.dataset.href;
  }));

  // ---- notifications
  const bell = document.getElementById('bell');
  if (!bell) return;
  const countEl = document.getElementById('bellCount');
  const menu = document.getElementById('bellMenu');
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  async function refresh() {
    try {
      const r = await fetch('/Notifications?handler=Recent', { headers: { 'Accept': 'application/json' } });
      if (!r.ok) return;
      const data = await r.json();
      countEl.textContent = data.unread > 99 ? '99+' : data.unread;
      countEl.classList.toggle('d-none', data.unread === 0);
      menu.innerHTML = (data.items.length ? data.items.map(n =>
        `<a class="item ${n.isRead ? '' : 'unread'}" href="/Notifications?handler=Open&id=${n.id}">${esc(n.message)}<div class="text-muted small">${esc(n.ago)}</div></a>`).join('')
        : `<div class="p-3 text-muted small">${esc(t('No notifications yet.'))}</div>`)
        + '<a class="d-block text-center p-2 small" href="/Notifications">' + esc(t('View all')) + '</a>';
    } catch { }
  }
  refresh();

  function toast(msg, link) {
    const el = document.createElement('div');
    el.className = 'toast align-items-center text-bg-primary border-0';
    el.innerHTML = `<div class="d-flex"><div class="toast-body">${link ? `<a class="text-white" href="${esc(link)}">${esc(msg)}</a>` : esc(msg)}</div><button class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button></div>`;
    document.getElementById('toasts').appendChild(el);
    new bootstrap.Toast(el, { delay: 6000 }).show();
    el.addEventListener('hidden.bs.toast', () => el.remove());
  }

  if (window.signalR) {
    const conn = new signalR.HubConnectionBuilder().withUrl('/hubs/notifications').withAutomaticReconnect().build();
    conn.on('notify', n => { toast(n.message, n.link); refresh(); });
    conn.start().catch(() => setInterval(refresh, 30000)); // fall back to polling
  } else {
    setInterval(refresh, 30000);
  }
})();
