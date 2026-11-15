Vue.component('v-select', VueSelect.VueSelect);
Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);

new Vue({
    el: '#update-maintenance-form',
    data: function () {
        return {
            maintenanceStatuses: {
                planned: 1,
                inProgress: 2,
                completed: 3,
                cancelled: 4
            },
            organizationId: typeof maintenanceOrganizationId !== 'undefined' && maintenanceOrganizationId ? maintenanceOrganizationId : null,
            maintenanceFormProfile: typeof maintenanceFormProfile !== 'undefined' && maintenanceFormProfile ? maintenanceFormProfile : {},
            formLoad: true,
            saveModal: false,
            saveSuccessMessage: '',
            photoUploadErrorMessage: '',
            uploadProgress: 0,
            isUploading: false,
            deletePhotoModal: {
                show: false,
                photoId: null,
                photoIndex: null
            },
            selectedMaintenance: {
                id: maintenanceId,
                status: 3,
                type: 1,
                plannedEndDate: null,
                plannedDate: null,
                plannedStartTime: null,
                plannedEndTime: null,
                completedDate: null,
                location: null,
                performer: null,
                mileage: null,
                comment: null,
                maintenanceDetails: [],
                linkedDefects: []
            },
            availableDefects: [],
            selectedLinkedDefectId: '',
            existingPhotos: [],
            newPhotos: [],
            selectedFiles: [],
            types: [
                { id: 1, name: 'Ремонт и обслуживание' },
                { id: 2, name: 'Плановое ТО' },
                { id: 3, name: 'Заправка топливом' },
                { id: 100, name: 'Другое' }
            ],
            scenarioWorkTypes: typeof maintenanceScenarioWorkTypes !== 'undefined' && Array.isArray(maintenanceScenarioWorkTypes) ? maintenanceScenarioWorkTypes : [],
            scenarioMaterials: typeof maintenanceScenarioMaterials !== 'undefined' && Array.isArray(maintenanceScenarioMaterials) ? maintenanceScenarioMaterials : [],
            statuses: [
                { id: 1, name: 'Запланировано' },
                { id: 2, name: 'В работе' },
                { id: 3, name: 'Выполнено' },
                { id: 4, name: 'Отменено' }
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
                return this.mileageUnit === 2 ? 'Моточасы' : 'Пробег, км';
            }

            return this.mileageUnit === 2 ? 'Плановая наработка' : 'Плановый пробег, км';
        },
        mileageInputPlaceholder: function () {
            if (this.selectedMaintenance.status === this.maintenanceStatuses.completed) {
                return this.mileageUnit === 2 ? 'Текущая наработка в моточасах' : 'Текущий пробег техники';
            }

            return this.mileageUnit === 2
                ? 'На какой наработке планируется обслуживание'
                : 'На каком пробеге планируется обслуживание';
        },
        maintenanceDetailsTotal: function () {
            return this.selectedMaintenance.maintenanceDetails.reduce(function (total, detail) {
                return total + (detail.price || 0);
            }, 0);
        },
        maintenanceDetailsTotalText: function () {
            if (!this.maintenanceDetailsTotal) {
                return 'стоимость не указана';
            }

            return this.formatPrice(this.maintenanceDetailsTotal);
        },
        availableDefectsToLink: function () {
            var linkedIds = this.selectedMaintenance.linkedDefects.map(function (defect) {
                return String(defect.id);
            });

            return this.availableDefects.filter(function (defect) {
                return linkedIds.indexOf(String(defect.id)) < 0;
            });
        }
    },
    methods: {
        normalizeDateForServer: function (value) {
            if (!value) return null;

            if (typeof value === 'string') {
                var raw = value.trim();
                if (!raw) return null;

                var ruDate = moment(raw, 'DD.MM.YYYY', true);
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
            return date ? moment(date, 'YYYY-MM-DD', true).format('DD.MM.YYYY') : null;
        },
        normalizeTimeForServer: function (value) {
            if (!value) return null;

            var parsed = moment(value, ['HH:mm', 'HH:mm:ss'], true);
            return parsed.isValid() ? parsed.format('HH:mm') : null;
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
        getReturnTargetUrl: function () {
            if (typeof maintenanceReturnUrl !== 'undefined' && maintenanceReturnUrl) {
                return maintenanceReturnUrl;
            }

            return this.appendOrganizationId('/vehicle/' + vehicleId);
        },
        getMaintenanceDetailsUrl: function () {
            var id = this.selectedMaintenance && this.selectedMaintenance.id
                ? this.selectedMaintenance.id
                : maintenanceId;

            return this.appendOrganizationId('/maintenance/' + id);
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
        loadMaintenanceData: async function () {
            try {
                var maintenanceResponse = await axios.get('/api/v1/maintenance/' + maintenanceId, {
                    params: this.getWorkspaceParams()
                });
                var data = maintenanceResponse.data;

                this.selectedMaintenance.id = data.id;
                this.selectedMaintenance.status = data.status || this.maintenanceStatuses.completed;
                this.selectedMaintenance.type = data.type;
                this.selectedMaintenance.plannedDate = this.normalizeDateForServer(data.plannedDate);
                this.selectedMaintenance.plannedEndDate = this.normalizeDateForServer(data.plannedEndDate || data.plannedDate);
                this.selectedMaintenance.plannedStartTime = this.normalizeTimeForServer(data.plannedStartTime);
                this.selectedMaintenance.plannedEndTime = this.normalizeTimeForServer(data.plannedEndTime);
                this.selectedMaintenance.completedDate = this.normalizeDateForServer(data.completedDate);
                this.selectedMaintenance.location = data.location;
                this.selectedMaintenance.performer = data.performer;
                this.selectedMaintenance.mileage = data.mileage;
                this.selectedMaintenance.comment = data.comment;
                this.selectedMaintenance.linkedDefects = Array.isArray(data.linkedDefects) ? data.linkedDefects : [];
                this.selectedMaintenance.maintenanceDetails = data.maintenanceDetails.map(function (detail) {
                    var workType = this.scenarioWorkTypes.find(function (item) {
                        return String(item.id).toLowerCase() === String(detail.catalogItemId || '').toLowerCase();
                    });
                    var material = this.scenarioMaterials.find(function (item) {
                        return String(item.id).toLowerCase() === String(detail.materialCatalogItemId || '').toLowerCase();
                    });

                    return {
                        name: detail.name,
                        catalogItem: workType || null,
                        description: detail.description,
                        price: detail.price,
                        materialCatalogItem: material || null,
                        materialName: detail.materialName || null,
                        materialQuantity: detail.materialQuantity || null,
                        materialUnit: detail.materialUnit || null
                    };
                }.bind(this));

                await this.loadExistingPhotos();

                if (this.selectedMaintenance.maintenanceDetails.length === 0) {
                    this.click_addMaintenanceDetail();
                }

                this.changeStatus();
                await this.loadAvailableDefects();
            } catch (error) {
                this.handleApiError(error, 'Произошла ошибка загрузки данных');
            } finally {
                this.formLoad = false;
            }
        },
        loadAvailableDefects: async function () {
            if (!this.organizationId) {
                this.availableDefects = [];
                return;
            }

            try {
                var response = await axios.get('/api/v1/maintenance/' + maintenanceId + '/defects/options', {
                    params: this.getWorkspaceParams()
                });
                this.availableDefects = Array.isArray(response.data) ? response.data : [];
            } catch (error) {
                this.availableDefects = [];
                this.handleApiError(error, 'Не удалось загрузить неисправности техники');
            }
        },
        linkSelectedDefect: function () {
            if (!this.selectedLinkedDefectId) {
                return;
            }

            var selectedId = String(this.selectedLinkedDefectId);
            var defect = this.availableDefects.find(function (item) {
                return String(item.id) === selectedId;
            });

            if (!defect) {
                return;
            }

            this.selectedMaintenance.linkedDefects.push({
                id: defect.id,
                title: defect.title,
                description: defect.description,
                severityCode: defect.severityCode,
                statusCode: defect.statusCode,
                createdAt: defect.createdAt || null,
                closedAt: defect.closedAt || null
            });
            this.selectedLinkedDefectId = '';
        },
        unlinkDefect: function (defect) {
            this.selectedMaintenance.linkedDefects = this.selectedMaintenance.linkedDefects.filter(function (item) {
                return String(item.id) !== String(defect.id);
            });
        },
        defectSeverityText: function (code) {
            var names = {
                low: 'Низкая',
                medium: 'Средняя',
                high: 'Высокая',
                critical: 'Критичная'
            };
            return names[code] || 'Не указана';
        },
        defectStatusText: function (code) {
            var names = {
                new: 'Новая',
                in_progress: 'В работе',
                resolved: 'Устранена',
                deferred: 'Отложена'
            };
            return names[code] || 'Не указан';
        },
        loadExistingPhotos: async function () {
            try {
                var response = await axios.get('/api/v1/media/maintenance/' + maintenanceId + '/photos', {
                    params: this.getWorkspaceParams()
                });
                this.existingPhotos = response.data.map(function (photo) {
                    return {
                        id: photo.photoId,
                        url: photo.url
                    };
                });
            } catch (error) {
                console.error('Ошибка загрузки фотографий:', error);
                this.existingPhotos = [];
            }
        },
        validateMainFields: function () {
            if (this.plannedDateRequired && !this.selectedMaintenance.plannedDate) {
                this.showError('Пожалуйста, укажите плановую дату обслуживания');
                return false;
            }

            var startTime = this.normalizeTimeForServer(this.selectedMaintenance.plannedStartTime);
            var endTime = this.normalizeTimeForServer(this.selectedMaintenance.plannedEndTime);
            if ((startTime && !endTime) || (!startTime && endTime)) {
                this.showError('Укажите время начала и окончания обслуживания');
                return false;
            }

            if (startTime && endTime && (this.selectedMaintenance.plannedEndDate || this.selectedMaintenance.plannedDate) + endTime <= this.selectedMaintenance.plannedDate + startTime) {
                this.showError('Время окончания должно быть позже времени начала');
                return false;
            }

            if (this.showsCompletedDate && !this.selectedMaintenance.completedDate) {
                this.showError('Пожалуйста, укажите фактическую дату выполнения');
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
        validateDetails: function () {
            var isValid = true;

            this.selectedMaintenance.maintenanceDetails.forEach(function (detail, index) {
                if (!detail.name || !detail.name.trim()) {
                    this.showError('Пожалуйста, укажите название позиции #' + (index + 1));
                    isValid = false;
                }

                if (detail.price != null && detail.price < 0) {
                    this.showError('Стоимость позиции #' + (index + 1) + ' не может быть отрицательной');
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
        redirectToCar: function () {
            window.location.href = this.getReturnTargetUrl();
        },
        redirectToMaintenance: function () {
            window.location.href = this.getMaintenanceDetailsUrl();
        },
        addNewPhotos: function (event) {
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
                this.showError('Общий размер файлов (' + this.formatFileSize(totalSize) + ') превышает максимально допустимый 100 МБ');
                this.resetFileInput();
                return;
            }

            if (this.newPhotos.length + files.length > 10) {
                this.showError('Можно загрузить не более 10 фотографий');
                this.resetFileInput();
                return;
            }

            var _this = this;
            Array.from(files).forEach(function (file) {
                _this.selectedFiles.push(file);
                _this.newPhotos.push({
                    file: file,
                    preview: URL.createObjectURL(file),
                    name: file.name,
                    size: _this.formatFileSize(file.size)
                });
            });
        },
        removeNewPhoto: function (index) {
            if (this.newPhotos[index].preview) {
                URL.revokeObjectURL(this.newPhotos[index].preview);
            }

            this.newPhotos.splice(index, 1);
            this.selectedFiles.splice(index, 1);

            if (this.selectedFiles.length === 0) {
                this.resetFileInput();
            }
        },
        clearNewPhotos: function () {
            this.newPhotos.forEach(function (photo) {
                if (photo.preview) {
                    URL.revokeObjectURL(photo.preview);
                }
            });
            this.newPhotos = [];
            this.selectedFiles = [];
            this.resetFileInput();
            this.resetPhotoMessages();
        },
        deleteExistingPhoto: function (photoObj, index) {
            this.deletePhotoModal.photoId = photoObj.id;
            this.deletePhotoModal.photoIndex = index;
            this.showDeletePhotoModal();
        },
        hideDeletePhotoModal: function () {
            this.deletePhotoModal.show = false;
            this.deletePhotoModal.photoId = null;
            this.deletePhotoModal.photoIndex = null;
            this.hideModal('#deletePhotoConfirmModal');
        },
        confirmDeletePhoto: async function () {
            var photoId = this.deletePhotoModal.photoId;
            var photoIndex = this.deletePhotoModal.photoIndex;

            if (!photoId) {
                this.hideDeletePhotoModal();
                return;
            }

            try {
                await axios.delete('/api/v1/media/maintenance/' + maintenanceId + '/photos/' + photoId, {
                    params: this.getWorkspaceParams()
                });
                this.existingPhotos.splice(photoIndex, 1);
                this.hideDeletePhotoModal();
            } catch (error) {
                this.hideDeletePhotoModal();
                this.handleApiError(error, 'Произошла ошибка удаления фотографии');
            }
        },
        showDeletePhotoModal: function () {
            this.deletePhotoModal.show = true;
            this.showModal('#deletePhotoConfirmModal');
        },
        uploadNewPhotosInternal: async function (redirectAfterUpload) {
            if (this.selectedFiles.length === 0) {
                return true;
            }

            this.isUploading = true;
            this.uploadProgress = 0;
            this.photoUploadErrorMessage = '';

            var formData = new FormData();
            var _this = this;
            this.selectedFiles.forEach(function (file) {
                formData.append('formFiles', file);
            });

            try {
                await axios.post('/api/v1/media/maintenance/' + maintenanceId + '/photos', formData, {
                    params: this.getWorkspaceParams(),
                    onUploadProgress: function (progressEvent) {
                        var percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total);
                        _this.uploadProgress = percentCompleted;
                    }
                });

                await this.loadExistingPhotos();
                this.clearNewPhotos();
                this.isUploading = false;

                if (redirectAfterUpload) {
                    this.redirectToCar();
                }

                return true;
            } catch (error) {
                this.photoUploadErrorMessage = 'Изменения сохранены, но новые фотографии не загрузились. Можно повторить загрузку ниже.';
                this.handleApiError(error, 'Произошла ошибка загрузки фотографий');
                this.isUploading = false;
                return false;
            }
        },
        uploadNewPhotos: async function () {
            if (this.selectedFiles.length === 0) {
                this.showError('Сначала выберите фотографии для загрузки.');
                return;
            }

            await this.uploadNewPhotosInternal(true);
        },
        changeMileage: function () {
            if (this.selectedMaintenance.mileage && this.selectedMaintenance.mileage > this.maxMileage) {
                this.selectedMaintenance.mileage = this.maxMileage;
            }

            if (this.selectedMaintenance.mileage === 0 || this.selectedMaintenance.mileage < 0) {
                this.selectedMaintenance.mileage = null;
            }
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
        formatPrice: function (price) {
            if (typeof price !== 'number') {
                return '0 руб.';
            }

            return price.toLocaleString('ru-RU', {
                minimumFractionDigits: 0,
                maximumFractionDigits: 2
            }) + ' руб.';
        },
        resetFileInput: function () {
            var fileInput = document.getElementById('newPhotos');
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
        save_editMaintenance: async function () {
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
                    LinkedDefectIds: this.maintenanceFormProfile.showLinkedDefects === false ? null : this.selectedMaintenance.linkedDefects.map(function (defect) {
                        return defect.id;
                    }),
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

                await axios.put('/api/v1/maintenance/' + this.selectedMaintenance.id, requestData, {
                    params: this.getWorkspaceParams()
                });

                this.resetPhotoMessages();

                if (this.selectedFiles.length > 0) {
                    var uploaded = await this.uploadNewPhotosInternal(false);
                    this.saveModal = false;

                    if (uploaded) {
                        this.redirectToMaintenance();
                        return;
                    }

                    this.scrollToPhotoSection();
                    return;
                }

                this.saveModal = false;
                this.redirectToMaintenance();
            } catch (error) {
                this.handleApiError(error, 'Произошла ошибка обновления обслуживания');
                this.saveModal = false;
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
        this.loadMaintenanceData();
        document.addEventListener('click', this.handleOutsideClick);
    },
    beforeDestroy: function () {
        document.removeEventListener('click', this.handleOutsideClick);
        this.newPhotos.forEach(function (photo) {
            if (photo.preview) {
                URL.revokeObjectURL(photo.preview);
            }
        });
    }
});

