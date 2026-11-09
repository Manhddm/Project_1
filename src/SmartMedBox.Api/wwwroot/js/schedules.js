(() => {
    const page = document.querySelector('[data-schedule-page]');
    if (!page) return;

    const form = document.querySelector('#schedule-form');
    const idInput = document.querySelector('#schedule-id');
    const list = document.querySelector('#schedule-list');
    const count = document.querySelector('#schedule-count');
    const error = document.querySelector('#schedule-error');
    const status = document.querySelector('#schedule-status');
    const submit = document.querySelector('#schedule-submit');
    const cancel = document.querySelector('#schedule-cancel');
    const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    let deviceId = null;

    const setMessage = (element, message) => {
        element.textContent = message || '';
        element.hidden = !message;
    };

    const request = async (url, options = {}) => {
        const response = await fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...(options.method && options.method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
            ...options,
        });
        if (!response.ok) {
            const problem = await response.json().catch(() => null);
            throw new Error(problem?.detail || problem?.code || `HTTP_${response.status}`);
        }
        return response.status === 204 ? null : response.json();
    };

    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[character]));
    const dayNames = ['Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7', 'Chủ nhật'];

    const resetForm = () => {
        form.reset();
        idInput.value = '';
        submit.textContent = 'Thêm lịch';
        cancel.hidden = true;
        document.querySelectorAll('input[name="day"]').forEach(input => { input.checked = true; });
        document.querySelector('#time-zone').value = 'Asia/Ho_Chi_Minh';
        document.querySelector('#grace-period').value = '15';
        document.querySelector('#unit').value = 'viên';
    };

    const loadSchedules = async () => {
        try {
            const device = await request('/api/devices/mine');
            deviceId = device.id;
            const data = await request(`/api/schedules?deviceId=${encodeURIComponent(deviceId)}`);
            render(data.items || []);
        } catch (exception) {
            render([]);
            setMessage(error, exception.message === 'DEVICE_NOT_FOUND' ? 'Bạn chưa ghép hộp.' : exception.message);
        }
    };

    const render = schedules => {
        count.textContent = `${schedules.length} lịch`;
        if (!schedules.length) {
            list.innerHTML = '<p class="empty-state">Chưa có lịch uống thuốc.</p>';
            return;
        }
        list.innerHTML = schedules.map(schedule => `
            <article class="prescription-card">
                <div class="prescription-card-details">
                    <strong>Ngăn ${schedule.compartmentSlot} — ${escapeHtml(schedule.compartmentName)} · ${escapeHtml(schedule.timeOfDay)}</strong>
                    <span>${schedule.daysOfWeek.map(day => dayNames[day - 1]).join(', ')}</span>
                    <span>${escapeHtml(schedule.startDate)}${schedule.endDate ? ` → ${escapeHtml(schedule.endDate)}` : ''} · chờ ${schedule.gracePeriodMinutes} phút</span>
                    <span>${schedule.items.map(item => `${escapeHtml(item.medicineName)} (${escapeHtml(item.quantity)} ${escapeHtml(item.unit)})`).join(', ')}</span>
                    <div class="prescription-actions">
                        <button type="button" class="secondary-button" data-edit="${schedule.id}">Sửa</button>
                        <button type="button" class="danger-button" data-delete="${schedule.id}">Xóa</button>
                    </div>
                </div>
            </article>`).join('');
    };

    form.addEventListener('submit', async event => {
        event.preventDefault();
        setMessage(error, '');
        const id = idInput.value;
        const body = {
            compartmentSlot: Number(document.querySelector('#compartment-slot').value),
            timeOfDay: document.querySelector('#time-of-day').value,
            daysOfWeek: [...document.querySelectorAll('input[name="day"]:checked')].map(input => Number(input.value)),
            startDate: document.querySelector('#start-date').value,
            endDate: document.querySelector('#end-date').value || null,
            gracePeriodMinutes: Number(document.querySelector('#grace-period').value),
            timeZoneId: document.querySelector('#time-zone').value,
            items: [{ medicineName: document.querySelector('#medicine-name').value, quantity: Number(document.querySelector('#quantity').value), unit: document.querySelector('#unit').value, instructions: document.querySelector('#instructions').value }],
        };
        try {
            await request(id ? `/api/schedules/${id}` : '/api/schedules', { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) });
            setMessage(status, id ? 'Đã cập nhật lịch.' : 'Đã thêm lịch.');
            resetForm();
            await loadSchedules();
        } catch (exception) { setMessage(error, exception.message); }
    });

    list.addEventListener('click', async event => {
        const editId = event.target.closest('[data-edit]')?.dataset.edit;
        const deleteId = event.target.closest('[data-delete]')?.dataset.delete;
        try {
            if (deleteId) {
                if (!window.confirm('Xóa mềm lịch này?')) return;
                await request(`/api/schedules/${deleteId}`, { method: 'DELETE' });
                setMessage(status, 'Đã xóa lịch.');
                await loadSchedules();
            } else if (editId) {
                const schedule = await request(`/api/schedules/${editId}`);
                idInput.value = schedule.id;
                document.querySelector('#compartment-slot').value = schedule.compartmentSlot;
                document.querySelector('#time-of-day').value = schedule.timeOfDay;
                document.querySelector('#start-date').value = schedule.startDate;
                document.querySelector('#end-date').value = schedule.endDate || '';
                document.querySelector('#grace-period').value = schedule.gracePeriodMinutes;
                document.querySelector('#time-zone').value = schedule.timeZoneId;
                const item = schedule.items[0];
                document.querySelector('#medicine-name').value = item?.medicineName || '';
                document.querySelector('#quantity').value = item?.quantity || '';
                document.querySelector('#unit').value = item?.unit || '';
                document.querySelector('#instructions').value = item?.instructions || '';
                document.querySelectorAll('input[name="day"]').forEach(input => { input.checked = schedule.daysOfWeek.includes(Number(input.value)); });
                submit.textContent = 'Lưu thay đổi';
                cancel.hidden = false;
                form.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        } catch (exception) { setMessage(error, exception.message); }
    });

    cancel.addEventListener('click', resetForm);
    loadSchedules();
})();
