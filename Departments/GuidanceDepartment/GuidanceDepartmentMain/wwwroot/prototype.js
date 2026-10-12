(() => {
  const requests = [
    { id: '1042', subject: 'Academic probation', category: 'Academic', urgency: 'Urgent', status: 'Requested', age: '18 min ago' },
    { id: '1077', subject: 'Financial aid appeal', category: 'Funding', urgency: 'Normal', status: 'Requested', age: '42 min ago' },
    { id: '0988', subject: 'Transfer pathway', category: 'Career', urgency: 'Normal', status: 'In progress', age: 'Yesterday' },
    { id: '1031', subject: 'Exam accommodations', category: 'Support', urgency: 'Urgent', status: 'In progress', age: 'Yesterday' }
  ];

  const statusClass = value => value.toLowerCase().replace(' ', '-');
  const requestMarkup = request => `<article class="request-card" data-request-id="${request.id}"><div class="request-topline"><span class="request-id">#${request.id}</span><span class="status-pill ${statusClass(request.urgency)}">${request.urgency}</span></div><h3>${request.subject}</h3><p>${request.category} <span>/</span> ${request.age}</p><div class="request-actions"><button class="secondary-button" type="button" data-action="advance">${request.status === 'Requested' ? 'Start' : 'Resolve'}</button><button class="text-button" type="button" data-action="refer">Refer</button></div></article>`;

  const queue = document.querySelector('[data-dashboard-queue]');
  if (queue) queue.innerHTML = requests.slice(0, 3).map(requestMarkup).join('');

  const board = document.querySelector('.board');
  if (board) {
    const lanes = [...board.querySelectorAll('.lane')];
    lanes.forEach((lane, index) => {
      const status = ['Requested', 'In progress', 'Resolved'][index];
      const items = requests.filter(request => request.status === status);
      const count = lane.querySelector('h2 span');
      if (count) count.textContent = items.length;
      lane.querySelector('.empty-card')?.remove();
      items.forEach(request => lane.insertAdjacentHTML('beforeend', requestMarkup(request)));
    });
  }

  document.addEventListener('click', event => {
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (!action) return;
    const card = event.target.closest('.request-card');
    const status = document.createElement('span');
    status.className = 'inline-confirmation';
    status.textContent = action === 'refer' ? 'Referral prepared for review.' : 'Preview status updated.';
    card?.append(status);
  });

  document.querySelectorAll('form').forEach(form => form.addEventListener('submit', event => {
    event.preventDefault();
    const message = form.querySelector('[data-form-status]') || document.createElement('span');
    message.className = 'inline-confirmation';
    message.textContent = 'Preview saved for this session only.';
    if (!message.parentElement) form.append(message);
  }));

  const calendar = document.querySelector('.calendar-grid');
  if (calendar) {
    calendar.querySelector('.calendar-empty')?.remove();
    ['08:30', '09:30', '13:00', '14:30'].forEach((time, index) => calendar.insertAdjacentHTML('beforeend', `<button class="slot-button" type="button" style="grid-column: ${index + 2}"><strong>${time}</strong><span>Maya Chen</span></button>`));
    calendar.addEventListener('click', event => {
      const slot = event.target.closest('.slot-button');
      if (!slot) return;
      calendar.querySelectorAll('.slot-button').forEach(item => item.classList.remove('selected'));
      slot.classList.add('selected');
    });
  }

  const monitoringTable = document.querySelector('[data-monitoring-table]');
  if (monitoringTable) {
    const students = [
      { reference: 'Student 1042', programme: 'Business / Year 2', signal: 'Urgent follow-up', signalClass: 'urgent', request: 'Academic probation', followUp: 'Today, 10:00', counselor: 'Maya Chen', overdue: false },
      { reference: 'Student 1077', programme: 'Engineering / Year 1', signal: 'Open request', signalClass: 'normal', request: 'Financial aid appeal', followUp: 'Today, 14:30', counselor: 'Maya Chen', overdue: false },
      { reference: 'Student 0988', programme: 'Arts / Year 3', signal: 'Follow-up overdue', signalClass: 'urgent', request: 'Transfer pathway', followUp: '2 days overdue', counselor: 'Jordan Lee', overdue: true },
      { reference: 'Student 1031', programme: 'Science / Year 2', signal: 'Urgent follow-up', signalClass: 'urgent', request: 'Exam accommodations', followUp: 'Tomorrow, 09:30', counselor: 'Jordan Lee', overdue: false }
    ];
    const renderStudents = filter => {
      const visible = students.filter(student => filter === 'all' || (filter === 'urgent' && student.signalClass === 'urgent') || (filter === 'overdue' && student.overdue));
      monitoringTable.innerHTML = visible.map(student => `<article class="monitoring-row"><div class="student-identity"><strong>${student.reference}</strong><small>${student.programme}</small></div><div><span class="status-pill ${student.signalClass}">${student.signal}</span><small>${student.request}</small></div><div><small class="row-label">Next follow-up</small><strong>${student.followUp}</strong></div><div><small class="row-label">Assigned counselor</small><strong>${student.counselor}</strong></div><button class="icon-button" type="button" data-monitoring-action>Open</button></article>`).join('') || '<div class="empty-card">No students match this view.</div>';
      const count = document.querySelector('[data-monitoring-count]');
      if (count) count.textContent = `${visible.length} visible`;
    };
    const filter = document.querySelector('[data-monitoring-filter]');
    renderStudents(filter?.value || 'all');
    filter?.addEventListener('change', event => renderStudents(event.target.value));
  }
})();
