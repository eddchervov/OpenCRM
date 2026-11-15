if (window.OpenCRMVehiclePlate) {
    window.OpenCRMVehiclePlate.installVue(Vue);
}

Vue.component('v-select', VueSelect.VueSelect);
if (window['vue-ctk-date-time-picker'] && !Vue.options.components['vue-ctk-date-time-picker']) {
    Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);
}

(function () {
    var page = window.vehicleEditPage;
    var root = document.getElementById('vehicle-edit-page');

    if (!root || !page) {
        return;
    }

    var isCreateMode = page.mode === 'create';

    new Vue({
        el: '#vehicle-edit-page',
        data: function () {
            return {
                isCreateMode: isCreateMode,
                formLoad: !isCreateMode,
                saveInProgress: false,
                errorMessage: '',
                lockOrganizationClient: page.lockOrganizationClient === true || !!page.organizationClientId,
                vehicle: {
                    id: page.vehicleId || '',
                    organizationId: page.organizationId || null,
                    kind: 1,
                    mileageUnit: 1,
                    brandName: '',
                    modelName: '',
                    yearIssue: null,
                    name: '',
                    vin: '',
                    registrationPlate: '',
                    bodyNumber: '',
                    engineNumber: '',
                    color: '',
                    registrationCertificate: '',
                    commissioningDate: '',
                    commissioningDateDisplay: '',
                    description: '',
                    brandId: null,
                    modelId: null,
                    manualBrandName: null,
                    manualModelName: null,
                    organizationClientId: page.organizationClientId || null,
                    organizationClientName: null,
                    isManualEntry: false
                },
                brands: [],
                models: [],
                organizationClients: [],
                organizationClientsEnabled: false,
                loadModels: false,
                loadOrganizationClients: false,
                selectedBrandId: null,
                isManualIdentity: false,
                catalogModeAvailable: true,
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
                manualIdentityDraft: {
                    brandName: null,
                    modelName: null
                },
                catalogIdentityDraft: {
                    brandId: null,
                    modelId: null
                }
            };
        },
        computed: {
            canSaveVehicle: function () {
                if (!this.vehicle) return false;
                return [this.vehicle.registrationPlate, this.vehicle.vin, this.vehicle.bodyNumber,
                    this.vehicle.engineNumber, this.vehicle.registrationCertificate, this.vehicle.name]
                    .some(function (value) { return value && String(value).trim(); });
            }
        },
        methods: {
            getVehicleDisplayName: getVehicleDisplayName,
            getWorkspaceParams: function () {
                return this.vehicle.organizationId ? { organizationId: this.vehicle.organizationId } : {};
            },
            goBack: function () {
                window.location.href = page.returnUrl || (this.vehicle.id ? '/vehicle/' + this.vehicle.id : '/vehicles');
            },
            handleApiError: function (error, defaultMessage) {
                this.errorMessage = error && error.response && error.response.data && error.response.data.message
                    ? error.response.data.message
                    : defaultMessage;

                window.scrollTo({ top: 0, behavior: 'smooth' });
                console.error('Vehicle form error:', error);
            },
            applyCatalogModeAvailability: function () {
                this.catalogModeAvailable = Array.isArray(this.brands) && this.brands.length > 0;

                if (!this.catalogModeAvailable) {
                    this.isManualIdentity = true;
                    this.selectedBrandId = null;
                    this.models = [];
                    this.vehicle.modelId = null;
                }
            },
            loadOrganizationClientOptions: function () {
                var vm = this;
                if (!vm.vehicle.organizationId) {
                    vm.organizationClients = [];
                    vm.organizationClientsEnabled = false;
                    return Promise.resolve([]);
                }

                vm.loadOrganizationClients = true;
                return axios.get('/api/v1/organizations/' + vm.vehicle.organizationId + '/clients/options')
                    .then(function (response) {
                        vm.organizationClients = response.data || [];
                        vm.organizationClientsEnabled = vm.organizationClients.length > 0;
                        vm.applyLockedOrganizationClient();
                        return vm.organizationClients;
                    })
                    .catch(function (error) {
                        vm.organizationClients = [];
                        vm.organizationClientsEnabled = false;
                        if (!error.response || error.response.status !== 403) {
                            vm.handleApiError(error, 'Не удалось загрузить клиентов.');
                        }

                        return [];
                    })
                    .finally(function () {
                        vm.loadOrganizationClients = false;
                    });
            },
            applyLockedOrganizationClient: function () {
                if (!this.lockOrganizationClient || !page.organizationClientId) {
                    return;
                }

                var selectedClientId = String(page.organizationClientId);
                var hasClient = this.organizationClients.some(function (client) {
                    return String(client.id || '') === selectedClientId;
                });

                if (hasClient) {
                    this.vehicle.organizationClientId = page.organizationClientId;
                }
            },
            getBrandOptions: function () {
                var vm = this;

                return axios.get('/api/v1/catalog/brands/options', {
                    params: {
                        kind: vm.vehicle.kind
                    }
                })
                    .then(function (response) {
                        vm.brands = response.data || [];
                        vm.applyCatalogModeAvailability();
                    })
                    .catch(function (error) {
                        vm.brands = [];
                        vm.applyCatalogModeAvailability();
                        vm.handleApiError(error, 'Не удалось загрузить марки техники.');
                    });
            },
            getModelOptions: function (modelId) {
                if (modelId === undefined) {
                    modelId = null;
                }

                this.vehicle.modelId = modelId;
                this.models = [];
                if (!this.selectedBrandId) {
                    return;
                }

                this.loadModels = true;
                var vm = this;

                axios.get('/api/v1/catalog/models/options', {
                    params: {
                        brandId: this.selectedBrandId,
                        kind: this.vehicle.kind
                    }
                })
                    .then(function (response) {
                        vm.models = response.data || [];
                    })
                    .catch(function (error) {
                        vm.handleApiError(error, 'Не удалось загрузить модели техники.');
                    })
                    .finally(function () {
                        vm.loadModels = false;
                    });
            },
            toggleIdentityMode: function (useManual) {
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
                this.vehicle.modelId = null;
                this.getBrandOptions();
            },
            changeYearIssue: function () {
                var currentYear = new Date().getFullYear();
                if (this.vehicle.yearIssue && this.vehicle.yearIssue > currentYear) {
                    this.vehicle.yearIssue = currentYear;
                }

                if (this.vehicle.yearIssue === 0 || this.vehicle.yearIssue < 0) {
                    this.vehicle.yearIssue = null;
                }
            },
            populateVehicle: function (data) {
                this.vehicle.id = data.id || this.vehicle.id;
                this.vehicle.organizationId = data.organizationId || this.vehicle.organizationId;
                this.vehicle.kind = data.kind || 1;
                this.vehicle.mileageUnit = data.mileageUnit || 1;
                this.vehicle.brandName = data.brandName || '';
                this.vehicle.modelName = data.modelName || '';
                this.vehicle.yearIssue = data.yearIssue;
                this.vehicle.name = data.name || '';
                this.vehicle.vin = data.vin || '';
                this.vehicle.registrationPlate = data.registrationPlate || '';
                this.vehicle.bodyNumber = data.bodyNumber || '';
                this.vehicle.engineNumber = data.engineNumber || '';
                this.vehicle.color = data.color || '';
                this.vehicle.registrationCertificate = data.registrationCertificate || '';
                this.vehicle.commissioningDate = data.commissioningDate ? data.commissioningDate.split('T')[0] : '';
                this.vehicle.commissioningDateDisplay = this.vehicle.commissioningDate
                    ? moment(this.vehicle.commissioningDate, 'YYYY-MM-DD').format('DD.MM.YYYY')
                    : '';
                this.vehicle.description = data.description || '';
                this.vehicle.brandId = data.brandId;
                this.vehicle.modelId = data.modelId;
                this.vehicle.manualBrandName = data.manualBrandName;
                this.vehicle.manualModelName = data.manualModelName;
                this.vehicle.organizationClientId = data.organizationClientId || null;
                this.vehicle.organizationClientName = data.organizationClientName || null;
                this.vehicle.isManualEntry = data.isManualEntry === true;

                this.selectedBrandId = data.brandId;
                this.isManualIdentity = data.isManualEntry === true;
                this.manualIdentityDraft = {
                    brandName: data.manualBrandName || data.brandName || null,
                    modelName: data.manualModelName || data.modelName || null
                };
                this.catalogIdentityDraft = {
                    brandId: data.brandId,
                    modelId: data.modelId
                };
            },
            loadVehicleData: async function () {
                try {
                    var response = await axios.get('/api/v1/vehicles/' + this.vehicle.id, {
                        params: this.getWorkspaceParams()
                    });

                    this.populateVehicle(response.data);
                    await this.loadOrganizationClientOptions();
                    await this.getBrandOptions();

                    if (!this.isManualIdentity && this.catalogModeAvailable && this.vehicle.modelId) {
                        this.getModelOptions(this.vehicle.modelId);
                    }
                } catch (error) {
                    this.handleApiError(error, 'Не удалось загрузить карточку техники.');
                } finally {
                    this.formLoad = false;
                }
            },
            prepareCreateForm: async function () {
                try {
                    await this.loadOrganizationClientOptions();
                    this.applyLockedOrganizationClient();
                    await this.getBrandOptions();
                } catch (error) {
                    this.handleApiError(error, 'Не удалось подготовить форму техники.');
                } finally {
                    this.formLoad = false;
                }
            },
            normalizeCommissioningDate: function () {
                if (!this.vehicle.commissioningDateDisplay) {
                    return null;
                }

                var parsed = moment(this.vehicle.commissioningDateDisplay, 'DD.MM.YYYY', true);
                if (!parsed.isValid()) {
                    return null;
                }

                return parsed.format('YYYY-MM-DD');
            },
            getSavePayload: function () {
                return {
                    Kind: this.vehicle.kind,
                    MileageUnit: this.vehicle.mileageUnit || 1,
                    ModelId: this.vehicle.modelId,
                    ManualBrandName: this.isManualIdentity ? this.vehicle.manualBrandName
                    : !this.vehicle.modelId ? (this.brands.find(function (brand) { return String(brand.id) === String(this.selectedBrandId); }, this) || {}).name || null : null,
                    ManualModelName: this.isManualIdentity ? this.vehicle.manualModelName : null,
                    YearIssue: this.vehicle.yearIssue,
                    Name: this.vehicle.name,
                    Vin: this.vehicle.vin,
                    RegistrationPlate: this.vehicle.registrationPlate,
                    AllowOtherRegistrationPlateFormat: this.vehicle.allowOtherRegistrationPlateFormat === true,
                    BodyNumber: this.vehicle.bodyNumber,
                    EngineNumber: this.vehicle.engineNumber,
                    Color: this.vehicle.color,
                    RegistrationCertificate: this.vehicle.registrationCertificate,
                    CommissioningDate: this.normalizeCommissioningDate(),
                    Description: this.vehicle.description,
                    OrganizationId: this.vehicle.organizationId || null,
                    OrganizationClientId: this.vehicle.organizationId ? (this.vehicle.organizationClientId || null) : null
                };
            },
            getCreatedVehicleUrl: function (vehicleId) {
                var url = '/vehicle/' + vehicleId;
                return this.vehicle.organizationId ? url + '?organizationId=' + encodeURIComponent(this.vehicle.organizationId) : url;
            },
            saveVehicle: function () {
                if (window.OpenCRMVehiclePlate && !window.OpenCRMVehiclePlate.validate()) {
                    return;
                }

                this.saveInProgress = true;
                var vm = this;
                var payload = this.getSavePayload();
                var request = this.isCreateMode
                    ? axios.post('/api/v1/vehicles', payload)
                    : axios.put('/api/v1/vehicles/' + this.vehicle.id, payload, {
                        params: this.getWorkspaceParams()
                    });

                request
                    .then(function (response) {
                        if (vm.isCreateMode) {
                            var createdVehicleId = response.data;
                            window.location.href = vm.getCreatedVehicleUrl(createdVehicleId);
                            return;
                        }

                        vm.goBack();
                    })
                    .catch(function (error) {
                        vm.handleApiError(error, vm.isCreateMode
                            ? 'Не удалось создать технику.'
                            : 'Не удалось сохранить изменения.');
                    })
                    .finally(function () {
                        vm.saveInProgress = false;
                    });
            }
        },
        mounted: function () {
            if (this.isCreateMode) {
                this.prepareCreateForm();
                return;
            }

            this.loadVehicleData();
        }
    });
})();
