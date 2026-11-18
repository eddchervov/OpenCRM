(function () {
    var root = document.querySelector('[data-ai-pilot]');
    if (!root) return;

    var uiMessages = JSON.parse(root.dataset.uiMessages || '{}');
    function uiText(key, parameters) {
        return (uiMessages[key] || key).replace(/\{(\w+)\}/g, function (_, name) {
            return parameters && parameters[name] !== undefined ? parameters[name] : '{' + name + '}';
        });
    }


    var apiBase = root.dataset.apiUrl || '';
    var organizationId = root.dataset.organizationId || '';
    var tabs = Array.from(root.querySelectorAll('[data-assistant-tab]'));
    var panels = Array.from(root.querySelectorAll('[data-assistant-panel]'));
    var activeTab = 'conversation';
    var initialized = false;
    var historyLoaded = false;
    var usageLoaded = false;
    var historyPage = 1;
    var historyPageSize = 20;
    var selectedConversationId = '';
    var messagePage = 1;
    var messagePageSize = 50;
    var messageTotalCount = 0;
    var usageDefault = null;
    var isBusy = false;
    var dashboardBusy = false;
    var assistantBusy = false;

    var historyStatus = root.querySelector('[data-history-status]');
    var historyContent = root.querySelector('[data-history-content]');
    var historyList = root.querySelector('[data-history-list]');
    var historyMessages = root.querySelector('[data-history-messages]');
    var historyChannel = root.querySelector('[data-history-channel]');
    var historyPrev = root.querySelector('[data-history-prev]');
    var historyNext = root.querySelector('[data-history-next]');
    var messagePrev = root.querySelector('[data-history-message-prev]');
    var messageNext = root.querySelector('[data-history-message-next]');
    var historyResume = root.querySelector('[data-history-resume]');
    var usageStatus = root.querySelector('[data-usage-status]');
    var usageContent = root.querySelector('[data-usage-content]');
    var usageFrom = root.querySelector('[data-usage-from]');
    var usageTo = root.querySelector('[data-usage-to]');
    var usageChannel = root.querySelector('[data-usage-channel]');
    var capabilitiesDialog = root.querySelector('[data-capabilities-dialog]');

    function endpoint(path) {
        return apiBase.replace(/\/+$/, '') + '/' + String(path || '').replace(/^\/+/, '');
    }

    async function getJson(path) {
        var response = await fetch(endpoint(path), {
            credentials: 'same-origin',
            headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
        });
        var payload = {};
        try { payload = await response.json(); } catch (error) { payload = {}; }
        if (!response.ok) {
            throw new Error(payload.message || uiText('loadDataFailed'));
        }

        return payload;
    }

    function params(values) {
        var query = new URLSearchParams({ organizationId: organizationId });
        Object.keys(values).forEach(function (key) {
            if (values[key] !== undefined && values[key] !== null && values[key] !== '') query.set(key, String(values[key]));
        });

        return query.toString();
    }

    function setTab(name) {
        activeTab = name;
        tabs.forEach(function (tab) {
            var selected = tab.dataset.assistantTab === name;
            tab.classList.toggle('is-active', selected);
            tab.setAttribute('aria-selected', String(selected));
            tab.tabIndex = selected ? 0 : -1;
        });
        panels.forEach(function (panel) { panel.hidden = panel.dataset.assistantPanel !== name; });

        if (!initialized || isBusy) return;
        if (name === 'history' && !historyLoaded) void loadHistory();
        if (name === 'usage' && !usageLoaded) void loadUsage();
    }

    function setBusy(value) {
        dashboardBusy = value;
        root.dataset.dashboardBusy = String(dashboardBusy);
        updateBusyState();
    }

    function updateBusyState() {
        isBusy = dashboardBusy || assistantBusy;
        tabs.forEach(function (tab) { tab.disabled = isBusy || !initialized; });
        root.querySelector('[data-history-refresh]').disabled = isBusy || !initialized;
        root.querySelector('[data-history-channel]').disabled = isBusy || !initialized;
        root.querySelector('[data-usage-refresh]').disabled = isBusy || !initialized;
        root.querySelector('[data-usage-apply]').disabled = isBusy || !initialized;
        root.querySelectorAll('[data-usage-from], [data-usage-to], [data-usage-channel]').forEach(function (input) {
            input.disabled = isBusy || !initialized;
            input.dispatchEvent(new Event('opencrm:field-sync'));
        });
        root.querySelectorAll('.history-pagination button').forEach(function (button) {
            button.disabled = isBusy || button.dataset.unavailable === 'true';
        });
    }

    function formatDate(value, includeTime) {
        if (!value) return '—';
        var dateOnly = !includeTime && /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
        var date = dateOnly
            ? new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]))
            : new Date(value);
        if (Number.isNaN(date.getTime())) return '—';
        return new Intl.DateTimeFormat(root.dataset.formatCulture || 'ru-RU', includeTime
            ? { dateStyle: 'short', timeStyle: 'short', timeZone: root.dataset.timeZoneId || 'UTC' }
            : { dateStyle: 'short' }).format(date);
    }

    function formatMoney(value) {
        if (value === null || value === undefined || !Number.isFinite(Number(value))) return uiText('unknown');
        return new Intl.NumberFormat(root.dataset.formatCulture || 'ru-RU', { style: 'currency', currency: 'RUB', currencyDisplay: 'code', maximumFractionDigits: 2 }).format(Number(value));
    }

    function formatNumber(value) {
        return new Intl.NumberFormat(root.dataset.formatCulture || 'ru-RU').format(Number(value) || 0);
    }

    function setMessage(element, message, isError) {
        element.textContent = message || '';
        element.hidden = !message;
        element.classList.toggle('is-error', Boolean(isError));
    }

    async function loadHistory() {
        if (isBusy) return;
        setBusy(true);
        setMessage(historyStatus, uiText('loadingHistoryNow'), false);
        try {
            var result = await getJson('history?' + params({ page: historyPage, channel: historyChannel.value }));
            historyPage = result.page || 1;
            historyPageSize = result.pageSize || historyPageSize;
            historyContent.hidden = false;
            root.querySelector('[data-history-retention]').textContent = result.retentionDays
                ? uiText('retention', { count: formatNumber(result.retentionDays) })
                : '';
            renderHistoryItems(result.items || []);
            renderPagination('history', historyPage, historyPageSize, result.totalCount, function (page) {
                historyPage = page;
                void loadHistory();
            });
            setMessage(historyStatus, result.items && result.items.length ? '' : uiText('noConversations'), false);
            historyLoaded = true;
            var conversationId = '';
            if (selectedConversationId && result.items.some(function (item) { return item.id === selectedConversationId; })) {
                conversationId = selectedConversationId;
            } else if (result.items && result.items.length) {
                conversationId = result.items[0].id;
            } else {
                selectedConversationId = '';
                resetHistoryDetail();
            }
            setBusy(false);
            if (conversationId) await loadConversation(conversationId, conversationId === selectedConversationId ? messagePage : 1);
        } catch (error) {
            setMessage(historyStatus, error.message || uiText('loadHistoryFailed'), true);
        } finally {
            setBusy(false);
        }
    }

    function renderPagination(prefix, page, pageSize, totalCount, navigate) {
        var pageCount = Math.max(1, Math.ceil((totalCount || 0) / pageSize));
        var pagination = root.querySelector('[data-' + prefix + '-pagination]');
        pagination.hidden = pageCount <= 1;
        var previous = root.querySelector('[data-' + prefix + '-prev]');
        var next = root.querySelector('[data-' + prefix + '-next]');
        previous.dataset.unavailable = String(page <= 1);
        next.dataset.unavailable = String(page >= pageCount);
        var numbers = root.querySelector('[data-' + prefix + '-page]');
        numbers.replaceChildren();
        if (pageCount <= 1) return;
        var start = Math.max(1, Math.min(page - 1, pageCount - 2));
        var end = Math.min(pageCount, start + 2);
        for (var number = start; number <= end; number++) {
            var button = document.createElement('button');
            button.type = 'button';
            button.textContent = String(number);
            button.dataset.page = String(number);
            button.setAttribute('aria-label', uiText('page', { number: number }));
            if (number === page) button.setAttribute('aria-current', 'page');
            button.addEventListener('click', function (event) {
                if (!isBusy) navigate(Number(event.currentTarget.dataset.page));
            });
            numbers.appendChild(button);
        }
    }

    function renderHistoryItems(items) {
        historyList.replaceChildren();
        items.forEach(function (item) {
            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'history-item' + (item.id === selectedConversationId ? ' is-selected' : '');
            button.dataset.conversationId = item.id;
            button.setAttribute('aria-current', item.id === selectedConversationId ? 'true' : 'false');

            var heading = document.createElement('strong');
            heading.textContent = item.userName || uiText('employee');
            var meta = document.createElement('span');
            meta.textContent = (item.channel === 'telegram' ? 'Telegram' : uiText('web')) + ' · ' + formatDate(item.updatedAt || item.createdAt, true);
            var preview = document.createElement('p');
            preview.textContent = item.preview || uiText('emptyConversation');
            button.append(heading, meta, preview);
            button.addEventListener('click', function () { void loadConversation(item.id, 1); });
            historyList.appendChild(button);
        });
    }

    function resetHistoryDetail() {
        root.querySelector('[data-history-title]').textContent = uiText('selectConversation');
        root.querySelector('[data-history-meta]').textContent = '';
        historyMessages.replaceChildren();
        historyResume.hidden = true;
        messagePrev.disabled = true;
        messageNext.disabled = true;
        root.querySelector('[data-history-message-pagination]').hidden = true;
        root.querySelector('[data-history-message-page]').replaceChildren();
    }

    async function loadConversation(id, page) {
        if (isBusy) return;
        selectedConversationId = id;
        messagePage = page;
        setBusy(true);
        resetHistoryDetail();
        try {
            var result = await getJson('history/' + encodeURIComponent(id) + '?' + params({ page: messagePage }));
            messagePage = result.page || 1;
            messagePageSize = result.pageSize || messagePageSize;
            messageTotalCount = result.totalCount || 0;
            root.querySelector('[data-history-title]').textContent = result.userName || uiText('employee');
            root.querySelector('[data-history-meta]').textContent = (result.channel === 'telegram' ? 'Telegram' : uiText('web')) +
                ' · ' + formatDate(result.createdAt, true) + (result.isClosed ? uiText('closedSuffix') : '');
            historyMessages.replaceChildren();
            (result.messages || []).forEach(function (message) {
                var bubble = document.createElement('article');
                bubble.className = 'history-message ' + (message.role === 'user' ? 'is-user' : 'is-assistant');
                var role = document.createElement('strong');
                role.textContent = message.role === 'user' ? uiText('employee') : uiText('assistant');
                var text = document.createElement('p');
                text.textContent = message.text || '';
                var time = document.createElement('time');
                time.textContent = formatDate(message.createdAt, true);
                bubble.append(role, text, time);
                historyMessages.appendChild(bubble);
            });
            renderPagination('history-message', messagePage, messagePageSize, messageTotalCount, function (page) {
                void loadConversation(selectedConversationId, page);
            });
            historyResume.hidden = result.canResume !== true;
            historyResume.dataset.conversationId = result.id;
            Array.from(historyList.querySelectorAll('.history-item')).forEach(function (button) {
                var selected = button.dataset.conversationId === id;
                button.classList.toggle('is-selected', selected);
                button.setAttribute('aria-current', String(selected));
            });
        } catch (error) {
            setMessage(historyStatus, error.message || uiText('openConversationFailed'), true);
        } finally {
            setBusy(false);
        }
    }

    function usageCards(scope, day, month, limits) {
        var card = function (label, value, detail) {
            var element = document.createElement('div');
            element.className = 'usage-card';
            var title = document.createElement('span');
            title.textContent = label;
            var amount = document.createElement('strong');
            amount.textContent = value;
            element.append(title, amount);
            if (detail) {
                var note = document.createElement('small');
                note.textContent = detail;
                element.appendChild(note);
            }

            return element;
        };
        var budgets = root.querySelector('[data-usage-budgets]');
        budgets.replaceChildren(
            card(uiText('todayCost'), formatMoney(day.costRub), uiText('organizationLimit', { amount: formatMoney(limits.dailyBudgetRub) })),
            card(uiText('dayRemaining'), formatMoney(day.remainingRub), uiText('requestCount', { count: formatNumber(day.requestCount) })),
            card(uiText('monthCost'), formatMoney(month.costRub), uiText('organizationLimit', { amount: formatMoney(limits.monthlyBudgetRub) })),
            card(uiText('monthRemaining'), formatMoney(month.remainingRub), uiText('withReserves') + (month.unknownCount ? uiText('unknownCount', { count: formatNumber(month.unknownCount) }) : ''))
        );
        if (scope === 'self') {
            var note = document.createElement('p');
            note.className = 'field-help';
            note.textContent = uiText('personalUsageHelp');
            budgets.appendChild(note);
        }
    }

    function usageSummary(totals) {
        var summary = root.querySelector('[data-usage-summary]');
        var values = [
            [uiText('actualCost'), formatMoney(totals.costRub)],
            [uiText('reservedCost'), formatMoney(totals.reservedRub)],
            [uiText('requests'), formatNumber(totals.requestCount)],
            [uiText('inputTokens'), formatNumber(totals.inputTokens)],
            [uiText('outputTokens'), formatNumber(totals.outputTokens)],
            [uiText('unknownCosts'), formatNumber(totals.unknownCount)]
        ];
        summary.replaceChildren();
        values.forEach(function (item) {
            var card = document.createElement('div');
            card.className = 'usage-card';
            var label = document.createElement('span');
            label.textContent = item[0];
            var value = document.createElement('strong');
            value.textContent = item[1];
            card.append(label, value);
            summary.appendChild(card);
        });
    }

    function renderUsageTable(selector, items, labelFor, showUserId) {
        var body = root.querySelector(selector);
        body.replaceChildren();
        (items || []).forEach(function (item) {
            var row = document.createElement('tr');
            var name = document.createElement('td');
            name.textContent = labelFor(item);
            row.appendChild(name);
            [['requestCount', function (value) { return formatNumber(value); }],
                ['costRub', formatMoney], ['reservedRub', formatMoney]].forEach(function (field) {
                var cell = document.createElement('td');
                cell.textContent = field[1](item[field[0]]);
                row.appendChild(cell);
            });
            var tokens = document.createElement('td');
            tokens.textContent = formatNumber(item.inputTokens) + ' / ' + formatNumber(item.outputTokens);
            row.appendChild(tokens);
            body.appendChild(row);
        });
        if (!items || !items.length) {
            var empty = document.createElement('tr');
            var cell = document.createElement('td');
            cell.colSpan = 5;
            cell.textContent = uiText('noRequests');
            empty.appendChild(cell);
            body.appendChild(empty);
        }
    }

    async function loadUsage(useFilters) {
        if (isBusy) return;
        setBusy(true);
        setMessage(usageStatus, uiText('loadingUsage'), false);
        try {
            var filters = useFilters ? { from: usageFrom.value, to: usageTo.value, channel: usageChannel.value } : {};
            var result = await getJson('usage?' + params(filters));
            usageDefault = usageDefault || result;
            if (!useFilters) {
                usageFrom.value = result.from || '';
                usageTo.value = result.to || '';
                usageFrom.dispatchEvent(new Event('opencrm:field-sync'));
                usageTo.dispatchEvent(new Event('opencrm:field-sync'));
            }
            var selected = useFilters ? result : usageDefault;
            usageContent.hidden = false;
            usageCards(selected.scope, selected.day, selected.month, selected.limits);
            usageSummary(selected.totals);
            renderUsageTable('[data-usage-channels]', selected.byChannel, function (item) {
                return item.channel === 'telegram' ? 'Telegram' : uiText('web');
            });
            renderUsageTable('[data-usage-users]', selected.byUser, function (item) {
                return item.userName || item.userId || uiText('employee');
            }, true);
            setMessage(usageStatus, '', false);
            usageLoaded = true;
        } catch (error) {
            setMessage(usageStatus, error.message || uiText('loadUsageFailed'), true);
        } finally {
            setBusy(false);
        }
    }

    function markReady() {
        if (initialized) return;
        initialized = true;
        updateBusyState();
    }

    tabs.forEach(function (tab) {
        tab.addEventListener('click', function () { setTab(tab.dataset.assistantTab); });
        tab.addEventListener('keydown', function (event) {
            if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
            event.preventDefault();
            var index = tabs.indexOf(tab);
            var next = (index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length;
            tabs[next].focus();
            setTab(tabs[next].dataset.assistantTab);
        });
    });

    root.addEventListener('assistant:ready', markReady, { once: true });
    root.addEventListener('assistant:busy', function (event) {
        assistantBusy = event.detail && event.detail.busy === true;
        updateBusyState();
    });
    root.addEventListener('assistant:conversation-updated', function () {
        historyLoaded = false;
        usageLoaded = false;
        if (activeTab === 'history' && initialized) void loadHistory();
        if (activeTab === 'usage' && initialized) void loadUsage(true);
    });

    root.querySelector('[data-history-refresh]').addEventListener('click', function () {
        historyLoaded = false;
        void loadHistory();
    });
    historyChannel.addEventListener('change', function () {
        historyPage = 1;
        historyLoaded = false;
        void loadHistory();
    });
    historyPrev.addEventListener('click', function () {
        if (historyPage <= 1) return;
        historyPage -= 1;
        historyLoaded = false;
        void loadHistory();
    });
    historyNext.addEventListener('click', function () {
        historyPage += 1;
        historyLoaded = false;
        void loadHistory();
    });
    messagePrev.addEventListener('click', function () {
        if (messagePage <= 1 || !selectedConversationId) return;
        void loadConversation(selectedConversationId, messagePage - 1);
    });
    messageNext.addEventListener('click', function () {
        if (messagePage * messagePageSize >= messageTotalCount || !selectedConversationId) return;
        void loadConversation(selectedConversationId, messagePage + 1);
    });
    historyResume.addEventListener('click', function () {
        var id = historyResume.dataset.conversationId;
        if (!id) return;
        setTab('conversation');
        root.dispatchEvent(new CustomEvent('assistant:resume-conversation', { detail: { id: id } }));
    });
    root.querySelector('[data-usage-refresh]').addEventListener('click', function () { void loadUsage(true); });
    root.querySelector('[data-usage-apply]').addEventListener('click', function () { void loadUsage(true); });

    root.querySelector('[data-open-capabilities]').addEventListener('click', function () {
        if (typeof capabilitiesDialog.showModal === 'function') capabilitiesDialog.showModal();
    });
    root.querySelector('[data-close-capabilities]').addEventListener('click', function () { capabilitiesDialog.close(); });
    capabilitiesDialog.addEventListener('click', function (event) {
        if (event.target === capabilitiesDialog) capabilitiesDialog.close();
    });
    capabilitiesDialog.addEventListener('close', function () { root.querySelector('[data-open-capabilities]').focus(); });
    setTab('conversation');
})();
