var maintenanceLegacyText = JSON.parse(document.getElementById('maintenance-legacy-messages').dataset.messages);

Vue.component('v-select', VueSelect.VueSelect);
Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);

new Vue({
    el: '#create-maintenance-form',
    data: function () {
        return {
            localizedText: maintenanceLegacyText,
            maintenanceStatuses: {
                planned: 1,
                inProgress: 2,
                completed: 3,
                cancelled: 4
            },
            organizationId: typeof maintenanceOrganizationId !== 'undefined' && maintenanceOrganizationId ? maintenanceOrganizationId : null,
            formLoad: false,
            saveModal: false,
            currentStep: 1,
            createdMaintenanceId: null,
            uploadProgress: 0,
            isUploading: false,

            selectedMaintenance: {
                status: 3,
                type: 1,
                plannedStartTime: null,
                plannedEndTime: null,
                plannedEndDate: null,
                plannedDate: null,
                completedDate: moment().format('YYYY-MM-DD'),
                location: null,
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
                { id: 100, name: maintenanceLegacyText.typeOther },
            ],
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

            return this.mileageUnit === 2 ? maintenanceLegacyText.mileagePlannedAtHours : maintenanceLegacyText.mileagePlannedAtMileage;
        },
        mileageReviewLabel: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                return this.mileageUnit === 2 ? maintenanceLegacyText.mileageHours : maintenanceLegacyText.mileageKilometers;
            }

            return this.mileageUnit === 2 ? maintenanceLegacyText.mileagePlannedHours : maintenanceLegacyText.mileagePlannedKilometers;
        },
        mileageUnitShortText: function () {
            return this.mileageUnit === 2 ? maintenanceLegacyText.mileageUnitHours : maintenanceLegacyText.mileageUnitKilometers;
        },
        mileageReviewValue: function () {
            return this.selectedMaintenance.mileage
                ? this.selectedMaintenance.mileage + ' ' + this.mileageUnitShortText
                : maintenanceLegacyText.valueUnspecified;
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

        showStep: function (stepNumber) {
            this.currentStep = stepNumber;
            window.scrollTo(0, 0);
        },

        nextStep: function (nextStepNumber) {
            if (this.currentStep === 1 && !this.validateStep1()) return;
            if (this.currentStep === 2 && !this.validateStep2()) return;

            if (this.currentStep === 1
                && this.selectedMaintenance.maintenanceDetails.length === 1
                && !this.selectedMaintenance.maintenanceDetails[0].name
                && this.selectedMaintenance.type === 3) {
                this.selectedMaintenance.maintenanceDetails[0].name = 'Заправка топливом'
            } else if (this.currentStep === 1
                && this.selectedMaintenance.maintenanceDetails.length === 1
                && this.selectedMaintenance.maintenanceDetails[0].name
                && this.selectedMaintenance.type !== 3
                && this.selectedMaintenance.maintenanceDetails[0].name === 'Заправка топливом') {
                this.selectedMaintenance.maintenanceDetails[0].name = null
            }

            this.showStep(nextStepNumber);
        },

        prevStep: function (prevStepNumber) {
            this.showStep(prevStepNumber);
        },

        validateStep1: function () {
            if (this.plannedDateRequired && !this.selectedMaintenance.plannedDate) {
                this.showError(maintenanceLegacyText.validationPlannedDate);
                return false;
            }

            if (this.showsCompletedDate && !this.selectedMaintenance.completedDate) {
                this.showError(maintenanceLegacyText.validationCompletedDate);
                return false;
            }
            return true;
        },

        changeStatus: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                if (!this.selectedMaintenance.completedDate) {
                    this.selectedMaintenance.completedDate = this.selectedMaintenance.plannedDate || moment().format('YYYY-MM-DD');
                }

                return;
            }

            if (!this.selectedMaintenance.plannedDate) {
                this.selectedMaintenance.plannedDate = this.selectedMaintenance.completedDate || moment().format('YYYY-MM-DD');
            }

            this.selectedMaintenance.completedDate = null;
        },

        validateStep2: function () {
            var isValid = true;
            this.selectedMaintenance.maintenanceDetails.forEach(function (detail, index) {
                if (!detail.name || !detail.name.trim()) {
                    this.showError(maintenanceLegacyText.validationServiceName.replace('{0}', index + 1));
                    isValid = false;
                }
                if (detail.price != null && detail.price < 0) {
                    this.showError(maintenanceLegacyText.validationServicePrice.replace('{0}', index + 1));
                    isValid = false;
                }
            }.bind(this));
            return isValid;
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
                description: null,
                price: null,
            });
        },

        removeService: function (index) {
            if (this.selectedMaintenance.maintenanceDetails.length > 1) {
                this.selectedMaintenance.maintenanceDetails.splice(index, 1);
            }
        },

        DD_MM_YYYY: function (date) {
            return this.formatDateForPicker(date) || maintenanceLegacyText.valueDateUnspecified;
        },

        getTypeName: function (typeId) {
            var type = this.types.find(function (t) {
                return t.id === typeId;
            });
            return type ? type.name : maintenanceLegacyText.statusUnknown;
        },

        getMaintenanceStatusText: function (statusId) {
            var status = this.statuses.find(function (item) {
                return item.id === statusId;
            });
            return status ? status.name : maintenanceLegacyText.statusUnknown;
        },

        changeMileage: function () {
            if (this.selectedMaintenance.mileage && this.selectedMaintenance.mileage > this.maxMileage) {
                this.selectedMaintenance.mileage = this.maxMileage;
            }
            if (this.selectedMaintenance.mileage == 0 || this.selectedMaintenance.mileage < 0) {
                this.selectedMaintenance.mileage = null;
            }
        },

        changeFile: function (event) {
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
        },

        formatFileSize: function (bytes) {
            if (bytes === 0) return '0 Bytes';
            var k = 1024;
            var sizes = ['Bytes', 'KB', 'MB', 'GB'];
            var i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
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
            var message = error.response && error.response.data && error.response.data.message ? error.response.data.message : defaultMessage;
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

        save_createMaintenance: async function () {
            if (this.plannedDateRequired && !this.selectedMaintenance.plannedDate) {
                this.showError(maintenanceLegacyText.validationPlannedDateShort);
                this.showStep(1);
                return;
            }

            if (this.showsCompletedDate && !this.selectedMaintenance.completedDate) {
                this.showError(maintenanceLegacyText.validationCompletedDateShort);
                this.showStep(1);
                return;
            }

            var hasErrors = false;
            var _this = this;
            this.selectedMaintenance.maintenanceDetails.forEach(function (x, index) {
                if (!x.name || !x.name.trim()) {
                    _this.showError(maintenanceLegacyText.validationServiceName.replace('{0}', index + 1));
                    hasErrors = true;
                }
            });

            if (hasErrors) {
                this.showStep(2);
                return;
            }

            this.saveModal = true;

            try {
                var requestData = {
                    Status: this.selectedMaintenance.status,
                    Type: this.selectedMaintenance.type,
                    PlannedDate: this.normalizeDateForServer(this.selectedMaintenance.plannedDate),
                    PlannedEndDate: this.normalizeDateForServer(this.selectedMaintenance.plannedEndDate || this.selectedMaintenance.plannedDate),
                    PlannedStartTime: this.selectedMaintenance.plannedStartTime ? this.selectedMaintenance.plannedStartTime.slice(0, 5) + ':00' : null,
                    PlannedEndTime: this.selectedMaintenance.plannedEndTime ? this.selectedMaintenance.plannedEndTime.slice(0, 5) + ':00' : null,
                    CompletedDate: this.normalizeDateForServer(this.selectedMaintenance.completedDate),
                    Location: this.selectedMaintenance.location || null,
                    Mileage: this.selectedMaintenance.mileage || null,
                    Comment: this.selectedMaintenance.comment || null,
                    Details: this.selectedMaintenance.maintenanceDetails.map(function (x) {
                        return {
                            Name: x.name || '',
                            Description: x.description || null,
                            Price: x.price == null ? null : x.price
                        }
                    })
                };

                var response = await axios.post('/api/v1/vehicles/' + vehicleId + '/maintenance', requestData, {
                    params: this.getWorkspaceParams()
                });

                this.createdMaintenanceId = response.data;
                this.saveModal = false;

                this.showStep(5);

            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationCreateError);
                this.saveModal = false;
            }
        },

        uploadPhotos: async function () {
            var totalSize = this.selectedFiles.reduce(function (total, file) {
                return total + file.size;
            }, 0);
            var maxSize = 100 * 1024 * 1024;

            if (totalSize > maxSize) {
                this.showError(maintenanceLegacyText.validationUploadSize.replace('{0}', this.formatFileSize(totalSize)));
                return;
            }

            if (this.selectedFiles.length === 0) {
                this.redirectToCar();
                return;
            }

            this.isUploading = true;
            this.uploadProgress = 0;

            var formData = new FormData();
            this.selectedFiles.forEach(function (file) {
                formData.append('formFiles', file);
            });

            try {
                await axios.post('/api/v1/media/maintenance/' + this.createdMaintenanceId + '/photos', formData, {
                    params: this.getWorkspaceParams(),
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    },
                    onUploadProgress: function (progressEvent) {
                        var percentCompleted = Math.round(
                            (progressEvent.loaded * 100) / progressEvent.total
                        );
                        this.uploadProgress = percentCompleted;
                    }.bind(this)
                });

                var _this = this;
                setTimeout(function () {
                    _this.redirectToCar();
                }, 1000);

            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationUploadPhotoError);
                this.isUploading = false;
            }
        },

        skipPhotoUpload: function () {
            if (this.isUploading) return;

            if (this.photos.length > 0) {
                if (confirm(maintenanceLegacyText.validationConfirmSkipPhotos)) {
                    this.redirectToCar();
                }
            } else {
                this.redirectToCar();
            }
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
        this.click_addMaintenanceDetail();
        this.changeStatus();
        if (typeof maintenancePrefillTitle !== 'undefined' && maintenancePrefillTitle) {
            this.selectedMaintenance.maintenanceDetails[0].name = maintenancePrefillTitle;
        }
        if (typeof maintenancePrefillComment !== 'undefined' && maintenancePrefillComment) {
            this.selectedMaintenance.comment = maintenancePrefillComment;
        }
        document.addEventListener('click', this.handleOutsideClick);
        this.showStep(1);
    },
    beforeDestroy: function () {
        document.removeEventListener('click', this.handleOutsideClick);
        this.clearPhotoPreviews();
    }
});



