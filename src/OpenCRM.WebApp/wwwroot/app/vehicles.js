if (window.OpenCRMVehiclePlate) {
    window.OpenCRMVehiclePlate.installVue(Vue);
}

Vue.component('v-select', VueSelect.VueSelect);
Vue.component('paginate', VuejsPaginate);
if (window['vue-ctk-date-time-picker'] && !Vue.options.components['vue-ctk-date-time-picker']) {
    Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);
}

var vehicleRoot = document.getElementById('vehicles-form');
var personalVehicleTransferOrganizations = Array.isArray(window.personalVehicleTransferOrganizations)
    ? window.personalVehicleTransferOrganizations
    : [];
var workspaceMaintenanceInitial = window.workspaceMaintenanceInitial || {};
var vehicleViewModeStorageKey = (window.location && window.location.pathname
    ? window.location.pathname
    : 'vehicles') + ':view-mode';

function formatWorkspaceDateForPicker(value) {
    if (!value) {
        return moment().format('DD.MM.YYYY');
    }

    var parsed = moment(value, 'YYYY-MM-DD', true);
    return parsed.isValid() ? parsed.format('DD.MM.YYYY') : moment().format('DD.MM.YYYY');
}

function normalizeWorkspaceDateForServer(value) {
    if (!value) {
        return moment().format('YYYY-MM-DD');
    }

    var parsedPickerValue = moment(value, 'DD.MM.YYYY', true);
    if (parsedPickerValue.isValid()) {
        return parsedPickerValue.format('YYYY-MM-DD');
    }

    var parsedServerValue = moment(value, 'YYYY-MM-DD', true);
    return parsedServerValue.isValid()
        ? parsedServerValue.format('YYYY-MM-DD')
        : moment().format('YYYY-MM-DD');
}

function normalizeWorkspaceTimeForServer(value) {
    if (!value) {
        return null;
    }

    var parsed = moment(value, ['HH:mm:ss', 'HH:mm'], true);
    return parsed.isValid() ? parsed.format('HH:mm') : null;
}

function showAppModal(selector) {
    var element = document.querySelector(selector);
    if (!element) {
        return;
    }

    if (window.bootstrap && window.bootstrap.Modal) {
        window.bootstrap.Modal.getOrCreateInstance(element).show();
        return;
    }

    var jquery = window.jQuery || window.$;
    if (jquery && typeof jquery.fn !== 'undefined' && typeof jquery.fn.modal === 'function') {
        jquery(element).modal('show');
    }
}

function hideAppModal(selector) {
    var element = document.querySelector(selector);
    if (!element) {
        return;
    }

    if (window.bootstrap && window.bootstrap.Modal) {
        window.bootstrap.Modal.getOrCreateInstance(element).hide();
        return;
    }

    var jquery = window.jQuery || window.$;
    if (jquery && typeof jquery.fn !== 'undefined' && typeof jquery.fn.modal === 'function') {
        jquery(element).modal('hide');
    }
}

