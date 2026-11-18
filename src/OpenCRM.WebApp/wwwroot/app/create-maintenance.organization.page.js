var maintenanceLegacyText = JSON.parse(document.getElementById('maintenance-legacy-messages').dataset.messages);

Vue.component('v-select', VueSelect.VueSelect);
Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);

new Vue({
    el: '#create-maintenance-form',
    data: function () {
        return {
            localizedText: maintenanceLegacyText,
            regionalCurrencyCode: null,
            maintenanceStatuses: {
                planned: 1,
                inProgress: 2,
                completed: 3,
                cancelled: 4
            },
            organizationId: typeof maintenanceOrganizationId !== 'undefined' && maintenanceOrganizationId ? maintenanceOrganizationId : null,
            maintenanceFormProfile: typeof maintenanceFormProfile !== 'undefined' && maintenanceFormProfile ? maintenanceFormProfile : {},
            formLoad: false,
            saveModal: false,
            createdMaintenanceId: null,
            saveSuccessMessage: '',
            photoUploadErrorMessage: '',
            uploadProgress: 0,
            isUploading: false,
            selectedMaintenance: {
                status: 1,
                type: 1,
                plannedEndDate: null,
                plannedDate: window.OpenCRMRegional.today(),
                plannedStartTime: null,
                plannedEndTime: null,
                completedDate: null,
                location: null,
                performer: null,
                mileage: typeof vehicleCurrentMileage !== 'undefined' ? vehicleCurrentMileage : null,
                comment: null,
                maintenanceDetails: []
            },
            photos: [],
            selectedFiles: [],
            types: [
                { id: 1, name: maintenanceLegacyText.typeRepair },
                { id: 2, name: maintenanceLegacyText.typeScheduled },
                { id: 3, name: maintenanceLegacyText.typeFuel },
                { id: 100, name: maintenanceLegacyText.typeOther }
            ],
            scenarioWorkTypes: Array.isArray(window.maintenanceScenarioWorkTypes)
                ? window.maintenanceScenarioWorkTypes
                : (typeof maintenanceScenarioWorkTypes !== 'undefined' && Array.isArray(maintenanceScenarioWorkTypes) ? maintenanceScenarioWorkTypes : []),
            scenarioMaterials: Array.isArray(window.maintenanceScenarioMaterials)
                ? window.maintenanceScenarioMaterials
                : (typeof maintenanceScenarioMaterials !== 'undefined' && Array.isArray(maintenanceScenarioMaterials) ? maintenanceScenarioMaterials : []),
            statuses: [
                { id: 1, name: maintenanceLegacyText.statusPlanned },
                { id: 2, name: maintenanceLegacyText.statusInProgress },
                { id: 3, name: maintenanceLegacyText.statusCompleted },
                { id: 4, name: maintenanceLegacyText.statusCancelled }
            ],
            maxMileage: 999999,
            mileageUnit: typeof vehicleMileageUnit !== 'undefined' ? vehicleMileageUnit : 1,
            errorMessage: '',
            notificationType: 'error'
        };
    },
    computed: {
        plannedDatePicker: {
            get: function () {
                return this.formatDateForPicker(this.selectedMaintenance.plannedDate);
            },
            set: function (value) {
                var date = this.normalizeDateForServer(value);
                if (!this.selectedMaintenance.plannedEndDate || this.selectedMaintenance.plannedEndDate === this.selectedMaintenance.plannedDate || this.selectedMaintenance.plannedEndDate < date) {
                    this.selectedMaintenance.plannedEndDate = date;
                }
                this.selectedMaintenance.plannedDate = date;
            }
        },
        completedDatePicker: {
            get: function () {
                return this.formatDateForPicker(this.selectedMaintenance.completedDate);
            },
            set: function (value) {
                this.selectedMaintenance.completedDate = this.normalizeDateForServer(value);
            }
        },
        plannedDateRequired: function () {
            return this.selectedMaintenance.status !== this.maintenanceStatuses.completed;
        },
        showsCompletedDate: function () {
            return this.selectedMaintenance.status === this.maintenanceStatuses.completed;
        },
        mileageInputLabel: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                return this.mileageUnit === 2 ? maintenanceLegacyText.mileageHours : maintenanceLegacyText.mileageKilometers;
            }

            return this.mileageUnit === 2 ? maintenanceLegacyText.mileagePlannedHours : maintenanceLegacyText.mileagePlannedKilometers;
        },
        mileageInputPlaceholder: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                return this.mileageUnit === 2 ? maintenanceLegacyText.mileageCurrentHours : maintenanceLegacyText.mileageCurrentMileage;
            }

            return this.mileageUnit === 2
                ? maintenanceLegacyText.mileagePlannedAtHours
                : maintenanceLegacyText.mileagePlannedAtMileage;
        },
        maintenanceDetailsTotal: function () {
            return this.selectedMaintenance.maintenanceDetails.reduce(function (total, detail) {
                return total + (detail.price || 0);
            }, 0);
        },
        maintenanceDetailsTotalText: function () {
            if (!this.maintenanceDetailsTotal) {
                return maintenanceLegacyText.valuePriceUnspecified;
            }

            return this.formatPrice(this.maintenanceDetailsTotal);
        }
    },
    methods: {
        normalizeDateForServer: function (value) {
            if (!value) return null;

            if (typeof value === 'string') {
                var raw = value.trim();
                if (!raw) return null;

                var localizedDate = moment(raw, maintenanceLegacyText.dateCalendarFormat, true);
                if (localizedDate.isValid()) return localizedDate.format('YYYY-MM-DD');

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
        normalizeTimeForServer: function (value) {
            if (!value) return null;

            var parsed = moment(value, ['HH:mm', 'HH:mm:ss'], true);
            return parsed.isValid() ? parsed.format('HH:mm') : null;
        },
        getWorkspaceParams: function () {
            var params = this.organizationId ? { organizationId: this.organizationId } : {};

            if (typeof maintenanceSchedulerSourceCode !== 'undefined' && maintenanceSchedulerSourceCode) {
                params.schedulerSourceCode = maintenanceSchedulerSourceCode;
            }

            if (typeof maintenanceSchedulerSourceEntityId !== 'undefined' && maintenanceSchedulerSourceEntityId) {
                params.schedulerSourceEntityId = maintenanceSchedulerSourceEntityId;
            }

            if (typeof maintenanceSchedulerTriggerKey !== 'undefined' && maintenanceSchedulerTriggerKey) {
                params.schedulerTriggerKey = maintenanceSchedulerTriggerKey;
            }

            return params;
        },
        appendOrganizationId: function (url) {
            if (!this.organizationId) {
                return url;
            }

            var separator = url.indexOf('?') >= 0 ? '&' : '?';
            return url + separator + 'organizationId=' + encodeURIComponent(this.organizationId);
        },
        showModal: function (selector) {
            $(selector).modal('show');
        },
        hideModal: function (selector) {
            $(selector).modal('hide');
        },
        scrollToTop: function () {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        },
        scrollToPhotoSection: function () {
            this.$nextTick(function () {
                var photoSection = document.getElementById('maintenance-photo-section');
                if (photoSection) {
                    photoSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        },
        resetPhotoMessages: function () {
            this.saveSuccessMessage = '';
            this.photoUploadErrorMessage = '';
        },
        applyDefaultDetailName: function () {
            if (this.selectedMaintenance.maintenanceDetails.length !== 1) {
                return;
            }

            var detail = this.selectedMaintenance.maintenanceDetails[0];
            if (this.selectedMaintenance.type === 3 && !detail.name) {
                detail.name = 'Заправка топливом';
                return;
            }

            if (this.selectedMaintenance.type !== 3 && detail.name === 'Заправка топливом') {
                detail.name = null;
            }
        },
        validateMainFields: function () {
            if (this.plannedDateRequired && !this.selectedMaintenance.plannedDate) {
                this.showError(maintenanceLegacyText.validationPlannedDate);
                return false;
            }

            var startTime = this.normalizeTimeForServer(this.selectedMaintenance.plannedStartTime);
            var endTime = this.normalizeTimeForServer(this.selectedMaintenance.plannedEndTime);
            if ((startTime && !endTime) || (!startTime && endTime)) {
                this.showError(maintenanceLegacyText.validationPlannedTimeRequired);
                return false;
            }

            if (startTime && endTime && (this.selectedMaintenance.plannedEndDate || this.selectedMaintenance.plannedDate) + endTime <= this.selectedMaintenance.plannedDate + startTime) {
                this.showError(maintenanceLegacyText.validationEndAfterStart);
                return false;
            }

            if (this.showsCompletedDate && !this.selectedMaintenance.completedDate) {
                this.showError(maintenanceLegacyText.validationCompletedDate);
                return false;
            }

            return true;
        },
        validateDetails: function () {
            var isValid = true;

            this.selectedMaintenance.maintenanceDetails.forEach(function (detail, index) {
                if (!detail.name || !detail.name.trim()) {
                    this.showError(maintenanceLegacyText.validationPositionName.replace('{0}', index + 1));
                    isValid = false;
                }

                if (detail.price != null && detail.price < 0) {
                    this.showError(maintenanceLegacyText.validationPositionPrice.replace('{0}', index + 1));
                    isValid = false;
                }
            }.bind(this));

            return isValid;
        },
        changeStatus: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                if (!this.selectedMaintenance.completedDate) {
                    this.selectedMaintenance.completedDate = this.selectedMaintenance.plannedDate || window.OpenCRMRegional.today();
                }

                return;
            }

            if (!this.selectedMaintenance.plannedDate) {
                this.selectedMaintenance.plannedDate = this.selectedMaintenance.completedDate || window.OpenCRMRegional.today();
            }

            this.selectedMaintenance.completedDate = null;
        },
        normalizeDetailPrice: function (detail) {
            if (detail.price === '' || detail.price == null) {
                detail.price = null;
                return;
            }

            if (detail.price < 0) {
                detail.price = 0;
            }
        },
        click_addMaintenanceDetail: function () {
            this.selectedMaintenance.maintenanceDetails.push({
                name: null,
                catalogItem: null,
                description: null,
                price: null,
                materialCatalogItem: null,
                materialName: null,
                materialQuantity: null,
                materialUnit: null
            });
        },
        selectWorkType: function (detail) {
            detail.name = detail.catalogItem && detail.catalogItem.name ? detail.catalogItem.name : null;
        },
        selectMaterial: function (detail) {
            detail.materialName = detail.materialCatalogItem && detail.materialCatalogItem.name ? detail.materialCatalogItem.name : null;
        },
        removeService: function (index) {
            if (this.selectedMaintenance.maintenanceDetails.length > 1) {
                this.selectedMaintenance.maintenanceDetails.splice(index, 1);
            }
        },
        changeMileage: function () {
            if (this.selectedMaintenance.mileage && this.selectedMaintenance.mileage > this.maxMileage) {
                this.selectedMaintenance.mileage = this.maxMileage;
            }

            if (this.selectedMaintenance.mileage === 0 || this.selectedMaintenance.mileage < 0) {
                this.selectedMaintenance.mileage = null;
            }
        },
        changeFile: function (event) {
            this.resetPhotoMessages();

            var files = event.target.files;
            if (!files || files.length === 0) {
                return;
            }

            var totalSize = Array.from(files).reduce(function (total, file) {
                return total + file.size;
            }, 0);
            var maxSize = 100 * 1024 * 1024;

            if (totalSize > maxSize) {
                this.showError(maintenanceLegacyText.validationUploadSize.replace('{0}', this.formatFileSize(totalSize)));
                this.resetFileInput();
                return;
            }

            if (this.selectedFiles.length + files.length > 10) {
                this.showError(maintenanceLegacyText.validationPhotoCount);
                this.resetFileInput();
                return;
            }

            var _this = this;
            this.selectedFiles = this.selectedFiles.concat(Array.from(files));
            this.photos = this.selectedFiles.map(function (file) {
                return {
                    file: file,
                    preview: URL.createObjectURL(file),
                    name: file.name,
                    size: _this.formatFileSize(file.size)
                };
            });
        },
        clearPhotoPreviews: function () {
            this.photos.forEach(function (photo) {
                if (photo.preview) {
                    URL.revokeObjectURL(photo.preview);
                }
            });

            this.photos = [];
            this.selectedFiles = [];
            this.resetFileInput();
            this.resetPhotoMessages();
        },
        formatFileSize: function (bytes) {
            if (bytes === 0) {
                return '0 Bytes';
            }

            var k = 1024;
            var sizes = ['Bytes', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
        },
        formatPrice: function (price, currencyCode) {
            const selectedCurrency = currencyCode || this.maintenance?.currencyCode || this.selectedMaintenance?.currencyCode || this.regionalCurrencyCode;

            return window.OpenCRMRegional.formatMoney(price, selectedCurrency);
        },
        removePhoto: function (index) {
            if (this.photos[index].preview) {
                URL.revokeObjectURL(this.photos[index].preview);
            }

            this.photos.splice(index, 1);
            this.selectedFiles.splice(index, 1);

            if (this.selectedFiles.length === 0) {
                this.resetFileInput();
            }
        },
        resetFileInput: function () {
            var fileInput = document.getElementById('photos');
            if (fileInput) {
                fileInput.value = '';
            }
        },
        showError: function (message) {
            this.notificationType = 'error';
            this.errorMessage = message;
            this.showModal('#errorModal');
        },
        hideError: function () {
            this.errorMessage = '';
            this.notificationType = 'error';
            this.hideModal('#errorModal');
        },
        handleApiError: function (error, defaultMessage) {
            var message = error.response && error.response.data && error.response.data.message
                ? error.response.data.message
                : defaultMessage;
            this.showError(message);
            console.error('API Error:', error);
        },
        redirectToCar: function () {
            if (typeof maintenanceReturnUrl !== 'undefined' && maintenanceReturnUrl) {
                window.location.href = maintenanceReturnUrl;
                return;
            }

            window.location.href = this.appendOrganizationId('/vehicle/' + vehicleId);
        },
        uploadPhotosInternal: async function (maintenanceIdToUpload, redirectAfterUpload) {
            if (!maintenanceIdToUpload || this.selectedFiles.length === 0) {
                return true;
            }

            this.isUploading = true;
            this.uploadProgress = 0;
            this.photoUploadErrorMessage = '';

            var formData = new FormData();
            this.selectedFiles.forEach(function (file) {
                formData.append('formFiles', file);
            });

            try {
                await axios.post('/api/v1/media/maintenance/' + maintenanceIdToUpload + '/photos', formData, {
                    params: this.getWorkspaceParams(),
                    onUploadProgress: function (progressEvent) {
                        var percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total);
                        this.uploadProgress = percentCompleted;
                    }.bind(this)
                });

                this.clearPhotoPreviews();
                this.isUploading = false;

                if (redirectAfterUpload) {
                    this.redirectToCar();
                }

                return true;
            } catch (error) {
                this.photoUploadErrorMessage = maintenanceLegacyText.validationUploadRetry;
                this.handleApiError(error, maintenanceLegacyText.validationUploadPhotoError);
                this.isUploading = false;
                return false;
            }
        },
        save_createMaintenance: async function () {
            this.applyDefaultDetailName();

            if (!this.validateMainFields() || !this.validateDetails()) {
                this.scrollToTop();
                return;
            }

            this.saveModal = true;

            try {
                var requestData = {
                    Status: this.selectedMaintenance.status,
                    Type: this.selectedMaintenance.type,
                    PlannedDate: this.normalizeDateForServer(this.selectedMaintenance.plannedDate),
                    PlannedEndDate: this.normalizeDateForServer(this.selectedMaintenance.plannedEndDate || this.selectedMaintenance.plannedDate),
                    PlannedStartTime: this.normalizeTimeForServer(this.selectedMaintenance.plannedStartTime),
                    PlannedEndTime: this.normalizeTimeForServer(this.selectedMaintenance.plannedEndTime),
                    CompletedDate: this.normalizeDateForServer(this.selectedMaintenance.completedDate),
                    Location: this.maintenanceFormProfile.showLocation === false ? null : (this.selectedMaintenance.location || null),
                    Performer: this.maintenanceFormProfile.showPerformer === false ? null : (this.selectedMaintenance.performer || null),
                    Mileage: this.maintenanceFormProfile.showMileage === false ? null : (this.selectedMaintenance.mileage || null),
                    Comment: this.selectedMaintenance.comment || null,
                    Details: this.selectedMaintenance.maintenanceDetails.map(function (x) {
                        return {
                            Name: x.name || '',
                            Description: x.description || null,
                            Price: x.price == null ? null : x.price,
                            CatalogItemId: x.catalogItem && x.catalogItem.id ? x.catalogItem.id : null,
                            MaterialCatalogItemId: this.maintenanceFormProfile.showMaterials === false ? null : (x.materialCatalogItem && x.materialCatalogItem.id ? x.materialCatalogItem.id : null),
                            MaterialName: this.maintenanceFormProfile.showMaterials === false ? null : (x.materialName || (x.materialCatalogItem && x.materialCatalogItem.name ? x.materialCatalogItem.name : null)),
                            MaterialQuantity: this.maintenanceFormProfile.showMaterials === false ? null : (x.materialQuantity == null ? null : x.materialQuantity),
                            MaterialUnit: this.maintenanceFormProfile.showMaterials === false ? null : (x.materialUnit || null)
                        };
                    }.bind(this))
                };

                var response = await axios.post('/api/v1/vehicles/' + vehicleId + '/maintenance', requestData, {
                    params: this.getWorkspaceParams()
                });

                this.createdMaintenanceId = response.data;
                this.resetPhotoMessages();

                if (this.selectedFiles.length > 0) {
                    var uploaded = await this.uploadPhotosInternal(this.createdMaintenanceId, false);
                    this.saveModal = false;

                    if (uploaded) {
                        this.redirectToCar();
                        return;
                    }

                    this.scrollToPhotoSection();
                    return;
                }

                this.saveModal = false;
                this.redirectToCar();
            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationCreateError);
                this.saveModal = false;
            }
        },
        uploadPhotos: async function () {
            if (!this.createdMaintenanceId) {
                this.showError(maintenanceLegacyText.validationSaveBeforeUpload);
                return;
            }

            await this.uploadPhotosInternal(this.createdMaintenanceId, true);
        },
        handleOutsideClick: function (event) {
            var isDateTimePicker = event.target.closest('.vdpComponent');
            if (!isDateTimePicker) {
                return;
            }

            event.stopPropagation();
        }
    },
    mounted: function () {
        window.OpenCRMRegional.initializeLegacyVm(this);
        this.click_addMaintenanceDetail();
        this.changeStatus();

        if (typeof maintenancePrefillTitle !== 'undefined' && maintenancePrefillTitle) {
            this.selectedMaintenance.maintenanceDetails[0].name = maintenancePrefillTitle;
        }

        if (typeof maintenancePrefillComment !== 'undefined' && maintenancePrefillComment) {
            this.selectedMaintenance.comment = maintenancePrefillComment;
        }

        if (typeof maintenancePrefillPlannedDate !== 'undefined' && maintenancePrefillPlannedDate) {
            var plannedDate = this.normalizeDateForServer(maintenancePrefillPlannedDate);
            if (plannedDate) {
                this.selectedMaintenance.status = this.maintenanceStatuses.planned;
                this.selectedMaintenance.plannedDate = plannedDate;
                this.selectedMaintenance.completedDate = null;
            }
        }

        if (typeof maintenancePrefillPlannedStartTime !== 'undefined' && maintenancePrefillPlannedStartTime) {
            this.selectedMaintenance.plannedStartTime = this.normalizeTimeForServer(maintenancePrefillPlannedStartTime);
        }

        if (typeof maintenancePrefillPlannedEndTime !== 'undefined' && maintenancePrefillPlannedEndTime) {
            this.selectedMaintenance.plannedEndTime = this.normalizeTimeForServer(maintenancePrefillPlannedEndTime);
        }

        document.addEventListener('click', this.handleOutsideClick);
    },
    beforeDestroy: function () {
        document.removeEventListener('click', this.handleOutsideClick);
        this.clearPhotoPreviews();
    }
});

