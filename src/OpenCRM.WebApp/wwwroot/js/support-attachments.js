(() => {
    const input = document.getElementById('support-attachments');
    const list = document.getElementById('support-attachments-list');
    const status = document.getElementById('support-attachments-status');
    if (!input || !list || !status) return;

    const maximumFiles = 5;
    const maximumFileSize = 10 * 1024 * 1024;
    const maximumTotalSize = 20 * 1024 * 1024;
    const allowedExtensions = new Set(['.jpg', '.jpeg', '.png', '.webp', '.pdf', '.txt', '.log']);
    const messages = input.closest('.support-attachments').dataset;
    const sizeFormatter = new Intl.NumberFormat(messages.formattingCulture, { maximumFractionDigits: 1 });
    let selectedFiles = [];

    function formatSize(size) {
        if (size < 1024) return `${size} ${messages.bytes}`;
        if (size < 1024 * 1024) return `${sizeFormatter.format(size / 1024)} ${messages.kilobytes}`;
        return `${sizeFormatter.format(size / 1024 / 1024)} ${messages.megabytes}`;
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
            remove.title = messages.removeTitle;
            remove.setAttribute('aria-label', messages.removeLabelTemplate.replace('{fileName}', file.name));
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
                message = messages.errorExtension;
                break;
            }
            if (file.size === 0 || file.size > maximumFileSize) {
                message = messages.errorFileSize.replace('{fileName}', file.name);
                break;
            }
            if (nextFiles.length >= maximumFiles) {
                message = messages.errorFileCount;
                break;
            }
            if (nextFiles.reduce((total, item) => total + item.size, 0) + file.size > maximumTotalSize) {
                message = messages.errorTotalSize;
                break;
            }
            nextFiles.push(file);
        }

        selectedFiles = nextFiles;
        status.textContent = message;
        render();
    });
})();