new Vue({
    el: '#vehicles-form',
    data: function () {
        return {
            formLoad: false,
            loadVehicles: true,
            initialVehiclesLoaded: false,
            organizationId: vehicleRoot ? (vehicleRoot.dataset.organizationId || '') : '',
            workspaceKind: vehicleRoot ? (vehicleRoot.dataset.workspaceKind || '') : '',
            defaultWorkspaceTab: vehicleRoot ? (vehicleRoot.dataset.defaultTab || 'vehicles') : 'vehicles',
            isWorkspaceDashboard: vehicleRoot ? (vehicleRoot.dataset.workspaceDashboard === 'true') : false,
            workspaceMaintenanceDateFrom: vehicleRoot ? (vehicleRoot.dataset.maintenanceDateFrom || '') : '',
            workspaceMaintenanceDateTo: vehicleRoot ? (vehicleRoot.dataset.maintenanceDateTo || '') : '',
            workspaceMaintenanceDateFromPicker: vehicleRoot ? formatWorkspaceDateForPicker(vehicleRoot.dataset.maintenanceDateFrom || '') : '',
            workspaceMaintenanceDateToPicker: vehicleRoot ? formatWorkspaceDateForPicker(vehicleRoot.dataset.maintenanceDateTo || '') : '',
            workspaceMaintenances: Array.isArray(workspaceMaintenanceInitial.items) ? workspaceMaintenanceInitial.items : [],
            workspaceMaintenanceLoaded: true,
            workspaceMaintenanceLoading: false,
            workspaceMaintenancePagination: {
                page: workspaceMaintenanceInitial.page || 1,
                pageSize: workspaceMaintenanceInitial.pageSize || 10,
                totalCount: workspaceMaintenanceInitial.totalCount || 0
            },
            quickMaintenanceVehicleSearch: '',
            quickMaintenanceVehicles: [],
            quickMaintenanceVehiclesLoading: false,
            quickMaintenanceVehicleSearchTimeout: null,
            selectedQuickMaintenanceVehicle: null,
            selectedQuickMaintenanceOrganizationClientId: 'all',
            quickMaintenancePlannedDate: null,
            quickMaintenanceModalLoadedOnce: false,
            enableOrganizationViewToggle: vehicleRoot ? (vehicleRoot.dataset.enableOrgViewToggle === 'true') : false,

            brands: [],
            models: [],
            organizationClients: [],
            organizationClientsEnabled: false,
            loadOrganizationClients: false,
            loadModels: false,
            catalogModeAvailable: true,
            vehicles: [],
            viewMode: window.location.pathname.toLowerCase().replace(/\/$/, '') === '/vehicles' ? 'list' : 'cards',
            selectedBrandId: null,
            isManualIdentity: false,
            saveModal: false,
            transferSaveInProgress: false,
            restoreSaving: false,
            quickMaintenanceOpening: false,
            manualIdentityDraft: {
                brandName: null,
                modelName: null
            },
            catalogIdentityDraft: {
                brandId: null,
                modelId: null
            },

            modalType: 'create',

            selectedBrandFilter: 'all',
            selectedOrganizationClientFilter: 'all',
            selectedKindFilter: 'all',
            availableBrands: [],
            availableKindValues: [],

            vehicle: null,
            deleteVehicle: null,
            transferVehicle: null,
            selectedTransferOrganizationId: '',
            transferOrganizations: personalVehicleTransferOrganizations,
            originalVehicleMileageUnit: null,
            mileageUnitWarningShown: false,

            currentFilters: {
                brandFilter: null,
                organizationClientId: null,
                kindFilter: null,
                searchQuery: ''
            },

            searchTimeout: null,
            searchDebounce: 2000,
            isSearchFocused: false,

            searchLoading: false,
            searchQuery: '',

            uploadingVehicleIds: {},
            currentPhotos: [],
            currentPhotoIndex: 0,

            errorMessage: '',

            activeVehiclesCount: vehicleRoot && vehicleRoot.dataset.activeVehiclesCount ? parseInt(vehicleRoot.dataset.activeVehiclesCount, 10) || 0 : 0,
            archivedVehiclesCount: vehicleRoot && vehicleRoot.dataset.archivedVehiclesCount ? parseInt(vehicleRoot.dataset.archivedVehiclesCount, 10) || 0 : 0,

            pagination: {
                page: 1,
                pageSize: 10,
                totalCount: 0
            },

            archivedVehicles: [],
            loadArchivedVehicles: false,
            selectedArchivedBrandFilter: 'all',
            archiveSearchQuery: '',
            archiveSearchLoading: false,
            archiveSearchTimeout: null,
            availableArchivedBrands: [],
            selectedRestoreVehicle: null,

            archivedPagination: {
                page: 1,
                pageSize: 10,
                totalCount: 0
            },

            pageSizeOptions: [10, 25, 50],

            vehicleKindOptions: [
                { value: 1, text: 'Легковой автомобиль' },
                { value: 2, text: 'Грузовая техника' },
                { value: 3, text: 'Мотоцикл' },
                { value: 4, text: 'Квадроцикл' },
                { value: 5, text: 'Багги / UTV' },
                { value: 6, text: 'Снегоход' },
                { value: 7, text: 'Гидроцикл' },
                { value: 8, text: 'Лодка / катер' },
                { value: 9, text: 'Спецтехника' },
                { value: 10, text: 'Прицеп' },
                { value: 99, text: 'Другая техника' }
            ],
            mileageUnitOptions: [
                { value: 1, text: 'Километры' },
                { value: 2, text: 'Моточасы' }
            ],

            loadingLimitInfo: true,
            vehicleLimitInfo: {
                currentVehicleCount: 0,
                canAddMoreVehicles: true,
                hasActiveSubscription: false,
                maxVehiclesForUser: 25,
                isMaxPlan: false
            },
        };
    },
    computed: {
        isOrganizationWorkspace: function () {
            return this.workspaceKind === 'organization' && !!this.organizationId;
        },
        hasActiveFilters: function () {
            return this.currentFilters.brandFilter || this.currentFilters.organizationClientId || this.currentFilters.kindFilter || this.currentFilters.searchQuery;
        },
        isActiveFleetUpdating: function () {
            return this.searchLoading || this.loadVehicles;
        },
        vehicleKindFilterOptions: function () {
            var options = [{ value: 'all', text: 'Все типы' }];
            var kindMap = {};
            var vm = this;

            this.availableKindValues.forEach(function (kindValue) {
                if (kindValue === null || kindValue === undefined) {
                    return;
                }

                kindMap[kindValue] = true;
            });

            Object.keys(kindMap).map(function (value) { return parseInt(value, 10); }).sort(function (a, b) { return a - b; }).forEach(function (kindValue) {
                var option = vm.vehicleKindOptions.find(function (item) { return item.value === kindValue; });
                if (option) {
                    options.push(option);
                }
            });

            if (this.selectedKindFilter !== 'all' && !kindMap[this.selectedKindFilter]) {
                var selectedOption = this.vehicleKindOptions.find(function (item) { return item.value === vm.selectedKindFilter; });
                if (selectedOption) {
                    options.push(selectedOption);
                }
            }

            return options;
        },
        organizationClientFilterOptions: function () {
            var options = [{ id: 'all', name: 'Все клиенты' }];
            if (!Array.isArray(this.organizationClients)) {
                return options;
            }

            this.organizationClients.forEach(function (client) {
                options.push(client);
            });

            return options;
        },
        hasActiveArchiveFilters: function () {
            return this.getSelectedBrandFilterValue(this.selectedArchivedBrandFilter) || this.archiveSearchQuery;
        },
        canSaveVehicle: function () {
            if (!this.vehicle) return false;
            return [this.vehicle.registrationPlate, this.vehicle.vin, this.vehicle.bodyNumber,
                this.vehicle.engineNumber, this.vehicle.registrationCertificate, this.vehicle.name]
                .some(function (value) { return value && String(value).trim(); });
        },
        filteredVehicles: function () {
            return this.vehicles;
        },
        canTransferPersonalVehicles: function () {
            return !this.isOrganizationWorkspace && this.transferOrganizations.length > 0;
        },

        paginationInfo: function () {
            if (this.pagination.totalCount === 0) return '';
            const start = ((this.pagination.page - 1) * this.pagination.pageSize) + 1;
            const end = Math.min(this.pagination.page * this.pagination.pageSize, this.pagination.totalCount);
            return `Показано ${start}-${end} из ${this.pagination.totalCount} ед. техники`;
        },

        archivedPaginationInfo: function () {
            if (this.archivedPagination.totalCount === 0) return '';
            const start = ((this.archivedPagination.page - 1) * this.archivedPagination.pageSize) + 1;
            const end = Math.min(this.archivedPagination.page * this.archivedPagination.pageSize, this.archivedPagination.totalCount);
            return `Показано ${start}-${end} из ${this.archivedPagination.totalCount} ед. техники`;
        },

        workspaceMaintenanceTotalPages: function () {
            return Math.max(1, Math.ceil(this.workspaceMaintenancePagination.totalCount / this.workspaceMaintenancePagination.pageSize));
        },

        workspaceMaintenancePaginationInfo: function () {
            if (this.workspaceMaintenancePagination.totalCount === 0) return '';
            const start = ((this.workspaceMaintenancePagination.page - 1) * this.workspaceMaintenancePagination.pageSize) + 1;
            const end = Math.min(this.workspaceMaintenancePagination.page * this.workspaceMaintenancePagination.pageSize, this.workspaceMaintenancePagination.totalCount);
            return `Показано ${start}-${end} из ${this.workspaceMaintenancePagination.totalCount} обслуживаний`;
        },

        quickMaintenanceVehicleOptions: function () {
            return Array.isArray(this.quickMaintenanceVehicles) ? this.quickMaintenanceVehicles : [];
        },

        maintenanceCountText: function () {
            var vm = this;
            return function (count) {
                if (count === 1) return '1 обслуживание';
                if (count >= 2 && count <= 4) return count + ' обслуживания';
                return count + ' обслуживаний';
            };
        },
        totalPages: function () {
            return Math.ceil(this.pagination.totalCount / this.pagination.pageSize) || 1;
        },

        archivedTotalPages: function () {
            return Math.ceil(this.archivedPagination.totalCount / this.archivedPagination.pageSize) || 1;
        },

        displayedPages: function () {
            var pages = [];
            var total = this.totalPages;
            var current = this.pagination.page;

            pages.push(1);
            if (total <= 5) {
                for (var i = 2; i <= total - 1; i++) {
                    pages.push(i);
                }
            } else {
                if (current <= 3) {
                    pages.push(2);
                    pages.push(3);
                    pages.push(4);
                    if (total > 5) pages.push('...');
                } else if (current >= total - 2) {
                    if (total > 5) pages.push('...');
                    pages.push(total - 3);
                    pages.push(total - 2);
                    pages.push(total - 1);
                } else {
                    pages.push('...');
                    pages.push(current - 1);
                    pages.push(current);
                    pages.push(current + 1);
                    pages.push('...');
                }
            }

            if (total > 1) {
                pages.push(total);
            }

            return pages;
        },

        archivedDisplayedPages: function () {
            var pages = [];
            var total = this.archivedTotalPages;
            var current = this.archivedPagination.page;

            pages.push(1);
            if (total <= 5) {
                for (var i = 2; i <= total - 1; i++) {
                    pages.push(i);
                }
            } else {
                if (current <= 3) {
                    pages.push(2);
                    pages.push(3);
                    pages.push(4);
                    if (total > 5) pages.push('...');
                } else if (current >= total - 2) {
                    if (total > 5) pages.push('...');
                    pages.push(total - 3);
                    pages.push(total - 2);
                    pages.push(total - 1);
                } else {
                    pages.push('...');
                    pages.push(current - 1);
                    pages.push(current);
                    pages.push(current + 1);
                    pages.push('...');
                }
            }

            if (total > 1) {
                pages.push(total);
            }

            return pages;
        },
        showLimitExceededWarning: function () {
            return this.vehicleLimitInfo.currentVehicleCount > this.vehicleLimitInfo.maxVehiclesForUser &&
                !this.loadingLimitInfo;
        },

        exceededCount: function () {
            if (!this.showLimitExceededWarning) return 0;
            return this.vehicleLimitInfo.currentVehicleCount - this.vehicleLimitInfo.maxVehiclesForUser;
        },

        alertClass: function () {
            if (this.showLimitExceededWarning) {
                return 'alert-danger limit-exceeded-alert';
            } else if (!this.vehicleLimitInfo.hasActiveSubscription) {
                return 'alert-warning';
            } else {
                return 'alert-info';
            }
        },

        alertMessage: function () {
            if (this.showLimitExceededWarning) {
                return 'Внимание! Превышен лимит техники на ' + this.exceededCount;
            } else if (!this.vehicleLimitInfo.hasActiveSubscription) {
                return 'Бесплатный тариф';
            } else {
                return 'Тариф "' + (this.vehicleLimitInfo.currentPlanName || '') + '"';
            }
        }
    },
    watch: {
        selectedBrandFilter: function (newVal) {
            this.currentFilters.brandFilter = this.getSelectedBrandFilterValue(newVal);
            this.pagination.page = 1;
            this.getVehicles();
        },
        selectedOrganizationClientFilter: function (newVal) {
            this.currentFilters.organizationClientId = this.getSelectedOrganizationClientFilterValue(newVal);
            this.pagination.page = 1;
            this.getVehicles();
        },
        selectedArchivedBrandFilter: function (newVal) {
            this.archivedPagination.page = 1;
            this.getArchivedVehicles();
        },
        selectedKindFilter: function (newVal) {
            this.currentFilters.kindFilter = this.getSelectedKindFilterValue(newVal);
            this.pagination.page = 1;
            this.getVehicles();
        },
        selectedQuickMaintenanceOrganizationClientId: function () {
            if (!this.isWorkspaceDashboard) {
                return;
            }

            this.selectedQuickMaintenanceVehicle = null;
            this.loadQuickMaintenanceVehicleOptions();
        }
    },
    methods: {
            getVehicleDisplayName: getVehicleDisplayName,
        getWorkspaceParams: function () {
            return this.organizationId ? { organizationId: this.organizationId } : {};
        },

        closeWorkspaceMaintenanceDatePicker: function (refName) {
            var picker = this.$refs ? this.$refs[refName] : null;
            if (Array.isArray(picker)) {
                picker = picker[0];
            }

            if (picker && typeof picker.closePicker === 'function') {
                picker.closePicker();
            }
        },

        applyWorkspaceMaintenanceDateFilter: function () {
            var dateFrom = normalizeWorkspaceDateForServer(this.workspaceMaintenanceDateFromPicker);
            var dateTo = normalizeWorkspaceDateForServer(this.workspaceMaintenanceDateToPicker);

            if (moment(dateTo, 'YYYY-MM-DD', true).isBefore(moment(dateFrom, 'YYYY-MM-DD', true))) {
                dateTo = dateFrom;
                this.workspaceMaintenanceDateToPicker = formatWorkspaceDateForPicker(dateTo);
            }

            this.workspaceMaintenanceDateFrom = dateFrom;
            this.workspaceMaintenanceDateTo = dateTo;
            this.loadWorkspaceMaintenances(1);
        },

        loadWorkspaceMaintenances: function (page) {
            if (!this.organizationId || this.workspaceMaintenanceLoading) {
                return Promise.resolve();
            }

            var vm = this;
            var nextPage = page || vm.workspaceMaintenancePagination.page || 1;
            vm.workspaceMaintenanceLoading = true;

            return axios.get('/api/v1/organizations/' + encodeURIComponent(vm.organizationId) + '/workspace/maintenance', {
                params: {
                    maintenanceDateFrom: vm.workspaceMaintenanceDateFrom,
                    maintenanceDateTo: vm.workspaceMaintenanceDateTo,
                    maintenancePage: nextPage,
                    maintenancePageSize: vm.workspaceMaintenancePagination.pageSize,
                    _ts: Date.now()
                }
            }).then(function (response) {
                var data = response.data || {};
                vm.workspaceMaintenances = Array.isArray(data.items) ? data.items : [];
                vm.workspaceMaintenancePagination.page = data.page || nextPage;
                vm.workspaceMaintenancePagination.pageSize = data.pageSize || vm.workspaceMaintenancePagination.pageSize;
                vm.workspaceMaintenancePagination.totalCount = data.totalCount || 0;
                vm.workspaceMaintenanceLoaded = true;

                if (window.history && window.history.replaceState) {
                    var url = '/organizations/' + encodeURIComponent(vm.organizationId)
                        + '?tab=maintenance'
                        + '&maintenanceDateFrom=' + encodeURIComponent(vm.workspaceMaintenanceDateFrom)
                        + '&maintenanceDateTo=' + encodeURIComponent(vm.workspaceMaintenanceDateTo)
                        + '&maintenancePage=' + encodeURIComponent(vm.workspaceMaintenancePagination.page)
                        + '&maintenancePageSize=' + encodeURIComponent(vm.workspaceMaintenancePagination.pageSize);
                    window.history.replaceState(null, document.title, url);
                }
            }).catch(function (error) {
                vm.handleApiError(error, 'Не удалось загрузить обслуживания рабочего стола');
            }).finally(function () {
                vm.workspaceMaintenanceLoading = false;
            });
        },

        resetWorkspaceMaintenanceDateFilter: function () {
            var today = moment().format('DD.MM.YYYY');
            this.workspaceMaintenanceDateFromPicker = today;
            this.workspaceMaintenanceDateToPicker = today;
            this.applyWorkspaceMaintenanceDateFilter();
        },

        changeWorkspaceMaintenancePage: function (page) {
            if (page < 1 || page > this.workspaceMaintenanceTotalPages || page === this.workspaceMaintenancePagination.page) {
                return;
            }

            this.loadWorkspaceMaintenances(page);
        },

        changeWorkspaceMaintenancePageSize: function () {
            this.workspaceMaintenancePagination.pageSize = this.normalizePageSize(this.workspaceMaintenancePagination.pageSize);
            this.loadWorkspaceMaintenances(1);
        },

        formatWorkspaceMaintenanceDate: function (value) {
            return value ? moment(value).format('DD.MM.YYYY') : '-';
        },

        formatWorkspaceMaintenanceDateTime: function (maintenance) {
            if (!maintenance) {
                return '-';
            }

            var dateText = this.formatWorkspaceMaintenanceDate(maintenance.date);
            var startTime = normalizeWorkspaceTimeForServer(maintenance.plannedStartTime);
            var endTime = normalizeWorkspaceTimeForServer(maintenance.plannedEndTime);
            if (startTime && endTime) {
                var endDate = maintenance.plannedEndDate && maintenance.plannedEndDate.slice(0, 10) !== maintenance.date.slice(0, 10)
                    ? this.formatWorkspaceMaintenanceDate(maintenance.plannedEndDate) + ', ' : '';
                return dateText + ', ' + startTime + ' — ' + endDate + endTime;
            }

            return dateText;
        },

        getWorkspaceMaintenanceVehicleUrl: function (maintenance) {
            if (!maintenance || !maintenance.vehicleId) {
                return '#';
            }

            var url = '/vehicle/' + maintenance.vehicleId;
            return this.organizationId ? url + '?organizationId=' + encodeURIComponent(this.organizationId) : url;
        },

        openQuickMaintenanceModal: function (plannedDate) {
            if (!this.isOrganizationWorkspace || !this.organizationId) {
                return;
            }

            var vm = this;
            vm.selectedQuickMaintenanceVehicle = null;
            vm.quickMaintenancePlannedDate = plannedDate || null;
            showAppModal('#quickMaintenanceModal');

            if (!vm.organizationClientsEnabled && !vm.loadOrganizationClients) {
                vm.getOrganizationClientOptions();
            }

            if (!vm.quickMaintenanceModalLoadedOnce || vm.quickMaintenanceVehicles.length === 0) {
                vm.loadQuickMaintenanceVehicleOptions();
            }

            vm.$nextTick(function () {
                var input = vm.$refs.quickMaintenanceVehicleSearchInput;
                if (Array.isArray(input)) {
                    input = input[0];
                }

                if (input && typeof input.focus === 'function') {
                    input.focus();
                }
            });
        },

        handleQuickMaintenanceVehicleSearch: function () {
            var vm = this;
            if (vm.quickMaintenanceVehicleSearchTimeout) {
                clearTimeout(vm.quickMaintenanceVehicleSearchTimeout);
            }

            vm.quickMaintenanceVehicleSearchTimeout = setTimeout(function () {
                vm.loadQuickMaintenanceVehicleOptions();
            }, 350);
        },

        loadQuickMaintenanceVehicleOptions: function () {
            if (!this.organizationId) {
                return Promise.resolve([]);
            }

            var vm = this;
            vm.quickMaintenanceVehiclesLoading = true;

            return axios.get('/api/v1/organizations/' + encodeURIComponent(vm.organizationId) + '/workspace/vehicle-options', {
                params: {
                    searchQuery: vm.quickMaintenanceVehicleSearch || '',
                    organizationClientId: vm.selectedQuickMaintenanceOrganizationClientId === 'all'
                        ? null
                        : vm.selectedQuickMaintenanceOrganizationClientId,
                    take: 20,
                    _ts: Date.now()
                }
            }).then(function (response) {
                vm.quickMaintenanceVehicles = Array.isArray(response.data) ? response.data : [];
                vm.quickMaintenanceModalLoadedOnce = true;

                if (!vm.selectedQuickMaintenanceVehicle && vm.quickMaintenanceVehicles.length === 1) {
                    vm.selectedQuickMaintenanceVehicle = vm.quickMaintenanceVehicles[0];
                }

                return vm.quickMaintenanceVehicles;
            }).catch(function (error) {
                vm.handleApiError(error, 'Не удалось загрузить технику для обслуживания');
                throw error;
            }).finally(function () {
                vm.quickMaintenanceVehiclesLoading = false;
            });
        },

        selectQuickMaintenanceVehicle: function (vehicle) {
            this.selectedQuickMaintenanceVehicle = vehicle || null;
        },

        getQuickMaintenanceVehicleId: function (vehicle) {
            return vehicle && (vehicle.id || vehicle.vehicleId || vehicle.value)
                ? vehicle.id || vehicle.vehicleId || vehicle.value
                : '';
        },

        getMaintenanceCreateUrl: function (vehicle) {
            var vehicleId = this.getQuickMaintenanceVehicleId(vehicle);
            if (!vehicleId) {
                return '#';
            }

            var url = '/vehicle/' + encodeURIComponent(vehicleId) + '/maintenance/create';
            if (this.organizationId) {
                url += '?organizationId=' + encodeURIComponent(this.organizationId);
            }

            url += (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
            if (this.quickMaintenancePlannedDate) {
                url += '&plannedDate=' + encodeURIComponent(this.quickMaintenancePlannedDate);
            }

            return url;
        },

        createMaintenanceForVehicle: function (vehicle) {
            var vehicleId = this.getQuickMaintenanceVehicleId(vehicle);
            if (!vehicleId || this.quickMaintenanceOpening) {
                return;
            }

            this.selectedQuickMaintenanceVehicle = vehicle;
            this.quickMaintenanceOpening = true;
            window.location.href = this.getMaintenanceCreateUrl(vehicle);
        },

        createMaintenanceForSelectedVehicle: function () {
            if (!this.selectedQuickMaintenanceVehicle) {
                return;
            }

            this.createMaintenanceForVehicle(this.selectedQuickMaintenanceVehicle);
        },

        getCookieNameSuffix: function () {
            if (!this.isOrganizationWorkspace) {
                return 'personal';
            }

            return 'org_' + String(this.organizationId || 'default').replace(/[^a-zA-Z0-9]/g, '_');
        },

        getPageSizeCookieKey: function (scope) {
            return 'opencrm_vehicles_' + this.getCookieNameSuffix() + '_' + scope + '_page_size';
        },

        readCookie: function (name) {
            var escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            var match = document.cookie.match(new RegExp('(?:^|; )' + escapedName + '=([^;]*)'));
            return match ? decodeURIComponent(match[1]) : null;
        },

        writeCookie: function (name, value, days) {
            var expires = '';
            if (typeof days === 'number') {
                var date = new Date();
                date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
                expires = '; expires=' + date.toUTCString();
            }

            document.cookie = name + '=' + encodeURIComponent(value) + expires + '; path=/; samesite=lax';
        },

        normalizePageSize: function (value) {
            var pageSize = parseInt(value, 10);
            if (!pageSize || this.pageSizeOptions.indexOf(pageSize) === -1) {
                return this.pageSizeOptions[0];
            }

            return pageSize;
        },

        restorePageSizePreferences: function () {
            this.pagination.pageSize = this.normalizePageSize(this.readCookie(this.getPageSizeCookieKey('active')));
            this.archivedPagination.pageSize = this.normalizePageSize(this.readCookie(this.getPageSizeCookieKey('archive')));
        },

        savePageSizePreference: function (scope, value) {
            this.writeCookie(this.getPageSizeCookieKey(scope), this.normalizePageSize(value), 365);
        },

        loadVehicleLimitInfo: function () {
            var vm = this;
            return new Promise(function (resolve, reject) {
                try {
                    vm.loadingLimitInfo = true;
                    axios.get('/api/v1/vehicles/limit-info', { params: vm.getWorkspaceParams() })
                        .then(function (response) {
                            var data = response.data || {};
                            vm.vehicleLimitInfo = {
                                currentVehicleCount: data.currentVehicleCount !== undefined && data.currentVehicleCount !== null ? data.currentVehicleCount : 0,
                                canAddMoreVehicles: data.canAddMoreVehicles !== undefined && data.canAddMoreVehicles !== null ? data.canAddMoreVehicles : true,
                                hasActiveSubscription: data.hasActiveSubscription === true,
                                maxVehiclesForUser: data.maxVehiclesForUser !== undefined && data.maxVehiclesForUser !== null ? data.maxVehiclesForUser : 25,
                                isMaxPlan: data.isMaxPlan === true,
                                currentPlanName: data.currentPlanName !== undefined && data.currentPlanName !== null ? data.currentPlanName : ''
                            };
                            resolve(vm.vehicleLimitInfo);
                        })
                        .catch(function (error) {
                            console.error('Error loading vehicle limit info:', error);
                            reject(error);
                        })
                        .finally(function () {
                            vm.loadingLimitInfo = false;
                        });
                } catch (error) {
                    console.error('Error loading vehicle limit info:', error);
                    vm.loadingLimitInfo = false;
                    reject(error);
                }
            });
        },

        loadCounts: function () {
            var vm = this;
            return axios.get('/api/v1/vehicles/counts', { params: vm.getWorkspaceParams() })
                .then(function (response) {
                    vm.activeVehiclesCount = response.data.activeCount;
                    vm.archivedVehiclesCount = response.data.archivedCount;
                })
                .catch(function (error) {
                    console.error('Error loading vehicle counts:', error);
                });
        },

        refreshFleetData: function (options) {
            var vm = this;
            var settings = options || {};
            var tasks = [
                vm.getVehicles(),
                vm.loadCounts(),
                vm.loadVehicleLimitInfo()
            ];

            if (settings.includeArchive) {
                tasks.push(vm.getArchivedVehicles());
            }

            return Promise.all(tasks);
        },

        switchToActiveTab: function () {
            if (!this.initialVehiclesLoaded && !this.loadVehicles) {
                this.loadVehiclesFromServer();
            }

            return Promise.resolve();
        },

        switchToArchiveTab: function () {
            var vm = this;
            // Загружаем архивную технику при первом переходе
            if (vm.archivedVehicles.length === 0 && !vm.loadArchivedVehicles) {
                vm.getArchivedVehicles();
            }
        },

        setVehicleViewMode: function (mode) {
            if (this.isOrganizationWorkspace && !this.enableOrganizationViewToggle) {
                this.viewMode = 'list';
                return;
            }

            this.viewMode = mode === 'list' ? 'list' : 'cards';

            try {
                window.localStorage.setItem(vehicleViewModeStorageKey, this.viewMode);
            } catch (error) {
            }
        },

        viewArchivedVehicleDetails: function (vehicle) {
            if (!vehicle || !vehicle.id) {
                return;
            }

            window.location.href = this.getVehicleDetailsUrl(vehicle);
        },

        getVehicleDetailsUrl: function (vehicle) {
            var url = '/vehicle/' + vehicle.id;
            return this.organizationId ? url + '?organizationId=' + encodeURIComponent(this.organizationId) : url;
        },

        getVehicleCreateUrl: function () {
            if (this.organizationId) {
                return this.getModernOrganizationWorkspaceUrl('vehicles/new');
            }

            var url = '/vehicle/create';
            return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
        },

        getModernOrganizationWorkspaceUrl: function (section) {
            var path = window.location && window.location.pathname ? window.location.pathname : '';
            var match = path.match(/^\/(autoservice|detailing|fleet)\/[0-9a-f-]+/i);
            if (match) {
                return match[0] + '#' + section;
            }

            return '/organizations';
        },

        getVehicleEditUrl: function (vehicle) {
            var url = '/vehicle/' + vehicle.id + '/edit';
            if (this.organizationId) {
                url += '?organizationId=' + encodeURIComponent(this.organizationId);
            }

            return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
        },

        getArchivedVehicles: function () {
            var vm = this;
            vm.loadArchivedVehicles = true;

            var params = {
                page: vm.archivedPagination.page,
                pageSize: vm.archivedPagination.pageSize,
                brandFilter: vm.getSelectedBrandFilterValue(vm.selectedArchivedBrandFilter),
                searchQuery: vm.archiveSearchQuery,
                isArchived: true
            };
            Object.assign(params, vm.getWorkspaceParams());

            return axios.get('/api/v1/vehicles', { params: params })
                .then(async function (response) {
                    vm.archivedVehicles = response.data.items;
                    vm.archivedPagination.totalCount = response.data.totalCount;

                    // Загружаем фото для архивной техники
                    await vm.preloadArchivedPhotos();

                    // Обновляем список брендов для фильтра архива
                    vm.updateAvailableArchivedBrands();

                    return response.data;
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка загрузки архива');
                    throw error;
                })
                .finally(function () {
                    vm.loadArchivedVehicles = false;
                    vm.archiveSearchLoading = false;
                });
        },

        preloadArchivedPhotos: async function () {
            var vm = this;
            for (var i = 0; i < vm.archivedVehicles.length; i++) {
                var vehicle = vm.archivedVehicles[i];
                vehicle.photoLoadFailed = false;

                if (vehicle.photoId) {
                    try {
                        vehicle.photoUrl = await vm.loadPhoto(vehicle.photoId);
                    } catch (error) {
                        console.error('Error loading photo for archived vehicle:', vehicle.id, error);
                        vehicle.photoUrl = null;
                        vehicle.photoLoadFailed = true;
                    }
                } else {
                    vehicle.photoUrl = null;
                }

                var photos = await vm.loadVehiclePhotoCollection(vehicle);
                vm.normalizeVehiclePhotos(vehicle, photos);
            }
        },

        updateAvailableArchivedBrands: function () {
            var vm = this;
            var uniqueBrands = [];
            var brandMap = {};

            for (var i = 0; i < vm.archivedVehicles.length; i++) {
                var brandFilterId = vm.getVehicleBrandFilterId(vm.archivedVehicles[i]);
                if (!brandFilterId) {
                    continue;
                }
                if (!brandMap[brandFilterId]) {
                    brandMap[brandFilterId] = true;
                    uniqueBrands.push(brandFilterId);
                }
            }

            vm.availableArchivedBrands = uniqueBrands.map(function (brandFilterId) {
                var vehicle = vm.archivedVehicles.find(function (c) { return vm.getVehicleBrandFilterId(c) === brandFilterId; });
                return {
                    id: brandFilterId,
                    name: vehicle ? vehicle.brandName : 'Unknown'
                };
            });

            vm.availableArchivedBrands.unshift({ id: 'all', name: 'Все марки' });
        },

        restoreFromArchive: function (vehicle) {
            this.selectedRestoreVehicle = vehicle;
            showAppModal('#restoreModal');
        },

        confirmRestoreFromArchive: function () {
            var vm = this;
            if (!vm.selectedRestoreVehicle || vm.restoreSaving) return Promise.resolve();

            vm.restoreSaving = true;
            return axios.post('/api/v1/vehicles/' + vm.selectedRestoreVehicle.id + '/restore', null, { params: vm.getWorkspaceParams() })
                .then(function (response) {
                    hideAppModal('#restoreModal');
                    vm.selectedRestoreVehicle = null;

                    return vm.refreshFleetData({ includeArchive: true }).then(function () {
                        return response.data;
                    });
                })
                .catch(function (error) {
                    hideAppModal('#restoreModal');

                    // Если ошибка 402 (превышение лимита) - показываем модалку подписки
                    if (error.response && error.response.status === 402) {
                        showAppModal('#subscriptionModal');
                    } else {
                        // Все остальные ошибки - показываем стандартное сообщение
                        vm.handleApiError(error, 'Произошла ошибка восстановления техники');
                    }
                    throw error;
                })
                .finally(function () {
                    vm.restoreSaving = false;
                });
        },

        getTimerClass: function (daysLeft) {
            if (daysLeft === undefined || daysLeft === null) {
                return 'bg-secondary';
            }

            if (daysLeft <= 0) {
                return 'bg-danger';
            }

            if (daysLeft <= 3) {
                return 'bg-danger';
            }

            if (daysLeft <= 7) {
                return 'bg-warning';
            }

            if (daysLeft <= 14) {
                return 'bg-info';
            }

            return 'bg-success';
        },

        getArchiveDeletionText: function (daysLeft) {
            if (daysLeft === undefined || daysLeft === null) {
                return 'Срок удаления уточняется';
            }

            if (daysLeft <= 0) {
                return 'Будет удалена сегодня';
            }

            if (daysLeft === 1) {
                return 'Удаление через 1 день';
            }

            if (daysLeft >= 2 && daysLeft <= 4) {
                return 'Удаление через ' + daysLeft + ' дня';
            }

            return 'Удаление через ' + daysLeft + ' дней';
        },

        formatDate: function (dateString) {
            if (!dateString) return 'Не указана';
            return moment(dateString).format('DD.MM.YYYY');
        },

        handleArchiveSearchInput: function () {
            var vm = this;
            if (vm.archiveSearchTimeout) {
                clearTimeout(vm.archiveSearchTimeout);
            }

            vm.archiveSearchTimeout = setTimeout(function () {
                vm.executeArchiveSearch();
            }, vm.searchDebounce);
        },

        executeArchiveSearch: function () {
            this.archiveSearchLoading = true;
            this.archiveSearchQuery = this.archiveSearchQuery.trim();
            this.archivedPagination.page = 1;
            this.getArchivedVehicles();
        },

        applyArchiveSearch: function () {
            if (this.archiveSearchTimeout) {
                clearTimeout(this.archiveSearchTimeout);
                this.archiveSearchTimeout = null;
            }
            this.executeArchiveSearch();
        },

        resetArchiveFilters: function () {
            if (this.archiveSearchTimeout) {
                clearTimeout(this.archiveSearchTimeout);
                this.archiveSearchTimeout = null;
            }

            this.archiveSearchLoading = true;
            this.selectedArchivedBrandFilter = 'all';
            this.archiveSearchQuery = '';
            this.archivedPagination.page = 1;
            this.getArchivedVehicles();
        },

        changeArchivePage: function (page) {
            if (page === '...' || page < 1 || page > this.archivedTotalPages || page === this.archivedPagination.page) {
                return;
            }

            this.archivedPagination.page = page;
            this.getArchivedVehicles();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        },

        changeArchivePageSize: function () {
            this.archivedPagination.pageSize = this.normalizePageSize(this.archivedPagination.pageSize);
            this.savePageSizePreference('archive', this.archivedPagination.pageSize);
            this.archivedPagination.page = 1;
            this.getArchivedVehicles();
        },

        prevArchivePage: function () {
            if (this.archivedPagination.page > 1) {
                this.changeArchivePage(this.archivedPagination.page - 1);
            }
        },

        nextArchivePage: function () {
            if (this.archivedPagination.page < this.archivedTotalPages) {
                this.changeArchivePage(this.archivedPagination.page + 1);
            }
        },

        // Существующие методы
        loadPhoto: function (photoId) {
            return fetch('/api/v1/media/' + photoId + '/path')
                .then(async function (response) {
                    if (!response.ok) {
                        throw new Error('Не удалось загрузить фотографию');
                    }

                    return response.text();
                });
        },
        loadVehiclePhotoCollection: async function (vehicle) {
            if (!vehicle || !vehicle.id) {
                return [];
            }

            try {
                var response = await axios.get('/api/v1/media/vehicles/' + vehicle.id + '/photos', {
                    params: this.getWorkspaceParams()
                });
                var photos = response.data || [];

                photos = photos.slice().sort(function (a, b) {
                    if (a.isPrimary === b.isPrimary) {
                        return 0;
                    }

                    return a.isPrimary ? -1 : 1;
                });

                return photos;
            } catch (error) {
                console.error('Error loading vehicle photos:', vehicle.id, error);
                return [];
            }
        },
        normalizeVehiclePhotos: function (vehicle, photos) {
            photos = Array.isArray(photos) ? photos : [];

            if (!photos.length && vehicle.photoUrl) {
                photos = [{
                    photoId: vehicle.photoId,
                    url: vehicle.photoUrl,
                    isPrimary: true
                }];
            }

            this.$set(vehicle, 'vehiclePhotos', photos);
            this.$set(vehicle, 'currentPhotoIndex', 0);
            this.$set(vehicle, 'photoUrl', photos.length ? photos[0].url : null);
        },
        currentVehiclePhotoUrl: function (vehicle) {
            if (!vehicle) {
                return '';
            }

            if (vehicle.vehiclePhotos && vehicle.vehiclePhotos.length) {
                var index = vehicle.currentPhotoIndex || 0;
                return (vehicle.vehiclePhotos[index] || vehicle.vehiclePhotos[0]).url || '';
            }

            return vehicle.photoUrl || '';
        },
        hasMultipleVehiclePhotos: function (vehicle) {
            return !!(vehicle && vehicle.vehiclePhotos && vehicle.vehiclePhotos.length > 1);
        },
        showPreviousVehiclePhoto: function (vehicle) {
            if (!this.hasMultipleVehiclePhotos(vehicle)) {
                return;
            }

            var index = (vehicle.currentPhotoIndex || 0) - 1;
            if (index < 0) {
                index = vehicle.vehiclePhotos.length - 1;
            }

            this.$set(vehicle, 'currentPhotoIndex', index);
        },
        showNextVehiclePhoto: function (vehicle) {
            if (!this.hasMultipleVehiclePhotos(vehicle)) {
                return;
            }

            var index = ((vehicle.currentPhotoIndex || 0) + 1) % vehicle.vehiclePhotos.length;
            this.$set(vehicle, 'currentPhotoIndex', index);
        },
        openVehiclePhotoViewer: function (vehicle) {
            if (!vehicle) {
                return;
            }

            var photos = vehicle.vehiclePhotos && vehicle.vehiclePhotos.length
                ? vehicle.vehiclePhotos.map(function (photo) { return photo.url; }).filter(Boolean)
                : (vehicle.photoUrl ? [vehicle.photoUrl] : []);

            if (!photos.length) {
                return;
            }

            this.currentPhotos = photos;
            this.currentPhotoIndex = vehicle.currentPhotoIndex || 0;
            showAppModal('#vehiclePhotoViewerModal');
        },
        handleSearchInput: function () {
            var vm = this;
            if (vm.searchTimeout) {
                clearTimeout(vm.searchTimeout);
            }

            vm.searchTimeout = setTimeout(function () {
                vm.executeSearch();
            }, vm.searchDebounce);
        },

        executeSearch: function () {
            this.searchLoading = true;
            this.currentFilters.searchQuery = this.searchQuery;
            this.pagination.page = 1;
            this.loadVehiclesFromServer();
        },

        applySearch: function () {
            if (this.searchTimeout) {
                clearTimeout(this.searchTimeout);
                this.searchTimeout = null;
            }
            this.executeSearch();
        },
        onSearchFocus: function () {
            this.isSearchFocused = true;
        },

        onSearchBlur: function () {
            this.isSearchFocused = false;
        },

        getBrandName: function (brandId) {
            var brand = this.availableBrands.find(function (b) { return b.id === brandId; });
            return brand ? brand.name : 'Unknown';
        },

        getSelectedBrandFilterValue: function (value) {
            return value === 'all' ? null : value;
        },

        getSelectedOrganizationClientFilterValue: function (value) {
            return value === 'all' ? null : value;
        },

        getSelectedKindFilterValue: function (value) {
            return value === 'all' ? null : value;
        },

        applyCatalogModeAvailability: function () {
            this.catalogModeAvailable = Array.isArray(this.brands) && this.brands.length > 0;

            if (!this.catalogModeAvailable) {
                this.isManualIdentity = true;
                this.selectedBrandId = null;
                this.models = [];
                if (this.vehicle) {
                    this.vehicle.modelId = null;
                }
            }
        },

        getVehicleBrandFilterId: function (vehicle) {
            if (!vehicle) {
                return null;
            }

            if (vehicle.brandId !== null && vehicle.brandId !== undefined) {
                return 'catalog:' + vehicle.brandId;
            }

            var brandName = vehicle.brandName || vehicle.manualBrandName;
            if (!brandName) {
                return null;
            }

            return 'manual:' + brandName.trim();
        },

        resetFilters: function () {
            if (this.searchTimeout) {
                clearTimeout(this.searchTimeout);
                this.searchTimeout = null;
            }

            this.searchLoading = true;
            this.selectedBrandFilter = 'all';
            this.selectedOrganizationClientFilter = 'all';
            this.selectedKindFilter = 'all';
            this.searchQuery = '';
            this.currentFilters.brandFilter = null;
            this.currentFilters.organizationClientId = null;
            this.currentFilters.kindFilter = null;
            this.currentFilters.searchQuery = '';
            this.pagination.page = 1;
            this.loadVehiclesFromServer();
        },

        changeFile: function (event, vehicle) {
            var vm = this;
            var file = event.target.files[0];
            if (!file) return;

            var vehicleId = vehicle.id;
            vm.$set(vm.uploadingVehicleIds, vehicleId, true);
            vehicle.isUploading = true;
            vehicle.photoLoadFailed = false;

            var bodyFormData = new FormData();
            bodyFormData.append('formFile', file);

            axios.post('/api/v1/media/vehicles/' + vehicleId + '/upload-photo', bodyFormData)
                .then(async function (response) {
                    var uploadedPhotoId = response.data;
                    return vm.getVehicles().then(async function () {
                        var refreshedVehicle = vm.vehicles.find(function (c) { return c.id === vehicleId; });
                        if (refreshedVehicle) {
                            refreshedVehicle.photoId = uploadedPhotoId;
                            refreshedVehicle.photoLoadFailed = false;
                            try {
                                refreshedVehicle.photoUrl = await vm.loadPhoto(uploadedPhotoId);
                            } catch (error) {
                                refreshedVehicle.photoUrl = null;
                                refreshedVehicle.photoLoadFailed = true;
                            }
                        }
                    });
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка загрузки фотографии техники');
                })
                .finally(function () {
                    vm.$delete(vm.uploadingVehicleIds, vehicleId);
                    event.target.value = '';
                });
        },
        isVehicleUploading: function (vehicleId) {
            return this.uploadingVehicleIds[vehicleId] === true;
        },

        openDeleteVehicleModal: function (vehicle) {
            this.deleteVehicle = vehicle;
            showAppModal('#deleteModal');
        },

        canTransferVehicle: function (vehicle) {
            return this.canTransferPersonalVehicles && vehicle && !vehicle.organizationId && !vehicle.isArchived;
        },

        openTransferVehicleModal: function (vehicle) {
            if (!this.canTransferVehicle(vehicle)) {
                return;
            }

            this.transferVehicle = vehicle;
            this.selectedTransferOrganizationId = '';
            showAppModal('#transferVehicleModal');
        },

        confirmTransferVehicle: function () {
            var vm = this;
            if (!vm.transferVehicle || !vm.selectedTransferOrganizationId) {
                return Promise.resolve();
            }

            vm.transferSaveInProgress = true;

            return axios.post('/api/v1/vehicles/' + vm.transferVehicle.id + '/transfer-to-organization', {
                organizationId: vm.selectedTransferOrganizationId
            })
                .then(function () {
                    hideAppModal('#transferVehicleModal');
                    vm.transferVehicle = null;
                    vm.selectedTransferOrganizationId = '';
                    return vm.refreshFleetData({ includeArchive: true });
                })
                .catch(function (error) {
                    if (error.response && error.response.status === 402) {
                        showAppModal('#subscriptionModal');
                    } else {
                        vm.handleApiError(error, 'Произошла ошибка переноса техники');
                    }
                    throw error;
                })
                .finally(function () {
                    vm.transferSaveInProgress = false;
                });
        },

        save_deleteVehicle: function () {
            var vm = this;
            vm.saveModal = true;
            return axios.delete('/api/v1/vehicles/' + vm.deleteVehicle.id, { params: vm.getWorkspaceParams() })
                .then(function (response) {
                    hideAppModal('#deleteModal');

                    return vm.refreshFleetData({ includeArchive: true }).then(function () {
                        return response.data;
                    });
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка удаления техники');
                    throw error;
                })
                .finally(function () {
                    vm.saveModal = false;
                });
        },

        openModal: function () {
            showAppModal('#exampleModal');
        },

        closeModal: function () {
            hideAppModal('#exampleModal');
        },

        save: function () {
            if (this.modalType === 'create') {
                return this.save_createVehicle();
            } else {
                return this.save_updateVehicle();
            }
        },

        showError: function (message) {
            this.errorMessage = message;
            showAppModal('#errorModal');
        },

        hideError: function () {
            this.errorMessage = '';
            hideAppModal('#errorModal');
        },

        handleApiError: function (error, defaultMessage) {
            var message = (error.response && error.response.data && error.response.data.message) || defaultMessage;
            this.showError(message);
            console.error('API Error:', error);
        },

        save_updateVehicle: function () {
            if (window.OpenCRMVehiclePlate && !window.OpenCRMVehiclePlate.validate()) {
                return;
            }

            var vm = this;
            vm.saveModal = true;
            return axios.put('/api/v1/vehicles/' + vm.vehicle.id, {
                Kind: vm.vehicle.kind,
                MileageUnit: vm.vehicle.mileageUnit || 1,
                ModelId: vm.vehicle.modelId,
                ManualBrandName: vm.isManualIdentity ? vm.vehicle.manualBrandName
                    : !vm.vehicle.modelId ? (vm.brands.find(function (brand) { return String(brand.id) === String(vm.selectedBrandId); }) || {}).name || null : null,
                ManualModelName: vm.isManualIdentity ? vm.vehicle.manualModelName : null,
                YearIssue: vm.vehicle.yearIssue,
                Name: vm.vehicle.name,
                Vin: vm.vehicle.vin || null,
                RegistrationPlate: vm.vehicle.registrationPlate || null,
                AllowOtherRegistrationPlateFormat: vm.vehicle.allowOtherRegistrationPlateFormat === true,
                BodyNumber: vm.vehicle.bodyNumber || null,
                EngineNumber: vm.vehicle.engineNumber || null,
                Color: vm.vehicle.color || null,
                RegistrationCertificate: vm.vehicle.registrationCertificate || null,
                CommissioningDate: vm.vehicle.commissioningDate || null,
                Description: vm.vehicle.description,
                OrganizationClientId: vm.isOrganizationWorkspace ? (vm.vehicle.organizationClientId || null) : null
            }, {
                params: vm.getWorkspaceParams()
            })
                .then(function (response) {
                    vm.closeModal();
                    return vm.getVehicles().then(function () {
                        return response.data;
                    });
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка изменения техники');
                    throw error;
                })
                .finally(function () {
                    vm.saveModal = false;
                });
        },

        save_createVehicle: function () {
            if (window.OpenCRMVehiclePlate && !window.OpenCRMVehiclePlate.validate()) {
                return;
            }

            var vm = this;
            vm.saveModal = true;
            return axios.post('/api/v1/vehicles', {
                Kind: vm.vehicle.kind,
                MileageUnit: vm.vehicle.mileageUnit || 1,
                ModelId: vm.vehicle.modelId,
                ManualBrandName: vm.isManualIdentity ? vm.vehicle.manualBrandName
                    : !vm.vehicle.modelId ? (vm.brands.find(function (brand) { return String(brand.id) === String(vm.selectedBrandId); }) || {}).name || null : null,
                ManualModelName: vm.isManualIdentity ? vm.vehicle.manualModelName : null,
                YearIssue: vm.vehicle.yearIssue,
                Name: vm.vehicle.name,
                Vin: vm.vehicle.vin || null,
                RegistrationPlate: vm.vehicle.registrationPlate || null,
                AllowOtherRegistrationPlateFormat: vm.vehicle.allowOtherRegistrationPlateFormat === true,
                BodyNumber: vm.vehicle.bodyNumber || null,
                EngineNumber: vm.vehicle.engineNumber || null,
                Color: vm.vehicle.color || null,
                RegistrationCertificate: vm.vehicle.registrationCertificate || null,
                CommissioningDate: vm.vehicle.commissioningDate || null,
                Description: vm.vehicle.description,
                OrganizationId: vm.organizationId || null,
                OrganizationClientId: vm.isOrganizationWorkspace ? (vm.vehicle.organizationClientId || null) : null
            })
                .then(function (response) {
                    vm.closeModal();
                    return vm.refreshFleetData().then(function () {
                        return response.data;
                    });
                })
                .catch(function (error) {
                    if (error.response && error.response.status === 402) {
                        showAppModal('#subscriptionModal');
                    } else {
                        vm.handleApiError(error, 'Произошла ошибка создания техники');
                    }
                    throw error;
                })
                .finally(function () {
                    vm.saveModal = false;
                });
        },

        goToSubscription: function () {
            window.location.href = '/subscription';
        },

        toggleIdentityMode: function (useManual) {
            if (!this.vehicle) return;

            this.isManualIdentity = useManual;

            if (useManual) {
                this.catalogIdentityDraft.brandId = this.selectedBrandId;
                this.catalogIdentityDraft.modelId = this.vehicle.modelId;

                this.vehicle.manualBrandName = this.manualIdentityDraft.brandName;
                this.vehicle.manualModelName = this.manualIdentityDraft.modelName;
                return;
            }

            this.manualIdentityDraft.brandName = this.vehicle.manualBrandName;
            this.manualIdentityDraft.modelName = this.vehicle.manualModelName;

            this.selectedBrandId = this.catalogIdentityDraft.brandId;
            this.vehicle.modelId = this.catalogIdentityDraft.modelId;

            this.getBrandOptions().then(function () {
                if (this.selectedBrandId) {
                    this.getModelOptions(this.vehicle.modelId);
                }
            }.bind(this));
        },

        changeVehicleKind: function () {
            this.selectedBrandId = null;
            this.models = [];

            if (this.vehicle) {
                this.vehicle.modelId = null;
            }

            this.getBrandOptions();
        },

        changeMileageUnit: function () {
            if (this.modalType !== 'edit' || this.mileageUnitWarningShown) return;
            if (!this.originalVehicleMileageUnit || !this.vehicle) return;

            if (this.vehicle.mileageUnit !== this.originalVehicleMileageUnit) {
                this.mileageUnitWarningShown = true;
                showAppModal('#mileageUnitWarningModal');
            }
        },

        getVehicleKindText: function (kind) {
            var item = this.vehicleKindOptions.find(function (option) { return option.value === kind; });
            return item ? item.text : 'Другая техника';
        },

        changeYearIssue: function () {
            if (this.vehicle.yearIssue && this.vehicle.yearIssue > moment().year()) {
                this.vehicle.yearIssue = moment().year();
            }
            if (this.vehicle.yearIssue && this.vehicle.yearIssue < 1900) {
                this.vehicle.yearIssue = 1900;
            }
            if (this.vehicle.yearIssue == 0 || this.vehicle.yearIssue < 0) {
                this.vehicle.yearIssue = null;
            }
        },

        click_updateVehicle: function (vehicle) {
            if (!vehicle || !vehicle.id) {
                return;
            }

            window.location.href = this.getVehicleEditUrl(vehicle);
        },

        click_addVehicle: function () {
            var vm = this;
            if (vm.loadingLimitInfo ||
                vm.vehicleLimitInfo.currentVehicleCount === undefined ||
                vm.vehicleLimitInfo.maxVehiclesForUser === undefined) {
                vm.showError('Информация о лимитах еще загружается. Пожалуйста, подождите.');
                return;
            }

            if (vm.vehicleLimitInfo.currentVehicleCount >= vm.vehicleLimitInfo.maxVehiclesForUser) {
                showAppModal('#subscriptionModal');
                return;
            }

            window.location.href = vm.getVehicleCreateUrl();
        },

        DD_MM_YYYY_HH_SS: function (date) {
            return moment(date).format('DD.MM.YYYY HH:ss');
        },

        getModelOptions: function (modelId) {
            var vm = this;
            vm.vehicle.modelId = modelId;
            vm.models = [];
            if (!vm.selectedBrandId) return;

            vm.loadModels = true;
            axios.get('/api/v1/catalog/models/options', {
                params: {
                    brandId: vm.selectedBrandId,
                    kind: vm.vehicle ? vm.vehicle.kind : null
                }
            })
                .then(function (response) {
                    vm.models = response.data;
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка получения моделей техники');
                })
                .finally(function () {
                    vm.loadModels = false;
                });
        },

        getBrandOptions: function () {
            var vm = this;
            return axios.get('/api/v1/catalog/brands/options', {
                params: {
                    kind: vm.vehicle ? vm.vehicle.kind : null
                }
            })
                .then(function (response) {
                    vm.brands = response.data;
                    vm.applyCatalogModeAvailability();
                })
                .catch(function (error) {
                    vm.brands = [];
                    vm.applyCatalogModeAvailability();
                    vm.handleApiError(error, 'Произошла ошибка получения марок техники');
                });
        },
        getOrganizationClientOptions: function () {
            var vm = this;
            if (!vm.isOrganizationWorkspace || !vm.organizationId) {
                vm.organizationClients = [];
                vm.organizationClientsEnabled = false;
                return Promise.resolve([]);
            }

            vm.loadOrganizationClients = true;
            return axios.get('/api/v1/organizations/' + vm.organizationId + '/clients/options')
                .then(function (response) {
                    vm.organizationClients = response.data || [];
                    vm.organizationClientsEnabled = true;
                    return vm.organizationClients;
                })
                .catch(function (error) {
                    vm.organizationClients = [];
                    vm.organizationClientsEnabled = false;
                    if (!error.response || error.response.status !== 403) {
                        vm.handleApiError(error, 'Произошла ошибка получения клиентов');
                        throw error;
                    }

                    return [];
                })
                .finally(function () {
                    vm.loadOrganizationClients = false;
                });
        },
        loadVehiclesFromServer: function () {
            this.vehicles = [];
            this.loadVehicles = true;
            this.getVehicles();
        },
        getVehicles: function () {
            var vm = this;
            vm.loadVehicles = true;

            var params = {
                page: vm.pagination.page,
                pageSize: vm.pagination.pageSize,
                brandFilter: vm.currentFilters.brandFilter,
                organizationClientId: vm.currentFilters.organizationClientId,
                kind: vm.currentFilters.kindFilter,
                searchQuery: vm.currentFilters.searchQuery,
                isArchived: false
            };
            Object.assign(params, vm.getWorkspaceParams());

            return axios.get('/api/v1/vehicles', { params: params })
                .then(async function (response) {
                    vm.vehicles = response.data.items;
                    vm.pagination.totalCount = response.data.totalCount;

                    await vm.preloadPhotos();

                    if (response.data.userBrands) {
                        vm.availableBrands = response.data.userBrands;
                        vm.availableBrands.unshift({ id: 'all', name: 'Все марки' });
                    } else {
                        vm.updateAvailableBrandsFromVehicles();
                    }

                    if (response.data.userKinds) {
                        vm.availableKindValues = response.data.userKinds;
                    } else {
                        vm.availableKindValues = vm.vehicles
                            .map(function (vehicle) { return vehicle ? vehicle.kind : null; })
                            .filter(function (kindValue, index, values) {
                                return kindValue !== null && kindValue !== undefined && values.indexOf(kindValue) === index;
                            })
                            .sort(function (a, b) { return a - b; });
                    }

                    return vm.vehicles;
                })
                .catch(function (error) {
                    vm.handleApiError(error, 'Произошла ошибка получения техники');
                    throw error;
                })
                .finally(function () {
                    vm.loadVehicles = false;
                    vm.searchLoading = false;
                    vm.initialVehiclesLoaded = true;
                });
        },

        preloadPhotos: async function () {
            var vm = this;
            for (var i = 0; i < vm.vehicles.length; i++) {
                var vehicle = vm.vehicles[i];
                vehicle.photoLoadFailed = false;

                if (vehicle.photoId) {
                    try {
                        vehicle.photoUrl = await vm.loadPhoto(vehicle.photoId);
                    } catch (error) {
                        console.error('Error loading photo for vehicle:', vehicle.id, error);
                        vehicle.photoUrl = null;
                        vehicle.photoLoadFailed = true;
                    }
                } else {
                    vehicle.photoUrl = null;
                }

                var photos = await vm.loadVehiclePhotoCollection(vehicle);
                vm.normalizeVehiclePhotos(vehicle, photos);
            }
        },

        updateAvailableBrandsFromVehicles: function () {
            var vm = this;
            var uniqueBrands = [];
            var brandMap = {};

            for (var i = 0; i < vm.vehicles.length; i++) {
                var brandFilterId = vm.getVehicleBrandFilterId(vm.vehicles[i]);
                if (!brandFilterId) {
                    continue;
                }
                if (!brandMap[brandFilterId]) {
                    brandMap[brandFilterId] = true;
                    uniqueBrands.push(brandFilterId);
                }
            }

            vm.availableBrands = uniqueBrands.map(function (brandFilterId) {
                var vehicle = vm.vehicles.find(function (c) { return vm.getVehicleBrandFilterId(c) === brandFilterId; });
                return {
                    id: brandFilterId,
                    name: vehicle ? vehicle.brandName : 'Unknown'
                };
            });

            vm.availableBrands.unshift({ id: 'all', name: 'Все марки' });
            return Promise.resolve();
        },
        changePage: function (page) {
            if (page === '...' || page < 1 || page > this.totalPages || page === this.pagination.page) {
                return;
            }

            this.pagination.page = page;
            this.loadVehiclesFromServer();
            window.scrollTo({ top: 0, behavior: 'smooth' });
        },

        changePageSize: function () {
            this.pagination.pageSize = this.normalizePageSize(this.pagination.pageSize);
            this.savePageSizePreference('active', this.pagination.pageSize);
            this.pagination.page = 1;
            this.loadVehiclesFromServer();
        },

        firstPage: function () {
            if (this.pagination.page !== 1) {
                this.changePage(1);
            }
        },

        lastPage: function () {
            if (this.pagination.page !== this.totalPages) {
                this.changePage(this.totalPages);
            }
        },

        prevPage: function () {
            if (this.pagination.page > 1) {
                this.changePage(this.pagination.page - 1);
            }
        },

        nextPage: function () {
            if (this.pagination.page < this.totalPages) {
                this.changePage(this.pagination.page + 1);
            }
        }
    },
    beforeDestroy: function () {
        if (this.$refs.searchInput) {
            this.$refs.searchInput.removeEventListener('focus', this.onSearchFocus);
            this.$refs.searchInput.removeEventListener('blur', this.onSearchBlur);
        }

        if (this.searchTimeout) {
            clearTimeout(this.searchTimeout);
        }

        if (this.archiveSearchTimeout) {
            clearTimeout(this.archiveSearchTimeout);
        }

        if (this.quickMaintenanceVehicleSearchTimeout) {
            clearTimeout(this.quickMaintenanceVehicleSearchTimeout);
        }

    },
    mounted: function () {
        var vm = this;

        vm.restorePageSizePreferences();

        try {
            var savedVehicleViewMode = window.localStorage.getItem(vehicleViewModeStorageKey);
            if (savedVehicleViewMode === 'list' || savedVehicleViewMode === 'cards') {
                vm.viewMode = savedVehicleViewMode;
            }
        } catch (error) {
        }

        if (vm.isOrganizationWorkspace && !vm.enableOrganizationViewToggle) {
            vm.viewMode = 'list';
        }

        if (typeof VuejsPaginate !== 'undefined' && !Vue.options.components.paginate) {
            Vue.component('paginate', VuejsPaginate);
        }

        if (vm.isOrganizationWorkspace) {
            var vehiclesTab = document.getElementById('organization-dashboard-vehicles-tab');

            if (vm.isWorkspaceDashboard && !vehiclesTab) {
                vm.loadVehicles = false;
                vm.loadingLimitInfo = false;
            } else if (vm.defaultWorkspaceTab === 'vehicles') {
                vm.refreshFleetData({ includeArchive: false });
            } else {
                vm.loadVehicles = false;
                vm.loadCounts();
                vm.loadVehicleLimitInfo();
            }
            if (!vm.isWorkspaceDashboard || vehiclesTab) {
                vm.getOrganizationClientOptions();
            }

            if (vehiclesTab) {
                vehiclesTab.addEventListener('shown.bs.tab', function () {
                    vm.switchToActiveTab();
                });
            }
        } else {
            vm.getVehicles().then(function () {
                vm.loadCounts();
            });
            vm.loadVehicleLimitInfo();
        }
        if (!vm.isWorkspaceDashboard) {
            vm.getBrandOptions();
        }

        if (vm.$refs.searchInput) {
            vm.$refs.searchInput.addEventListener('focus', vm.onSearchFocus);
            vm.$refs.searchInput.addEventListener('blur', vm.onSearchBlur);
        }
    }
});




