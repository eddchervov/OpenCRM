(function () {
    var modal = document.getElementById('transferOwnershipModal');
    if (!modal) {
        return;
    }

    var form = document.getElementById('transferOwnershipForm');
    var memberNameNode = document.getElementById('transferOwnershipMemberName');

    modal.addEventListener('show.bs.modal', function (event) {
        var trigger = event.relatedTarget;
        if (!trigger) {
            return;
        }

        var organizationId = trigger.getAttribute('data-organization-id');
        var memberId = trigger.getAttribute('data-member-id');
        var memberName = trigger.getAttribute('data-member-name') || '-';

        if (memberNameNode) {
            memberNameNode.textContent = memberName;
        }

        if (form && organizationId && memberId) {
            form.setAttribute('action', '/organizations/' + encodeURIComponent(organizationId) + '/members/' + encodeURIComponent(memberId) + '/transfer-ownership');
        }
    });
})();
