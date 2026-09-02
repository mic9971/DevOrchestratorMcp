(() => {
  'use strict';

  async function status() {
    try {
      const response = await fetch('/auth/status', { credentials: 'same-origin' });
      if (!response.ok) return;
      const data = await response.json();
      const github = document.getElementById('github-login');
      const identityNote = document.getElementById('identity-note');
      if (github) github.classList.toggle('hidden', !data.githubConfigured);
      if (identityNote) {
        identityNote.textContent = data.authenticated
          ? `Signed in as ${data.user?.login || data.user?.name || 'user'} · ${(data.user?.roles || []).join(', ') || 'no role assigned'}`
          : data.githubConfigured ? 'Use GitHub sign-in for human access, or the Auditor key as break-glass access.' : 'GitHub sign-in is not configured; Auditor API key remains available as break-glass access.';
      }

      if (data.authenticated && !sessionStorage.getItem('devorchestrator.auditorKey')) {
        sessionStorage.setItem('devorchestrator.auditorKey', 'human-session');
        location.reload();
      }
    } catch { /* status is a progressive enhancement */ }
  }

  const disconnect = document.getElementById('disconnect-button');
  if (disconnect) {
    disconnect.addEventListener('click', async () => {
      try { await fetch('/auth/logout', { method: 'POST', credentials: 'same-origin' }); } catch { }
      sessionStorage.removeItem('devorchestrator.auditorKey');
    });
  }

  status();
})();
