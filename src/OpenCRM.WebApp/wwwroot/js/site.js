// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(function () {
    function getRequestVerificationToken() {
        var meta = document.querySelector('meta[name="request-verification-token"]');
        return meta ? meta.getAttribute('content') : '';
    }

    function isUnsafeHttpMethod(method) {
        var normalized = String(method || 'GET').toUpperCase();
        return normalized !== 'GET' && normalized !== 'HEAD' && normalized !== 'OPTIONS' && normalized !== 'TRACE';
    }

    function isSameOriginUrl(url) {
        try {
            return new URL(url, window.location.origin).origin === window.location.origin;
        } catch (e) {
            return false;
        }
    }

    function initApiRequestProtection() {
        var headerName = 'RequestVerificationToken';
        var tokenRefreshPromise = null;

        function storeRequestVerificationToken(token) {
            if (!token) {
                return;
            }

            var meta = document.querySelector('meta[name="request-verification-token"]');
            if (!meta) {
                meta = document.createElement('meta');
                meta.setAttribute('name', 'request-verification-token');
                document.head.appendChild(meta);
            }

            meta.setAttribute('content', token);
        }

        function refreshRequestVerificationToken(fetchImpl, force) {
            if (!force && getRequestVerificationToken()) {
                return Promise.resolve(getRequestVerificationToken());
            }

            if (!tokenRefreshPromise) {
                tokenRefreshPromise = fetchImpl('/api/v1/antiforgery/token', {
                    method: 'GET',
                    credentials: 'same-origin',
                    headers: {
                        Accept: 'application/json',
                        'X-Requested-With': 'XMLHttpRequest'
                    }
                })
                    .then(function (response) {
                        if (!response.ok) {
                            return '';
                        }

                        return response.json().then(function (payload) {
                            var token = payload && payload.requestToken ? payload.requestToken : '';
                            storeRequestVerificationToken(token);
                            return token;
                        });
                    })
                    .catch(function () {
                        return '';
                    })
                    .finally(function () {
                        tokenRefreshPromise = null;
                    });
            }

            return tokenRefreshPromise;
        }

        function applyToken(headers) {
            var token = getRequestVerificationToken();
            if (!token) {
                return headers;
            }

            headers = headers || {};
            if (typeof headers.set === 'function') {
                headers.set(headerName, token);
                return headers;
            }

            headers[headerName] = token;
            return headers;
        }

        if (window.axios && window.axios.interceptors) {
            if (axios.defaults && axios.defaults.headers) {
                axios.defaults.xsrfHeaderName = headerName;
            }

            if (!axios.__opencrmCsrfInterceptor) {
                axios.interceptors.request.use(function (config) {
                    var method = config && config.method ? config.method : 'get';
                    var url = config && config.url ? config.url : '';

                    if (isUnsafeHttpMethod(method) && isSameOriginUrl(url)) {
                        return refreshRequestVerificationToken(window.fetch).then(function () {
                            config.headers = applyToken(config.headers);
                            return config;
                        });
                    }

                    return config;
                });
                axios.__opencrmCsrfInterceptor = true;
            }
        }

        if (window.fetch && !window.fetch.__opencrmCsrfWrapped) {
            var originalFetch = window.fetch;
            var protectedFetch = function (input, init) {
                var requestUrl = typeof input === 'string' || input instanceof URL
                    ? String(input)
                    : input && input.url ? input.url : '';
                var requestMethod = init && init.method
                    ? init.method
                    : input && input.method ? input.method : 'GET';

                if (isUnsafeHttpMethod(requestMethod) && isSameOriginUrl(requestUrl)) {
                    var nextInit = Object.assign({}, init || {});
                    var headers = new Headers(input && input.headers ? input.headers : undefined);

                    if (nextInit.headers) {
                        new Headers(nextInit.headers).forEach(function (value, key) {
                            headers.set(key, value);
                        });
                    }

                    return refreshRequestVerificationToken(originalFetch).then(function () {
                        applyToken(headers);
                        nextInit.headers = headers;
                        return originalFetch(input, nextInit).then(function (response) {
                            if (response.status !== 400) {
                                return response;
                            }

                            return response.clone().json().catch(function () {
                                return null;
                            }).then(function (payload) {
                                var message = payload && payload.message ? payload.message : '';
                                if (message.indexOf('защитный токен') === -1) {
                                    return response;
                                }

                                return refreshRequestVerificationToken(originalFetch, true).then(function () {
                                    applyToken(headers);
                                    nextInit.headers = headers;
                                    return originalFetch(input, nextInit);
                                });
                            });
                        });
                    });
                }

                return originalFetch(input, init);
            };

            protectedFetch.__opencrmCsrfWrapped = true;
            window.fetch = protectedFetch;
        }
    }

    window.getRequestVerificationToken = getRequestVerificationToken;

    function normalizeTooltipNode(node) {
        if (typeof bootstrap === 'undefined' || !bootstrap.Tooltip) {
            return null;
        }

        if (!node) {
            return null;
        }

        if (!node.getAttribute('data-bs-title') && node.getAttribute('title')) {
            node.setAttribute('data-bs-title', node.getAttribute('title'));
            node.removeAttribute('title');
        }

        if (!node.getAttribute('data-bs-toggle')) {
            node.setAttribute('data-bs-toggle', 'tooltip');
        }

        if (!node.getAttribute('data-bs-custom-class')) {
            node.setAttribute('data-bs-custom-class', 'opencrm-tooltip');
        }

        if (!node.getAttribute('data-bs-placement')) {
            node.setAttribute('data-bs-placement', 'top');
        }

        return bootstrap.Tooltip.getOrCreateInstance(node, {
            container: 'body',
            trigger: 'hover focus'
        });
    }

    function getTooltipHost(target) {
        return target && target.closest
            ? target.closest('[data-ui-tooltip="true"], [data-bs-toggle="tooltip"]')
            : null;
    }

    function initTooltips(root) {
        if (typeof bootstrap === 'undefined' || !bootstrap.Tooltip) {
            return;
        }

        var scope = root && root.querySelectorAll ? root : document;
        var nodes = scope.querySelectorAll('[data-ui-tooltip="true"], [data-bs-toggle="tooltip"]');

        if (root && root.matches && root.matches('[data-ui-tooltip="true"], [data-bs-toggle="tooltip"]')) {
            normalizeTooltipNode(root);
        }

        Array.prototype.forEach.call(nodes, function (node) {
            normalizeTooltipNode(node);
        });
    }

    function initTooltipDelegates() {
        if (document.documentElement.dataset.tooltipDelegates === 'true') {
            return;
        }

        document.documentElement.dataset.tooltipDelegates = 'true';

        ['pointerenter', 'focusin'].forEach(function (eventName) {
            document.addEventListener(eventName, function (event) {
                normalizeTooltipNode(getTooltipHost(event.target));
            }, true);
        });

        document.addEventListener('click', function (event) {
            var host = getTooltipHost(event.target);
            if (!host || host.getAttribute('data-ui-tooltip') !== 'true') {
                return;
            }

            var tooltip = normalizeTooltipNode(host);
            if (!tooltip) {
                return;
            }

            tooltip.show();
            window.setTimeout(function () {
                tooltip.hide();
            }, 2200);
        }, true);
    }

    function initTooltipObserver() {
        if (!window.MutationObserver || document.documentElement.dataset.tooltipObserver === 'true') {
            return;
        }

        document.documentElement.dataset.tooltipObserver = 'true';
        var timeoutId = null;
        var observer = new MutationObserver(function (mutations) {
            var shouldRefresh = mutations.some(function (mutation) {
                if (!mutation.addedNodes || mutation.addedNodes.length === 0) {
                    return false;
                }

                return Array.prototype.some.call(mutation.addedNodes, function (node) {
                    return node.nodeType === 1 && (
                        (node.matches && node.matches('[data-ui-tooltip="true"], [data-bs-toggle="tooltip"]')) ||
                        (node.querySelector && node.querySelector('[data-ui-tooltip="true"], [data-bs-toggle="tooltip"]'))
                    );
                });
            });

            if (!shouldRefresh) {
                return;
            }

            window.clearTimeout(timeoutId);
            timeoutId = window.setTimeout(function () {
                initTooltips(document);
            }, 80);
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    function updatePasswordToggleButton(input, button) {
        if (!input || !button) {
            return;
        }

        var isVisible = input.type === 'text';
        button.setAttribute('aria-label', isVisible ? window.OpenCRMText('hidePassword') : window.OpenCRMText('showPassword'));
        button.setAttribute('title', isVisible ? window.OpenCRMText('hidePassword') : window.OpenCRMText('showPassword'));
        button.setAttribute('aria-pressed', isVisible ? 'true' : 'false');
        button.innerHTML = isVisible
            ? '<i class="fa fa-eye-slash" aria-hidden="true"></i>'
            : '<i class="fa fa-eye" aria-hidden="true"></i>';
    }

    function initPasswordToggles(root) {
        var scope = root && root.querySelectorAll ? root : document;
        var inputs = scope.querySelectorAll('input[type="password"]:not([data-password-toggle="false"])');

        Array.prototype.forEach.call(inputs, function (input) {
            if (!input || input.dataset.passwordToggleInitialized === 'true') {
                return;
            }

            var parent = input.parentElement;
            var wrapper = parent;
            var isFloating = !!(parent && parent.classList.contains('form-floating'));

            if (isFloating) {
                wrapper.classList.add('opencrm-password-toggle-floating');
            } else if (!wrapper || !wrapper.classList.contains('opencrm-password-toggle')) {
                wrapper = document.createElement('div');
                wrapper.className = 'opencrm-password-toggle';
                input.parentNode.insertBefore(wrapper, input);
                wrapper.appendChild(input);
            }

            input.classList.add('opencrm-password-input');

            var button = document.createElement('button');
            button.type = 'button';
            button.className = 'opencrm-password-toggle-button';
            button.setAttribute('data-password-toggle-button', 'true');

            button.addEventListener('click', function () {
                input.type = input.type === 'password' ? 'text' : 'password';
                updatePasswordToggleButton(input, button);
            });

            wrapper.appendChild(button);
            updatePasswordToggleButton(input, button);
            input.dataset.passwordToggleInitialized = 'true';
        });
    }

    function isPostForm(form) {
        return !!form
            && form.tagName === 'FORM'
            && String(form.getAttribute('method') || 'get').toLowerCase() === 'post';
    }

    function ensureFormRequestVerificationToken(form) {
        if (!isPostForm(form) || form.querySelector('input[name="__RequestVerificationToken"]')) {
            return;
        }

        var token = getRequestVerificationToken();
        if (!token) {
            return;
        }

        var input = document.createElement('input');
        input.type = 'hidden';
        input.name = '__RequestVerificationToken';
        input.value = token;
        form.appendChild(input);
    }

    function shouldHandleSubmitLoader(form) {
        return isPostForm(form) && !form.hasAttribute('data-disable-submit-loader');
    }

    function isClientValidationPassed(form) {
        if (!form) {
            return true;
        }

        if (window.jQuery && typeof window.jQuery.fn.valid === 'function') {
            var $form = window.jQuery(form);

            if (window.jQuery.validator && window.jQuery.validator.unobtrusive) {
                window.jQuery.validator.unobtrusive.parse(form);
            }

            if (!$form.valid()) {
                return false;
            }
        }

        if (typeof form.checkValidity === 'function' && !form.checkValidity()) {
            if (!form.noValidate && typeof form.reportValidity === 'function') {
                form.reportValidity();
            }

            return false;
        }

        return true;
    }

    function getSubmitButtons(form) {
        return Array.prototype.slice.call(
            form.querySelectorAll('button[type="submit"], input[type="submit"]')
        );
    }

    function resetSubmittingState(form) {
        if (!form) {
            return;
        }

        delete form.dataset.isSubmitting;

        Array.prototype.slice.call(form.querySelectorAll('input[data-submit-loader-submitter="true"]'))
            .forEach(function (input) {
                input.remove();
            });

        getSubmitButtons(form).forEach(function (button) {
            setSubmittingState(button, false, false);
        });
    }

    function preserveSubmitterValue(form, submitter) {
        if (!form || !submitter || !submitter.name) {
            return;
        }

        Array.prototype.slice.call(form.querySelectorAll('input[data-submit-loader-submitter="true"]'))
            .forEach(function (input) {
                input.remove();
            });

        var input = document.createElement('input');
        input.type = 'hidden';
        input.name = submitter.name;
        input.value = submitter.value || '';
        input.setAttribute('data-submit-loader-submitter', 'true');
        form.appendChild(input);
    }

    function initImageLightbox() {
        if (document.body.dataset.imageLightboxReady === 'true') {
            return;
        }

        document.body.dataset.imageLightboxReady = 'true';

        var lightbox = null;
        var lightboxImage = null;
        var lightboxCaption = null;
        var closeButton = null;
        var previousFocus = null;

        function ensureLightbox() {
            if (lightbox) {
                return;
            }

            lightbox = document.createElement('div');
            lightbox.className = 'image-lightbox';
            lightbox.setAttribute('role', 'dialog');
            lightbox.setAttribute('aria-modal', 'true');
            lightbox.setAttribute('aria-label', window.OpenCRMText('imagePreview'));
            lightbox.innerHTML = [
                '<div class="image-lightbox__dialog" role="document">',
                '<button type="button" class="image-lightbox__close">&times;</button>',
                '<img class="image-lightbox__image" alt="">',
                '<p class="image-lightbox__caption"></p>',
                '</div>'
            ].join('');

            document.body.appendChild(lightbox);
            lightboxImage = lightbox.querySelector('.image-lightbox__image');
            lightboxCaption = lightbox.querySelector('.image-lightbox__caption');
            closeButton = lightbox.querySelector('.image-lightbox__close');
            closeButton.setAttribute('aria-label', window.OpenCRMText('closeImage'));

            lightbox.addEventListener('click', function (event) {
                if (event.target === lightbox || event.target === closeButton) {
                    closeLightbox();
                }
            });
        }

        function openLightbox(image) {
            if (!image) {
                return;
            }

            ensureLightbox();
            previousFocus = document.activeElement;
            var caption = image.getAttribute('data-lightbox-caption') || image.getAttribute('alt') || '';

            lightboxImage.src = image.currentSrc || image.src;
            lightboxImage.alt = image.getAttribute('alt') || caption;
            lightboxCaption.textContent = caption;
            lightbox.classList.add('is-open');
            document.body.classList.add('modal-open');
            closeButton.focus();
        }

        function closeLightbox() {
            if (!lightbox || !lightbox.classList.contains('is-open')) {
                return;
            }

            lightbox.classList.remove('is-open');
            document.body.classList.remove('modal-open');
            lightboxImage.removeAttribute('src');

            if (previousFocus && typeof previousFocus.focus === 'function') {
                previousFocus.focus();
            }
        }

        document.addEventListener('click', function (event) {
            var image = event.target && event.target.closest ? event.target.closest('img[data-image-lightbox]') : null;
            if (!image) {
                return;
            }

            event.preventDefault();
            openLightbox(image);
        });

        document.addEventListener('keydown', function (event) {
            if (event.key === 'Escape') {
                closeLightbox();
                return;
            }

            if ((event.key === 'Enter' || event.key === ' ') && event.target && event.target.matches && event.target.matches('img[data-image-lightbox]')) {
                event.preventDefault();
                openLightbox(event.target);
            }
        });

        Array.prototype.slice.call(document.querySelectorAll('img[data-image-lightbox]')).forEach(function (image) {
            if (!image.hasAttribute('tabindex')) {
                image.setAttribute('tabindex', '0');
            }
            if (!image.hasAttribute('role')) {
                image.setAttribute('role', 'button');
            }
        });
    }

    function initDecorativeIcons(root) {
        var scope = root && root.querySelectorAll ? root : document;
        Array.prototype.slice.call(scope.querySelectorAll('.fa')).forEach(function (icon) {
            if (!icon.hasAttribute('aria-hidden')) {
                icon.setAttribute('aria-hidden', 'true');
            }
        });
    }

    function setSubmittingState(button, isSubmitting, isInitiator) {
        if (!button) {
            return;
        }

        if (!button.dataset.originalDisabledState) {
            button.dataset.originalDisabledState = button.disabled ? 'true' : 'false';
        }

        if (!button.dataset.originalHtml && button.tagName === 'BUTTON') {
            button.dataset.originalHtml = button.innerHTML;
        }

        if (!button.dataset.originalValue && button.tagName === 'INPUT') {
            button.dataset.originalValue = button.value;
        }

        if (isSubmitting) {
            button.disabled = true;
            button.classList.add('is-submitting');

            if (!isInitiator) {
                return;
            }

            var loadingText = button.getAttribute('data-submit-loading-text') || window.OpenCRMText('sending');
            if (button.tagName === 'BUTTON') {
                button.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>' + loadingText;
                return;
            }

            button.value = loadingText;
            return;
        }

        button.classList.remove('is-submitting');
        button.disabled = button.dataset.originalDisabledState === 'true';

        if (button.tagName === 'BUTTON' && button.dataset.originalHtml) {
            button.innerHTML = button.dataset.originalHtml;
        }

        if (button.tagName === 'INPUT' && button.dataset.originalValue) {
            button.value = button.dataset.originalValue;
        }
    }

    document.addEventListener('submit', function (event) {
        var form = event.target;
        ensureFormRequestVerificationToken(form);

        if (!shouldHandleSubmitLoader(form)) {
            return;
        }

        if (!isClientValidationPassed(form)) {
            resetSubmittingState(form);
            return;
        }

        if (form.dataset.isSubmitting === 'true') {
            event.preventDefault();
            return;
        }

        form.dataset.isSubmitting = 'true';

        var submitter = event.submitter || null;
        preserveSubmitterValue(form, submitter);
        getSubmitButtons(form).forEach(function (button) {
            setSubmittingState(button, true, button === submitter);
        });

        window.setTimeout(function () {
            if (event.defaultPrevented) {
                resetSubmittingState(form);
            }
        }, 0);
    }, true);

    document.addEventListener('invalid', function (event) {
        var field = event.target;
        if (!field || !field.form || !shouldHandleSubmitLoader(field.form)) {
            return;
        }

        resetSubmittingState(field.form);
    }, true);

    window.initAppTooltips = initTooltips;
    window.initAppPasswordToggles = initPasswordToggles;

    initApiRequestProtection();

    document.addEventListener('DOMContentLoaded', function () {
        initApiRequestProtection();
        initTooltips(document);
        initTooltipDelegates();
        initTooltipObserver();
        initPasswordToggles(document);
        initImageLightbox();
        initDecorativeIcons(document);
    });
})();

// Подпись используется только для отображения, исходные поля автомобиля остаются неизменными.
function getVehicleDisplayName(vehicle) {
    if (!vehicle) return '';
    var identity = [vehicle.brandName || vehicle.manualBrandName, vehicle.modelName || vehicle.manualModelName]
        .filter(function (value) { return value && value.trim(); })
        .map(function (value) { return value.trim(); }).join(' ');

    return identity || [window.OpenCRMText('vehicle'), vehicle.registrationPlate].filter(Boolean).join(' · ');
}
