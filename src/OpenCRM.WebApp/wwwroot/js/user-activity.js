(function () {
    'use strict';

    const config = document.querySelector('script[data-opencrm-activity]');
    if (!config || window.OpenCRMActivity) return;

    function start() {
        const cookie = name => document.cookie.split(';').map(value => value.trim())
            .find(value => value.startsWith(`${name}=`))?.slice(name.length + 1);

        const isId = value => /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(value || '')
            || /^\d{1,20}$/.test(value || '');
        const sessionId = /^[a-f0-9-]{36}$/i.test(cookie('opencrmActivitySession') || '')
            ? cookie('opencrmActivitySession') : window.crypto.randomUUID();
        const controller = new AbortController();
        const pending = [];
        let stopped = false;
        let sending = false;
        let lastPage = '';
        let lastFlush = 0;
        let lastClick = '';
        let lastClickAt = 0;

        const words = new Set(('clients detailing autoservice fleet personal account vehicles vehicle maintenance organizations '
            + 'organization auth subscription payment support scheduler workspace visualization materials work-journal finance '
            + 'statistics shifts employees members settings billing wiki defects counters catalog services bundles profile '
            + 'index list create edit new details history journal albums album photos documents payments export import '
            + 'quick-booking quickbooking overview rates invitations notifications login logout register confirmemail '
            + 'changepassword setpassword deleteaccount vehiclepersonal vehicleeditpersonal vehiclecreatepersonal '
            + 'vehiclejournal vehiclealbums vehiclealbum vehiclephotos maintenancepersonal maintenanceorganization '
            + 'createmaintenance createmaintenancepersonal createmaintenanceorganization maintenancehistory '
            + 'accountprofile createorganization setup resources posts schedule search help').split(' '));

        const labels = new Map([
            ['сохранить', 'save'], ['сохранить изменения', 'save'], ['создать', 'create'],
            ['создать заказ', 'create_order'], ['создать запись', 'create_booking'], ['новая запись', 'create_booking'],
            ['добавить запись', 'create_booking'], ['добавить клиента', 'create_client'], ['новый клиент', 'create_client'],
            ['добавить автомобиль', 'create_vehicle'], ['добавить технику', 'create_vehicle'],
            ['добавить услугу', 'add_service'], ['добавить материал', 'add_material'], ['добавить сотрудника', 'add_employee'],
            ['добавить', 'add'], ['удалить', 'delete'], ['редактировать', 'edit'], ['изменить', 'edit'],
            ['отмена', 'cancel'], ['отменить', 'cancel'], ['закрыть', 'close'], ['назад', 'back'],
            ['продолжить', 'continue'], ['далее', 'next'], ['готово', 'done'], ['подтвердить', 'confirm'],
            ['открыть', 'open'], ['открыть обслуживание', 'open_order'], ['открыть заказ', 'open_order'],
            ['начать работу', 'start_work'], ['завершить', 'complete'], ['завершить работу', 'complete_work'],
            ['выдать автомобиль', 'handover'], ['принять оплату', 'record_payment'], ['добавить оплату', 'record_payment'],
            ['оплатить', 'pay'], ['подключить', 'activate'], ['активировать', 'activate'], ['попробовать бесплатно', 'start_demo'],
            ['обновить', 'refresh'], ['применить', 'apply'], ['сбросить', 'reset'], ['найти', 'search'],
            ['скачать', 'download'], ['экспорт', 'export'], ['импорт', 'import'], ['печать', 'print'],
            ['распечатать', 'print'], ['отправить', 'send'], ['пригласить', 'invite'], ['архивировать', 'archive'],
            ['восстановить', 'restore'], ['список', 'view_list'], ['календарь', 'view_calendar'], ['посты', 'view_posts'],
            ['слоты', 'view_slots'], ['месяц', 'view_month'], ['неделя', 'view_week'], ['день', 'view_day'],
            ['сегодня', 'today'], ['предыдущий', 'previous'], ['следующий', 'next'], ['показать ещё', 'load_more'],
            ['расписание', 'open_scheduler'], ['клиенты', 'open_clients'], ['финансы', 'open_finance'],
            ['настройки организации', 'open_settings'], ['услуги', 'open_services'], ['команда', 'open_employees']
        ]);
        const goals = new Set(['client_created', 'work_created', 'vehicle_created', 'demo_activated', 'checkout_started', 'plan_activated']);

        function route(url = new URL(window.location.href)) {
            const scenario = url.pathname.match(/^\/(?:clients\/)?(detailing|autoservice|fleet)(?:\/|$)/i)?.[1]?.toLowerCase()
                || (/^\/(personal|vehicles?|maintenance)(?:\/|$)/i.test(url.pathname) ? 'personal' : 'account');
            const pathParts = url.pathname.toLowerCase().split('/').filter(Boolean);
            const hashParts = scenario === 'account' || scenario === 'personal' ? []
                : url.hash.replace(/^#\/?/, '').split('?')[0].split('/').filter(Boolean);
            const parts = [...pathParts, ...hashParts];
            const page = '/' + parts.map(part => isId(part) ? '{id}' : words.has(part) ? part : '{value}').join('/');
            const query = new URLSearchParams(url.search);
            const targetId = hashParts.find(isId)
                || ['id', 'vehicleId', 'maintenanceId', 'clientId'].map(key => query.get(key)).find(isId)
                || pathParts.find(isId) || null;
            const section = hashParts.length ? (words.has(hashParts[0]) ? hashParts[0] : 'workspace')
                : (words.has(pathParts[0]) ? pathParts[0] : 'account');

            return { page: page.slice(0, 240), scenario, section, targetId };
        }

        function add(kind, action, context = route()) {
            if (stopped) return;

            if (pending.length >= 100) return;
            pending.push({ occurredAt: new Date().toISOString(), kind, action, ...context });
            if (pending.length >= 20) void flush();
        }

        function trackPage() {
            const context = route();
            const key = `${context.page}:${context.targetId}`;
            if (key === lastPage) return;
            lastPage = key;
            add('page', 'view', context);
        }

        async function flush(leaving = false) {
            if (stopped || sending || pending.length === 0 || (!leaving && Date.now() - lastFlush < 5000)) return;
            sending = true;
            lastFlush = Date.now();
            const organizationId = config.dataset.organizationId;
            const events = pending.splice(0, leaving ? 50 : 20);
            const payload = JSON.stringify({ sessionId, organizationId: isId(organizationId) ? organizationId : null, events });
            try {
                const response = await window.fetch('/api/v1/user-activity', {
                    method: 'POST', credentials: 'same-origin', keepalive: leaving,
                    headers: { 'Content-Type': 'application/json', RequestVerificationToken: config.dataset.requestToken },
                    body: payload, signal: leaving ? undefined : AbortSignal.timeout(5000)
                });
                if ([401, 403, 410].includes(response.status)) stop();
            } catch {
                // Диагностика не должна повторять запросы или задерживать работу пользователя.
            } finally {
                sending = false;
            }
        }

        function click(event) {
            if (!(event.target instanceof Element) || event.isTrusted === false) return;
            const target = event.target.closest('a,button,[role="button"],input[type="submit"]');
            if (!target || target.closest('[data-activity-ignore],#cookieConsent') || target.disabled) return;
            let action = target.getAttribute('data-activity-action');
            if (!/^[a-z][a-z0-9_]{0,63}$/.test(action || '')) {
                const label = (target.getAttribute('aria-label') || target.textContent || '').trim().toLowerCase().replace(/\s+/g, ' ');
                action = labels.get(label) || (target.tagName === 'A' ? 'link' : target.type === 'submit' ? 'submit' : 'button');
            }

            const context = route();
            if (target.tagName === 'A' && target.getAttribute('href')) {
                try {
                    const url = new URL(target.getAttribute('href'), window.location.href);
                    if (url.origin !== window.location.origin || !/^https?:$/.test(url.protocol)) return;
                    const destination = route(url);
                    context.targetId = destination.targetId;
                    if (action === 'link') action = `open_${destination.section}`;
                } catch {
                    return;
                }
            }

            const key = `${action}:${context.page}:${context.targetId}`;
            if (key === lastClick && Date.now() - lastClickAt < 300) return;
            lastClick = key;
            lastClickAt = Date.now();
            add('click', action, context);
            window.setTimeout(trackPage, 0);
        }

        // Переключение разделов CRM использует replaceState без события hashchange.
        const historyMethods = ['pushState', 'replaceState'].map(name => {
            const original = window.history[name];
            const wrapped = function (...args) {
                const result = original.apply(this, args);
                window.setTimeout(trackPage, 0);
                return result;
            };
            window.history[name] = wrapped;
            return { name, original, wrapped };
        });
        const timer = window.setInterval(() => void flush(), 15000);

        function stop() {
            if (stopped) return;
            stopped = true;
            pending.length = 0;
            controller.abort();
            window.clearInterval(timer);
            for (const { name, original, wrapped } of historyMethods) {
                if (window.history[name] === wrapped) window.history[name] = original;
            }
            document.cookie = 'opencrmActivitySession=; Max-Age=0; Path=/; SameSite=Lax';
        }

        const listen = (target, name, handler) => target.addEventListener(name, handler, { signal: controller.signal, capture: name === 'click' });
        listen(document, 'click', click);
        listen(window, 'hashchange', trackPage);
        listen(window, 'popstate', trackPage);
        listen(document, 'visibilitychange', () => {
            if (document.visibilityState === 'hidden') void flush(true);
        });
        listen(window, 'pagehide', () => void flush(true));
        listen(window, 'opencrm:analytics-goal', event => {
            if (goals.has(event.detail?.goal)) add('goal', event.detail.goal);
        });
        window.OpenCRMActivity.stop = stop;
        document.cookie = `opencrmActivitySession=${sessionId}; Path=/; SameSite=Lax${window.location.protocol === 'https:' ? '; Secure' : ''}`;
        trackPage();
    }

    window.OpenCRMActivity = { stop: function () {} };
    start();
})();
