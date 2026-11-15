(() => {
    const getEmailValidationMessage = value => {
        const email = value.trim();
        if (!email) {
            return 'Укажите email.';
        }

        const atCount = [...email].filter(character => character === '@').length;
        if (atCount === 0) {
            return 'Email должен содержать символ @, например contact@example.invalid.';
        }

        if (atCount > 1) {
            return 'Email должен содержать только один символ @. Удалите лишний @.';
        }

        const [localPart, domainPart] = email.split('@');
        if (!localPart) {
            return 'Укажите имя почтового ящика перед @.';
        }

        if (!domainPart) {
            return 'Укажите почтовый домен после @, например gmail.com.';
        }

        if (localPart.endsWith('.')) {
            return `Перед @ не должно быть точки. Возможно, вы имели в виду «${localPart.replace(/\.+$/, '')}@${domainPart}».`;
        }

        if (localPart.startsWith('.')) {
            return 'Email не может начинаться с точки.';
        }

        if (localPart.includes('..')) {
            return 'В email не должно быть двух точек подряд.';
        }

        return 'Email содержит недопустимые символы. Проверьте адрес и попробуйте ещё раз.';
    };

    document.querySelectorAll('input[type="email"]').forEach(input => {
        input.addEventListener('input', () => input.setCustomValidity(''));
        input.addEventListener('invalid', () => {
            input.setCustomValidity(getEmailValidationMessage(input.value));
        });
    });
})();
