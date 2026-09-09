/**
 * Fleetify Hybrid AI Support & Live Chat Client Widget
 */
(function () {
    function initSupportWidget() {
        // Safe token extraction
        let sessionToken = '';
        try {
            sessionToken = localStorage.getItem('fleetify_support_token') || '';
            if (!sessionToken) {
                sessionToken = 'sess_' + Math.random().toString(36).substring(2, 12) + Date.now().toString(36);
                localStorage.setItem('fleetify_support_token', sessionToken);
            }
        } catch (e) {
            sessionToken = 'sess_' + Math.random().toString(36).substring(2, 12) + Date.now().toString(36);
        }

        let conversationId = null;
        let isOpen = false;
        let isSending = false;
        let isEscalating = false;
        let pollTimer = null;
        let knownMessageIds = new Set();
        let initPromise = null;

        // DOM Elements
        const widgetBtn = document.getElementById('supportChatTrigger');
        const chatModal = document.getElementById('supportChatModal');
        const closeBtn = document.getElementById('supportChatClose');
        const msgContainer = document.getElementById('supportChatMessages');
        const inputField = document.getElementById('supportChatInput');
        const sendBtn = document.getElementById('supportChatSend');
        const statusBadge = document.getElementById('supportChatStatus');
        const quickChips = document.querySelectorAll('.support-quick-chip');
        const escalateBtn = document.getElementById('supportChatEscalate');

        if (!widgetBtn || !chatModal || !sendBtn || !inputField) {
            return;
        }

        // Initial default welcome message shown immediately
        const defaultWelcomeMsg = {
            messageID: 'temp_welcome',
            senderType: 'Bot',
            senderName: 'Fleetify AI Assistant',
            messageText: '👋 Hello! I am Fleetify\'s AI Virtual Assistant. How can I help you today? You can ask me about tracking packages, cost estimates, vehicle capacities, or request human admin support anytime.'
        };

        if (msgContainer && msgContainer.children.length === 0) {
            appendMessage(defaultWelcomeMsg);
        }

        // Initialize Chat (Returns Promise resolving to conversationId)
        function initChat() {
            if (initPromise) return initPromise;

            initPromise = fetch('/api/support/init', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ sessionToken: sessionToken })
            })
            .then(res => {
                if (!res.ok) throw new Error('Init HTTP ' + res.status);
                return res.json();
            })
            .then(data => {
                if (data && data.success) {
                    conversationId = data.conversationId;
                    updateStatus(data.status);
                    if (data.messages && data.messages.length > 0) {
                        renderMessages(data.messages);
                    }
                    if (isOpen) {
                        startPolling();
                    }
                    return conversationId;
                } else {
                    throw new Error(data ? data.message : 'Initialization failed');
                }
            })
            .catch(err => {
                console.warn('Support Chat initialization error:', err);
                initPromise = null; // Allow retry on next interaction
                return null;
            });

            return initPromise;
        }

        // Preload in background immediately so it is ready on first click
        initChat();

        // Helper to ensure conversation ID is ready
        async function ensureInitialized() {
            if (conversationId) return conversationId;
            return await initChat();
        }

        // Toggle Chat Modal
        widgetBtn.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            isOpen = !isOpen;
            chatModal.classList.toggle('d-none', !isOpen);
            if (isOpen) {
                ensureInitialized().then(() => {
                    startPolling();
                    scrollToBottom();
                });
                setTimeout(() => inputField.focus(), 120);
            } else {
                stopPolling();
            }
        });

        if (closeBtn) {
            closeBtn.addEventListener('click', (e) => {
                e.preventDefault();
                isOpen = false;
                chatModal.classList.add('d-none');
                stopPolling();
            });
        }

        // Send Message
        async function sendUserMessage() {
            const text = inputField.value.trim();
            if (!text || isSending) return;

            // Clear input and keep user experience fluid
            inputField.value = '';

            // Render user bubble immediately
            appendMessage({
                senderType: 'User',
                senderName: 'You',
                messageText: text
            });

            showTypingIndicator();
            setSendButtonLoading(true);
            isSending = true;

            try {
                const id = await ensureInitialized();
                if (!id) {
                    removeTypingIndicator();
                    appendMessage({
                        senderType: 'Bot',
                        senderName: 'System',
                        messageText: '⚠️ Support server is temporarily unreachable. Please check your connection and try again.'
                    });
                    return;
                }

                const res = await fetch('/api/support/message', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ conversationId: id, message: text })
                });

                if (!res.ok) throw new Error('Message HTTP ' + res.status);
                const data = await res.json();
                removeTypingIndicator();

                if (data && data.success) {
                    appendMessage(data.botReply);
                    updateStatus(data.status);
                } else {
                    appendMessage({
                        senderType: 'Bot',
                        senderName: 'System',
                        messageText: '⚠️ ' + (data ? data.message : 'Unable to process message right now.')
                    });
                }
            } catch (err) {
                console.error('Send message error:', err);
                removeTypingIndicator();
                appendMessage({
                    senderType: 'Bot',
                    senderName: 'System',
                    messageText: '⚠️ Network connection issue. Please try again.'
                });
            } finally {
                isSending = false;
                setSendButtonLoading(false);
                inputField.focus();
            }
        }

        // Send button click
        sendBtn.addEventListener('click', (e) => {
            e.preventDefault();
            sendUserMessage();
        });

        // Input keydown (Enter)
        inputField.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                sendUserMessage();
            }
        });

        // Quick Action Chips
        quickChips.forEach(chip => {
            chip.addEventListener('click', (e) => {
                e.preventDefault();
                const query = chip.getAttribute('data-query');
                if (!query) return;

                if (query.toLowerCase().includes('admin') || query.toLowerCase().includes('human')) {
                    escalateToAdmin();
                } else {
                    inputField.value = query;
                    sendUserMessage();
                }
            });
        });

        // Escalate to Human Admin
        async function escalateToAdmin() {
            if (isEscalating) return;
            isEscalating = true;
            if (escalateBtn) {
                escalateBtn.disabled = true;
                escalateBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" style="width: 10px; height: 10px;"></span>Connecting...';
            }

            try {
                const id = await ensureInitialized();
                if (!id) {
                    alert('Unable to reach support service. Please try again.');
                    return;
                }

                const res = await fetch('/api/support/escalate', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ conversationId: id, reason: 'User requested human admin assistance' })
                });

                if (!res.ok) throw new Error('Escalate HTTP ' + res.status);
                const data = await res.json();

                if (data && data.success) {
                    if (data.botReply) {
                        appendMessage(data.botReply);
                    } else {
                        appendMessage({
                            senderType: 'Bot',
                            senderName: 'Fleetify AI Support',
                            messageText: '🚨 **Escalated!** You have connected to the Admin Support Desk. An administrator has been notified and will reply here directly.'
                        });
                    }
                    updateStatus(data.status || 'NeedsAdmin');
                } else {
                    alert('Could not escalate ticket: ' + (data?.message || 'Unknown error'));
                }
            } catch (err) {
                console.error('Escalation error:', err);
                alert('Network error while connecting to administrator. Please try again.');
            } finally {
                isEscalating = false;
                if (escalateBtn) {
                    escalateBtn.disabled = false;
                    escalateBtn.innerHTML = '<i class="bi bi-person-fill me-1"></i>Talk to Admin';
                }
            }
        }

        if (escalateBtn) {
            escalateBtn.addEventListener('click', (e) => {
                e.preventDefault();
                escalateToAdmin();
            });
        }

        function setSendButtonLoading(loading) {
            if (!sendBtn) return;
            if (loading) {
                sendBtn.disabled = true;
                sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" style="width: 14px; height: 14px;"></span>';
            } else {
                sendBtn.disabled = false;
                sendBtn.innerHTML = '<i class="bi bi-send-fill" style="font-size: 0.85rem;"></i>';
            }
        }

        function startPolling() {
            stopPolling();
            pollTimer = setInterval(async () => {
                if (!conversationId || !isOpen) return;
                try {
                    const res = await fetch('/api/support/poll/' + conversationId);
                    if (!res.ok) return;
                    const data = await res.json();
                    if (data && data.success && data.messages) {
                        updateStatus(data.status);
                        data.messages.forEach(msg => {
                            if (!knownMessageIds.has(msg.messageID)) {
                                appendMessage(msg);
                            }
                        });
                    }
                } catch (e) { }
            }, 3500);
        }

        function stopPolling() {
            if (pollTimer) {
                clearInterval(pollTimer);
                pollTimer = null;
            }
        }

        function renderMessages(messages) {
            msgContainer.innerHTML = '';
            knownMessageIds.clear();
            if (messages && messages.length) {
                messages.forEach(msg => appendMessage(msg));
            }
        }

        function appendMessage(msg) {
            if (msg.messageID && msg.messageID !== 'temp_welcome') {
                knownMessageIds.add(msg.messageID);
            }

            const isUser = msg.senderType === 'User';
            const isAdmin = msg.senderType === 'Admin';
            const isBot = msg.senderType === 'Bot';

            const wrapper = document.createElement('div');
            wrapper.className = `d-flex flex-column ${isUser ? 'align-items-end' : 'align-items-start'} mb-3`;

            let badgeHtml = '';
            let bubbleClass = '';

            if (isUser) {
                badgeHtml = `<small class="text-muted mb-1" style="font-size: 0.72rem;">You</small>`;
                bubbleClass = 'bg-primary text-white rounded-4 p-3 shadow-xs';
            } else if (isAdmin) {
                badgeHtml = `<small class="text-primary fw-bold mb-1" style="font-size: 0.72rem;"><i class="bi bi-shield-check me-1"></i>${escapeHtml(msg.senderName)}</small>`;
                bubbleClass = 'bg-primary-subtle text-dark border border-primary-subtle rounded-4 p-3 shadow-xs';
            } else {
                badgeHtml = `<small class="text-info fw-bold mb-1" style="font-size: 0.72rem;"><i class="bi bi-robot me-1"></i>AI Assistant</small>`;
                bubbleClass = 'bg-white text-dark border rounded-4 p-3 shadow-xs';
            }

            // Format markdown bold, italic and line breaks
            let formattedText = escapeHtml(msg.messageText || '')
                .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
                .replace(/\*(.*?)\*/g, '<em>$1</em>')
                .replace(/\n/g, '<br/>');

            wrapper.innerHTML = `
                ${badgeHtml}
                <div class="${bubbleClass}" style="max-width: 88%; font-size: 0.9rem; line-height: 1.45; word-break: break-word;">
                    ${formattedText}
                </div>
            `;

            msgContainer.appendChild(wrapper);
            scrollToBottom();
        }

        function updateStatus(status) {
            if (!statusBadge) return;
            if (status === 'NeedsAdmin') {
                statusBadge.className = 'badge bg-danger rounded-pill px-2 py-1';
                statusBadge.innerHTML = '<i class="bi bi-person-fill me-1"></i>Admin Assigned';
            } else if (status === 'InProgress') {
                statusBadge.className = 'badge bg-warning text-dark rounded-pill px-2 py-1';
                statusBadge.innerHTML = '<i class="bi bi-headset me-1"></i>Live Admin Chat';
            } else if (status === 'Resolved') {
                statusBadge.className = 'badge bg-success rounded-pill px-2 py-1';
                statusBadge.innerHTML = '<i class="bi bi-check2 me-1"></i>Resolved';
            } else {
                statusBadge.className = 'badge bg-info text-white rounded-pill px-2 py-1';
                statusBadge.innerHTML = '<i class="bi bi-robot me-1"></i>AI Online';
            }
        }

        function showTypingIndicator() {
            removeTypingIndicator();
            const indicator = document.createElement('div');
            indicator.id = 'chatTypingIndicator';
            indicator.className = 'd-flex align-items-center gap-1 text-muted small px-3 py-2 bg-light rounded-pill mb-2 align-self-start';
            indicator.innerHTML = '<i class="bi bi-three-dots fs-5"></i> <span>Fleetify AI is typing...</span>';
            msgContainer.appendChild(indicator);
            scrollToBottom();
        }

        function removeTypingIndicator() {
            const ind = document.getElementById('chatTypingIndicator');
            if (ind) ind.remove();
        }

        function scrollToBottom() {
            msgContainer.scrollTop = msgContainer.scrollHeight;
        }

        function escapeHtml(string) {
            const div = document.createElement('div');
            div.textContent = string;
            return div.innerHTML;
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initSupportWidget);
    } else {
        initSupportWidget();
    }
})();
