(() => {
  const api = '/api/guidance';
  const message = (node, text, error = false) => { if (node) { node.textContent = text; node.style.color = error ? '#b91c1c' : '#15803d'; } };
  const escape = value => String(value ?? '').replace(/[&<>'"]/g, char => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', "'":'&#39;', '"':'&quot;' })[char]);
  const request = async (path, options = {}) => {
    const response = await fetch(`${api}${path}`, { headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }, ...options });
    if (!response.ok) throw new Error((await response.json().catch(() => ({}))).error || 'Request failed.');
    return response.status === 204 ? null : response.json();
  };

  async function loadTriage() {
    const board = document.querySelector('.board');
    if (!board) return;
    try {
      const items = await request('/requests');
      const groups = [['Requested', 'Requested'], ['InProgress', 'In Progress'], ['Resolved', 'Resolved']];
      board.querySelectorAll('.lane').forEach((lane, index) => {
        const [value, title] = groups[index];
        const cases = items.filter(item => item.status === value);
        lane.innerHTML = `<h2><span>${title}</span> <span>${cases.length}</span></h2>` + (cases.length ? cases.map(item => `<article class="request-card"><div class="request-topline"><span class="request-id">#${item.id}</span><span class="status-pill ${item.urgency === 'Urgent' ? 'urgent' : 'normal'}">${escape(item.urgency)}</span></div><h3>${escape(item.subject)}</h3><p>Student ID ${item.studentId}</p><div class="request-actions">${value !== 'Resolved' ? `<button class="secondary-button" data-status="${value === 'Requested' ? 'InProgress' : 'Resolved'}" data-id="${item.id}">${value === 'Requested' ? 'Start' : 'Resolve'}</button>` : ''}</div></article>`).join('') : '<div class="empty-card">No cases in this stage.</div>');
      });
    } catch (error) { board.innerHTML = `<div class="empty-card">${escape(error.message)}</div>`; }
  }

  async function loadDashboard() {
    if (!document.querySelector('[data-open-count]')) return;
    try {
      const [items, appointments] = await Promise.all([request('/requests'), request('/appointments')]);
      document.querySelector('[data-open-count]').textContent = items.filter(item => item.status !== 'Resolved').length;
      document.querySelector('[data-urgent-count]').textContent = items.filter(item => item.urgency === 'Urgent' && item.status !== 'Resolved').length;
      document.querySelector('[data-appointment-count]').textContent = appointments.filter(item => item.status === 'Scheduled').length;
      const queue = document.querySelector('[data-dashboard-queue]');
      if (queue) queue.innerHTML = items.filter(item => item.status !== 'Resolved').slice(0, 3).map(item => `<article class="request-card"><div class="request-topline"><span class="request-id">#${item.id}</span><span class="status-pill ${item.urgency === 'Urgent' ? 'urgent' : 'normal'}">${escape(item.urgency)}</span></div><h3>${escape(item.subject)}</h3><p>Student ID ${item.studentId}</p></article>`).join('') || '<div class="empty-card">No active requests.</div>';
    } catch (error) { /* The queue remains usable when a session has expired. */ }
  }

  async function loadMonitoring() {
    const target = document.querySelector('[data-monitoring-table]');
    if (!target) return;
    try {
      const items = await request('/requests');
      const render = filter => {
        const active = items.filter(item => item.status !== 'Resolved');
        const visible = active.filter(item => filter === 'all' || (filter === 'urgent' && item.urgency === 'Urgent'));
        target.innerHTML = visible.map(item => `<article class="monitoring-row"><div class="student-identity"><strong>Student ${item.studentId}</strong><small>${escape(item.subject)}</small></div><div><span class="status-pill ${item.urgency === 'Urgent' ? 'urgent' : 'normal'}">${escape(item.status)}</span><small>${escape(item.urgency)} priority</small></div><div><small class="row-label">Assigned counselor</small><strong>${item.assignedCounselorId ? `User ${item.assignedCounselorId}` : 'Unassigned'}</strong></div><a class="icon-button" href="/triage.html">Open</a></article>`).join('') || '<div class="empty-card">No cases match this view.</div>';
        document.querySelector('[data-monitoring-count]').textContent = `${visible.length} visible`;
      };
      const filter = document.querySelector('[data-monitoring-filter]'); render(filter?.value || 'all'); filter?.addEventListener('change', event => render(event.target.value));
    } catch (error) { target.innerHTML = `<div class="empty-card">${escape(error.message)}</div>`; }
  }

  document.addEventListener('click', async event => {
    const button = event.target.closest('[data-status]');
    if (!button) return;
    button.disabled = true;
    try { await request(`/requests/${button.dataset.id}/status`, { method: 'POST', body: JSON.stringify({ status: button.dataset.status }) }); await loadTriage(); }
    catch (error) { button.disabled = false; alert(error.message); }
  });

  function bindNotes() {
    const form = document.querySelector('[data-notes-form]');
    if (!form) return;
    form.addEventListener('submit', async event => {
      event.preventDefault(); const status = form.querySelector('[data-form-status]');
      try {
        await request('/case-notes', { method: 'POST', body: JSON.stringify({ studentId: Number(form.querySelector('[data-student-id]').value), category: form.querySelector('[data-category]').value, content: form.querySelector('[data-note-content]').value, isConfidential: true }) });
        form.reset(); message(status, 'Case note saved securely.');
      } catch (error) { message(status, error.message, true); }
    });
  }

  function bindRequests() {
    const form = document.querySelector('[data-request-form]');
    if (!form) return;
    form.addEventListener('submit', async event => {
      event.preventDefault(); const status = form.querySelector('[data-form-status]');
      const category = form.querySelector('[data-request-category]').value;
      const subject = form.querySelector('[data-request-subject]').value;
      const details = form.querySelector('[data-request-details]').value;
      try {
        await request('/requests', { method: 'POST', body: JSON.stringify({ studentId: Number(form.querySelector('[data-student-id]').value), subject: category ? `${category}: ${subject}` : subject, details, urgency: form.querySelector('[data-request-urgent]').checked ? 'Urgent' : 'Normal' }) });
        form.reset(); message(status, 'Guidance request submitted.');
      } catch (error) { message(status, error.message, true); }
    });
  }

  async function loadAppointments() {
    const calendar = document.querySelector('.calendar-grid');
    if (!calendar) return;
    try {
      const items = await request('/appointments');
      calendar.querySelector('.calendar-empty')?.remove();
      calendar.querySelectorAll('.slot-button').forEach(node => node.remove());
      if (!items.length) calendar.insertAdjacentHTML('beforeend', '<div class="calendar-empty">No appointment slots scheduled.</div>');
      items.forEach(item => calendar.insertAdjacentHTML('beforeend', `<div class="slot-button"><strong>${new Date(item.appointmentAt).toLocaleString()}</strong><span>Student ${item.studentId}: ${escape(item.purpose)}</span></div>`));
    } catch (error) { calendar.insertAdjacentHTML('beforeend', `<div class="calendar-empty">${escape(error.message)}</div>`); }
  }

  function bindAppointments() {
    const form = document.querySelector('[data-appointment-form]');
    document.querySelector('[data-show-appointment]')?.addEventListener('click', () => { form.hidden = !form.hidden; });
    if (!form) return;
    form.addEventListener('submit', async event => {
      event.preventDefault(); const status = form.querySelector('[data-form-status]');
      try {
        await request('/appointments', { method: 'POST', body: JSON.stringify({ studentId: Number(form.querySelector('[data-student-id]').value), appointmentAt: new Date(form.querySelector('[data-appointment-at]').value).toISOString(), purpose: form.querySelector('[data-purpose]').value, remarks: null }) });
        form.reset(); form.hidden = true; message(status, 'Appointment saved.'); await loadAppointments();
      } catch (error) { message(status, error.message, true); }
    });
  }

  document.addEventListener('DOMContentLoaded', () => { loadTriage(); loadDashboard(); loadMonitoring(); bindNotes(); bindRequests(); loadAppointments(); bindAppointments(); });
})();
