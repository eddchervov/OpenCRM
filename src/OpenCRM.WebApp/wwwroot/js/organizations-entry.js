(function () {
    var storageKey = 'opencrm.workspace.theme';
    function applyTheme(value) {
        var theme = value === 'dark' ? 'dark' : 'light';
        document.documentElement.dataset.crmTheme = theme;
        document.querySelectorAll('[data-entry-theme]').forEach(function (button) {
            var label = theme === 'dark' ? window.OpenCRMText('lightTheme') : window.OpenCRMText('darkTheme');
            button.setAttribute('aria-label', label);
            button.title = label;
        });
    }
    try {
        applyTheme(localStorage.getItem(storageKey));
    } catch {
        // Недоступное хранилище не должно мешать работе формы.
        applyTheme('light');
    }
    document.addEventListener('DOMContentLoaded', function () {
        applyTheme(document.documentElement.dataset.crmTheme);
        document.querySelectorAll('[data-entry-theme]').forEach(function (button) {
            button.addEventListener('click', function () {
                var theme = document.documentElement.dataset.crmTheme === 'dark' ? 'light' : 'dark';
                applyTheme(theme);
                try {
                    localStorage.setItem(storageKey, theme);
                } catch {
                    // В приватном режиме выбор действует до закрытия страницы.
                }
            });
        });
    });
    window.addEventListener('storage', function (event) {
        if (event.key === storageKey || event.key === null) applyTheme(event.newValue);
    });
})();
