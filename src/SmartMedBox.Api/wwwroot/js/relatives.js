(() => {
    const page = document.querySelector('[data-relatives-page]');
    if (!page) return;

    const error = document.querySelector('#relatives-error');
    const status = document.querySelector('#relatives-status');
    const ownerSection = document.querySelector('#owner-section');
    const invitationList = document.querySelector('#invitation-list');
    const relationshipList = document.querySelector('#relationship-list');
    const followingList = document.querySelector('#following-list');
    const tokenInput = document.querySelector('#accept-token');
    const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    const messages = {
        INVITE_ALREADY_PENDING: 'Email này đã có lời mời đang chờ.',
        RELATIONSHIP_EXISTS: 'Người này đã là người thân của hộp.',
        RATE_LIMITED: 'Bạn đã thử quá nhiều lần, vui lòng thử lại sau.',
        INVITE_NOT_FOUND: 'Mã lời mời không hợp lệ.',
        INVITE_NOT_PENDING: 'Lời mời không còn hiệu lực.',
        INVITE_EXPIRED: 'Lời mời đã hết hạn.',
        INVITE_EMAIL_MISMATCH: 'Lời mời không dành cho email của tài khoản này.',
        RELATIONSHIP_NOT_FOUND: 'Không tìm thấy quan hệ theo dõi.',
        FORBIDDEN: 'Bạn không có quyền thực hiện thao tác này.',
    };
    const statusLabels = { Pending: 'Đang chờ', Accepted: 'Đã chấp nhận', Revoked: 'Đã thu hồi', Expired: 'Hết hạn', Active: 'Đang theo dõi' };

    const setMessage = (element, message) => { element.textContent = message || ''; element.hidden = !message; };
    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
    const formatTime = value => value ? new Date(value).toLocaleString() : '';

    const request = async (url, options = {}) => {
        const response = await fetch(url, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...(options.method && options.method !== 'GET' ? { 'X-CSRF-TOKEN': csrf } : {}) },
            ...options,
        });
        if (!response.ok) {
            const problem = await response.json().catch(() => null);
            const validation = problem?.errors ? Object.values(problem.errors).flat()[0] : null;
            const failure = new Error(problem?.detail || validation || messages[problem?.code] || problem?.code || `HTTP_${response.status}`);
            failure.status = response.status;
            throw failure;
        }
        return response.status === 204 ? null : response.json();
    };

    const card = (title, lines, button) => `<article class="prescription-card"><div class="prescription-card-details"><strong>${title}</strong>${lines.map(line => `<span>${line}</span>`).join('')}${button ? `<div class="prescription-actions">${button}</div>` : ''}</div></article>`;

    const load = async () => {
        setMessage(error, '');
        let ownedDeviceId = null;
        try {
            ownedDeviceId = (await request('/api/devices/mine')).id;
        } catch (exception) {
            if (exception.status !== 404) setMessage(error, exception.message);
        }
        ownerSection.hidden = !ownedDeviceId;
        try {
            // Không có deviceId: chủ nhận quan hệ của hộp mình + quan hệ Active mà mình là người thân.
            const relationships = (await request('/api/relatives/relationships')).items || [];
            const following = relationships.filter(x => x.deviceId !== ownedDeviceId);
            followingList.innerHTML = following.length
                ? following.map(x => card(`Hộp ${escapeHtml(x.serialNumber)}`, [`${escapeHtml(statusLabels[x.status] || x.status)} từ ${escapeHtml(formatTime(x.acceptedAt))}`],
                    x.status === 'Active' ? `<button type="button" class="danger-button" data-stop="${escapeHtml(x.id)}">Ngừng theo dõi</button>` : '')).join('')
                : '<p class="empty-state">Bạn chưa theo dõi hộp nào.</p>';
            if (ownedDeviceId) {
                const own = relationships.filter(x => x.deviceId === ownedDeviceId);
                relationshipList.innerHTML = own.length
                    ? own.map(x => card(escapeHtml(x.relativeEmail), [`${escapeHtml(statusLabels[x.status] || x.status)} · chấp nhận ${escapeHtml(formatTime(x.acceptedAt))}`],
                        x.status === 'Active' ? `<button type="button" class="danger-button" data-revoke-relationship="${escapeHtml(x.id)}">Thu hồi</button>` : '')).join('')
                    : '<p class="empty-state">Chưa có người thân nào.</p>';
                const invitations = (await request(`/api/relatives/invitations?deviceId=${encodeURIComponent(ownedDeviceId)}`)).items || [];
                invitationList.innerHTML = invitations.length
                    ? invitations.map(x => card(escapeHtml(x.inviteeEmail), [`${escapeHtml(statusLabels[x.status] || x.status)} · hết hạn ${escapeHtml(formatTime(x.expiresAt))}`],
                        x.status === 'Pending' ? `<button type="button" class="danger-button" data-revoke-invitation="${escapeHtml(x.id)}">Thu hồi lời mời</button>` : '')).join('')
                    : '<p class="empty-state">Chưa có lời mời nào.</p>';
            }
        } catch (exception) { setMessage(error, exception.message); }
    };

    const act = async (action, success) => {
        setMessage(error, ''); setMessage(status, '');
        try { await action(); setMessage(status, success); await load(); } catch (exception) { setMessage(error, exception.message); }
    };

    document.querySelector('#invite-form').addEventListener('submit', event => {
        event.preventDefault();
        const email = document.querySelector('#invite-email');
        act(() => request('/api/relatives/invitations', { method: 'POST', body: JSON.stringify({ email: email.value }) }).then(() => { email.value = ''; }), 'Đã gửi lời mời.');
    });

    document.querySelector('#accept-form').addEventListener('submit', event => {
        event.preventDefault();
        act(() => request('/api/relatives/invitations/accept', { method: 'POST', body: JSON.stringify({ token: tokenInput.value.trim() }) }).then(() => { tokenInput.value = ''; }), 'Đã chấp nhận lời mời. Bạn có thể xem cảnh báo của hộp này.');
    });

    page.addEventListener('click', event => {
        const stop = event.target.closest('[data-stop]')?.dataset.stop;
        const revokeRelationship = event.target.closest('[data-revoke-relationship]')?.dataset.revokeRelationship;
        const revokeInvitation = event.target.closest('[data-revoke-invitation]')?.dataset.revokeInvitation;
        if (stop && window.confirm('Ngừng theo dõi hộp này? Bạn sẽ không nhận email cảnh báo nữa.')) {
            act(() => request(`/api/relatives/relationships/${encodeURIComponent(stop)}`, { method: 'DELETE' }), 'Đã ngừng theo dõi.');
        } else if (revokeRelationship && window.confirm('Thu hồi quyền theo dõi của người thân này?')) {
            act(() => request(`/api/relatives/relationships/${encodeURIComponent(revokeRelationship)}`, { method: 'DELETE' }), 'Đã thu hồi người thân.');
        } else if (revokeInvitation && window.confirm('Thu hồi lời mời này?')) {
            act(() => request(`/api/relatives/invitations/${encodeURIComponent(revokeInvitation)}/revoke`, { method: 'POST' }), 'Đã thu hồi lời mời.');
        }
    });

    tokenInput.value = new URLSearchParams(window.location.search).get('token') || '';
    load();
})();
