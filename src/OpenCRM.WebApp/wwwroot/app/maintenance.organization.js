var maintenanceLegacyText = JSON.parse(document.getElementById('maintenance-legacy-messages').dataset.messages);

if (window['vue-ctk-date-time-picker'] && !Vue.options.components['vue-ctk-date-time-picker']) {
    Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);
}

if (window.VueSelect && window.VueSelect.VueSelect && !Vue.options.components['v-select']) {
    Vue.component('v-select', window.VueSelect.VueSelect);
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
            loadingTitle: maintenanceLegacyText.loadingFileTitle,
            loadingMessage: maintenanceLegacyText.loadingWait,
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
            maintenanceDocuments: [],
            maintenanceDocumentsLoaded: false,
            scenarioDocumentTemplates: (typeof maintenanceScenarioDocuments !== 'undefined' && Array.isArray(maintenanceScenarioDocuments))
                ? maintenanceScenarioDocuments
                : [],
            selectedDocumentTemplateId: null,
            documentUploading: false,
            documentActionKey: null,
            deleteSaving: false,
            workflowAction: null,
            workflowDate: '',
            workflowDatePickerValue: '',
            workflowEndDate: '',
            workflowPlannedStartTime: '',
            workflowPlannedEndTime: '',
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
        },
        selectedDocumentTemplate: function () {
            var selectedId = this.selectedDocumentTemplateId;
            return this.scenarioDocumentTemplates.find(function (template) {
                return String(template.id || '') === String(selectedId || '');
            }) || this.scenarioDocumentTemplates[0] || null;
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

        getDefectUrl: function (defect) {
            if (!defect || !defect.id || !this.organizationId) {
                return '#';
            }

            return '/organizations/' + encodeURIComponent(this.organizationId)
                + '/defects/' + encodeURIComponent(defect.id);
        },

        getMaintenanceHistoryUrl: function () {
            var url = this.appendOrganizationId('/maintenance/' + this.maintenance.id + '/history/view');
            var returnUrl = window.location.pathname + window.location.search;
            return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(returnUrl);
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
            this.loadingTitle = title || maintenanceLegacyText.loadingFileTitle;
            this.loadingMessage = message || maintenanceLegacyText.loadingWait;
            this.showModal('#loadingModal');
        },

        openDocument: async function (url, loadingTitle, popupErrorMessage, fallbackApiErrorMessage, actionKey) {
            if (this.documentActionKey) {
                return;
            }

            this.documentActionKey = actionKey || 'document';
            this.showLoadingModal(loadingTitle, maintenanceLegacyText.loadingWait);

            try {
                await this.downloadBlobFromUrl(url, 'document');
            } catch (error) {
                await this.handleDocumentDownloadError(error, fallbackApiErrorMessage || popupErrorMessage);
            } finally {
                this.hideModal('#loadingModal');
                this.documentActionKey = null;
            }
        },

        downloadBlobFromUrl: async function (url, fallbackName) {
            var response = await axios.get(url, {
                responseType: 'blob'
            });

            var contentType = response.headers['content-type'] || 'application/octet-stream';
            var blob = new Blob([response.data], { type: contentType });
            var fileName = this.getFileNameFromDisposition(response.headers['content-disposition'], fallbackName);
            var downloadUrl = window.URL.createObjectURL(blob);
            var link = document.createElement('a');

            link.href = downloadUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();

            window.setTimeout(function () {
                window.URL.revokeObjectURL(downloadUrl);
            }, 1000);
        },

        getFileNameFromDisposition: function (contentDisposition, fallbackName) {
            if (!contentDisposition) {
                return fallbackName || 'document';
            }

            var encodedMatch = contentDisposition.match(/filename\*=UTF-8''([^;]+)/i);
            if (encodedMatch && encodedMatch[1]) {
                return decodeURIComponent(encodedMatch[1].replace(/"/g, ''));
            }

            var match = contentDisposition.match(/filename="?([^"]+)"?/i);
            return match && match[1] ? match[1] : (fallbackName || 'document');
        },

        handleDocumentDownloadError: async function (error, fallbackMessage) {
            var message = fallbackMessage || maintenanceLegacyText.validationDocumentDownloadError;

            if (error && error.response && error.response.data instanceof Blob) {
                try {
                    var text = await error.response.data.text();
                    if (text) {
                        var parsed = JSON.parse(text);
                        message = parsed.message || message;
                    }
                } catch (parseError) {
                    // Оставляем человекочитаемое сообщение по умолчанию.
                }
            }

            this.showError(message);
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
                maintenanceLegacyText.loadingWordError,
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
                maintenanceLegacyText.loadingGeneratingPdf,
                maintenanceLegacyText.loadingPopupBlockedPdf,
                maintenanceLegacyText.loadingPdfError,
                includePhotos ? 'report-pdf-photos' : 'report-pdf'
            );
        },

        generateWorkActPDF: function () {
            if (!this.organizationId) {
                this.showError(maintenanceLegacyText.validationCancelDocumentOnlyOrg);
                return;
            }

            var url = this.appendOrganizationId('/api/v1/documents/pdf/maintenance/' + this.maintenance.id + '/work-act');

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingReport,
                maintenanceLegacyText.loadingPopupBlockedReport,
                maintenanceLegacyText.loadingReportError,
                'work-act-pdf'
            );
        },

        generateWorkActWord: function () {
            if (!this.organizationId) {
                this.showError(maintenanceLegacyText.validationCancelDocumentOnlyOrg);
                return;
            }

            var url = this.appendOrganizationId('/api/v1/documents/word/maintenance/' + this.maintenance.id + '/work-act');

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingReportWord,
                maintenanceLegacyText.loadingPopupBlockedReportWord,
                maintenanceLegacyText.loadingReportError,
                'work-act-word'
            );
        },

        generateWorkOrderPDF: function () {
            if (!this.organizationId) {
                this.showError(maintenanceLegacyText.validationOrderDocumentOnlyOrg);
                return;
            }

            var url = this.appendOrganizationId('/api/v1/documents/pdf/maintenance/' + this.maintenance.id + '/work-order');

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingOrder,
                maintenanceLegacyText.loadingPopupBlockedOrder,
                maintenanceLegacyText.loadingOrderError,
                'work-order-pdf'
            );
        },

        generateWorkOrderWord: function () {
            if (!this.organizationId) {
                this.showError(maintenanceLegacyText.validationOrderDocumentOnlyOrg);
                return;
            }

            var url = this.appendOrganizationId('/api/v1/documents/word/maintenance/' + this.maintenance.id + '/work-order');

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingOrderWord,
                maintenanceLegacyText.loadingPopupBlockedOrderWord,
                maintenanceLegacyText.loadingOrderError,
                'work-order-word'
            );
        },

        generateScenarioDocument: function (format) {
            var template = this.selectedDocumentTemplate;
            if (!template) {
                this.showError(maintenanceLegacyText.validationSelectDocumentTemplate);
                return;
            }

            var documentKind = this.resolveScenarioDocumentKind(template);
            var normalizedFormat = format === 'word' ? 'word' : 'pdf';

            if (!this.organizationId) {
                this.showError(maintenanceLegacyText.validationScenarioDocumentOnlyOrg);
                return;
            }

            var url = this.appendOrganizationId(
                '/api/v1/documents/' + normalizedFormat
                + '/maintenance/' + this.maintenance.id
                + '/scenario/' + template.id
            );
            var formatTitle = normalizedFormat === 'word' ? 'Word' : 'PDF';

            this.openDocument(
                url,
                maintenanceLegacyText.loadingGeneratingDocument,
                maintenanceLegacyText.loadingPopupBlockedDocument,
                maintenanceLegacyText.loadingDocumentError,
                'scenario-' + documentKind + '-' + normalizedFormat
            );
        },

        generateScenarioDocumentForTemplate: function (template, format) {
            if (!template || !template.id) {
                this.showError(maintenanceLegacyText.validationDocumentTemplateMissing);
                return;
            }

            this.selectedDocumentTemplateId = template.id;
            this.generateScenarioDocument(format);
        },

        resolveScenarioDocumentKind: function (template) {
            if (template.documentKindCode) {
                return template.documentKindCode;
            }

            var text = ((template.code || '') + ' ' + (template.name || '')).toLowerCase();
            if (text.indexOf('заказ') >= 0 || text.indexOf('наряд') >= 0 || text.indexOf('order') >= 0) {
                return 'work-order';
            }

            if (text.indexOf('акт') >= 0 || text.indexOf('act') >= 0) {
                return 'work-act';
            }

            if (text.indexOf('чек') >= 0 || text.indexOf('check') >= 0) {
                return 'receipt';
            }

            return 'maintenance-report';
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
                this.maintenance.plannedStartTime = this.normalizeTimeForServer(maintenanceData.plannedStartTime);
                this.maintenance.plannedEndTime = this.normalizeTimeForServer(maintenanceData.plannedEndTime);
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
                await this.loadMaintenanceDocuments();
                if (!this.selectedDocumentTemplateId && this.scenarioDocumentTemplates.length > 0) {
                    this.selectedDocumentTemplateId = this.scenarioDocumentTemplates[0].id;
                }

            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationLoadError);
            } finally {
                this.formLoad = false;
            }
        },

        loadMaintenanceDocuments: async function () {
            try {
                var response = await axios.get('/api/v1/maintenance/' + this.maintenance.id + '/documents', {
                    params: this.getWorkspaceParams()
                });

                this.maintenanceDocuments = response.data || [];
            } catch (error) {
                this.maintenanceDocuments = [];
                this.handleApiError(error, maintenanceLegacyText.validationDocumentsLoadError);
            } finally {
                this.maintenanceDocumentsLoaded = true;
            }
        },

        openSignedDocumentPicker: function () {
            if (this.$refs.signedDocumentInput) {
                this.$refs.signedDocumentInput.value = '';
                this.$refs.signedDocumentInput.click();
            }
        },

        uploadSignedDocument: async function (event) {
            if (this.documentUploading) {
                return;
            }

            var file = event.target.files && event.target.files[0];
            if (!file) {
                return;
            }

            var formData = new FormData();
            formData.append('formFile', file);
            this.documentUploading = true;

            try {
                await axios.post('/api/v1/maintenance/' + this.maintenance.id + '/documents/upload', formData, {
                    params: this.getWorkspaceParams(),
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    }
                });

                await this.loadMaintenanceDocuments();
            } catch (error) {
                this.handleApiError(error, maintenanceLegacyText.validationDocumentLoadError);
            } finally {
                this.documentUploading = false;
                if (this.$refs.signedDocumentInput) {
                    this.$refs.signedDocumentInput.value = '';
                }
            }
        },

        getDocumentDownloadUrl: function (documentId) {
            return this.appendOrganizationId('/api/v1/maintenance/documents/' + documentId + '/download');
        },

        downloadSignedDocument: async function (document) {
            if (!document || !document.id || this.documentActionKey) {
                return;
            }

            this.documentActionKey = 'signed-document';
            this.showLoadingModal(maintenanceLegacyText.loadingDownloadingDocument, maintenanceLegacyText.loadingWait);

            try {
                await this.downloadBlobFromUrl(
                    this.getDocumentDownloadUrl(document.id),
                    document.fileName || document.title || 'document');
            } catch (error) {
                await this.handleDocumentDownloadError(error, maintenanceLegacyText.validationDocumentDownloadError);
            } finally {
                this.hideModal('#loadingModal');
                this.documentActionKey = null;
            }
        },

        getDocumentFormatClass: function (document) {
            var format = document.format ? document.format.toLowerCase() : '';
            if (format === 'pdf') return 'maintenance-document-icon-pdf';
            if (format === 'jpg' || format === 'jpeg' || format === 'png') return 'maintenance-document-icon-image';
            return 'maintenance-document-icon-word';
        },

        getDocumentIconClass: function (document) {
            var format = document.format ? document.format.toLowerCase() : '';
            if (format === 'pdf') return 'fa-file-pdf-o';
            if (format === 'jpg' || format === 'jpeg' || format === 'png') return 'fa-file-image-o';
            return 'fa-file-word-o';
        },

        formatDocumentDate: function (dateString) {
            return dateString ? window.OpenCRMRegional.formatDate(dateString, { dateStyle: 'short', timeStyle: 'short' }) : '—';
        },

        formatFileSize: function (size) {
            if (!size) {
                return maintenanceLegacyText.documentFileZero;
            }

            var formatter = new Intl.NumberFormat(window.OpenCRMRegional.culture);
            if (size < 1024 * 1024) {
                var kilobytes = formatter.format(Math.max(1, Math.round(size / 1024)));
                return maintenanceLegacyText.documentFileKilobytes.replace('{0}', kilobytes);
            }

            var megabytes = new Intl.NumberFormat(window.OpenCRMRegional.culture, {
                minimumFractionDigits: 1,
                maximumFractionDigits: 1
            }).format(size / 1024 / 1024);
            return maintenanceLegacyText.documentFileMegabytes.replace('{0}', megabytes);
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
            return targetDay ? targetDay.format('YYYY-MM-DD') < window.OpenCRMRegional.today() : false;
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
            var dateText = this.formatDate(date);
            var timeText = this.formatPlannedTimeRange();
            if (this.maintenance.plannedEndDate && this.maintenance.plannedEndDate.slice(0, 10) !== (date || '').slice(0, 10)) {
                return dateText + ' ' + (this.maintenance.plannedStartTime || '').slice(0, 5) + ' — ' + this.formatDate(this.maintenance.plannedEndDate) + ' ' + (this.maintenance.plannedEndTime || '').slice(0, 5);
            }
            return timeText ? dateText + ', ' + timeText : dateText;
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
            this.workflowPlannedStartTime = this.normalizeTimeForServer(this.maintenance.plannedStartTime) || '09:00';
            this.workflowPlannedEndTime = this.normalizeTimeForServer(this.maintenance.plannedEndTime) || '10:00';

            if (action === 'complete') {
                this.workflowModalTitle = maintenanceLegacyText.workflowCompleteTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowCompleteAction;
                this.workflowDate = this.maintenance.completedDate || window.OpenCRMRegional.today();
            } else if (action === 'cancel') {
                this.workflowModalTitle = maintenanceLegacyText.workflowCancelTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowCancelAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || window.OpenCRMRegional.today();
            } else if (action === 'reschedule') {
                this.workflowModalTitle = maintenanceLegacyText.workflowRescheduleTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowRescheduleAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || window.OpenCRMRegional.today();
            } else if (action === 'reopen') {
                this.workflowModalTitle = maintenanceLegacyText.workflowResumeTitle;
                this.workflowConfirmText = maintenanceLegacyText.workflowResumeAction;
                this.workflowDate = this.maintenance.plannedDate || this.maintenance.date || window.OpenCRMRegional.today();
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
                payload.completedDate = this.getWorkflowDateForServer() || window.OpenCRMRegional.today();
                payload.completedDateText = payload.completedDate;
                payload.mileage = this.workflowMileage || null;
            } else if (action === 'reschedule' || action === 'reopen') {
                payload.plannedDate = this.getWorkflowDateForServer();
                payload.plannedDateText = payload.plannedDate;
                payload.plannedEndDate = this.normalizeDateForServer(this.workflowEndDate || payload.plannedDate);
                payload.plannedStartTimeText = this.normalizeTimeForServer(this.workflowPlannedStartTime);
                payload.plannedEndTimeText = this.normalizeTimeForServer(this.workflowPlannedEndTime);
                if (!payload.plannedStartTimeText || !payload.plannedEndTimeText) {
                    this.showError(maintenanceLegacyText.validationPlannedTimeRequired);
                    return;
                }
                if ((payload.plannedEndDate + 'T' + payload.plannedEndTimeText) <= (payload.plannedDate + 'T' + payload.plannedStartTimeText)) {
                    this.showError(maintenanceLegacyText.validationEndAfterStart);
                    return;
                }
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
                this.handleApiError(error, maintenanceLegacyText.deleteMaintenanceError);
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
        window.OpenCRMRegional.initializeLegacyVm(this);
        this.loadMaintenanceData();

        var _this = this;
        $('#maintenanceWorkflowModal, #deleteMaintenanceModal, #photoModal, #loadingModal, #errorModal').on('hidden.bs.modal', function () {
            _this.cleanupModalState();
        });

        $('#photoModal').on('hidden.bs.modal', function () {
            _this.currentPhotoIndex = 0;
        });
    },
    beforeDestroy: function () {
        $('#maintenanceWorkflowModal, #deleteMaintenanceModal, #photoModal, #loadingModal, #errorModal').off('hidden.bs.modal');
    }
});





