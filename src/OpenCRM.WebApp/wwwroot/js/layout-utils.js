function clickLogOut() {
    const logOutButton = document.getElementById('logOutBtnNav');
    if (logOutButton) {
        logOutButton.click();
    }
}

async function stopImpersonation(endpoint) {
    if (!confirm(window.OpenCRMText('returnAdmin'))) {
        return;
    }

    try {
        const response = await axios.post(endpoint || '/Auth/StopImpersonation');

        if (response.data && response.data.success) {
            window.location.href = '/admin-panel/#/users';
            return;
        }

        alert(window.OpenCRMText('error', { message: (response.data && response.data.message) || window.OpenCRMText('returnAdminFailed') }));
    } catch (error) {
        console.error('Ошибка возврата из имперсонации:', error);
        alert(window.OpenCRMText('returnAdminError'));
    }
}

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.js-stop-impersonation').forEach(function (button) {
        button.addEventListener('click', function () {
            stopImpersonation(button.dataset.stopImpersonationUrl);
        });
    });
});
