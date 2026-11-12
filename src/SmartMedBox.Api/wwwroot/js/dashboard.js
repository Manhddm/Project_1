(() => {
    const page = document.querySelector('[data-dashboard-page]');
    if (!page) return;
    const content = document.querySelector('#dashboard-content');
    const error = document.querySelector('#dashboard-error');
    // Nhãn theo API_CONTRACT §10.2; "Đã mở" không có nghĩa là đã dùng thuốc.
    const statusLabels = { Pending: 'Chờ xác nhận', Confirmed: 'Đã mở', Overdue: 'Quá hạn', Unknown: 'Không thể xác nhận' };
    const slotLabels = { 1: '1 — Sáng', 2: '2 — Trưa', 3: '3 — Tối' };
    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const getJson = url => fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' } });
    // Chủ hộp: hộp của mình. Không sở hữu (404): hộp đầu tiên đang theo dõi với vai trò người thân.
    getJson('/api/devices/mine')
        .then(async response => {
            if (response.ok) return (await response.json()).id;
            if (response.status !== 404) throw new Error(`HTTP_${response.status}`);
            const relationships = await getJson('/api/relatives/relationships');
            if (!relationships.ok) throw new Error(`HTTP_${relationships.status}`);
            const followed = ((await relationships.json()).items || []).find(x => x.status === 'Active');
            if (!followed) throw new Error('NO_DEVICE');
            return followed.deviceId;
        })
        .then(deviceId => getJson(`/api/dashboard?deviceId=${encodeURIComponent(deviceId)}`))
        .then(response => response.ok ? response.json() : Promise.reject(new Error(`HTTP_${response.status}`)))
        .then(data => {
            const doses = data.today.doses.map(dose => `<article class="prescription-card"><div class="prescription-card-details"><strong>Ngăn ${escapeHtml(slotLabels[dose.compartmentSlot] || dose.compartmentSlot)} · ${escapeHtml(new Date(dose.scheduledAt).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: dose.timeZoneId }))}</strong><span>Trạng thái: ${escapeHtml(statusLabels[dose.status] || dose.status)}</span><span>${dose.items.map(item => `${escapeHtml(item.medicineName)} (${escapeHtml(item.quantity)} ${escapeHtml(item.unit)})`).join(', ')}</span></div></article>`).join('');
            content.innerHTML = `<p>Thiết bị: <strong>${escapeHtml(data.device.serialNumber)}</strong> · ${data.device.isOnline ? 'Đang kết nối' : 'Offline'} · Lần cuối kết nối: ${escapeHtml(data.device.lastSeenAt ? new Date(data.device.lastSeenAt).toLocaleString('vi-VN') : 'Chưa có dữ liệu')}</p><div class="prescription-grid">${doses || '<p class="empty-state">Hôm nay chưa có lần nhắc nào.</p>'}</div>`;
        })
        .catch(exception => {
            if (exception.message === 'NO_DEVICE') {
                content.innerHTML = '<p class="empty-state">Tài khoản này chưa ghép hoặc theo dõi hộp nào. <a href="/devices/pair">Ghép hộp</a> · <a href="/relatives">Người thân</a></p>';
                return;
            }
            error.textContent = exception.message; error.hidden = false; content.innerHTML = ''; });
})();
