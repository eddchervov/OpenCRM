function clickLogOut() {
    const logOutButton = document.getElementById('logOutBtnNav');
    if (logOutButton) {
        logOutButton.click();
    }
}

async function stopImpersonation(endpoint) {
    if (!confirm('Вернуться к админской учетке?')) {
        return;
    }

    try {
        const response = await axios.post(endpoint || '/Auth/StopImpersonation');

        if (response.data && response.data.success) {
            window.location.href = '/admin-panel/#/users';
            return;
        }

        alert('Ошибка: ' + ((response.data && response.data.message) || 'не удалось вернуться к админской учетке'));
    } catch (error) {
        console.error('Ошибка возврата из имперсонации:', error);
        alert('Произошла ошибка при возврате к админской учетке');
    }
}

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.js-stop-impersonation').forEach(function (button) {
        button.addEventListener('click', function () {
            stopImpersonation(button.dataset.stopImpersonationUrl);
        });
    });
});
