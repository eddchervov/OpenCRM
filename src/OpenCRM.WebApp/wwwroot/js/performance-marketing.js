(function () {
    'use strict';

    var lightbox;
    var lightboxImage;
    var lightboxCaption;
    var closeButton;
    var previousFocus;

    function ensureLightbox() {
        if (lightbox) {
            return;
        }

        lightbox = document.createElement('div');
        lightbox.className = 'marketing-lightbox';
        lightbox.hidden = true;
        lightbox.setAttribute('role', 'dialog');
        lightbox.setAttribute('aria-modal', 'true');
        lightbox.setAttribute('aria-label', 'Увеличенное изображение');
        lightbox.innerHTML = [
            '<figure class="marketing-lightbox__dialog">',
            '<button type="button" class="marketing-lightbox__close" aria-label="Закрыть">&times;</button>',
            '<img class="marketing-lightbox__image" alt="">',
            '<figcaption class="marketing-lightbox__caption"></figcaption>',
            '</figure>'
        ].join('');
        document.body.appendChild(lightbox);

        lightboxImage = lightbox.querySelector('.marketing-lightbox__image');
        lightboxCaption = lightbox.querySelector('.marketing-lightbox__caption');
        closeButton = lightbox.querySelector('.marketing-lightbox__close');

        lightbox.addEventListener('click', function (event) {
            if (event.target === lightbox || event.target === closeButton) {
                closeLightbox();
            }
        });
    }

    function openLightbox(image) {
        ensureLightbox();
        previousFocus = document.activeElement;
        lightboxImage.src = image.currentSrc || image.src;
        lightboxImage.alt = image.alt || '';
        lightboxCaption.textContent = image.getAttribute('data-lightbox-caption') || image.alt || '';
        lightbox.hidden = false;
        document.body.classList.add('marketing-lightbox-open');
        closeButton.focus();
    }

    function closeLightbox() {
        if (!lightbox || lightbox.hidden) {
            return;
        }

        lightbox.hidden = true;
        lightboxImage.removeAttribute('src');
        document.body.classList.remove('marketing-lightbox-open');
        if (previousFocus && typeof previousFocus.focus === 'function') {
            previousFocus.focus();
        }
    }

    document.addEventListener('click', function (event) {
        var image = event.target && event.target.closest
            ? event.target.closest('img[data-image-lightbox]')
            : null;
        if (!image) {
            return;
        }

        event.preventDefault();
        openLightbox(image);
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'Escape') {
            closeLightbox();
        }
    });

    Array.prototype.slice.call(document.querySelectorAll('img[data-image-lightbox]')).forEach(function (image) {
        image.tabIndex = 0;
        image.setAttribute('role', 'button');
        image.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                openLightbox(image);
            }
        });
    });
})();
