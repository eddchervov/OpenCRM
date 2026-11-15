(() => {
    const input = document.getElementById('support-attachments');
    const list = document.getElementById('support-attachments-list');
    const status = document.getElementById('support-attachments-status');
    if (!input || !list || !status) return;

    const maximumFiles = 5;
    const maximumFileSize = 10 * 1024 * 1024;
    const maximumTotalSize = 20 * 1024 * 1024;
    const allowedExtensions = new Set(['.jpg', '.jpeg', '.png', '.webp', '.pdf', '.txt', '.log']);
    const sizeFormatter = new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 });
    let selectedFiles = [];

    function formatSize(size) {
        if (size < 1024) return `${size} Б`;
        if (size < 1024 * 1024) return `${sizeFormatter.format(size / 1024)} КБ`;
        return `${sizeFormatter.format(size / 1024 / 1024)} МБ`;
    }

    function render() {
        const transfer = new DataTransfer();
        selectedFiles.forEach(file => transfer.items.add(file));
        input.files = transfer.files;
        list.replaceChildren();

        selectedFiles.forEach((file, index) => {
            const item = document.createElement('li');
            const icon = document.createElement('i');
            const name = document.createElement('span');
            const size = document.createElement('span');
            const remove = document.createElement('button');
            icon.className = `support-attachments__icon fa ${file.type.startsWith('image/') ? 'fa-file-image-o' : 'fa-file-text-o'}`;
            icon.setAttribute('aria-hidden', 'true');
            name.className = 'support-attachments__name';
            name.textContent = file.name;
            name.title = file.name;
            size.className = 'support-attachments__size';
            size.textContent = formatSize(file.size);
            remove.type = 'button';
            remove.className = 'support-attachments__remove';
            remove.textContent = '×';
            remove.title = 'Удалить файл';
            remove.setAttribute('aria-label', `Убрать файл ${file.name}`);
            remove.addEventListener('click', () => {
                selectedFiles.splice(index, 1);
                status.textContent = '';
                render();
                const nextRemove = list.querySelectorAll('button')[Math.min(index, selectedFiles.length - 1)];
                (nextRemove || input).focus();
            });
            item.append(icon, name, size, remove);
            list.append(item);
        });
    }

    input.addEventListener('change', () => {
        const incoming = Array.from(input.files || []);
        const nextFiles = [...selectedFiles];
        let message = '';

        for (const file of incoming) {
            const extension = file.name.includes('.') ? `.${file.name.split('.').pop().toLowerCase()}` : '';
            if (!allowedExtensions.has(extension)) {
                message = 'Выберите JPG, PNG, WebP, PDF, TXT или LOG.';
                break;
            }
            if (file.size === 0 || file.size > maximumFileSize) {
                message = `Файл «${file.name}» должен быть непустым и не больше 10 МБ.`;
                break;
            }
            if (nextFiles.length >= maximumFiles) {
                message = 'Можно прикрепить не больше пяти файлов.';
                break;
            }
            if (nextFiles.reduce((total, item) => total + item.size, 0) + file.size > maximumTotalSize) {
                message = 'Общий размер файлов не должен превышать 20 МБ.';
                break;
            }
            nextFiles.push(file);
        }

        selectedFiles = nextFiles;
        status.textContent = message;
        render();
    });
})();
