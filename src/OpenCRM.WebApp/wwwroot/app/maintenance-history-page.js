new Vue({
    el: '#maintenance-details-form',
    data: function () {
        var page = window.maintenanceHistoryPage || {};
        return {
            maintenanceId: page.maintenanceId || '',
            organizationId: page.organizationId || '',
            returnUrl: page.returnUrl || '',
            events: [],
            loading: true,
            errorMessage: ''
        };
    },
    methods: {
        getWorkspaceParams: function () {
            return this.organizationId ? { organizationId: this.organizationId } : {};
        },
        appendOrganizationId: function (url) {
            if (!this.organizationId) {
                return url;
            }

            var separator = url.indexOf('?') >= 0 ? '&' : '?';
            return url + separator + 'organizationId=' + encodeURIComponent(this.organizationId);
        },
        getMaintenanceUrl: function () {
            return this.appendOrganizationId('/maintenance/' + this.maintenanceId);
        },
        formatDate: function (dateString) {
            if (!dateString) {
                return '—';
            }

            var value = String(dateString).trim();
            if (!value) {
                return '—';
            }

            var date = value.indexOf('T') >= 0
                ? moment(value)
                : moment(value.substring(0, 10), 'YYYY-MM-DD', true);

            return date.isValid() ? date.format('DD.MM.YYYY HH:mm') : '—';
        },
        loadEvents: async function () {
            this.loading = true;

            try {
                var response = await axios.get('/api/v1/maintenance/' + this.maintenanceId + '/history', {
                    params: this.getWorkspaceParams()
                });

                this.events = response.data || [];
            } catch (error) {
                this.events = [];
                this.handleApiError(error, 'Не удалось загрузить историю обслуживания');
            } finally {
                this.loading = false;
            }
        },
        showModal: function (selector) {
            $(selector).modal('show');
        },
        hideModal: function (selector) {
            $(selector).modal('hide');
        },
        handleApiError: function (error, defaultMessage) {
            this.errorMessage = error.response && error.response.data && error.response.data.message
                ? error.response.data.message
                : defaultMessage;
            this.showModal('#errorModal');
            console.error('API Error:', error);
        },
        hideError: function () {
            this.errorMessage = '';
            this.hideModal('#errorModal');
        }
    },
    mounted: function () {
        this.loadEvents();
    }
});
