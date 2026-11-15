new Vue({
    el: '#vehicle-form',
    data: function () {
        var page = window.vehicleJournalPage || {};
        return {
            vehicleId: page.vehicleId || '',
            organizationId: page.organizationId || '',
            returnUrl: page.returnUrl || '',
            events: [],
            loading: true
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
        getVehicleUrl: function () {
            return this.appendOrganizationId('/vehicle/' + this.vehicleId);
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
        getVehicleHistoryIcon: function (event) {
            var type = event && event.eventType ? event.eventType : '';
            if (type.indexOf('maintenance_') === 0) return 'fa-wrench';
            if (type.indexOf('counter_') === 0) return 'fa-tachometer';
            if (type.indexOf('defect_') === 0) return 'fa-exclamation-triangle';
            if (type.indexOf('vehicle_') === 0) return 'fa-car';
            return 'fa-history';
        },
        loadEvents: async function () {
            this.loading = true;

            try {
                var response = await axios.get('/api/v1/vehicles/' + this.vehicleId + '/history', {
                    params: Object.assign({}, this.getWorkspaceParams(), {
                        take: 300,
                        _ts: Date.now()
                    })
                });

                this.events = response.data || [];
            } catch (error) {
                this.events = [];
                console.error('API Error:', error);
            } finally {
                this.loading = false;
            }
        }
    },
    mounted: function () {
        this.loadEvents();
    }
});
