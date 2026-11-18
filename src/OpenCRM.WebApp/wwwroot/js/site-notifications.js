(function () {
    'use strict';

    const bell = document.querySelector('[data-site-notification-bell]');
    const home = document.querySelector('[data-site-notification-home]');
    const history = document.querySelector('[data-site-notification-history]');
    if (!bell && !home && !history) {
        return;
    }

    let notifications = [];
    const dateFormatter = new Intl.DateTimeFormat(document.querySelector('meta[name="opencrm-formatting-culture"]')?.content || 'ru-RU', {
        day: 'numeric',
        month: 'long',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });

    function createItem(notification, showClose) {
        const item = document.createElement('article');
        item.className = 'site-notification-item' + (notification.closedAt ? ' site-notification-item-closed' : '');

        const title = document.createElement('strong');
        title.textContent = notification.title;
        item.appendChild(title);

        const body = document.createElement('div');
        body.className = 'site-notification-item-body mt-1';
        body.textContent = notification.body;
        item.appendChild(body);

        const footer = document.createElement('div');
        footer.className = 'd-flex align-items-center justify-content-between gap-2 mt-2 small text-muted';
        const date = document.createElement('span');
        date.textContent = dateFormatter.format(new Date(notification.createdAt));
        footer.appendChild(date);

        if (notification.closedAt) {
            const closed = document.createElement('span');
            closed.textContent = window.OpenCRMText('closed');
            footer.appendChild(closed);
        } else if (showClose) {
            const close = document.createElement('button');
            close.type = 'button';
            close.className = 'btn btn-sm btn-outline-secondary';
            close.textContent = window.OpenCRMText('close');
            close.dataset.notificationClose = notification.id;
            footer.appendChild(close);
        }

        item.appendChild(footer);
        return item;
    }

    function renderList(container, items, emptyText, showClose) {
        container.replaceChildren();
        if (items.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'text-muted mb-0';
            empty.textContent = emptyText;
            container.appendChild(empty);
            return;
        }

        for (const notification of items) {
            container.appendChild(createItem(notification, showClose));
        }
    }

    function render() {
        const active = notifications.filter(notification => !notification.closedAt);
        if (bell) {
            const count = bell.querySelector('[data-site-notification-count]');
            const preview = bell.querySelector('[data-site-notification-preview]');
            count.textContent = active.length > 99 ? '99+' : String(active.length);
            count.hidden = active.length === 0;
            renderList(preview, active.slice(0, 5), window.OpenCRMText('noNewNotifications'), true);
        }

        if (home) {
            home.hidden = active.length === 0;
            renderList(home.querySelector('[data-site-notification-home-list]'), active, '', true);
        }

        if (history) {
            renderList(history, notifications, window.OpenCRMText('noNotifications'), true);
        }
    }

    async function load() {
        try {
            const response = await fetch('/api/v1/notifications', { credentials: 'same-origin' });
            if (!response.ok) {
                throw new Error(window.OpenCRMText('loadNotificationsError'));
            }

            notifications = await response.json();
            render();
        } catch (error) {
            if (history) {
                history.textContent = error.message;
            }
        }
    }

    document.addEventListener('click', async event => {
        const close = event.target.closest('[data-notification-close]');
        if (!close) {
            return;
        }

        close.disabled = true;
        try {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            const response = await fetch('/api/v1/notifications/' + encodeURIComponent(close.dataset.notificationClose) + '/close', {
                method: 'POST',
                credentials: 'same-origin',
                headers: token ? { RequestVerificationToken: token } : {}
            });
            if (!response.ok) {
                throw new Error(window.OpenCRMText('closeNotificationError'));
            }

            const notification = notifications.find(item => item.id === close.dataset.notificationClose);
            if (notification) {
                notification.closedAt = new Date().toISOString();
            }

            render();
        } catch (error) {
            close.disabled = false;
            window.alert(error.message);
        }
    });

    load();
    window.setInterval(load, 60000);
})();
