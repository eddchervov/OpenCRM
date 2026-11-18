(function () {
    window.OpenCRMAssistantTelegram = {
        create: function (root, request) {

            var uiMessages = JSON.parse(root.dataset.uiMessages || '{}');
            function uiText(key, parameters) {
                return (uiMessages[key] || key).replace(/\{(\w+)\}/g, function (_, name) {
                    return parameters && parameters[name] !== undefined ? parameters[name] : '{' + name + '}';
                });
            }

            var dialog = root.querySelector('[data-telegram-dialog]');
            var openButton = root.querySelector('[data-telegram-connect]');
            var organizationId = root.dataset.organizationId;
            var status = null;
            var enabled = false;
            var canConnect = false;
            var loading = false;
            var mutating = false;
            var teamPage = 1;
            var teamPages = 1;
            var linkBaseline = null;

            function query(values) {
                return new URLSearchParams(Object.assign({ organizationId: organizationId }, values || {})).toString();
            }

            function message(text, isError) {
                var element = root.querySelector('[data-telegram-feedback]');
                element.textContent = text;
                element.hidden = !text;
                element.classList.toggle('is-error', isError === true);
            }

            function formatDate(value) {
                if (!value) return '—';
                var date = new Date(value);
                return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(root.dataset.formatCulture || 'ru-RU', {
                    dateStyle: 'short', timeStyle: 'short', timeZone: root.dataset.timeZoneId || 'UTC'
                }).format(date);
            }

            function updateControls() {
                openButton.disabled = !enabled || mutating;
                openButton.textContent = status && status.isBound ? uiText('telegramConnected') : 'Telegram';
                root.querySelector('[data-telegram-close]').disabled = mutating;
                root.querySelector('[data-telegram-refresh]').disabled = loading || mutating;
                var linkButton = root.querySelector('[data-telegram-create-link]');
                linkButton.disabled = loading || mutating || !canConnect || !status || !status.isConfigured;
                linkButton.textContent = status && status.isBound ? uiText('connectOther') : uiText('connectTelegram');
                root.querySelector('[data-telegram-disconnect]').disabled = loading || mutating || !status || !status.isBound
                    || !root.querySelector('[data-telegram-disconnect-confirm]').checked;
                root.querySelector('[data-telegram-disconnect-confirm]').disabled = loading || mutating;
                root.querySelector('[data-telegram-team-prev]').disabled = loading || mutating || teamPage <= 1;
                root.querySelector('[data-telegram-team-next]').disabled = loading || mutating || teamPage >= teamPages;
            }

            function renderStatus(value) {
                var changed = !status || status.telegramUserId !== value.telegramUserId || status.connectedAt !== value.connectedAt;
                status = value;
                var statusText = value.isBound ? uiText('accountConnected') : uiText('telegramNotConnected');
                root.querySelector('[data-telegram-status]').textContent = statusText;
                root.querySelector('[data-telegram-binding]').hidden = !value.isBound;
                root.querySelector('[data-telegram-user-id]').textContent = value.telegramUserId || '—';
                root.querySelector('[data-telegram-connected-at]').textContent = formatDate(value.connectedAt);
                root.querySelector('[data-telegram-bot]').textContent = value.botUsername ? '@' + value.botUsername : uiText('botNotConfigured');
                var selection = root.querySelector('[data-telegram-selection]');
                selection.hidden = !value.isBound || value.isConnected;
                selection.textContent = value.organizationId
                    ? uiText('otherOrganization')
                    : uiText('noOrganization');
                root.querySelector('[data-telegram-unavailable]').hidden = canConnect && value.isConfigured;
                root.querySelector('[data-telegram-disconnect-section]').hidden = !value.isBound;
                root.querySelector('[data-telegram-team]').hidden = !value.canViewOrganizationConnections;
                if (changed) root.querySelector('[data-telegram-disconnect-confirm]').checked = false;
                if (linkBaseline && value.isBound && (linkBaseline.id !== value.telegramUserId || linkBaseline.connectedAt !== value.connectedAt)) {
                    root.querySelector('[data-telegram-link-panel]').hidden = true;
                    root.querySelector('[data-telegram-link]').removeAttribute('href');
                    linkBaseline = null;
                    message(uiText('telegramReady'), false);
                }
            }

            async function loadTeam() {
                var result = await request('telegram/connections?' + query({ page: teamPage }));
                var lastPage = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
                if (result.page > lastPage) result = await request('telegram/connections?' + query({ page: lastPage }));
                teamPage = result.page;
                teamPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
                var list = root.querySelector('[data-telegram-team-list]');
                list.replaceChildren();
                (result.items || []).forEach(function (item) {
                    var row = document.createElement('li');
                    var name = document.createElement('strong');
                    name.textContent = (item.userName || uiText('employee')) + (item.isCurrentUser ? uiText('youSuffix') : '');
                    var identity = document.createElement('span');
                    identity.textContent = 'Telegram ID: ' + item.telegramUserId;
                    var date = document.createElement('small');
                    date.textContent = uiText('connectedAt', { date: formatDate(item.connectedAt) });
                    row.append(name, identity, date);
                    list.appendChild(row);
                });
                root.querySelector('[data-telegram-team-empty]').hidden = result.items.length > 0;
                root.querySelector('[data-telegram-team-page]').textContent = uiText('pageOf', { number: teamPage, total: teamPages });
                root.querySelector('[data-telegram-team-pagination]').hidden = teamPages <= 1;
            }

            async function refresh(silent) {
                if (loading || mutating) return;
                loading = true;
                if (!silent) message(uiText('checkingConnections'), false);
                updateControls();
                try {
                    renderStatus(await request('telegram/status?' + query()));
                    if (dialog.open && status.canViewOrganizationConnections) await loadTeam();
                    if (!silent) message('', false);
                } catch (error) {
                    message(error.message || uiText('checkTelegramFailed'), true);
                } finally {
                    loading = false;
                    updateControls();
                }
            }

            openButton.addEventListener('click', function () {
                if (!enabled || mutating) return;
                if (!dialog.open) dialog.showModal();
                void refresh(false);
            });
            root.querySelector('[data-telegram-close]').addEventListener('click', function () {
                if (!mutating) dialog.close();
            });
            dialog.addEventListener('cancel', function (event) { if (mutating) event.preventDefault(); });
            root.querySelector('[data-telegram-refresh]').addEventListener('click', function () { void refresh(false); });
            root.querySelector('[data-telegram-disconnect-confirm]').addEventListener('change', updateControls);
            root.querySelector('[data-telegram-team-prev]').addEventListener('click', function () { teamPage--; void refresh(false); });
            root.querySelector('[data-telegram-team-next]').addEventListener('click', function () { teamPage++; void refresh(false); });

            root.querySelector('[data-telegram-create-link]').addEventListener('click', async function () {
                if (loading || mutating || !canConnect || !status || !status.isConfigured) return;
                mutating = true;
                root.querySelector('[data-telegram-link-panel]').hidden = true;
                root.querySelector('[data-telegram-link]').removeAttribute('href');
                linkBaseline = null;
                message(uiText('creatingLink'), false);
                updateControls();
                try {
                    var link = await request('telegram/link', { method: 'POST', body: { organizationId: organizationId } });
                    var url = new URL(link.url);
                    if (url.protocol !== 'https:' || url.hostname !== 't.me') throw new Error(uiText('invalidLink'));
                    linkBaseline = { id: status.telegramUserId, connectedAt: status.connectedAt };
                    root.querySelector('[data-telegram-link]').href = url.href;
                    root.querySelector('[data-telegram-link-expiry]').textContent = uiText('linkExpiry', { date: formatDate(link.expiresAt) });
                    root.querySelector('[data-telegram-link-panel]').hidden = false;
                    message(uiText('telegramLinkHelp'), false);
                } catch (error) {
                    message(error.message || uiText('createLinkFailed'), true);
                } finally {
                    mutating = false;
                    updateControls();
                }
            });

            root.querySelector('[data-telegram-disconnect]').addEventListener('click', async function () {
                if (loading || mutating || !status || !status.isBound || !root.querySelector('[data-telegram-disconnect-confirm]').checked) return;
                mutating = true;
                message(uiText('disconnectingTelegram'), false);
                updateControls();
                try {
                    await request('telegram/binding?' + query(), { method: 'DELETE' });
                    root.querySelector('[data-telegram-link-panel]').hidden = true;
                    root.querySelector('[data-telegram-link]').removeAttribute('href');
                    linkBaseline = null;
                    renderStatus(await request('telegram/status?' + query()));
                    if (status.canViewOrganizationConnections) await loadTeam();
                    message(uiText('telegramDisconnected'), false);
                } catch (error) {
                    message(error.message || uiText('disconnectFailed'), true);
                } finally {
                    mutating = false;
                    updateControls();
                }
            });

            root.addEventListener('assistant:ready', function () { void refresh(true); });
            window.addEventListener('focus', function () { if (dialog.open) void refresh(true); });
            document.addEventListener('visibilitychange', function () { if (!document.hidden && dialog.open) void refresh(true); });

            return {
                setAvailability: function (value) {
                    enabled = value.enabled;
                    canConnect = value.canConnect;
                    if (status) root.querySelector('[data-telegram-unavailable]').hidden = canConnect && status.isConfigured;
                    updateControls();
                }
            };
        }
    };
}());
