(function () {
    var root = document.querySelector('[data-ai-pilot][data-embedded="true"]');
    if (!root || window.parent === window) return;
    root.addEventListener('assistant:action-completed', function () {
        window.parent.postMessage({
            type: 'opencrm:assistant-updated',
            organizationId: root.dataset.organizationId
        }, window.location.origin);
    });
}());
