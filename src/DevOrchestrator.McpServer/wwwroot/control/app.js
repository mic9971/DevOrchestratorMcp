(() => {
  'use strict';

  const titles = { overview: 'Overview', projects: 'Projects', tasks: 'Tasks', workers: 'Workers', webhooks: 'Webhooks', audit: 'Audit' };
  const state = {
    key: sessionStorage.getItem('devorchestrator.auditorKey') || '',
    view: 'overview',
    projects: [],
    dashboard: null,
    taskOffset: 0,
    taskNextOffset: null,
    webhookOffset: 0,
    webhookNextOffset: null,
    auditOffset: 0,
    auditNextOffset: null
  };

  const $ = (id) => document.getElementById(id);
  const esc = (value) => String(value ?? '')
    .replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;').replaceAll("'", '&#039;');
  const safeUrl = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' || url.protocol === 'http:' ? url.href : '#';
    } catch { return '#'; }
  };
  const fmt = (value) => value ? new Date(value).toLocaleString() : '—';
  const relative = (value) => {
    if (!value) return '—';
    const seconds = Math.round((new Date(value).getTime() - Date.now()) / 1000);
    const abs = Math.abs(seconds);
    if (abs < 60) return seconds >= 0 ? `in ${abs}s` : `${abs}s ago`;
    const minutes = Math.round(abs / 60);
    if (minutes < 60) return seconds >= 0 ? `in ${minutes}m` : `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    return seconds >= 0 ? `in ${hours}h` : `${hours}h ago`;
  };
  const statusBadge = (value) => `<span class="status ${esc(value)}">${esc(value)}</span>`;
  const totalTasks = (tasks) => Object.values(tasks || {}).reduce((sum, value) => sum + Number(value || 0), 0);
  const jsonPreview = (value) => {
    if (!value || value === '{}') return '';
    try { return JSON.stringify(JSON.parse(value), null, 2); } catch { return String(value); }
  };

  async function api(path, options = {}) {
    const headers = new Headers(options.headers || {});
    if (state.key) headers.set('X-DevOrchestrator-Key', state.key);
    if (options.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
    const response = await fetch(path, { ...options, headers });
    if (response.status === 401) {
      disconnect(false);
      throw new Error('Auditor key was rejected. Reconnect with a valid Auditor credential.');
    }
    if (response.status === 403) throw new Error('This credential is valid but does not have the Auditor role.');
    const type = response.headers.get('content-type') || '';
    const payload = type.includes('application/json') ? await response.json() : await response.text();
    if (!response.ok) throw new Error(payload?.message || payload?.error || `Request failed (${response.status})`);
    return payload;
  }

  function setConnected(connected) {
    $('auth-screen').classList.toggle('hidden', connected);
    $('disconnect-button').classList.toggle('hidden', !connected);
    $('health-dot').classList.toggle('live', connected);
    $('health-label').textContent = connected ? 'Connected' : 'Disconnected';
    $('health-detail').textContent = connected ? 'Auditor control session' : 'Auditor session required';
  }

  function disconnect(showAuth = true) {
    state.key = '';
    sessionStorage.removeItem('devorchestrator.auditorKey');
    setConnected(false);
    if (showAuth) $('auditor-key').focus();
  }

  function showError(message) {
    const el = $('global-error');
    el.textContent = message;
    el.classList.remove('hidden');
  }

  function clearError() { $('global-error').classList.add('hidden'); }
  function markRefresh() { $('last-refresh').textContent = `Updated ${new Date().toLocaleTimeString()}`; }

  async function bootstrap() {
    clearError();
    const [dashboard, projects] = await Promise.all([api('/control/api/dashboard'), api('/control/api/projects')]);
    state.dashboard = dashboard;
    state.projects = projects;
    renderProjectFilters();
    setConnected(true);
    await loadView(state.view);
  }

  function renderProjectFilters() {
    for (const id of ['task-project-filter', 'audit-project-filter']) {
      const select = $(id);
      const current = select.value;
      const first = id === 'task-project-filter' ? 'All projects' : 'All projects';
      select.innerHTML = `<option value="">${first}</option>` + state.projects.map(p => `<option value="${esc(p.key)}">${esc(p.key)} · ${esc(p.name)}</option>`).join('');
      if ([...select.options].some(o => o.value === current)) select.value = current;
    }
  }

  async function switchView(view) {
    if (!titles[view]) return;
    state.view = view;
    document.querySelectorAll('.view').forEach(el => el.classList.toggle('active', el.id === `view-${view}`));
    document.querySelectorAll('.nav-item').forEach(el => el.classList.toggle('active', el.dataset.view === view));
    $('page-title').textContent = titles[view];
    clearError();
    await loadView(view);
  }

  async function loadView(view) {
    try {
      if (view === 'overview') await loadOverview();
      if (view === 'projects') await loadProjects();
      if (view === 'tasks') await loadTasks();
      if (view === 'workers') await loadWorkers();
      if (view === 'webhooks') await loadWebhooks();
      if (view === 'audit') await loadAudit();
      markRefresh();
    } catch (error) { showError(error.message); }
  }

  async function loadOverview() {
    const [dashboard, workers, projects] = await Promise.all([
      api('/control/api/dashboard'), api('/control/api/workers'), api('/control/api/projects')
    ]);
    state.dashboard = dashboard;
    state.projects = projects;
    renderProjectFilters();
    const tasks = dashboard.tasks || {};
    const webhookAttention = Number(dashboard.webhooks?.retrying || 0) + Number(dashboard.webhooks?.['dead-lettered'] || 0);
    const attention = Number(tasks.Blocked || 0) + Number(tasks.ChangesRequested || 0) + Number(dashboard.leases?.expired || 0) + webhookAttention;
    $('overview-cards').innerHTML = [
      ['Active projects', dashboard.projects?.active || 0, `${dashboard.projects?.paused || 0} paused`, 'good'],
      ['Tasks tracked', totalTasks(tasks), `${tasks.Done || 0} done`, ''],
      ['Active workers', dashboard.leases?.workers || 0, `${dashboard.leases?.active || 0} live leases`, 'good'],
      ['Needs attention', attention, `${dashboard.webhooks?.['dead-lettered'] || 0} dead-lettered · ${dashboard.webhooks?.retrying || 0} retrying`, attention ? 'warning' : 'good']
    ].map(([label, value, detail, tone]) => `<article class="metric ${tone}"><span class="label">${esc(label)}</span><span class="value">${esc(value)}</span><span class="detail">${esc(detail)}</span></article>`).join('');

    const pipelineOrder = ['Draft','Ready','InProgress','ReadyForReview','ChangesRequested','Blocked','Done'];
    const max = Math.max(1, ...pipelineOrder.map(name => Number(tasks[name] || 0)));
    $('pipeline').innerHTML = pipelineOrder.map(name => {
      const count = Number(tasks[name] || 0);
      return `<div class="pipeline-row"><span class="name">${esc(name)}</span><div class="bar-track"><div class="bar" style="width:${Math.max(count ? 4 : 0, Math.round(count / max * 100))}%"></div></div><span class="count">${count}</span></div>`;
    }).join('');

    $('overview-workers').innerHTML = workers.length
      ? workers.slice(0, 6).map(worker => `<div class="list-item"><div><strong>${esc(worker.workerId)}</strong><small>${esc(worker.projectKey)} / ${esc(worker.taskCode)} · heartbeat ${esc(relative(worker.lastHeartbeatAtUtc))}</small></div><span class="lease-dot ${worker.leaseState === 'expired' ? 'expired' : ''}"></span></div>`).join('')
      : '<div class="empty">No claimed tasks right now.</div>';

    $('overview-projects').innerHTML = projects.length
      ? projects.slice(0, 6).map(project => `<div class="project-card"><strong>${esc(project.name)}</strong><div class="repo">${esc(project.key)} · ${esc(project.defaultBranch)}</div><div class="mini-stats"><span><b>${totalTasks(project.tasks)}</b> tasks</span><span><b>${project.tasks?.InProgress || 0}</b> active</span><span>${project.isActive ? '● active' : '○ paused'}</span></div></div>`).join('')
      : '<div class="empty">No projects registered.</div>';
  }

  async function loadProjects() {
    state.projects = await api('/control/api/projects');
    renderProjectFilters();
    $('project-count').textContent = `${state.projects.length} registered`;
    $('projects-table').innerHTML = state.projects.map(project => {
      const repo = safeUrl(project.repositoryUrl);
      const activeWork = Number(project.tasks?.InProgress || 0) + Number(project.tasks?.ReadyForReview || 0);
      return `<tr><td><strong>${esc(project.name)}</strong><small>${esc(project.key)} · default ${esc(project.defaultBranch)}</small></td><td><a class="inline-link" href="${esc(repo)}" target="_blank" rel="noopener noreferrer">${esc(project.repositoryUrl)}</a></td><td><strong>${totalTasks(project.tasks)}</strong><small>${project.tasks?.Done || 0} done · ${project.tasks?.Blocked || 0} blocked</small></td><td>${activeWork}</td><td>${statusBadge(project.isActive ? 'active' : 'paused')}</td><td><button class="ghost compact" data-project-action="${project.isActive ? 'pause' : 'resume'}" data-project="${esc(project.key)}">${project.isActive ? 'Pause' : 'Resume'}</button></td></tr>`;
    }).join('') || '<tr><td colspan="6"><div class="empty">No projects registered.</div></td></tr>';
  }

  async function loadTasks() {
    const project = $('task-project-filter').value;
    const status = $('task-status-filter').value;
    const params = new URLSearchParams({ offset: String(state.taskOffset), limit: '50' });
    if (project) params.set('projectKey', project);
    if (status) params.set('status', status);
    const page = await api(`/control/api/tasks?${params}`);
    state.taskNextOffset = page.nextOffset;
    $('tasks-prev').disabled = state.taskOffset === 0;
    $('tasks-next').disabled = page.nextOffset == null;
    $('tasks-page').textContent = `${state.taskOffset + 1}–${state.taskOffset + page.items.length}`;
    $('tasks-table').innerHTML = page.items.map(task => {
      const lease = task.leaseOwner ? `${esc(task.leaseOwner)} · ${esc(relative(task.leaseExpiresAtUtc))}` : '—';
      return `<tr><td><strong>${esc(task.code)}</strong><small>${esc(task.title)}</small></td><td>${esc(task.projectKey)}<small>${esc(task.projectName)}</small></td><td>${statusBadge(task.status)}</td><td>${esc(task.priority)}</td><td>${lease}<small>${esc(task.activeBranch || '')}</small></td><td>${esc(relative(task.updatedAtUtc))}<small>rev ${esc(task.revision)}</small></td><td><button class="ghost compact" data-task-detail="${esc(task.projectKey)}|${esc(task.code)}">Inspect</button></td></tr>`;
    }).join('') || '<tr><td colspan="7"><div class="empty">No tasks match this filter.</div></td></tr>';
  }

  async function loadWorkers() {
    const workers = await api('/control/api/workers');
    $('worker-count').textContent = `${workers.length} claimed`;
    $('workers-empty').classList.toggle('hidden', workers.length !== 0);
    $('workers-table').innerHTML = workers.map(worker => `<tr><td><strong>${esc(worker.workerId)}</strong><small>${statusBadge(worker.leaseState)}</small></td><td><strong>${esc(worker.projectKey)} / ${esc(worker.taskCode)}</strong><small>${esc(worker.taskTitle)}</small></td><td>${esc(worker.activeBranch || '—')}</td><td>${esc(relative(worker.lastHeartbeatAtUtc))}<small>${esc(fmt(worker.lastHeartbeatAtUtc))}</small></td><td>${esc(relative(worker.leaseExpiresAtUtc))}<small>${esc(fmt(worker.leaseExpiresAtUtc))}</small></td><td>${worker.leaseState === 'expired' ? `<button class="ghost compact" data-expire="${esc(worker.projectKey)}|${esc(worker.taskCode)}">Release</button>` : `<button class="ghost compact" data-task-detail="${esc(worker.projectKey)}|${esc(worker.taskCode)}">Inspect</button>`}</td></tr>`).join('');
  }

  async function loadWebhooks() {
    const filter = $('webhook-state-filter').value;
    const params = new URLSearchParams({ state: filter, offset: String(state.webhookOffset), limit: '50' });
    const page = await api(`/control/api/webhooks?${params}`);
    state.webhookNextOffset = page.nextOffset;
    $('webhooks-prev').disabled = state.webhookOffset === 0;
    $('webhooks-next').disabled = page.nextOffset == null;
    $('webhooks-page').textContent = `${state.webhookOffset + 1}–${state.webhookOffset + page.items.length}`;
    $('webhooks-table').innerHTML = page.items.map(item => {
      const itemState = item.deadLetteredAtUtc ? 'dead-lettered' : item.completedAtUtc ? 'completed' : item.attemptCount > 1 ? 'retrying' : 'pending';
      return `<tr><td><strong>${esc(item.deliveryId)}</strong><small>${esc(item.repositoryUrl)}</small></td><td>${esc(item.eventName)}<small>${esc(item.action)}</small></td><td>#${esc(item.issueNumber)}</td><td>${esc(item.attemptCount)}</td><td>${esc(relative(item.receivedAtUtc))}<small>${esc(item.lastError || '')}</small></td><td>${statusBadge(itemState)}</td><td>${itemState !== 'pending' ? `<button class="ghost compact" data-replay="${esc(item.deliveryId)}">Replay</button>` : ''}</td></tr>`;
    }).join('') || '<tr><td colspan="7"><div class="empty">No webhook deliveries in this state.</div></td></tr>';
  }

  async function loadAudit() {
    const params = new URLSearchParams({ offset: String(state.auditOffset), limit: '50' });
    const project = $('audit-project-filter').value;
    const taskCode = $('audit-task-filter').value.trim();
    if (project) params.set('projectKey', project);
    if (taskCode) params.set('taskCode', taskCode);
    const page = await api(`/control/api/audit?${params}`);
    state.auditNextOffset = page.nextOffset;
    $('audit-prev').disabled = state.auditOffset === 0;
    $('audit-next').disabled = page.nextOffset == null;
    $('audit-page').textContent = `${state.auditOffset + 1}–${state.auditOffset + page.items.length}`;
    $('audit-list').innerHTML = page.items.map(item => {
      const payload = jsonPreview(item.payloadJson);
      return `<article class="event"><strong>${esc(item.eventType)}</strong><div class="meta">${esc(item.projectKey)} / ${esc(item.taskCode)} · ${esc(item.actor)} · ${esc(fmt(item.createdAtUtc))}</div>${payload ? `<code>${esc(payload)}</code>` : ''}</article>`;
    }).join('') || '<div class="empty">No audit events match this filter.</div>';
  }

  async function openTask(projectKey, taskCode) {
    const detail = await api(`/control/api/tasks/${encodeURIComponent(projectKey)}/${encodeURIComponent(taskCode)}`);
    const task = detail.task;
    $('drawer-title').textContent = `${task.code} · ${task.title}`;
    const repo = safeUrl(task.repositoryUrl);
    const pr = safeUrl(task.pullRequestUrl);
    const criteria = detail.criteria.map(c => `<div class="criterion ${c.isSatisfied ? 'ok' : ''}">${esc(c.description)}</div>`).join('') || '<div class="empty">No acceptance criteria.</div>';
    const dependencies = detail.dependencies.map(d => `<div class="criterion ${d.status === 'Done' ? 'ok' : ''}"><strong>${esc(d.code)}</strong> · ${esc(d.title)} · ${esc(d.status)}</div>`).join('') || '<div class="muted">No dependencies.</div>';
    const evidence = detail.evidence.map(e => `<div class="evidence"><strong>${esc(e.commitSha)}</strong> · ${esc(e.branch)}<br><span class="muted">${esc(e.actor)} · ${esc(fmt(e.createdAtUtc))}</span>${e.pullRequestUrl ? `<br><a class="inline-link" href="${esc(safeUrl(e.pullRequestUrl))}" target="_blank" rel="noopener noreferrer">Pull request</a>` : ''}</div>`).join('') || '<div class="muted">No evidence yet.</div>';
    const reviews = detail.reviews.map(r => `<div class="review"><strong>${esc(r.decision)}</strong> · ${esc(r.summary)}<br><span class="muted">${esc(r.actor)} · ${esc(fmt(r.createdAtUtc))}</span></div>`).join('') || '<div class="muted">No reviews yet.</div>';
    const events = detail.events.map(e => `<article class="event"><strong>${esc(e.eventType)}</strong><div class="meta">${esc(e.actor)} · ${esc(fmt(e.createdAtUtc))}</div></article>`).join('');
    $('drawer-body').innerHTML = `
      <div class="detail-grid">
        <div class="detail-box"><span>Status</span><strong>${statusBadge(task.status)}</strong></div>
        <div class="detail-box"><span>Priority</span><strong>${esc(task.priority)}</strong></div>
        <div class="detail-box"><span>Project</span><strong>${esc(task.projectKey)}</strong></div>
        <div class="detail-box"><span>Revision</span><strong>${esc(task.revision)}</strong></div>
        <div class="detail-box"><span>Worker</span><strong>${esc(task.leaseOwner || '—')}</strong></div>
        <div class="detail-box"><span>Lease</span><strong>${esc(relative(task.leaseExpiresAtUtc))}</strong></div>
      </div>
      <div class="detail-section"><h3>Objective</h3><div class="review">${esc(task.objective)}</div></div>
      ${task.constraints ? `<div class="detail-section"><h3>Constraints</h3><div class="review">${esc(task.constraints)}</div></div>` : ''}
      <div class="detail-section"><h3>Source</h3><div class="review"><a class="inline-link" href="${esc(repo)}" target="_blank" rel="noopener noreferrer">Repository</a>${pr !== '#' ? ` · <a class="inline-link" href="${esc(pr)}" target="_blank" rel="noopener noreferrer">Pull request</a>` : ''}<br><span class="muted">${esc(task.activeBranch || 'no branch')} · ${esc(task.lastCommitSha || 'no commit')}</span></div></div>
      <div class="detail-section"><h3>Acceptance criteria</h3>${criteria}</div>
      <div class="detail-section"><h3>Dependencies</h3>${dependencies}</div>
      <div class="detail-section"><h3>Evidence</h3>${evidence}</div>
      <div class="detail-section"><h3>Reviews</h3>${reviews}</div>
      <div class="detail-section"><h3>Recent events</h3><div class="timeline">${events || '<div class="empty">No events.</div>'}</div></div>`;
    $('task-drawer').classList.add('open');
    $('task-drawer').setAttribute('aria-hidden', 'false');
    $('drawer-scrim').classList.remove('hidden');
  }

  function closeDrawer() {
    $('task-drawer').classList.remove('open');
    $('task-drawer').setAttribute('aria-hidden', 'true');
    $('drawer-scrim').classList.add('hidden');
  }

  async function mutation(path, message) {
    if (!window.confirm(message)) return;
    try {
      await api(path, { method: 'POST' });
      await loadView(state.view);
    } catch (error) { showError(error.message); }
  }

  $('auth-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    const key = $('auditor-key').value.trim();
    const error = $('auth-error');
    error.classList.add('hidden');
    state.key = key;
    try {
      await api('/control/api/dashboard');
      sessionStorage.setItem('devorchestrator.auditorKey', key);
      $('auditor-key').value = '';
      await bootstrap();
    } catch (ex) {
      state.key = '';
      error.textContent = ex.message;
      error.classList.remove('hidden');
    }
  });

  $('disconnect-button').addEventListener('click', () => disconnect(true));
  $('refresh-button').addEventListener('click', () => loadView(state.view));
  $('drawer-close').addEventListener('click', closeDrawer);
  $('drawer-scrim').addEventListener('click', closeDrawer);

  document.addEventListener('click', async (event) => {
    const target = event.target.closest('button');
    if (!target) return;
    if (target.dataset.view) await switchView(target.dataset.view);
    if (target.dataset.jump) await switchView(target.dataset.jump);
    if (target.dataset.taskDetail) {
      const [project, task] = target.dataset.taskDetail.split('|');
      try { await openTask(project, task); } catch (error) { showError(error.message); }
    }
    if (target.dataset.projectAction) {
      await mutation(`/ops/projects/${encodeURIComponent(target.dataset.project)}/${target.dataset.projectAction}`, `${target.dataset.projectAction} project ${target.dataset.project}?`);
    }
    if (target.dataset.expire) {
      const [project, task] = target.dataset.expire.split('|');
      await mutation(`/ops/tasks/${encodeURIComponent(project)}/${encodeURIComponent(task)}/expire-lease`, `Expire the lease for ${project}/${task} so another worker can reclaim it?`);
    }
    if (target.dataset.replay) await mutation(`/ops/webhooks/${encodeURIComponent(target.dataset.replay)}/replay`, `Replay webhook delivery ${target.dataset.replay}?`);
  });

  $('task-project-filter').addEventListener('change', () => { state.taskOffset = 0; loadTasks().catch(e => showError(e.message)); });
  $('task-status-filter').addEventListener('change', () => { state.taskOffset = 0; loadTasks().catch(e => showError(e.message)); });
  $('tasks-prev').addEventListener('click', () => { state.taskOffset = Math.max(0, state.taskOffset - 50); loadTasks().catch(e => showError(e.message)); });
  $('tasks-next').addEventListener('click', () => { if (state.taskNextOffset != null) state.taskOffset = state.taskNextOffset; loadTasks().catch(e => showError(e.message)); });
  $('webhook-state-filter').addEventListener('change', () => { state.webhookOffset = 0; loadWebhooks().catch(e => showError(e.message)); });
  $('webhooks-prev').addEventListener('click', () => { state.webhookOffset = Math.max(0, state.webhookOffset - 50); loadWebhooks().catch(e => showError(e.message)); });
  $('webhooks-next').addEventListener('click', () => { if (state.webhookNextOffset != null) state.webhookOffset = state.webhookNextOffset; loadWebhooks().catch(e => showError(e.message)); });
  $('audit-project-filter').addEventListener('change', () => { state.auditOffset = 0; loadAudit().catch(e => showError(e.message)); });
  $('audit-task-filter').addEventListener('change', () => { state.auditOffset = 0; loadAudit().catch(e => showError(e.message)); });
  $('audit-prev').addEventListener('click', () => { state.auditOffset = Math.max(0, state.auditOffset - 50); loadAudit().catch(e => showError(e.message)); });
  $('audit-next').addEventListener('click', () => { if (state.auditNextOffset != null) state.auditOffset = state.auditNextOffset; loadAudit().catch(e => showError(e.message)); });

  setConnected(false);
  if (state.key) bootstrap().catch(error => { disconnect(false); $('auth-error').textContent = error.message; $('auth-error').classList.remove('hidden'); });
})();