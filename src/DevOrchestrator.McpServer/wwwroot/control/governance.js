(() => {
  'use strict';

  const $ = id => document.getElementById(id);
  const esc = value => String(value ?? '')
    .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&#039;');
  const fmt = value => value ? new Date(value).toLocaleString() : '—';
  const state = { user: null, view: 'users' };

  async function api(path, options = {}) {
    const headers = new Headers(options.headers || {});
    if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    const response = await fetch(path, { ...options, headers, credentials: 'same-origin' });
    const type = response.headers.get('content-type') || '';
    const body = type.includes('application/json') ? await response.json() : await response.text();
    if (!response.ok) throw new Error(body?.message || body?.error || `Request failed (${response.status})`);
    return body;
  }

  function showError(message) {
    $('governance-error').textContent = message;
    $('governance-error').classList.remove('hidden');
  }

  function clearError() { $('governance-error').classList.add('hidden'); }

  function showSecret(secret, warning) {
    const box = $('governance-secret');
    box.innerHTML = `<strong>Copy this credential now</strong><br><code>${esc(secret)}</code><br><small>${esc(warning)}</small>`;
    box.classList.remove('hidden');
  }

  async function initialize() {
    const auth = await api('/auth/status');
    if (!auth.authenticated) {
      location.href = '/auth/login?returnUrl=/control/governance.html';
      return;
    }
    state.user = auth.user;
    $('current-user').textContent = `${auth.user?.login || auth.user?.name} · ${(auth.user?.roles || []).join(', ')}`;
    if (!(auth.user?.roles || []).includes('Admin')) {
      showError('Admin role is required for Identity & Governance.');
      return;
    }
    await loadUsers();
  }

  async function loadUsers() {
    clearError();
    const users = await api('/control/api/users');
    $('users-table').innerHTML = users.map(user => `<tr>
      <td><strong>${esc(user.displayName)}</strong><small>${esc(user.login)}${user.email ? ` · ${esc(user.email)}` : ''}</small></td>
      <td>${esc(user.provider)}<small>${esc(user.subject)}</small></td>
      <td>${(user.roles || []).map(role => `<span class="pill">${esc(role)}</span>`).join(' ') || '—'}</td>
      <td>${esc(fmt(user.lastLoginAtUtc))}</td>
      <td>${user.isEnabled ? '<span class="status active">active</span>' : '<span class="status paused">disabled</span>'}</td>
      <td><button class="ghost compact" data-user-roles="${esc(user.id)}" data-current-roles="${esc((user.roles || []).join(','))}">Roles</button> <button class="ghost compact" data-user-enabled="${esc(user.id)}" data-enabled="${user.isEnabled}">${user.isEnabled ? 'Disable' : 'Enable'}</button></td>
    </tr>`).join('') || '<tr><td colspan="6"><div class="empty">No human identities have signed in yet.</div></td></tr>';
  }

  async function loadCredentials() {
    clearError();
    const credentials = await api('/control/api/machine-credentials');
    $('credentials-table').innerHTML = credentials.map(item => `<tr>
      <td><strong>${esc(item.name)}</strong><small>${esc(item.id)}</small></td>
      <td>${esc(item.role)}</td>
      <td><code>${esc(item.keyPrefix)}…</code></td>
      <td>${esc(fmt(item.expiresAtUtc))}</td>
      <td>${esc(fmt(item.lastUsedAtUtc))}</td>
      <td>${item.usable ? '<span class="status active">usable</span>' : '<span class="status paused">inactive</span>'}</td>
      <td>${item.isActive ? `<button class="ghost compact" data-credential-rotate="${esc(item.id)}">Rotate</button> <button class="ghost compact" data-credential-revoke="${esc(item.id)}">Revoke</button>` : ''}</td>
    </tr>`).join('') || '<tr><td colspan="7"><div class="empty">No database-managed machine credentials.</div></td></tr>';
  }

  async function loadAudit() {
    clearError();
    const events = await api('/control/api/security-audit?limit=200');
    $('security-audit-list').innerHTML = events.map(event => `<article class="event">
      <strong>${esc(event.action)}</strong>
      <div class="meta">${esc(event.actor)} · ${esc(event.actorType)} · ${esc(event.resourceType)}/${esc(event.resourceId)} · ${esc(fmt(event.createdAtUtc))}</div>
      ${event.reason ? `<div>${esc(event.reason)}</div>` : ''}
    </article>`).join('') || '<div class="empty">No security audit events yet.</div>';
  }

  async function switchView(view) {
    state.view = view;
    document.querySelectorAll('[data-governance-view]').forEach(el => el.classList.toggle('active', el.dataset.governanceView === view));
    document.querySelectorAll('section[id^="governance-"]').forEach(el => el.classList.toggle('active', el.id === `governance-${view}`));
    $('governance-title').textContent = view === 'users' ? 'Users' : view === 'credentials' ? 'Credentials' : 'Security Audit';
    if (view === 'users') await loadUsers();
    if (view === 'credentials') await loadCredentials();
    if (view === 'security-audit') await loadAudit();
  }

  document.addEventListener('click', async event => {
    const target = event.target.closest('button');
    if (!target) return;
    try {
      if (target.dataset.governanceView) await switchView(target.dataset.governanceView);
      if (target.dataset.userRoles) {
        const entered = prompt('Roles (Admin, Architect, Auditor, Implementer), comma separated:', target.dataset.currentRoles || '');
        if (entered == null) return;
        const roles = entered.split(',').map(x => x.trim()).filter(Boolean);
        const reason = prompt('Reason for role change:', 'administrative role update') || 'administrative role update';
        await api(`/control/api/users/${encodeURIComponent(target.dataset.userRoles)}/roles`, { method: 'POST', body: JSON.stringify({ roles, reason }) });
        await loadUsers();
      }
      if (target.dataset.userEnabled) {
        const enabled = target.dataset.enabled !== 'true';
        const reason = prompt(`Reason to ${enabled ? 'enable' : 'disable'} this user:`, 'administrative access change') || 'administrative access change';
        await api(`/control/api/users/${encodeURIComponent(target.dataset.userEnabled)}/enabled`, { method: 'POST', body: JSON.stringify({ enabled, reason }) });
        await loadUsers();
      }
      if (target.dataset.credentialRevoke) {
        if (!confirm('Revoke this machine credential immediately?')) return;
        const reason = prompt('Reason for revocation:', 'credential revoked by administrator') || 'credential revoked by administrator';
        await api(`/control/api/machine-credentials/${encodeURIComponent(target.dataset.credentialRevoke)}/revoke`, { method: 'POST', body: JSON.stringify({ reason }) });
        await loadCredentials();
      }
      if (target.dataset.credentialRotate) {
        const days = Number(prompt('New credential lifetime in days (1-365):', '90'));
        if (!Number.isFinite(days)) return;
        const result = await api(`/control/api/machine-credentials/${encodeURIComponent(target.dataset.credentialRotate)}/rotate`, { method: 'POST', body: JSON.stringify({ expiresInDays: days, reason: 'credential rotation' }) });
        showSecret(result.secret, result.warning);
        await loadCredentials();
      }
    } catch (error) { showError(error.message); }
  });

  $('credential-form').addEventListener('submit', async event => {
    event.preventDefault();
    try {
      const result = await api('/control/api/machine-credentials', {
        method: 'POST',
        body: JSON.stringify({
          name: $('credential-name').value.trim(),
          role: $('credential-role').value,
          expiresInDays: Number($('credential-expiry').value)
        })
      });
      showSecret(result.secret, result.warning);
      $('credential-name').value = '';
      await loadCredentials();
    } catch (error) { showError(error.message); }
  });

  $('audit-refresh').addEventListener('click', () => loadAudit().catch(error => showError(error.message)));
  initialize().catch(error => showError(error.message));
})();
