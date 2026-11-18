(function () {
    'use strict';

    const culture = document.querySelector('meta[name="opencrm-formatting-culture"]')?.content || 'ru-RU';
    const timeZone = document.querySelector('meta[name="opencrm-time-zone"]')?.content || undefined;
    const personalCurrency = document.querySelector('meta[name="opencrm-personal-currency"]')?.content || 'RUB';
    const pathBase = document.querySelector('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') || '';
    const requests = new Map();

    function formatMoney(value, currencyCode) {
        if (!currencyCode) return '—';

        return new Intl.NumberFormat(culture, {
            style: 'currency', currency: currencyCode, minimumFractionDigits: 0, maximumFractionDigits: 2
        }).format(typeof value === 'number' && Number.isFinite(value) ? value : 0);
    }

    function formatDate(value, options) {
        if (value === null || value === undefined || value === '') return '—';

        // Календарные значения без смещения сохраняют свои дату и часы в любом поясе устройства.
        const isCalendarValue = typeof value === 'string'
            && /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?)?$/.test(value);
        let dateValue = value;
        if (isCalendarValue) {
            dateValue = value.length === 10 ? value + 'T00:00:00Z' : value.replace(' ', 'T') + 'Z';
        }

        const date = new Date(dateValue);
        if (Number.isNaN(date.getTime())) return '—';

        const dateOptions = isCalendarValue
            ? Object.assign({}, options, { timeZone: 'UTC' })
            : Object.assign({ timeZone: timeZone }, options);

        return new Intl.DateTimeFormat(culture, dateOptions).format(date);
    }

    function today() {
        const parts = new Intl.DateTimeFormat('en', {
            timeZone: timeZone, calendar: 'gregory', numberingSystem: 'latn',
            year: 'numeric', month: '2-digit', day: '2-digit'
        }).formatToParts(new Date());
        const values = Object.fromEntries(parts.map(part => [part.type, part.value]));

        return values.year + '-' + values.month + '-' + values.day;
    }

    async function loadCurrency(organizationId) {
        if (!organizationId) return personalCurrency;
        if (!requests.has(organizationId)) {
            const request = fetch(`${pathBase}/api/v1/organizations/${encodeURIComponent(organizationId)}/currency`, {
                credentials: 'same-origin', headers: { Accept: 'application/json' }
            }).then(async response => {
                if (!response.ok) throw new Error(window.OpenCRMText('accountingCurrencyError'));
                const settings = await response.json();
                if (!/^[A-Z]{3}$/.test(settings.currencyCode || '')) throw new Error(window.OpenCRMText('accountingCurrencyError'));

                return settings.currencyCode;
            }).catch(error => {
                requests.delete(organizationId);
                throw error;
            });
            requests.set(organizationId, request);
        }

        return requests.get(organizationId);
    }

    async function initializeLegacyVm(vm) {
        try {
            vm.regionalCurrencyCode = await loadCurrency(vm.organizationId || vm.vehicle?.organizationId);
        } catch (error) {
            vm.errorMessage = error.message;
        }
    }

    window.OpenCRMRegional = { culture, timeZone, formatMoney, formatDate, today, loadCurrency, initializeLegacyVm };
})();
