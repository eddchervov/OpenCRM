(() => {
    const form = document.querySelector('[data-email-resend]');
    const button = form?.querySelector('button[type="submit"]');
    if (!button) return;

    const availableAt = Date.now() + Number(button.dataset.retryAfter || 0) * 1000;
    const updateButton = () => {
        const seconds = Math.max(0, Math.ceil((availableAt - Date.now()) / 1000));
        button.disabled = seconds > 0;
        button.textContent = seconds > 0
            ? `Отправить повторно через ${seconds} с`
            : 'Отправить письмо повторно';

        return seconds;
    };

    if (updateButton() > 0) {
        const timer = window.setInterval(() => {
            if (updateButton() === 0) window.clearInterval(timer);
        }, 1000);
    }

    form.addEventListener('submit', event => {
        if (button.disabled) {
            event.preventDefault();
            return;
        }

        button.disabled = true;
        button.textContent = 'Отправляем…';
    });

    window.addEventListener('pageshow', event => {
        if (event.persisted) window.location.reload();
    });
})();
