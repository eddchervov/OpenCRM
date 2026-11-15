new Vue({
    el: '#maintenance-details-form',
    data: function () {
        return {
            organizationId: typeof maintenanceOrganizationId !== 'undefined' && maintenanceOrganizationId ? maintenanceOrganizationId : null,
            formLoad: true,
            errorMessage: '',
            loadingTitle: 'Подготовка файла...',
            loadingMessage: 'Пожалуйста, подождите',
            maintenance: {
                id: maintenanceId,
                status: 3,
                type: 1,
                date: null,
                plannedEndDate: null,
                plannedDate: null,
                plannedStartTime: null,
                plannedEndTime: null,
                completedDate: null,
                location: null,
                mileage: null,
                comment: null,
                price: null,
                maintenanceDetails: [],
                photoIds: [],
                linkedDefects: []
            },
            maintenanceHistoryEvents: [],
            maintenanceHistoryLoaded: false,
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
            documentActionKey: null,
            deleteSaving: false
        };
    },
    computed: {
        hasAnyExportAccess: function () {
            return this.exportAccess.pdf || this.exportAccess.pdfWithPhotos || this.exportAccess.word || this.exportAccess.wordWithPhotos;
        },
        mileageUnitLabel: function () {
            return this.vehicle.mileageUnit === 2 ? 'Моточасы' : 'Пробег';
        },
        mileageUnitShortText: function () {
            return this.vehicle.mileageUnit === 2 ? 'м/ч' : 'км';
        }
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
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id);
        },

        getMaintenanceUpdateUrl: function () {
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/maintenance/' + this.maintenance.id + '/update');
        },

        cleanupModalState: function () {
            this.$nextTick(function () {
                if (document.querySelectorAll('.modal.show').length > 0) {
                    return;
                }

                document.querySelectorAll('.modal-backdrop').forEach(function (backdrop) {
                    backdrop.remove();
                });
                document.body.classList.remove('modal-open');
                document.body.classList.remove('workspace-planner-maintenance-modal-open');
                document.body.style.removeProperty('overflow');
                document.body.style.removeProperty('padding-right');
            });
        },

        showModal: function (selector) {
            var element = document.querySelector(selector);
            if (!element) {
                return;
            }

            document.body.classList.remove('workspace-planner-maintenance-modal-open');
            if (document.querySelectorAll('.modal.show').length === 0) {
                document.querySelectorAll('.modal-backdrop').forEach(function (backdrop) {
                    backdrop.remove();
                });
                document.body.classList.remove('modal-open');
                document.body.style.removeProperty('overflow');
                document.body.style.removeProperty('padding-right');
            }

            if (window.bootstrap && window.bootstrap.Modal) {
                window.bootstrap.Modal.getOrCreateInstance(element).show();
                return;
            }

            $(element).modal('show');
        },

        hideModal: function (selector) {
            var element = document.querySelector(selector);
            if (!element) {
                this.cleanupModalState();
                return;
            }

            if (window.bootstrap && window.bootstrap.Modal) {
                var modal = window.bootstrap.Modal.getInstance(element);
                if (modal) {
                    modal.hide();
                } else {
                    element.classList.remove('show');
                    element.style.display = 'none';
                }
                this.cleanupModalState();
                return;
            }

            $(element).modal('hide');
            this.cleanupModalState();
        },

        showLoadingModal: function (title, message) {
            this.loadingTitle = title || 'Подготовка файла...';
            this.loadingMessage = message || 'Пожалуйста, подождите';
            this.showModal('#loadingModal');
        },

        openDocument: function (url, loadingTitle, popupErrorMessage, fallbackApiErrorMessage, actionKey) {
            if (this.documentActionKey) {
                return;
            }

            this.documentActionKey = actionKey || 'document';
            this.showLoadingModal(loadingTitle, 'Пожалуйста, подождите');

            try {
                var documentWindow = window.open(url, '_blank');
                var vm = this;

                if (documentWindow) {
                    documentWindow.onload = function () {
                        vm.hideModal('#loadingModal');
                        vm.documentActionKey = null;
                    };

                    setTimeout(function () {
                        vm.hideModal('#loadingModal');
                        vm.documentActionKey = null;
                    }, 2000);

                    return;
                }

                this.hideModal('#loadingModal');
                this.documentActionKey = null;
                this.showError(popupErrorMessage);
            } catch (error) {
                this.hideModal('#loadingModal');
                this.documentActionKey = null;
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
                    throw new Error('Не удалось загрузить фотографию');
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
                'Генерация Word...',
                'Разрешите всплывающие окна для скачивания Word документа',
                'Ошибка при генерации Word документа',
                includePhotos ? 'report-word-photos' : 'report-word'
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
                'Генерация PDF...',
                'Разрешите всплывающие окна для скачивания PDF',
                'Ошибка при генерации PDF',
                includePhotos ? 'report-pdf-photos' : 'report-pdf'
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
                this.maintenance.date = maintenanceData.date;
                this.maintenance.plannedDate = maintenanceData.plannedDate;
                this.maintenance.plannedEndDate = maintenanceData.plannedEndDate;
                this.maintenance.plannedStartTime = this.normalizeTimeForServer(maintenanceData.plannedStartTime);
                this.maintenance.plannedEndTime = this.normalizeTimeForServer(maintenanceData.plannedEndTime);
                this.maintenance.completedDate = maintenanceData.completedDate;
                this.maintenance.location = maintenanceData.location;
                this.maintenance.mileage = maintenanceData.mileage;
                this.maintenance.comment = maintenanceData.comment;
                this.maintenance.price = maintenanceData.price;
                this.maintenance.maintenanceDetails = maintenanceData.maintenanceDetails || [];
                this.maintenance.photoIds = maintenanceData.photoIds || [];
                this.maintenance.linkedDefects = maintenanceData.linkedDefects || [];

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
                await this.loadMaintenanceHistoryEvents();

            } catch (error) {
                this.handleApiError(error, 'Произошла ошибка загрузки данных обслуживания');
            } finally {
                this.formLoad = false;
            }
        },

        loadMaintenanceHistoryEvents: async function () {
            this.maintenanceHistoryLoaded = false;

            try {
                var response = await axios.get('/api/v1/maintenance/' + this.maintenance.id + '/history', {
                    params: this.getWorkspaceParams()
                });

                this.maintenanceHistoryEvents = response.data || [];
            } catch (error) {
                this.maintenanceHistoryEvents = [];
                this.handleApiError(error, 'Не удалось загрузить историю обслуживания');
            } finally {
                this.maintenanceHistoryLoaded = true;
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
                1: 'Ремонт и обслуживание',
                2: 'Плановое ТО',
                3: 'Заправка топливом',
                100: 'Другое'
            };
            return typeTexts[type] || 'Обслуживание';
        },

        getMaintenanceStatusText: function (status) {
            var statusTexts = {
                1: 'Запланировано',
                2: 'В работе',
                3: 'Выполнено',
                4: 'Отменено'
            };
            return statusTexts[status] || 'Неизвестно';
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
            return this.maintenance.status === 3 ? 'Дата выполнения' : 'Плановая дата';
        },

        formatDate: function (dateString) {
            return new Date(dateString).toLocaleDateString('ru-RU', {
                day: '2-digit',
                month: '2-digit',
                year: 'numeric'
            });
        },

        normalizeTimeForServer: function (value) {
            if (!value) return null;

            var parsed = moment(String(value), ['HH:mm', 'HH:mm:ss'], true);
            return parsed.isValid() ? parsed.format('HH:mm') : null;
        },

        formatPlannedTimeRange: function () {
            var start = this.normalizeTimeForServer(this.maintenance.plannedStartTime);
            var end = this.normalizeTimeForServer(this.maintenance.plannedEndTime);

            if (start && end) {
                return start + ' - ' + end;
            }

            return start || end || '';
        },

        formatPlannedDateTime: function () {
            var date = this.maintenance.plannedDate || this.maintenance.date;
            var dateText = date ? this.formatDate(date) : '—';
            var timeText = this.formatPlannedTimeRange();
            if (this.maintenance.plannedEndDate && this.maintenance.plannedEndDate.slice(0, 10) !== (date || '').slice(0, 10)) {
                return dateText + ' ' + (this.maintenance.plannedStartTime || '').slice(0, 5) + ' — ' + this.formatDate(this.maintenance.plannedEndDate) + ' ' + (this.maintenance.plannedEndTime || '').slice(0, 5);
            }
            return timeText ? dateText + ', ' + timeText : dateText;
        },

        formatPrice: function (price) {
            return new Intl.NumberFormat('ru-RU', {
                style: 'currency',
                currency: 'RUB',
                minimumFractionDigits: 0
            }).format(price);
        },

        openPhotoGallery: function (index) {
            this.currentPhotoIndex = index;
            this.showModal('#photoModal');
        },

        deleteMaintenance: function () {
            this.showModal('#deleteMaintenanceModal');
        },

        confirmDelete: async function () {
            if (this.deleteSaving) {
                return;
            }

            try {
                this.deleteSaving = true;
                await axios.delete('/api/v1/maintenance/' + this.maintenance.id, {
                    params: this.getWorkspaceParams()
                });
                this.hideModal('#deleteMaintenanceModal');
                this.redirectToVehicle();
            } catch (error) {
                this.handleApiError(error, 'Не удалось удалить обслуживание');
                this.hideModal('#deleteMaintenanceModal');
            } finally {
                this.deleteSaving = false;
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
        this.loadMaintenanceData();

        var _this = this;
        $('#deleteMaintenanceModal, #photoModal, #loadingModal, #errorModal').on('hidden.bs.modal', function () {
            _this.cleanupModalState();
        });

        $('#photoModal').on('hidden.bs.modal', function () {
            _this.currentPhotoIndex = 0;
        });
    },
    beforeDestroy: function () {
        $('#deleteMaintenanceModal, #photoModal, #loadingModal, #errorModal').off('hidden.bs.modal');
    }
});


