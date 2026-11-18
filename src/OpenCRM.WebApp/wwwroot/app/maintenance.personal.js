var maintenanceLegacyText = JSON.parse(document.getElementById('maintenance-legacy-messages').dataset.messages);

if (window['vue-ctk-date-time-picker'] && !Vue.options.components['vue-ctk-date-time-picker']) {
    Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);
}

new Vue({
    el: '#maintenance-details-form',
    data: function () {
        return {
            localizedText: maintenanceLegacyText,
            regionalCurrencyCode: null,
            organizationId: typeof maintenanceOrganizationId !== 'undefined' && maintenanceOrganizationId ? maintenanceOrganizationId : null,
            formLoad: true,
            errorMessage: '',
            notificationType: 'error',
            loadingTitle: maintenanceLegacyText.loadingFileTitle,
            loadingMessage: maintenanceLegacyText.loadingWait,
            maintenance: {
                id: maintenanceId,
                status: 3,
                type: 1,
                date: null,
                plannedEndDate: null,
                plannedStartTime: null,
                plannedEndTime: null,
                plannedDate: null,
                completedDate: null,
                location: null,
                performer: null,
                mileage: null,
                comment: null,
                price: null,
                maintenanceDetails: [],
                linkedDefects: [],
                photoIds: []
            },
            maintenanceHistoryEvents: [],
            maintenanceHistoryLoaded: false,
            showMaintenanceHistory: false,
            vehicle: {
                id: null,
                brandName: '',
                modelName: '',
                yearIssue: null,
                name: '',
                description: '',
                mileageUnit: 1
            },
            photoUrls: [],
            failedPhotoCount: 0,
            currentPhotoIndex: 0,
            exportAccess: {
                loaded: false,
                pdf: false,
                pdfWithPhotos: false,
                word: false,
                wordWithPhotos: false
            },
            workflowAction: null,
            workflowDate: '',
            workflowDatePickerValue: '',
            workflowEndDate: '',
            workflowMileage: null,
            workflowReason: '',
            workflowSaving: false,
            workflowModalTitle: '',
            workflowConfirmText: ''
        };
    },
    watch: {
        workflowDatePickerValue: function (value, previous) {
            var date = this.normalizeDateForServer(value);
            var oldDate = this.normalizeDateForServer(previous);
            if (!date || !oldDate || !this.workflowEndDate) return;
            var days = moment(date).diff(moment(oldDate), 'days');
            this.workflowEndDate = moment(this.workflowEndDate).add(days, 'days').format('YYYY-MM-DD');
        }
    },
    computed: {
        hasAnyExportAccess: function () {
            return this.exportAccess.pdf || this.exportAccess.pdfWithPhotos || this.exportAccess.word || this.exportAccess.wordWithPhotos;
        },
        mileageUnitLabel: function () {
            return this.vehicle.mileageUnit === 2 ? maintenanceLegacyText.mileageHours : maintenanceLegacyText.mileageKilometers;
        },
        mileageUnitShortText: function () {
            return this.vehicle.mileageUnit === 2 ? maintenanceLegacyText.mileageUnitHours : maintenanceLegacyText.mileageUnitKilometers;
        }
    },
    methods: {
        normalizeDateForServer: function (value) {
            if (!value) return null;

            if (typeof value === 'string') {
                var raw = value.trim();
                if (!raw) return null;

                var ruDate = moment(raw, maintenanceLegacyText.dateCalendarFormat, true);
                if (ruDate.isValid()) return ruDate.format('YYYY-MM-DD');

                var isoDate = moment(raw.substring(0, 10), 'YYYY-MM-DD', true);
                if (isoDate.isValid()) return isoDate.format('YYYY-MM-DD');

                if (raw.indexOf('T') >= 0) {
                    var dateTime = moment(raw);
                    if (dateTime.isValid()) return dateTime.format('YYYY-MM-DD');
                }
            }

            var parsedDate = moment(value);
            return parsedDate.isValid() ? parsedDate.format('YYYY-MM-DD') : null;
        },

        formatDateForPicker: function (value) {
            var date = this.normalizeDateForServer(value);
            return date ? moment(date, 'YYYY-MM-DD', true).format(maintenanceLegacyText.dateCalendarFormat) : null;
        },

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
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id);
        },

        getMaintenanceUpdateUrl: function () {
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/maintenance/' + this.maintenance.id + '/update');
        },

        getMaintenanceHistoryUrl: function () {
            var url = this.appendOrganizationId('/maintenance/' + this.maintenance.id + '/history/view');
            var returnUrl = window.location.pathname + window.location.search;
            return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(returnUrl);
        },

        showModal: function (selector) {
            $(selector).modal('show');
        },

        hideModal: function (selector) {
            $(selector).modal('hide');
        },

        showLoadingModal: function (title, message) {
            this.loadingTitle = title || maintenanceLegacyText.loadingFileTitle;
            this.loadingMessage = message || maintenanceLegacyText.loadingWait;
            this.showModal('#loadingModal');
        },

        openDocument: function (url, loadingTitle, popupErrorMessage, fallbackApiErrorMessage) {
            this.showLoadingModal(loadingTitle, maintenanceLegacyText.loadingWait);

            try {
                var documentWindow = window.open(url, '_blank');

                if (documentWindow) {
                    var vm = this;
                    documentWindow.onload = function () {
                        vm.hideModal('#loadingModal');
                    };

                    setTimeout(function () {
                        vm.hideModal('#loadingModal');
                    }, 2000);

                    return;
                }

                this.hideModal('#loadingModal');
                this.showError(popupErrorMessage);
            } catch (error) {
                this.hideModal('#loadingModal');
                this.handleApiError(error, fallbackApiErrorMessage);
            }
        },

        redirectToVehicle: function () {
            window.location.href = this.getVehicleUrl();
        },

        loadPhoto: async function (photoId) {
            try {
                var response = await fetch('/api/v1/media/' + photoId + '/path');
                if (!response.ok) {
                    throw new Error(maintenanceLegacyText.validationUploadPhotoFailure);
                }

                return await response.text();
            } catch (error) {
                console.error('Ошибка загрузки фото:', error);
                return null;
            }
        },

        loadMaintenancePhotos: async function () {
            if (!this.maintenance.photoIds || this.maintenance.photoIds.length === 0) {
                this.photoUrls = [];
                this.failedPhotoCount = 0;
                return;
            }

            try {
                var _this = this;
                var photoPromises = this.maintenance.photoIds.map(function (photoId) {
                    return _this.loadPhoto(photoId);
                });

                this.photoUrls = await Promise.all(photoPromises);
                this.failedPhotoCount = this.photoUrls.filter(function (url) {
                    return url === null;
                }).length;
                this.photoUrls = this.photoUrls.filter(function (url) {
                    return url !== null;
                });

            } catch (error) {
                console.error('Ошибка загрузки фотографий:', error);
                this.photoUrls = [];
                this.failedPhotoCount = this.maintenance.photoIds.length;
            }
        },
        loadFeatureAccess: async function () {
            try {
                var response = await axios.get('/api/v1/features/current');
                var features = response.data.features || [];
                var featureMap = {};

                features.forEach(function (feature) {
                    featureMap[feature.code] = feature;
                });

                this.exportAccess.pdf = featureMap['export.pdf'] && featureMap['export.pdf'].boolValue === true;
                this.exportAccess.pdfWithPhotos = featureMap['export.pdf.with_photos'] && featureMap['export.pdf.with_photos'].boolValue === true;
                this.exportAccess.word = featureMap['export.word'] && featureMap['export.word'].boolValue === true;
                this.exportAccess.wordWithPhotos = featureMap['export.word.with_photos'] && featureMap['export.word.with_photos'].boolValue === true;
            } catch (error) {
                console.error('Ошибка загрузки feature access:', error);
            } finally {
                this.exportAccess.loaded = true;
            }
        },
        generateWord: function (includePhotos = false) {
            var url = '/api/v1/documents/word/maintenance/' + this.maintenance.id;
            if (includePhotos) {
                url += '?includePhotos=true';
            }
            url = this.appendOrganizationId(url);

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingWord,
                maintenanceLegacyText.loadingPopupBlockedWord,
                maintenanceLegacyText.loadingWordError
            );
        },

        generatePDF: function (includePhotos = true) {
            var url = '/api/v1/documents/pdf/maintenance/' + this.maintenance.id;
            if (includePhotos) {
                url += '?includePhotos=true';
            }
            url = this.appendOrganizationId(url);

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingPdf,
                maintenanceLegacyText.loadingPopupBlockedPdf,
                maintenanceLegacyText.loadingPdfError
            );
        },

        loadMaintenanceData: async function () {
            try {
                var maintenanceResponse = await axios.get('/api/v1/maintenance/' + maintenanceId, {
                    params: this.getWorkspaceParams()
                });
                var maintenanceData = maintenanceResponse.data;

                this.maintenance.id = maintenanceData.id;
                this.maintenance.status = maintenanceData.status;
                this.maintenance.type = maintenanceData.type;
                this.maintenance.date = this.normalizeDateForDisplay(maintenanceData.date);
                this.maintenance.plannedDate = this.normalizeDateForDisplay(maintenanceData.plannedDate);
                this.maintenance.plannedEndDate = this.normalizeDateForDisplay(maintenanceData.plannedEndDate);
                this.maintenance.plannedStartTime = maintenanceData.plannedStartTime;
                this.maintenance.plannedEndTime = maintenanceData.plannedEndTime;
                this.maintenance.completedDate = this.normalizeDateForDisplay(maintenanceData.completedDate);
                this.maintenance.location = maintenanceData.location;
                this.maintenance.performer = maintenanceData.performer;
                this.maintenance.mileage = maintenanceData.mileage;
                this.maintenance.comment = maintenanceData.comment;
                this.maintenance.price = maintenanceData.price;
                this.maintenance.maintenanceDetails = maintenanceData.maintenanceDetails || [];
                this.maintenance.linkedDefects = maintenanceData.linkedDefects || [];
                this.maintenance.photoIds = maintenanceData.photoIds || [];

                var vehicleResponse = await axios.get('/api/v1/vehicles/' + maintenanceData.vehicleId, {
                    params: this.getWorkspaceParams()
                });
                var vehicleData = vehicleResponse.data;

                this.vehicle.id = vehicleData.id;
                this.vehicle.brandName = vehicleData.brandName;
                this.vehicle.modelName = vehicleData.modelName;
                this.vehicle.yearIssue = vehicleData.yearIssue;
                this.vehicle.name = vehicleData.name;
                this.vehicle.description = vehicleData.description;
                this.vehicle.mileageUnit = vehicleData.mileageUnit || 1;

                await this.loadMaintenancePhotos();
                await this.loadFeatureAccess();

            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationLoadError);
            } finally {
                this.formLoad = false;
            }
        },

        loadMaintenanceHistoryEvents: async function () {
            if (this.maintenanceHistoryLoaded) {
                return;
            }

            this.maintenanceHistoryLoaded = false;

            try {
                var response = await axios.get('/api/v1/maintenance/' + this.maintenance.id + '/history', {
                    params: this.getWorkspaceParams()
                });

                this.maintenanceHistoryEvents = response.data || [];
            } catch (error) {
                this.maintenanceHistoryEvents = [];
                this.handleApiError(error, maintenanceLegacyText.validationHistoryLoadError);
            } finally {
                this.maintenanceHistoryLoaded = true;
            }
        },

        toggleMaintenanceHistory: async function () {
            this.showMaintenanceHistory = !this.showMaintenanceHistory;

            if (this.showMaintenanceHistory) {
                await this.loadMaintenanceHistoryEvents();
            }
        },

        calculateServicesTotal: function () {
            if (!this.maintenance.maintenanceDetails || this.maintenance.maintenanceDetails.length === 0) {
                return 0;
            }

            return this.maintenance.maintenanceDetails.reduce(function (total, detail) {
                return total + (detail.price || 0);
            }, 0);
        },

        getMaintenanceTypeText: function (type) {
            var typeTexts = {
                1: maintenanceLegacyText.typeRepair,
                2: maintenanceLegacyText.typeScheduled,
                3: maintenanceLegacyText.typeFuel,
                100: maintenanceLegacyText.typeOther
            };
            return typeTexts[type] || maintenanceLegacyText.typeUnknown;
        },

        getMaintenanceStatusText: function (status) {
            var statusTexts = {
                1: maintenanceLegacyText.statusPlanned,
                2: maintenanceLegacyText.statusInProgress,
                3: maintenanceLegacyText.statusCompleted,
                4: maintenanceLegacyText.statusCancelled
            };
            return statusTexts[status] || maintenanceLegacyText.statusUnknown;
        },

        normalizeDateForDisplay: function (dateString) {
            if (!dateString) return null;

            if (typeof dateString === 'string') {
                var raw = dateString.trim();
                if (!raw) return null;

                var ruDate = moment(raw, maintenanceLegacyText.dateCalendarFormat, true);
                if (ruDate.isValid()) return ruDate.format('YYYY-MM-DD');

                var isoDate = moment(raw.substring(0, 10), 'YYYY-MM-DD', true);
                if (isoDate.isValid()) return isoDate.format('YYYY-MM-DD');

                if (raw.indexOf('T') >= 0) {
                    var dateTime = moment(raw);
                    if (dateTime.isValid()) return dateTime.format('YYYY-MM-DD');
                }
            }

            return this.normalizeDateForServer(dateString);
        },

        getMaintenanceDateDay: function (dateString) {
            var date = this.normalizeDateForDisplay(dateString);
            return date ? moment(date, 'YYYY-MM-DD', true) : null;
        },

        isMaintenanceOverdue: function () {
            if (this.maintenance.status === 3 || this.maintenance.status === 4) {
                return false;
            }

            var targetDate = this.maintenance.plannedDate || this.maintenance.date;
            if (!targetDate) {
                return false;
            }

            var targetDay = this.getMaintenanceDateDay(targetDate);
            return targetDay ? targetDay.isBefore(moment().startOf('day'), 'day') : false;
        },

        getMaintenanceStatusDisplayText: function (status) {
            return this.getMaintenanceStatusText(status);
        },

        getMaintenanceStatusClass: function (status) {
            var statusClasses = {
                1: 'text-bg-warning',
                2: 'text-bg-primary',
                3: 'text-bg-success',
                4: 'text-bg-secondary'
            };
            return statusClasses[status] || 'text-bg-light border';
        },

        getPrimaryMaintenanceDateLabel: function () {
            return this.maintenance.status === 3 ? maintenanceLegacyText.dateCompleted : maintenanceLegacyText.datePlanned;
        },

        getPrimaryMaintenanceDateValue: function () {
            return this.maintenance.status === 3
                ? (this.maintenance.completedDate || this.maintenance.date)
                : (this.maintenance.plannedDate || this.maintenance.date);
        },

        formatDate: function (dateString) {
            return dateString ? window.OpenCRMRegional.formatDate(dateString, { dateStyle: 'short' }) : '—';
        },

        formatPrice: function (price, currencyCode) {
            const selectedCurrency = currencyCode || this.maintenance?.currencyCode || this.selectedMaintenance?.currencyCode || this.regionalCurrencyCode;

            return window.OpenCRMRegional.formatMoney(price, selectedCurrency);
        },

        openPhotoGallery: function (index) {
            this.currentPhotoIndex = index;
            this.showModal('#photoModal');
        },

        deleteMaintenance: function () {
            this.showModal('#deleteMaintenanceModal');
        },

        openWorkflowModal: function (action) {
            this.workflowAction = action;
            this.workflowReason = '';
            this.workflowMileage = this.maintenance.mileage;

            if (action === 'complete') {
                this.workflowModalTitle = maintenanceLegacyText.workflowCompleteTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowCompleteAction;
                this.workflowDate = this.maintenance.completedDate || moment().format('YYYY-MM-DD');
            } else if (action === 'cancel') {
                this.workflowModalTitle = maintenanceLegacyText.workflowCancelTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowCancelAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || moment().format('YYYY-MM-DD');
            } else if (action === 'reschedule') {
                this.workflowModalTitle = maintenanceLegacyText.workflowRescheduleTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowRescheduleAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || moment().format('YYYY-MM-DD');
            } else if (action === 'reopen') {
                this.workflowModalTitle = maintenanceLegacyText.workflowResumeTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowResumeAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || moment().format('YYYY-MM-DD');
            }

            this.workflowDatePickerValue = this.formatDateForPicker(this.workflowDate);
            this.$nextTick(() => {
                this.workflowEndDate = this.normalizeDateForServer(this.maintenance.plannedEndDate || this.workflowDate);
            });
            this.showModal('#maintenanceWorkflowModal');
        },

        startMaintenance: async function () {
            await this.submitWorkflowAction('start');
        },

        submitWorkflowModal: async function () {
            await this.submitWorkflowAction(this.workflowAction);
        },

        getWorkflowDateForServer: function () {
            return this.normalizeDateForServer(this.workflowDatePickerValue)
                || this.normalizeDateForServer(this.workflowDate);
        },

        submitWorkflowAction: async function (action) {
            if (!action || this.workflowSaving) {
                return;
            }

            var payload = {
                action: action,
                reason: this.workflowReason || null
            };

            if (action === 'complete') {
                payload.completedDate = this.getWorkflowDateForServer() || moment().format('YYYY-MM-DD');
                payload.completedDateText = payload.completedDate;
                payload.mileage = this.workflowMileage || null;
            } else if (action === 'reschedule' || action === 'reopen') {
                payload.plannedDate = this.getWorkflowDateForServer();
                payload.plannedDateText = payload.plannedDate;
                payload.plannedEndDate = this.normalizeDateForServer(this.workflowEndDate || payload.plannedDate);
            } else if (action === 'cancel') {
                payload.plannedDate = this.getWorkflowDateForServer()
                    || this.normalizeDateForServer(this.maintenance.plannedDate)
                    || this.normalizeDateForServer(this.maintenance.date);
                payload.plannedDateText = payload.plannedDate;
                payload.plannedEndDate = this.normalizeDateForServer(this.workflowEndDate || payload.plannedDate);
            }

            try {
                this.workflowSaving = true;
                await axios.post('/api/v1/maintenance/' + this.maintenance.id + '/workflow', payload, {
                    params: this.getWorkspaceParams()
                });

                this.hideModal('#maintenanceWorkflowModal');
                this.maintenanceHistoryLoaded = false;
                await this.loadMaintenanceData();
                if (this.showMaintenanceHistory) {
                    await this.loadMaintenanceHistoryEvents();
                }
            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.workflowChangeError);
            } finally {
                this.workflowSaving = false;
            }
        },

        confirmDelete: async function () {
            try {
                await axios.delete('/api/v1/maintenance/' + this.maintenance.id, {
                    params: this.getWorkspaceParams()
                });
                this.hideModal('#deleteMaintenanceModal');
                this.redirectToVehicle();
            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.deleteMaintenanceError);
                this.hideModal('#deleteMaintenanceModal');
            }
        },

        handleApiError: function (error, defaultMessage) {
            var message = error.response && error.response.data && error.response.data.message
                ? error.response.data.message
                : defaultMessage;
            this.showError(message);
            console.error('API Error:', error);
        },

        showError: function (message) {
            this.errorMessage = message;
            this.showModal('#errorModal');
        },

        hideError: function () {
            this.errorMessage = '';
            this.hideModal('#errorModal');
        }
    },
    mounted: function () {
        window.OpenCRMRegional.initializeLegacyVm(this);
        this.loadMaintenanceData();

        var _this = this;
        $('#photoModal').on('hidden.bs.modal', function () {
            _this.currentPhotoIndex = 0;
        });
    },
    beforeDestroy: function () {
        $('#photoModal').off('hidden.bs.modal');
    }
});





