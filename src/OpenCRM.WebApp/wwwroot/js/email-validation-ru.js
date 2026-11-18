(() => {
    const getEmailValidationMessage = value => {
        const email = value.trim();
        if (!email) {
            return window.OpenCRMText('emailRequired');
        }

        const atCount = [...email].filter(character => character === '@').length;
        if (atCount === 0) {
            return window.OpenCRMText('emailAtRequired');
        }

        if (atCount > 1) {
            return window.OpenCRMText('emailOneAt');
        }

        const [localPart, domainPart] = email.split('@');
        if (!localPart) {
            return window.OpenCRMText('emailLocalRequired');
        }

        if (!domainPart) {
            return window.OpenCRMText('emailDomainRequired');
        }

        if (localPart.endsWith('.')) {
            return window.OpenCRMText('emailDotSuggestion', { email: localPart.replace(/\.+$/, '') + '@' + domainPart });
        }

        if (localPart.startsWith('.')) {
            return window.OpenCRMText('emailStartDot');
        }

        if (localPart.includes('..')) {
            return window.OpenCRMText('emailDoubleDot');
        }

        return window.OpenCRMText('emailInvalid');
    };

    document.querySelectorAll('input[type="email"]').forEach(input => {
        input.addEventListener('input', () => input.setCustomValidity(''));
        input.addEventListener('invalid', () => {
            input.setCustomValidity(getEmailValidationMessage(input.value));
        });
    });
})();
