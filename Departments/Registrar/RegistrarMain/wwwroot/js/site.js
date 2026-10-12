// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(() => {
	const page = document.querySelector('[data-page]');
	if (!page) return;

	const api = async (url, options = {}) => {
		const response = await fetch(url, { headers: { Accept: 'application/json', ...(options.headers || {}) }, ...options });
		if (response.status === 204) return null;
		const body = await response.json().catch(() => null);
		if (!response.ok) throw new Error(body?.detail || body?.title || body?.message || `Request failed (${response.status})`);
		return body;
	};

	const escapeHtml = (value) => String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[character]));
	const showMessage = (type, message) => {
		const element = page.querySelector(`[data-${type}]`);
		if (!element) return;
		element.textContent = message;
		element.classList.remove('d-none');
	};
	const clearMessage = (type) => page.querySelector(`[data-${type}]`)?.classList.add('d-none');
	const setApiStatus = (connected) => page.querySelectorAll('[data-api-status]').forEach(element => {
		element.textContent = connected ? 'API connected' : 'API unavailable';
		element.classList.toggle('status-error', !connected);
	});

	const courseCard = course => `<article class="course-card">
		<div><span class="course-code">${escapeHtml(course.code)}</span><h2>${escapeHtml(course.title)}</h2><span class="course-meta">${course.credits} credit${course.credits === 1 ? '' : 's'}</span></div>
		<button class="btn btn-outline-primary" type="button" data-enroll-course="${course.id}">Add to schedule</button>
	</article>`;

	const loadCourses = async (target, search = '') => {
		const courses = await api(`/api/courses${search ? `?search=${encodeURIComponent(search)}` : ''}`);
		target.innerHTML = courses.length ? courses.map(courseCard).join('') : '<div class="empty-state">No matching courses found.</div>';
		return courses;
	};

	const initDashboard = async () => {
		try {
			const [courses, enrollments, verifications, records] = await Promise.all([
				api('/api/courses'), api('/api/registrations/mine'), api('/api/verifications/mine'), api('/api/records/mine')
			]);
			page.querySelector('[data-metric="courses"]').textContent = courses.length;
			page.querySelector('[data-metric="enrollment"]').textContent = enrollments.length;
			page.querySelector('[data-metric="requests"]').textContent = verifications.length + records.length;
			setApiStatus(true);
		} catch (error) {
			setApiStatus(false);
			showMessage('error', error.message);
		}
	};

	const initCourses = async () => {
		const target = page.querySelector('#course-list');
		const count = page.querySelector('#course-count');
		const search = page.querySelector('#course-search');
		const refresh = async () => {
			try {
				clearMessage('error');
				const courses = await loadCourses(target, search.value.trim());
				count.textContent = `${courses.length} course${courses.length === 1 ? '' : 's'} available`;
				setApiStatus(true);
			} catch (error) { setApiStatus(false); showMessage('error', error.message); }
		};
		let timer;
		search.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(refresh, 220); });
		await refresh();
	};

	const initRegistration = async () => {
		const coursesTarget = page.querySelector('#registration-courses');
		const scheduleTarget = page.querySelector('#my-schedule');
		const total = page.querySelector('#schedule-total');
		const refresh = async () => {
			const [courses, enrollments] = await Promise.all([api('/api/courses'), api('/api/registrations/mine')]);
			page.querySelector('#registration-course-count').textContent = `${courses.length} available`;
			coursesTarget.innerHTML = courses.length ? courses.map(course => `<div class="schedule-item"><span><strong>${escapeHtml(course.code)}</strong><small>${escapeHtml(course.title)} - ${course.credits} credits</small></span><button class="btn btn-sm btn-outline-primary" data-enroll-course="${course.id}">Add</button></div>`).join('') : '<div class="empty-state">No courses are available.</div>';
			scheduleTarget.innerHTML = enrollments.length ? enrollments.map(item => `<div class="schedule-item"><span><strong>${escapeHtml(item.courseCode)}</strong><small>${escapeHtml(item.semester)}</small></span><button class="btn btn-sm btn-outline-danger" data-drop-id="${item.id}" data-row-version="${escapeHtml(item.rowVersion)}">Drop</button></div>`).join('') : '<div class="empty-state">Your schedule is clear. Add a course to begin.</div>';
			total.textContent = `${enrollments.length} course${enrollments.length === 1 ? '' : 's'}`;
			setApiStatus(true);
		};
		page.addEventListener('click', async event => {
			const add = event.target.closest('[data-enroll-course]');
			const drop = event.target.closest('[data-drop-id]');
			try {
				if (add) { add.disabled = true; clearMessage('error'); clearMessage('success'); await api('/api/registrations', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ courseId: Number(add.dataset.enrollCourse), semester: '2026-Fall' }) }); showMessage('success', 'Course added to your 2026 Fall schedule.'); await refresh(); }
				if (drop) { drop.disabled = true; clearMessage('error'); await api(`/api/registrations/${drop.dataset.dropId}?rowVersion=${encodeURIComponent(drop.dataset.rowVersion)}`, { method: 'DELETE' }); showMessage('success', 'Course dropped from your schedule.'); await refresh(); }
			} catch (error) { showMessage('error', error.message); if (add) add.disabled = false; if (drop) drop.disabled = false; }
		});
		try { await refresh(); } catch (error) { setApiStatus(false); showMessage('error', error.message); }
	};

	const initTranscript = async () => {
		try {
			const transcript = await api('/api/transcript/mine');
			const target = page.querySelector('#transcript-list');
			target.innerHTML = transcript.semesters.length ? transcript.semesters.map(term => `<section class="transcript-term"><h2>${escapeHtml(term.semester)}</h2><div class="table-responsive"><table class="table"><thead><tr><th>Course</th><th>Grade</th></tr></thead><tbody>${term.entries.map(entry => `<tr><td>${escapeHtml(entry.courseCode)}</td><td><strong>${escapeHtml(entry.grade)}</strong></td></tr>`).join('')}</tbody></table></div></section>`).join('') : '<div class="surface empty-state">No grades have been recorded yet.</div>';
			setApiStatus(true);
		} catch (error) { setApiStatus(false); showMessage('error', error.message); }
	};

	const initRequests = async (kind) => {
		const isVerification = kind === 'verification';
		const list = page.querySelector(`#${kind}-list`);
		const endpoint = isVerification ? '/api/verifications' : '/api/records';
		const render = requests => {
			list.innerHTML = requests.length ? requests.map(request => `<div class="request-item"><span><strong>${isVerification ? 'Enrollment verification' : escapeHtml(request.documentType)}</strong><small>${new Date(request.requestedAt).toLocaleDateString()}</small></span><span class="request-status">${escapeHtml(request.status)}</span></div>`).join('') : '<div class="empty-state">No requests yet.</div>';
		};
		const refresh = async () => { render(await api(`${endpoint}/mine`)); setApiStatus(true); };
		const submit = page.querySelector(`#${kind}-submit`);
		submit.addEventListener('click', async () => {
			submit.disabled = true; clearMessage('error'); clearMessage('success');
			try { const body = isVerification ? undefined : JSON.stringify({ documentType: page.querySelector('#document-type').value }); await api(endpoint, { method: 'POST', headers: body ? { 'Content-Type': 'application/json' } : undefined, body }); showMessage('success', 'Your request was submitted.'); await refresh(); }
			catch (error) { showMessage('error', error.message); } finally { submit.disabled = false; }
		});
		try { await refresh(); } catch (error) { setApiStatus(false); showMessage('error', error.message); }
	};

	const initializers = { dashboard: initDashboard, courses: initCourses, registration: initRegistration, transcript: initTranscript, verification: () => initRequests('verification'), records: () => initRequests('records') };
	initializers[page.dataset.page]?.();
})();
