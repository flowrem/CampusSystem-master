async function loadGuidanceStudents() {
  const tableTarget = document.querySelector('[data-guidance-students-table]');
  const cardTarget = document.querySelector('[data-guidance-students]');

  if (!tableTarget && !cardTarget) return;

  try {
    const response = await fetch('/api/guidance/students');
    if (!response.ok) {
      throw new Error('Failed to load students');
    }

    const students = await response.json();

    if (tableTarget) {
      if (!students || students.length === 0) {
        tableTarget.innerHTML = '<tr><td colspan="4">No student records found.</td></tr>';
        return;
      }

      tableTarget.innerHTML = students.map(student => `
        <tr>
          <td>${student.studentNumber}</td>
          <td>${student.fullName}</td>
          <td>${student.email}</td>
          <td>${student.id}</td>
        </tr>
      `).join('');
      return;
    }

    if (!students || students.length === 0) {
      cardTarget.innerHTML = '<div class="empty-card">No student records found.</div>';
      return;
    }

    cardTarget.innerHTML = students.map(student => `
      <article class="monitoring-row">
        <div class="student-identity">
          <strong>Student ${student.studentNumber}</strong>
          <small>${student.fullName}</small>
        </div>
        <div>
          <small class="row-label">Email</small>
          <strong>${student.email}</strong>
        </div>
        <div>
          <small class="row-label">Student ID</small>
          <strong>${student.id}</strong>
        </div>
      </article>
    `).join('');
  } catch (error) {
    if (tableTarget) {
      tableTarget.innerHTML = '<tr><td colspan="4">Unable to load guidance student records.</td></tr>';
      return;
    }

    cardTarget.innerHTML = '<div class="empty-card">Unable to load guidance student records.</div>';
  }
}

document.addEventListener('DOMContentLoaded', () => {
  loadGuidanceStudents();
});
