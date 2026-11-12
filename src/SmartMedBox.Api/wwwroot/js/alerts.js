(() => {
    const page = document.querySelector('[data-alerts-page]');
    if (!page) return;

    const list = document.querySelector('#alerts-list');
    const count = document.querySelector('#alerts-count');
    const error = document.querySelector('#alerts-error');
    const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const typeLabels = { OverdueUnconfirmed: 'Quá hạn, chưa xác nhận', UnconfirmedOffline: 'Không thể xác nhận do mất kết nối' };

    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const setError = message => { error.textContent = message || ''; error.hidden = !message; };

    const request = async (url, options = {}) => {
        const response = await fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json', ...(options.method && options.method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
            ...options,
        });
        if (!response.ok) {
            const problem = await response.json().catch(() => null);
            const failure = new Error(problem?.detail || problem?.code || `HTTP_${response.status}`);
            failure.status = response.status;
            throw failure;
        }
        return response.json();
    };

    // Hộp sở hữu (/api/devices/mine) + hộp đang theo dõi (relationships Active có deviceId).
    const loadDeviceIds = async () => {
        const ids = new Set();
        try { ids.add((await request('/api/devices/mine')).id); } catch (exception) { if (exception.status !== 404) throw exception; }
        const relationships = (await request('/api/relatives/relationships')).items || [];
        relationships.filter(x => x.status === 'Active').forEach(x => ids.add(x.deviceId));
        return [...ids];
    };

    const load = async () => {
        setError('');
        try {
            const deviceIds = await loadDeviceIds();
            if (!deviceIds.length) {
                count.textContent = '';
                list.innerHTML = '<p class="empty-state">Bạn chưa sở hữu hoặc theo dõi hộp nào. <a href="/devices/pair">Ghép hộp</a> · <a href="/relatives">Người thân</a></p>';
                return;
            }
            // shortcut: chỉ lấy 100 cảnh báo mới nhất mỗi hộp, thêm phân trang khi cần.
            const pages = await Promise.all(deviceIds.map(id => request(`/api/alerts?deviceId=${encodeURIComponent(id)}&pageSize=100`)));
            const alerts = pages.flatMap(p => p.items || []).sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
            const unread = alerts.filter(x => !x.readAt).length;
            count.textContent = `${alerts.length} cảnh báo · ${unread} chưa đọc`;
            list.innerHTML = alerts.length ? alerts.map(alert => `
                <article class="prescription-card">
                    <div class="prescription-card-details">
                        <strong>${escapeHtml(typeLabels[alert.alertType] || alert.alertType)}${alert.readAt ? '' : ' · Chưa đọc'}</strong>
                        <span>${escapeHtml(alert.message)}</span>
                        <span>${escapeHtml(new Date(alert.createdAt).toLocaleString('vi-VN'))}</span>
                        ${alert.resolvedAt ? `<span>Đã xác nhận muộn lúc ${escapeHtml(new Date(alert.resolvedAt).toLocaleString('vi-VN'))}</span>` : ''}
                        ${alert.readAt ? '' : `<div class="prescription-actions"><button type="button" class="secondary-button" data-read="${escapeHtml(alert.id)}">Đánh dấu đã đọc</button></div>`}
                    </div>
                </article>`).join('') : '<p class="empty-state">Chưa có cảnh báo nào.</p>';
        } catch (exception) {
            list.innerHTML = '';
            setError(exception.message);
        }
    };

    list.addEventListener('click', async event => {
        const id = event.target.closest('[data-read]')?.dataset.read;
        if (!id) return;
        try {
            await request(`/api/alerts/${encodeURIComponent(id)}/read`, { method: 'POST' });
            await load();
        } catch (exception) { setError(exception.message); }
    });

    load();
})();
