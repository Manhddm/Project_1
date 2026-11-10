(() => {
    const page = document.querySelector('[data-dashboard-page]');
    if (!page) return;
    const content = document.querySelector('#dashboard-content');
    const error = document.querySelector('#dashboard-error');
    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    fetch('/api/devices/mine', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
        // 404 = tài khoản chưa sở hữu hộp (người mới, người thân): không phải lỗi.
        .then(response => response.status === 404 ? Promise.reject(new Error('NO_DEVICE')) : response)
        .then(response => response.ok ? response.json() : Promise.reject(new Error(`HTTP_${response.status}`)))
        .then(device => fetch(`/api/dashboard?deviceId=${encodeURIComponent(device.id)}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } }))
        .then(response => response.ok ? response.json() : Promise.reject(new Error(`HTTP_${response.status}`)))
        .then(data => {
            const doses = data.today.doses.map(dose => `<article class="prescription-card"><div class="prescription-card-details"><strong>Ngăn ${dose.compartmentSlot} · ${escapeHtml(new Date(dose.scheduledAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }))}</strong><span>Trạng thái: ${escapeHtml(dose.status)}</span><span>${dose.items.map(item => `${escapeHtml(item.medicineName)} (${escapeHtml(item.quantity)} ${escapeHtml(item.unit)})`).join(', ')}</span></div></article>`).join('');
            content.innerHTML = `<p>Thiết bị: <strong>${escapeHtml(data.device.serialNumber)}</strong> · Lần cuối kết nối: ${escapeHtml(data.device.lastSeenAt || 'Chưa có dữ liệu')}</p><div class="prescription-grid">${doses || '<p class="empty-state">Chưa có dose trong hôm nay.</p>'}</div>`;
        })
        .catch(exception => {
            if (exception.message === 'NO_DEVICE') {
                content.innerHTML = '<p class="empty-state">Tài khoản này chưa ghép hộp. <a href="/devices/pair">Ghép hộp</a></p>';
                return;
            }
            error.textContent = exception.message; error.hidden = false; content.innerHTML = ''; });
})();
