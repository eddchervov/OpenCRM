(function () {
    var root = document.querySelector('[data-ai-pilot]');
    if (!root) return;

    var apiBase = root.dataset.apiUrl || '';
    var tokenUrl = root.dataset.tokenUrl || '';
    var organizationId = root.dataset.organizationId || '';
    var requestToken = '';
    var context = null;
    var conversationId = null;
    var activeDraft = null;
    var lastMessageId = null;
    var busy = false;
    var transcriptionCount = 0;
    var recorder = null;
    var recordingChunks = [];
    var recordingBytes = 0;
    var recordingStartedAt = 0;
    var recordingTimer = null;
    var recordingStopping = false;
    var microphoneRequest = 0;
    var microphonePending = false;
    var pageUnloading = false;
    var toastTimer = null;
    var availabilityTimer = null;
    var speechAudio = null;
    var speechUrl = null;
    var maximumMessageCharacters = 0;
    var maximumAudioBytes = 0;
    var maximumAudioDurationSeconds = 0;

    var transcript = root.querySelector('[data-transcript]');
    var sendButton = root.querySelector('[data-prepare]');
    var confirmButton = root.querySelector('[data-execute]');
    var recordButton = root.querySelector('[data-record]');
    var chatLog = root.querySelector('[data-chat-messages]');
    var composerDialog = root.querySelector('[data-composer-dialog]');
    var draftDialog = root.querySelector('[data-draft-dialog]');
    var telegramManager = window.OpenCRMAssistantTelegram ? window.OpenCRMAssistantTelegram.create(root, request) : null;

    function conversationKey() {
        return context ? 'assistant:' + context.userId + ':' + organizationId : '';
    }

    function rememberConversation() {
        try {
            if (conversationId) sessionStorage.setItem(conversationKey(), conversationId);
            else sessionStorage.removeItem(conversationKey());
        } catch (error) { /* Хранение в браузере может быть запрещено. */ }
    }

    async function restoreConversation(targetId) {
        var storedId = targetId;
        if (!storedId) {
            try { storedId = sessionStorage.getItem(conversationKey()); } catch (error) { return; }
        }
        if (!storedId) return;
        try {
            var state = await request('conversations/' + encodeURIComponent(storedId) + '?organizationId=' + encodeURIComponent(organizationId));
            conversationId = storedId;
            rememberConversation();
            lastMessageId = null;
            clearDraft();
            chatLog.replaceChildren();
            (state.messages || []).forEach(function (message) {
                appendMessage(message.role, message.text);
                if (message.role === 'assistant') lastMessageId = message.id;
            });
            if (state.draft) renderDraft(state.draft, false);
            else if (state.clarification) renderClarification(state.clarification);
        } catch (error) {
            if (targetId) throw error;
            conversationId = null;
            rememberConversation();
        }
    }

    function apiUrl(path) {
        return apiBase.replace(/\/+$/, '') + '/' + String(path || '').replace(/^\/+/, '');
    }

    function showToast(message) {
        var toast = root.querySelector('[data-toast]');
        toast.textContent = message;
        var activeDialog = composerDialog.open ? composerDialog : draftDialog.open ? draftDialog : null;
        if (activeDialog) setDialogFeedback(activeDialog, message, false);
        toast.hidden = false;
        window.clearTimeout(toastTimer);
        toastTimer = window.setTimeout(function () { toast.hidden = true; }, 5000);
    }

    function errorText(payload, status) {
        var message = payload && typeof payload.message === 'string' ? payload.message : '';
        if (!message && payload && typeof payload.detail === 'string') message = payload.detail;
        if (message) return message;

        var statusMessages = {
            400: 'Проверьте сообщение и попробуйте ещё раз.',
            401: 'Сессия завершилась. Войдите в аккаунт и откройте помощника снова.',
            403: 'У помощника нет доступа к выбранной организации.',
            404: 'Действие или разговор больше не доступны.',
            409: 'Данные изменились или черновик устарел. Отправьте сообщение заново.',
            413: 'Файл превышает допустимый размер.',
            429: 'Превышен лимит запросов. Попробуйте позже.',
            502: 'Сервис распознавания или ИИ временно недоступен.',
            503: 'Помощник временно недоступен.'
        };
        return statusMessages[status] || 'Не удалось выполнить запрос. Попробуйте ещё раз.';
    }

    async function ensureRequestToken() {
        if (requestToken) return requestToken;
        var response = await fetch(tokenUrl, {
            credentials: 'same-origin',
            headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
        });
        var payload = {};
        try { payload = await response.json(); } catch (error) { payload = {}; }
        if (!response.ok || !payload.requestToken) {
            throw new Error(errorText(payload, response.status));
        }
        requestToken = payload.requestToken;
        return requestToken;
    }

    async function request(path, options) {
        var settings = Object.assign({}, options || {});
        var method = String(settings.method || 'GET').toUpperCase();
        var headers = Object.assign({ Accept: 'application/json' }, settings.headers || {});
        if (method !== 'GET' && method !== 'HEAD' && method !== 'OPTIONS') {
            headers.RequestVerificationToken = headers.RequestVerificationToken || await ensureRequestToken();
            if (settings.body && !(settings.body instanceof FormData) && typeof settings.body !== 'string') {
                headers['Content-Type'] = headers['Content-Type'] || 'application/json';
                settings.body = JSON.stringify(settings.body);
            }
        }

        var response = await fetch(apiUrl(path), Object.assign(settings, {
            credentials: 'same-origin',
            headers: headers
        }));
        if (!response.ok) {
            var payload = null;
            try { payload = await response.json(); } catch (error) { payload = null; }
            var apiError = new Error(errorText(payload, response.status));
            apiError.status = response.status;
            throw apiError;
        }
        if (method === 'POST' && /^(chat|choose|confirm|close)$/.test(path)) {
            root.dispatchEvent(new CustomEvent('assistant:conversation-updated'));
        }
        if (response.status === 204) return {};
        var text = await response.text();
        return text.trim() ? JSON.parse(text) : {};
    }

    async function postJson(path, body) {
        return request(path, { method: 'POST', body: body });
    }

    function setStatus(text) {
        root.querySelector('[data-draft-status]').textContent = text;
        root.querySelector('[data-conversation-status]').textContent = text;
    }

    function setDialogFeedback(dialog, message, isError) {
        var feedback = dialog.querySelector('[data-dialog-feedback]');
        feedback.textContent = message;
        feedback.hidden = !message;
        feedback.classList.toggle('is-error', isError);
    }

    function composerLocked() {
        return busy || microphonePending || recordingStopping || transcriptionCount > 0 || Boolean(recorder && recorder.state === 'recording');
    }

    function openComposerDialog() {
        if (!contextAvailable || composerLocked()) return;
        if (draftDialog.open) draftDialog.close();
        setDialogFeedback(composerDialog, '', false);
        if (!composerDialog.open) composerDialog.showModal();
        var textMode = root.querySelector('[data-tab="text"]').getAttribute('aria-selected') === 'true';
        if (textMode) transcript.focus();
        else recordButton.focus();
    }

    function openDraftDialog() {
        if (!activeDraft || !contextAvailable) return;
        if (composerDialog.open) composerDialog.close();
        if (!draftDialog.open) draftDialog.showModal();
    }

    function closeDialog(dialog) {
        if (dialog === composerDialog ? composerLocked() : busy) return;
        dialog.close();
    }

    function appendMessage(role, message) {
        if (!message || pageUnloading) return;
        var entry = document.createElement('div');
        entry.className = 'chat-message chat-' + role;
        var label = document.createElement('strong');
        label.textContent = role === 'user' ? 'Вы' : 'Помощник';
        var content = document.createElement('p');
        content.textContent = message;
        entry.append(label, content);
        chatLog.appendChild(entry);
        chatLog.scrollTop = chatLog.scrollHeight;
    }

    function formatAmount(value) {
        if (value === null || value === undefined || value === '') return 'Не указано';
        var amount = Number(value);
        return Number.isFinite(amount)
            ? new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(amount) + ' ₽'
            : String(value);
    }

    function renderData(parent, value, depth) {
        if (depth > 4 || value == null) return;
        if (Array.isArray(value)) {
            if (!value.length) {
                parent.textContent = 'Ничего не найдено.';
                return;
            }
            value.slice(0, 100).forEach(function (item) {
                var row = document.createElement('div');
                row.className = 'read-result-entry';
                renderData(row, item, depth + 1);
                parent.appendChild(row);
            });
            return;
        }
        if (typeof value === 'object') {
            Object.keys(value).forEach(function (key) {
                if (value[key] == null) return;
                var row = document.createElement('div');
                var label = document.createElement('strong');
                label.textContent = key + ': ';
                row.appendChild(label);
                renderData(row, value[key], depth + 1);
                parent.appendChild(row);
            });
            return;
        }
        parent.appendChild(document.createTextNode(String(value)));
    }

    function safeResultUrl(value) {
        if (!value) return '';
        try {
            var url = new URL(value, window.location.origin);
            return url.origin === window.location.origin ? url.href : '';
        } catch (error) {
            return '';
        }
    }

    function addResultLink(container, label, value) {
        var url = safeResultUrl(value);
        if (!url) return;
        var link = document.createElement('a');
        link.href = url;
        link.textContent = label;
        container.appendChild(link);
    }

    function renderResult(result) {
        var container = root.querySelector('[data-result]');
        container.replaceChildren();
        var message = document.createElement('p');
        message.className = 'result-message';
        message.textContent = result.message || 'Действие выполнено.';
        container.appendChild(message);

        var links = document.createElement('div');
        links.className = 'result-links';
        addResultLink(links, 'Открыть обслуживание', result.maintenanceUrl);
        addResultLink(links, 'Открыть автомобиль', result.vehicleUrl);
        addResultLink(links, 'Открыть клиента', result.clientUrl);
        addResultLink(links, 'Открыть раздел', result.url);
        if (links.childElementCount) container.appendChild(links);
        container.hidden = false;
        appendMessage('assistant', message.textContent);
    }

    function clearDraft() {
        activeDraft = null;
        if (draftDialog.open) draftDialog.close();
        setDialogFeedback(draftDialog, '', false);
        root.querySelector('[data-pending-action]').hidden = true;
        confirmButton.disabled = true;
        root.querySelector('[data-draft]').hidden = true;
        root.querySelector('[data-draft-empty]').hidden = false;
        root.querySelector('[data-result]').hidden = true;
        root.querySelector('[data-clarification]').hidden = true;
        root.querySelector('[data-clarification-choices]').replaceChildren();
        root.querySelector('[data-plan-changes]').replaceChildren();
        root.querySelector('[data-read-result]').replaceChildren();
        root.querySelector('[data-destructive-confirm]').hidden = true;
        root.querySelector('[data-confirm-destructive]').checked = false;
        setStatus('Ожидает команды');
    }

    function renderDraft(draft, showDialog) {
        activeDraft = draft;
        setDialogFeedback(draftDialog, '', false);
        root.querySelector('[data-pending-target]').textContent = draft.target || draft.summary || '';
        root.querySelector('[data-draft-empty]').hidden = true;
        root.querySelector('[data-result]').hidden = true;
        root.querySelector('[data-clarification]').hidden = true;
        root.querySelector('[data-summary]').textContent = draft.summary || '';
        root.querySelector('[data-plan-target]').textContent = draft.target || 'Действие для выбранной организации';
        var changes = root.querySelector('[data-plan-changes]');
        changes.replaceChildren();
        (draft.changes || []).forEach(function (change) {
            var item = document.createElement('li');
            item.textContent = change;
            changes.appendChild(item);
        });
        (draft.warnings || []).forEach(function (warning) {
            var item = document.createElement('li');
            item.textContent = warning;
            changes.appendChild(item);
        });
        changes.hidden = changes.childElementCount === 0;

        var confirmation = root.querySelector('[data-destructive-confirm]');
        confirmation.hidden = !draft.destructive;
        root.querySelector('[data-confirm-destructive]').checked = false;
        var data = root.querySelector('[data-read-result]');
        data.replaceChildren();
        data.hidden = draft.data == null;
        if (!data.hidden) renderData(data, draft.data, 0);

        root.querySelector('[data-draft]').hidden = false;
        confirmButton.textContent = draft.readOnly ? 'Показать' : 'Подтвердить';
        setStatus(draft.readOnly ? 'Проверьте результат' : 'Проверьте черновик');
        updateControls();
        if (showDialog !== false) openDraftDialog();
    }

    function renderClarification(clarification) {
        clearDraft();
        if (!clarification) return;
        root.querySelector('[data-draft-empty]').hidden = false;
        setStatus('Нужно уточнение');
        root.querySelector('[data-clarification-message]').textContent = clarification.message || 'Уточните выбор';
        var choices = root.querySelector('[data-clarification-choices]');
        choices.replaceChildren();
        (clarification.choices || []).forEach(function (choice) {
            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'button button-secondary';
            button.textContent = choice.label;
            button.addEventListener('click', function () {
                choose(clarification.field, choice.value);
            });
            choices.appendChild(button);
        });
        root.querySelector('[data-clarification]').hidden = choices.childElementCount === 0;
    }

    function updateControls() {
        var isRecording = recorder && recorder.state === 'recording';
        var blocked = busy || !context || !contextAvailable;
        root.querySelector('[data-open-composer]').disabled = blocked || composerLocked();
        root.querySelector('[data-open-draft]').disabled = blocked;
        root.querySelector('[data-pending-action]').hidden = !activeDraft;
        root.querySelectorAll('[data-close-dialog]').forEach(function (button) {
            button.disabled = button.closest('dialog') === composerDialog ? composerLocked() : busy;
        });
        var telegramButton = root.querySelector('[data-telegram-connect]');
        telegramButton.hidden = !contextAvailable || !context;
        telegramButton.disabled = blocked || !telegramManager;
        if (telegramManager) telegramManager.setAvailability({
            enabled: !blocked,
            canConnect: Boolean(context && context.telegramAvailable && context.telegramConfigured)
        });
        sendButton.disabled = blocked || composerLocked()
            || !transcript.value.trim() || transcript.value.length > maximumMessageCharacters;
        confirmButton.disabled = blocked || !activeDraft
            || (activeDraft.destructive && !root.querySelector('[data-confirm-destructive]').checked);
        root.querySelector('[data-new-conversation]').disabled = composerLocked();
        recordButton.disabled = recordingStopping || transcriptionCount > 0 || !isRecording && !microphonePending && blocked;
        root.querySelector('[data-audio-file]').disabled = blocked || composerLocked();
        root.querySelectorAll('[data-tab], [data-example]').forEach(function (element) {
            element.disabled = composerLocked();
        });
        root.querySelectorAll('[data-clarification-choices] button').forEach(function (element) {
            element.disabled = busy;
        });
        transcript.readOnly = busy;
    }

    var contextAvailable = false;

    function setPane(name) {
        root.querySelectorAll('[data-tab]').forEach(function (tab) {
            var active = tab.dataset.tab === name;
            tab.classList.toggle('is-active', active);
            tab.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        root.querySelectorAll('[data-pane]').forEach(function (pane) {
            pane.hidden = pane.dataset.pane !== name;
        });
        if (name === 'text') transcript.focus();
    }

    function syncText(value) {
        transcript.value = value;
        if (activeDraft) {
            clearDraft();
            setStatus('Черновик устарел · отправьте новое сообщение');
        }
        updateControls();
    }

    function handleReply(reply) {
        if (reply.conversationId) conversationId = reply.conversationId;
        rememberConversation();
        if (reply.message) appendMessage('assistant', reply.message);
        if (reply.messageId) {
            lastMessageId = reply.messageId;
            if (root.querySelector('[data-speak-replies]').checked) speakMessage(reply.messageId, null);
        }
        if (reply.draft) {
            renderDraft(reply.draft);
            if (reply.cost != null) showToast('Черновик подготовлен · запрос ' + formatAmount(reply.cost));
        } else if (reply.clarification) {
            renderClarification(reply.clarification);
        } else {
            clearDraft();
            setStatus(reply.awaitingAnswer ? 'Ждёт вашего ответа' : 'Ожидает команды');
            if (reply.cost != null) showToast('Ответ помощника · запрос ' + formatAmount(reply.cost));
        }
    }

    async function withBusy(button, busyText, action) {
        if (busy) return;
        busy = true;
        root.dispatchEvent(new CustomEvent('assistant:busy', { detail: { busy: true } }));
        if (button) {
            button.dataset.originalText = button.textContent;
            button.textContent = busyText;
        }
        updateControls();
        try {
            return await action();
        } finally {
            busy = false;
            root.dispatchEvent(new CustomEvent('assistant:busy', { detail: { busy: transcriptionCount > 0 } }));
            if (button && button.dataset.originalText) {
                button.textContent = button.dataset.originalText;
                delete button.dataset.originalText;
            }
            updateControls();
        }
    }

    async function sendMessage() {
        var text = transcript.value.trim();
        if (!text || text.length > maximumMessageCharacters || composerLocked()) return;
        stopSpeaking();
        clearDraft();
        appendMessage('user', text);
        setStatus('Помощник отвечает');
        setDialogFeedback(composerDialog, '', false);
        try {
            await withBusy(sendButton, 'Отвечаем…', async function () {
                var reply = await postJson('chat', {
                    organizationId: organizationId || null,
                    text: text,
                    conversationId: conversationId
                });
                transcript.value = '';
                root.querySelector('[data-transcribe-cost]').textContent = '';
                if (composerDialog.open) composerDialog.close();
                handleReply(reply);
            });
            if (!draftDialog.open && !composerDialog.open) root.querySelector('[data-open-composer]').focus();
        } catch (error) {
            setStatus('Не удалось получить ответ');
            setDialogFeedback(composerDialog, error.message || 'Не удалось отправить сообщение.', true);
            appendMessage('assistant', error.message || 'Не удалось получить ответ. Повторите сообщение.');
        }
    }

    async function choose(field, value) {
        if (busy || !conversationId) return;
        try {
            await withBusy(null, '', async function () {
                var reply = await postJson('choose', {
                    organizationId: organizationId || null,
                    conversationId: conversationId,
                    field: field,
                    value: value
                });
                handleReply(reply);
            });
        } catch (error) {
            setStatus('Не удалось применить выбор');
            appendMessage('assistant', error.message || 'Выбор устарел. Отправьте сообщение заново.');
            root.querySelector('[data-clarification]').hidden = true;
        }
    }

    async function confirmDraft() {
        if (!activeDraft || busy) return;
        var draft = activeDraft;
        try {
            await withBusy(confirmButton, 'Выполняем…', async function () {
                var result = await postJson('confirm', {
                    organizationId: organizationId || null,
                    draftId: draft.id,
                    confirmed: !draft.destructive || root.querySelector('[data-confirm-destructive]').checked
                });
                activeDraft = null;
                root.querySelector('[data-draft]').hidden = true;
                setStatus('Выполнено');
                renderResult(result);
                root.dispatchEvent(new CustomEvent('assistant:action-completed'));
                if (root.querySelector('[data-speak-replies]').checked) speakMessage(null, draft.id);
            });
        } catch (error) {
            setStatus('Не удалось выполнить');
            setDialogFeedback(draftDialog, error.message || 'Не удалось выполнить действие.', true);
            appendMessage('assistant', error.message || 'Не удалось выполнить действие.');
        }
    }

    function stopSpeaking() {
        if (speechAudio) {
            speechAudio.pause();
            speechAudio = null;
        }
        if (speechUrl) {
            URL.revokeObjectURL(speechUrl);
            speechUrl = null;
        }
    }

    async function speakMessage(messageId, draftId) {
        if (!context || !context.serverSpeechEnabled || !root.querySelector('[data-speak-replies]').checked) return;
        stopSpeaking();
        try {
            var response = await fetch(apiUrl('speech'), {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    Accept: 'audio/*',
                    'Content-Type': 'application/json',
                    RequestVerificationToken: await ensureRequestToken()
                },
                body: JSON.stringify({
                    organizationId: organizationId || null,
                    messageId: messageId || null,
                    draftId: draftId || null
                })
            });
            if (!response.ok) {
                var errorPayload = null;
                try { errorPayload = await response.json(); } catch (error) { errorPayload = null; }
                throw new Error(errorText(errorPayload, response.status));
            }
            var audio = await response.blob();
            speechUrl = URL.createObjectURL(audio);
            speechAudio = new Audio(speechUrl);
            speechAudio.addEventListener('ended', stopSpeaking, { once: true });
            await speechAudio.play();
        } catch (error) {
            if (!pageUnloading) showToast(error.message || 'Не удалось озвучить ответ.');
        }
    }

    function showTranscript(text, cost) {
        syncText(text || '');
        root.querySelector('[data-transcribe-cost]').textContent = cost == null
            ? ''
            : 'Распознавание: ' + formatAmount(cost);
        transcript.focus();
    }

    async function transcribe(blob, filename) {
        if (busy || !maximumAudioBytes || blob.size > maximumAudioBytes) {
            showToast('Аудиофайл превышает допустимый размер.');
            return;
        }
        transcriptionCount += 1;
        root.dispatchEvent(new CustomEvent('assistant:busy', { detail: { busy: true } }));
        root.querySelector('[data-record-title]').textContent = 'Распознаём речь';
        updateControls();
        try {
            var form = new FormData();
            form.append('audio', blob, filename || 'voice-recording.webm');
            var query = new URLSearchParams({ organizationId: organizationId });
            var result = await request('transcribe?' + query.toString(), { method: 'POST', body: form });
            showTranscript(result.text || '', result.cost);
            root.querySelector('[data-record-title]').textContent = 'Голос распознан';
        } catch (error) {
            showToast(error.message || 'Не удалось распознать аудио.');
        } finally {
            transcriptionCount = Math.max(0, transcriptionCount - 1);
            root.dispatchEvent(new CustomEvent('assistant:busy', { detail: { busy: busy || transcriptionCount > 0 } }));
            if (!transcriptionCount) root.querySelector('[data-record-title]').textContent = 'Записать голосовое';
            updateControls();
        }
    }

    function applyLimits(limits) {
        maximumMessageCharacters = Number(limits.maximumMessageCharacters) || 0;
        maximumAudioBytes = Number(limits.maximumAudioBytes) || 0;
        maximumAudioDurationSeconds = Number(limits.maximumAudioDurationSeconds) || 0;

        if (!maximumMessageCharacters || !maximumAudioBytes || !maximumAudioDurationSeconds) {
            throw new Error('Сервер не передал лимиты помощника.');
        }
        transcript.maxLength = maximumMessageCharacters;
        root.querySelector('[data-audio-limit]').textContent = 'Максимум ' +
            new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(maximumAudioBytes / (1024 * 1024)) +
            ' МБ · ' + maximumAudioDurationSeconds + ' с';
    }

    async function refreshAvailability() {
        if (!organizationId || busy || transcriptionCount > 0 || root.dataset.dashboardBusy === 'true') return;
        try {
            var query = new URLSearchParams({ organizationId: organizationId });
            var availability = await request('availability?' + query.toString());
            contextAvailable = availability.visible === true && availability.enabled === true;
            if (!contextAvailable) {
                setStatus('Помощник отключён для этой организации');
                root.querySelector('[data-organization]').textContent = 'Доступ отключён';
                stopSpeaking();
            }
            updateControls();
        } catch (error) {
            contextAvailable = false;
            updateControls();
        }
    }

    async function initialize() {
        try {
            var query = organizationId ? '?organizationId=' + encodeURIComponent(organizationId) : '';
            context = await request('context' + query);
            if (!organizationId) organizationId = context.organizationId || '';
            applyLimits(context.limits || {});
            contextAvailable = true;
            root.querySelector('[data-organization]').textContent = context.organizationName || 'Рабочее пространство';
            var speechOption = root.querySelector('[data-speak-replies]');
            speechOption.disabled = context.serverSpeechEnabled !== true;
            if (!context.serverSpeechEnabled) speechOption.checked = false;

            var actionsList = root.querySelector('[data-actions-list]');
            actionsList.replaceChildren();
            (context.actions || []).forEach(function (action) {
                var entry = document.createElement('div');
                var title = document.createElement('strong');
                var description = document.createElement('p');
                title.textContent = action.title || action.action || 'Действие';
                description.textContent = action.description || '';
                entry.append(title, description);
                actionsList.appendChild(entry);
            });

            await restoreConversation();
            var pollingSeconds = Number(context.limits.availabilityPollingSeconds);
            if (Number.isFinite(pollingSeconds) && pollingSeconds > 0) {
                availabilityTimer = window.setInterval(refreshAvailability, pollingSeconds * 1000);
                window.addEventListener('focus', refreshAvailability);
            }
            updateControls();
            root.dispatchEvent(new CustomEvent('assistant:ready'));
        } catch (error) {
            contextAvailable = false;
            root.querySelector('[data-organization]').textContent = 'Не удалось загрузить';
            showToast(error.message || 'Не удалось загрузить помощника.');
            updateControls();
        }
    }

    function setRecordingState(recording) {
        recordButton.classList.toggle('is-recording', recording);
        recordButton.setAttribute('aria-label', recording ? 'Остановить запись голоса' : 'Начать запись голоса');
        root.querySelector('[data-record-title]').textContent = recording
            ? 'Идёт запись — нажмите, чтобы остановить'
            : 'Записать голосовое';
    }

    function stopRecording() {
        if (recorder && recorder.state === 'recording') {
            recordingStopping = true;
            recorder.stop();
        }
        window.clearInterval(recordingTimer);
        root.querySelector('[data-record-timer]').hidden = true;
        setRecordingState(false);
        if (recordingStopping) root.querySelector('[data-record-title]').textContent = 'Завершаем запись…';
        updateControls();
    }

    function startRecording() {
        if (recordingStopping) return;
        if (microphonePending) {
            microphoneRequest += 1;
            microphonePending = false;
            setRecordingState(false);
            updateControls();
            return;
        }
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !window.MediaRecorder) {
            showToast('Запись голоса недоступна в этом браузере. Загрузите аудио или введите сообщение.');
            return;
        }

        var requestId = ++microphoneRequest;
        microphonePending = true;
        setRecordingState(true);
        updateControls();
        navigator.mediaDevices.getUserMedia({ audio: true }).then(function (stream) {
            if (requestId !== microphoneRequest || pageUnloading) {
                stream.getTracks().forEach(function (track) { track.stop(); });
                return;
            }
            recorder = new MediaRecorder(stream);
            recordingChunks = [];
            recordingBytes = 0;
            recordingStartedAt = Date.now();
            recorder.addEventListener('dataavailable', function (event) {
                if (!event.data.size) return;
                recordingChunks.push(event.data);
                recordingBytes += event.data.size;
                if (maximumAudioBytes && recordingBytes > maximumAudioBytes) {
                    showToast('Достигнут предел размера записи.');
                    stopRecording();
                }
            });
            recorder.addEventListener('stop', function () {
                stream.getTracks().forEach(function (track) { track.stop(); });
                if (pageUnloading) return;
                var mime = (recorder.mimeType || 'audio/webm').split(';')[0].toLowerCase();
                var extensions = {
                    'audio/mp4': 'mp4',
                    'audio/ogg': 'ogg',
                    'audio/webm': 'webm',
                    'audio/mpeg': 'mp3',
                    'audio/wav': 'wav',
                    'audio/x-wav': 'wav'
                };
                var extension = extensions[mime] || 'webm';
                recordingStopping = false;
                transcribe(new Blob(recordingChunks, { type: mime }), 'voice-recording.' + extension);
                updateControls();
            }, { once: true });
            recorder.start(1000);
            microphonePending = false;
            setRecordingState(true);
            updateControls();
            recordingTimer = window.setInterval(function () {
                var seconds = Math.floor((Date.now() - recordingStartedAt) / 1000);
                var timer = root.querySelector('[data-record-timer]');
                timer.textContent = String(Math.floor(seconds / 60)).padStart(2, '0') + ':' + String(seconds % 60).padStart(2, '0');
                timer.hidden = false;
                if (maximumAudioDurationSeconds && seconds >= maximumAudioDurationSeconds) {
                    showToast('Достигнут предел длительности записи.');
                    stopRecording();
                }
            }, 250);
        }).catch(function () {
            if (requestId !== microphoneRequest) return;
            microphonePending = false;
            setRecordingState(false);
            showToast('Не удалось включить микрофон. Проверьте разрешение браузера или загрузите аудио.');
            updateControls();
        });
    }

    root.querySelector('[data-open-composer]').addEventListener('click', openComposerDialog);
    root.querySelector('[data-open-draft]').addEventListener('click', openDraftDialog);
    root.querySelectorAll('[data-close-dialog]').forEach(function (button) {
        button.addEventListener('click', function () { closeDialog(button.closest('dialog')); });
    });
    [composerDialog, draftDialog].forEach(function (dialog) {
        dialog.addEventListener('cancel', function (event) {
            if (dialog === composerDialog ? composerLocked() : busy) event.preventDefault();
        });
        dialog.addEventListener('click', function (event) {
            if (event.target !== dialog) return;
            var bounds = dialog.getBoundingClientRect();
            if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) closeDialog(dialog);
        });
    });

    root.querySelectorAll('[data-tab]').forEach(function (tab) {
        tab.addEventListener('click', function () { setPane(tab.dataset.tab); });
    });
    transcript.addEventListener('input', function () { syncText(transcript.value); });
    sendButton.addEventListener('click', sendMessage);
    confirmButton.addEventListener('click', confirmDraft);
    root.querySelector('[data-confirm-destructive]').addEventListener('change', updateControls);
    root.querySelectorAll('[data-example]').forEach(function (button) {
        button.addEventListener('click', function () {
            setPane('text');
            syncText(button.dataset.example || '');
            transcript.focus();
        });
    });
    root.querySelector('[data-audio-file]').addEventListener('change', function (event) {
        var file = event.target.files && event.target.files[0];
        event.target.value = '';
        if (!file) return;
        if (!maximumAudioBytes || file.size > maximumAudioBytes) {
            showToast('Аудиофайл превышает допустимый размер.');
            return;
        }
        transcribe(file, file.name);
    });
    recordButton.addEventListener('click', function () {
        if (recorder && recorder.state === 'recording') stopRecording();
        else startRecording();
    });
    root.addEventListener('assistant:resume-conversation', async function (event) {
        var id = event.detail && event.detail.id;
        if (!id || busy || transcriptionCount > 0) return;
        try {
            await withBusy(null, '', async function () {
                stopSpeaking();
                await restoreConversation(id);
                transcript.value = '';
                updateControls();
            });
        } catch (error) {
            showToast(error.message || 'Не удалось продолжить разговор.');
        }
    });
    root.querySelector('[data-new-conversation]').addEventListener('click', async function () {
        if (busy) return;
        var button = this;
        await withBusy(button, 'Закрываем…', async function () {
            if (conversationId) {
                await postJson('close', { organizationId: organizationId || null, conversationId: conversationId });
            }
            stopSpeaking();
            conversationId = null;
            rememberConversation();
            lastMessageId = null;
            chatLog.replaceChildren();
            transcript.value = '';
            root.querySelector('[data-transcribe-cost]').textContent = '';
            clearDraft();
            appendMessage('assistant', 'Новая беседа. Расскажите про автомобиль и работы — я уточню недостающее.');
        }).catch(function (error) {
            showToast(error.message || 'Не удалось закрыть беседу.');
        });
    });
    root.querySelector('[data-speak-replies]').addEventListener('change', function () {
        if (!this.checked) {
            stopSpeaking();
            return;
        }
        if (lastMessageId) speakMessage(lastMessageId, null);
    });

    window.addEventListener('beforeunload', function () {
        pageUnloading = true;
        window.clearInterval(recordingTimer);
        window.clearInterval(availabilityTimer);
        window.clearTimeout(toastTimer);
        window.removeEventListener('focus', refreshAvailability);
        stopSpeaking();
        microphoneRequest += 1;
        if (recorder && recorder.state !== 'inactive') recorder.stop();
    });

    initialize();
}());
