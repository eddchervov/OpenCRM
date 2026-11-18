if (window.OpenCRMVehiclePlate) {
    window.OpenCRMVehiclePlate.installVue(Vue);
}

// vehicle-card.js
Vue.component('v-select', VueSelect.VueSelect);
if (window['vue-ctk-date-time-picker'] && !Vue.options.components['vue-ctk-date-time-picker']) {
    Vue.component('vue-ctk-date-time-picker', window['vue-ctk-date-time-picker']);
}

var vehicleText = document.getElementById('vehicle-form').dataset;

new Vue({
    el: '#vehicle-form',
    data: function () {
        return {
            regionalCurrencyCode: null,
            formLoad: true,
            errorMessage: '',
            vehicle: {
                id: vehicleId,
                organizationId: typeof vehicleOrganizationId !== 'undefined' && vehicleOrganizationId ? vehicleOrganizationId : null,
                kind: 1,
                mileageUnit: 1,
                brandName: '',
                modelName: '',
                yearIssue: null,
                name: '',
                vin: '',
                registrationPlate: '',
                registrationCountryCode: '',
                bodyNumber: '',
                engineNumber: '',
                color: '',
                registrationCertificate: '',
                commissioningDate: '',
                description: '',
                currentMileage: null,
                currentMileageUpdatedAt: null,
                photoId: '',
                photoUrl: '',
                photoLoadFailed: false,
                isUploading: false,
                brandId: null,
                modelId: null,
                organizationClientId: null,
                organizationClientName: null,
                manualBrandName: null,
                manualModelName: null,
                isManualEntry: false
            },
            maintenanceHistory: [],
            deletedMaintenanceHistory: [],
            maintenanceAnalytics: [],
            maintenanceAnalyticsLoaded: false,
            counterHistoryChartEntries: [],
            counterHistoryLoaded: false,
            expandedMaintenance: null,
            currentPhotos: [],
            currentPhotoIndex: 0,
            vehiclePhotos: [],
            vehiclePhotosLoading: false,
            currentVehiclePhotoIndex: 0,
            albumPreview: [],
            albumsPreviewLoading: false,
            saveModal: false,
            modalType: 'edit',
            selectedBrandId: null,
            isManualIdentity: false,
            catalogModeAvailable: true,
            manualIdentityDraft: {
                brandName: null,
                modelName: null
            },
            catalogIdentityDraft: {
                brandId: null,
                modelId: null
            },
            brands: [],
            organizationClients: [],
            organizationClientsEnabled: false,
            models: [],
            loadOrganizationClients: false,
            loadModels: false,
            deleteModal: false,
            originalVehicleData: null,
            originalSelectedBrandId: null,
            mileageUnitWarningShown: false,
            selectedMaintenance: null,
            selectedMaintenanceDate: '',
            maintenanceFilters: {
                sort: 'date_desc',
                type: 'all',
                status: 'all'
            },
            pagination: {
                page: 1,
                pageSize: 10,
                hasMore: false,
                isLoading: false
            },
            deletedPagination: {
                page: 1,
                pageSize: 10,
                hasMore: false,
                isLoading: false
            },
            vehicleKindOptions: [
                { value: 1, text: vehicleText.cardKindPassenger },
                { value: 2, text: vehicleText.cardKindTruck },
                { value: 3, text: vehicleText.cardKindMotorcycle },
                { value: 4, text: vehicleText.cardKindAtv },
                { value: 5, text: vehicleText.cardKindUtv },
                { value: 6, text: vehicleText.cardKindSnowmobile },
                { value: 7, text: vehicleText.cardKindJetski },
                { value: 8, text: vehicleText.cardKindBoat },
                { value: 9, text: vehicleText.cardKindSpecial },
                { value: 10, text: vehicleText.cardKindTrailer },
                { value: 99, text: vehicleText.cardKindOther }
            ],
            mileageUnitOptions: [
                { value: 1, text: vehicleText.cardMileageKilometers },
                { value: 2, text: vehicleText.cardMileageEngineHours }
            ],
            maintenanceSortOptions: [
                { value: 'mileage_desc', text: vehicleText.cardSortMileageDesc },
                { value: 'mileage_asc', text: vehicleText.cardSortMileageAsc },
                { value: 'date_desc', text: vehicleText.cardSortDateNewest },
                { value: 'date_asc', text: vehicleText.cardSortDateOldest }
            ],
            maintenanceTypeFilterOptions: [
                { value: 'all', text: vehicleText.cardFilterAllTypes },
                { value: 1, text: vehicleText.cardMaintenanceRepair },
                { value: 2, text: vehicleText.cardMaintenancePlannedService },
                { value: 3, text: vehicleText.cardMaintenanceFuel },
                { value: 100, text: vehicleText.cardOther }
            ],
            maintenanceStatusFilterOptions: [
                { value: 'all', text: vehicleText.cardFilterAllStatuses },
                { value: 1, text: vehicleText.cardStatusPlanned },
                { value: 2, text: vehicleText.cardStatusInProgress },
                { value: 3, text: vehicleText.cardStatusCompleted },
                { value: 4, text: vehicleText.cardStatusCancelled }
            ],
            maintenanceTotalCount: 0
            ,
            deletedMaintenanceTotalCount: 0,
            showDeletedMaintenance: false,
            deletedMaintenanceLoading: false,
            exportAccess: {
                loaded: false,
                pdf: false,
                pdfWithPhotos: false,
                word: false,
                wordWithPhotos: false
            },
            initialMaintenanceLoaded: false
            ,
            vehicleHistory: [],
            vehicleHistoryLoaded: false,
            vehicleHistoryVisible: false,
            vehicleHistoryLoading: false,
            transferOrganizations: window.personalVehicleTransferOrganizations || [],
            selectedTransferOrganizationId: '',
            transferSaveInProgress: false,
            personalCounterModal: null,
            personalCounterLookupLoading: false,
            personalCounterSaveInProgress: false,
            personalCounterLookup: null,
            personalCounterForm: {
                entryId: '',
                recordedAt: '',
                value: null,
                comment: ''
            }
        };
    },
    computed: {
        currentVehiclePhoto: function () {
            return this.vehiclePhotos[this.currentVehiclePhotoIndex] || null;
        },
        currentVehiclePhotoUrl: function () {
            return this.currentVehiclePhoto && this.currentVehiclePhoto.url
                ? this.currentVehiclePhoto.url
                : this.vehicle.photoUrl;
        },
        hasMultipleVehiclePhotos: function () {
            return this.vehiclePhotos.length > 1;
        },
        hasMaintenanceRecordsForExport: function () {
            return this.maintenanceTotalCount > 0;
        },
        hasVehicleHistory: function () {
            return this.vehicleHistory.length > 0;
        },
        hasAnyExportAccess: function () {
            return this.exportAccess.pdf || this.exportAccess.pdfWithPhotos || this.exportAccess.word || this.exportAccess.wordWithPhotos;
        },
        canTransferCurrentVehicle: function () {
            return !this.vehicle.organizationId && this.transferOrganizations.length > 0;
        },
        canSaveVehicle: function () {
            if (!this.vehicle) return false;
            return [this.vehicle.registrationPlate, this.vehicle.vin, this.vehicle.bodyNumber,
                this.vehicle.engineNumber, this.vehicle.registrationCertificate, this.vehicle.name]
                .some(function (value) { return value && String(value).trim(); });
        },
        mileageUnitShortText: function () {
            return this.vehicle.mileageUnit === 2 ? vehicleText.cardUnitShortHours : vehicleText.cardUnitShortKilometers;
        },
        personalCounterUnitLabel: function () {
            return this.vehicle.mileageUnit === 2 ? vehicleText.cardMileageEngineHours : vehicleText.cardMileageKilometers;
        },
        personalCounterButtonText: function () {
            return this.vehicle.mileageUnit === 2 ? vehicleText.cardCounterAddHours : vehicleText.cardCounterAddKilometers;
        },
        personalCounterTitle: function () {
            if (this.personalCounterLookup && this.personalCounterLookup.hasEntry) {
                return this.personalCounterLookup.sourceType === 'maintenance'
                    ? (this.vehicle.mileageUnit === 2 ? vehicleText.cardCounterAddHoursOnDate : vehicleText.cardCounterAddKilometersOnDate)
                    : vehicleText.cardCounterEditEntry;
            }

            return this.vehicle.mileageUnit === 2 ? vehicleText.cardCounterAddHours : vehicleText.cardCounterAddKilometers;
        },
        personalCounterSubmitText: function () {
            return this.personalCounterLookup && this.personalCounterLookup.hasEntry && this.personalCounterLookup.sourceType !== 'maintenance'
                ? vehicleText.cardCounterSaveChanges
                : vehicleText.cardCounterSaveEntry;
        },
        personalCounterLookupMessage: function () {
            if (!this.personalCounterLookup || !this.personalCounterLookup.hasEntry) {
                return '';
            }

            return this.personalCounterLookup.sourceType === 'maintenance'
                ? vehicleText.cardCounterFromMaintenance
                : vehicleText.cardCounterManualEntry;
        },
        personalCounterLookupAlertClass: function () {
            if (!this.personalCounterLookup || !this.personalCounterLookup.hasEntry) {
                return 'alert-light border';
            }

            return this.personalCounterLookup.sourceType === 'maintenance'
                ? 'alert-info'
                : 'alert-success';
        },
        hasMaintenanceFilters: function () {
            return this.maintenanceFilters.sort !== 'date_desc'
                || this.maintenanceFilters.type !== 'all'
                || this.maintenanceFilters.status !== 'all'
                || this.showDeletedMaintenance;
        },
        displayedMaintenanceHistory: function () {
            if (!this.showDeletedMaintenance) {
                return this.maintenanceHistory;
            }

            return this.deletedMaintenanceHistory.slice().sort(function (a, b) {
                var aDate = a.date || a.completedDate || a.plannedDate;
                var bDate = b.date || b.completedDate || b.plannedDate;
                return new Date(bDate || 0) - new Date(aDate || 0);
            });
        },
        maintenanceLoadMoreVisible: function () {
            return this.showDeletedMaintenance
                ? this.deletedPagination.hasMore
                : this.pagination.hasMore;
        },
        maintenanceListLoading: function () {
            return this.showDeletedMaintenance
                ? this.deletedPagination.isLoading
                : this.pagination.isLoading;
        },
        serviceChartRecords: function () {
            return this.maintenanceAnalytics.slice().sort(function (a, b) {
                return new Date(a.date) - new Date(b.date);
            });
        },
        manualCounterChartRecords: function () {
            return this.counterHistoryChartEntries
                .filter(function (item) {
                    return item
                        && item.sourceType === 'manual'
                        && typeof item.value === 'number'
                        && item.value >= 0
                        && item.recordedAt;
                })
                .slice()
                .sort(function (a, b) { return new Date(a.recordedAt) - new Date(b.recordedAt); });
        },
        vehicleTimelineSeries: function () {
            var counterLabel = this.vehicle.mileageUnit === 2 ? vehicleText.cardCounterHoursLabel : vehicleText.cardCounterMileageLabel;

            var counterItems = this.manualCounterChartRecords.map(function (item) {
                return {
                    id: 'counter-' + item.id,
                    kind: 'counter',
                    colorClass: 'counter-chart-point-counter',
                    date: item.recordedAt,
                    value: item.value,
                    title: counterLabel,
                    subtitle: item.comment || '',
                    tooltip: vehicleText.cardChartType + ': ' + counterLabel + '\n' + vehicleText.cardChartDate + ': ' + this.formatDate(item.recordedAt) + '\n' + vehicleText.cardChartValue + ': ' + this.formatCounterValue(item.value, this.vehicle.mileageUnit) + (item.comment ? '\n' + vehicleText.cardChartComment + ': ' + item.comment : '')
                };
            }, this);

            var maintenanceItems = this.serviceChartRecords
                .filter(function (item) { return typeof item.mileage === 'number' && item.mileage >= 0; })
                .map(function (item) {
                    var maintenanceTypeText = this.getMaintenanceTypeText(item.type);
                    return {
                        id: 'maintenance-' + item.id,
                        kind: 'maintenance',
                        colorClass: 'counter-chart-point-maintenance',
                        date: item.date,
                        value: item.mileage,
                        title: vehicleText.cardChartMaintenance,
                        subtitle: maintenanceTypeText,
                        tooltip: vehicleText.cardChartType + ': ' + vehicleText.cardChartMaintenance + '\n' + vehicleText.cardChartKind + ': ' + maintenanceTypeText + '\n' + vehicleText.cardChartDate + ': ' + this.formatDate(item.date) + '\n' + vehicleText.cardChartValue + ': ' + this.formatCounterValue(item.mileage, this.vehicle.mileageUnit)
                    };
                }, this);

            return counterItems.concat(maintenanceItems).sort(function (a, b) {
                return new Date(a.date) - new Date(b.date);
            });
        },
        hasVehicleTimelineData: function () {
            return this.vehicleTimelineSeries.length > 0;
        },
        vehicleTimelineUsesIntegerScale: function () {
            return this.vehicleTimelineSeries.length
                ? this.vehicleTimelineSeries.every(function (item) {
                    var value = Number(item.value || 0);
                    return Math.abs(value - Math.round(value)) < 1e-6;
                })
                : true;
        },
        vehicleTimelineBounds: function () {
            if (!this.vehicleTimelineSeries.length) {
                return { min: 0, max: 0, range: 1 };
            }

            var values = this.vehicleTimelineSeries.map(function (item) { return Number(item.value || 0); });
            var min = Math.min.apply(null, values);
            var max = Math.max.apply(null, values);
            var useIntegerScale = this.vehicleTimelineUsesIntegerScale;

            if (min === max) {
                if (useIntegerScale) {
                    min = Math.max(0, min - 2);
                    max = max + 2;
                } else {
                    min = Math.max(0, min - 1);
                    max = max + 1;
                }
            }

            return {
                min: min,
                max: max,
                range: Math.max(1, max - min)
            };
        },
        vehicleTimelinePoints: function () {
            if (!this.vehicleTimelineSeries.length) {
                return [];
            }

            var bounds = this.vehicleTimelineBounds;
            var leftPadding = 56;
            var topPadding = 24;
            var width = 724;
            var height = 276;
            var availableWidth = width - leftPadding;
            var availableHeight = height - topPadding * 2;
            var count = this.vehicleTimelineSeries.length;

            return this.vehicleTimelineSeries.map(function (item, index) {
                var x = count === 1
                    ? leftPadding + availableWidth / 2
                    : leftPadding + (availableWidth * index / (count - 1));
                var y = height - ((Number(item.value || 0) - bounds.min) / bounds.range * availableHeight);

                return Object.assign({}, item, {
                    x: Math.round(x * 100) / 100,
                    y: Math.round(y * 100) / 100,
                    shortDate: window.OpenCRMRegional.formatDate(item.date, { month: 'short', year: '2-digit' })
                });
            });
        },
        vehicleTimelinePolyline: function () {
            return this.vehicleTimelinePoints.map(function (point) {
                return point.x + ',' + point.y;
            }).join(' ');
        },
        vehicleTimelineGridLines: function () {
            var bounds = this.vehicleTimelineBounds;
            var topPadding = 24;
            var height = 276;
            var steps = 4;
            var lines = [];
            var useIntegerScale = this.vehicleTimelineUsesIntegerScale;

            for (var i = 0; i <= steps; i += 1) {
                var ratio = i / steps;
                var rawValue = bounds.max - bounds.range * ratio;
                var y = topPadding + (height - topPadding * 2) * ratio;
                var displayValue = useIntegerScale ? Math.round(rawValue) : rawValue;

                lines.push({
                    value: i,
                    y: Math.round(y * 100) / 100,
                    label: Number(displayValue).toLocaleString(window.OpenCRMRegional.culture, useIntegerScale
                        ? { maximumFractionDigits: 0 }
                        : { maximumFractionDigits: 2 })
                });
            }

            return lines;
        },
        vehicleTimelineLabelPoints: function () {
            if (this.vehicleTimelinePoints.length <= 4) {
                return this.vehicleTimelinePoints;
            }

            var indexes = [
                0,
                Math.floor((this.vehicleTimelinePoints.length - 1) / 3),
                Math.floor((this.vehicleTimelinePoints.length - 1) * 2 / 3),
                this.vehicleTimelinePoints.length - 1
            ];
            var used = {};

            return indexes
                .filter(function (index) {
                    if (used[index]) {
                        return false;
                    }

                    used[index] = true;
                    return true;
                })
                .map(function (index) {
                    return this.vehicleTimelinePoints[index];
                }, this);
        },
        averageMaintenanceIntervalText: function () {
            var mileageItems = this.serviceChartRecords
                .filter(function (item) { return typeof item.mileage === 'number' && item.mileage >= 0; })
                .sort(function (a, b) { return new Date(a.date) - new Date(b.date); });

            var mileageDiffs = [];
            for (var i = 1; i < mileageItems.length; i++) {
                var diff = mileageItems[i].mileage - mileageItems[i - 1].mileage;
                if (diff > 0) {
                    mileageDiffs.push(diff);
                }
            }

            if (mileageDiffs.length > 0) {
                var mileageAverage = Math.round(mileageDiffs.reduce(function (sum, value) { return sum + value; }, 0) / mileageDiffs.length);
                return mileageAverage.toLocaleString(window.OpenCRMRegional.culture) + ' ' + this.mileageUnitShortText;
            }

            var dateItems = this.serviceChartRecords
                .filter(function (item) { return item.date; })
                .sort(function (a, b) { return new Date(a.date) - new Date(b.date); });
            var dayDiffs = [];

            for (var j = 1; j < dateItems.length; j++) {
                var days = Math.round((new Date(dateItems[j].date) - new Date(dateItems[j - 1].date)) / 86400000);
                if (days > 0) {
                    dayDiffs.push(days);
                }
            }

            if (dayDiffs.length === 0) {
                return vehicleText.cardNoData;
            }

            var dayAverage = Math.round(dayDiffs.reduce(function (sum, value) { return sum + value; }, 0) / dayDiffs.length);
            return dayAverage + ' ' + vehicleText.cardDaysShort;
        }
    },
    methods: {
            getVehicleDisplayName: getVehicleDisplayName,
        showModal: function (selector) {
            $(selector).modal('show');
        },

        hideModal: function (selector) {
            $(selector).modal('hide');
        },

        loadPhoto: async function (photoId) {
            var response = await fetch('/api/v1/media/' + photoId + '/path');
            if (!response.ok) {
                throw new Error(vehicleText.cardErrorPhoto);
            }

            return await response.text();
        },

        preloadVehiclePhoto: async function () {
            this.vehicle.photoLoadFailed = false;

            if (this.vehicle.photoId) {
                try {
                    this.vehicle.photoUrl = await this.loadPhoto(this.vehicle.photoId);
                } catch (error) {
                    console.error('Error loading photo for vehicle:', this.vehicle.id, error);
                    this.vehicle.photoUrl = null;
                    this.vehicle.photoLoadFailed = true;
                }
            } else {
                this.vehicle.photoUrl = null;
            }
        },
        getWorkspaceParams: function () {
            return this.vehicle.organizationId ? { organizationId: this.vehicle.organizationId } : {};
        },

        loadVehiclePhotos: async function () {
            this.vehiclePhotosLoading = true;

            try {
                var response = await axios.get('/api/v1/media/vehicles/' + this.vehicle.id + '/photos', {
                    params: this.getWorkspaceParams()
                });
                var photos = response.data || [];

                photos = photos.slice().sort(function (a, b) {
                    if (a.isPrimary === b.isPrimary) {
                        return 0;
                    }

                    return a.isPrimary ? -1 : 1;
                });

                this.vehiclePhotos = photos;
                this.currentVehiclePhotoIndex = 0;

                if (photos.length) {
                    this.vehicle.photoUrl = photos[0].url;
                    this.vehicle.photoLoadFailed = false;
                }
            } catch (error) {
                console.error('Error loading vehicle photos:', error);
                this.vehiclePhotos = [];
            } finally {
                this.vehiclePhotosLoading = false;
            }
        },

        showPreviousVehiclePhoto: function () {
            if (!this.vehiclePhotos.length) {
                return;
            }

            this.currentVehiclePhotoIndex = (this.currentVehiclePhotoIndex - 1 + this.vehiclePhotos.length) % this.vehiclePhotos.length;
        },

        showNextVehiclePhoto: function () {
            if (!this.vehiclePhotos.length) {
                return;
            }

            this.currentVehiclePhotoIndex = (this.currentVehiclePhotoIndex + 1) % this.vehiclePhotos.length;
        },

        openVehiclePhotoGallery: function () {
            if (!this.vehiclePhotos.length) {
                return;
            }

            this.openPhotoGallery(this.vehiclePhotos.map(function (photo) {
                return photo.url;
            }), this.currentVehiclePhotoIndex);
        },

        loadAlbumPreview: async function () {
            this.albumsPreviewLoading = true;

            try {
                var response = await axios.get('/api/v1/media/vehicles/' + vehicleId + '/albums', {
                    params: this.getWorkspaceParams()
                });

                this.albumPreview = (response.data.albums || []).slice(0, 4);
            } catch (error) {
                console.error('Error loading vehicle albums:', error);
                this.albumPreview = [];
            } finally {
                this.albumsPreviewLoading = false;
            }
        },

        getAlbumCover: function (album) {
            if (!album || !album.photos || !album.photos.length) {
                return '';
            }

            var primary = album.photos.find(function (photo) {
                return photo.photoId === album.primaryPhotoId || photo.isPrimary;
            });

            return (primary || album.photos[0]).url || '';
        },

        getAlbumUrl: function (album) {
            if (!album) {
                return '#';
            }

            return this.appendOrganizationId('/vehicle/' + vehicleId + '/albums/' + album.albumId);
        },

        appendOrganizationId: function (url) {
            if (!this.vehicle.organizationId) {
                return url;
            }

            var separator = url.indexOf('?') >= 0 ? '&' : '?';
            return url + separator + 'organizationId=' + encodeURIComponent(this.vehicle.organizationId);
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
            var message = fallbackMessage || vehicleText.cardErrorDownloadReport;

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

        getMaintenanceCreateUrl: function () {
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/maintenance/create');
        },

        getVehicleJournalUrl: function () {
            var url = this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/journal');
            var returnUrl = window.location.pathname + window.location.search;
            return url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(returnUrl);
        },

        getMaintenanceDetailsUrl: function (maintenance) {
            return this.appendOrganizationId('/maintenance/' + maintenance.id);
        },

        getMaintenanceUpdateUrl: function (maintenance) {
            return this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/maintenance/' + maintenance.id + '/update');
        },
        getDefaultCounterDate: function () {
            return moment().format('YYYY-MM-DD');
        },
        resetPersonalCounterForm: function () {
            this.personalCounterLookup = null;
            this.personalCounterLookupLoading = false;
            this.personalCounterSaveInProgress = false;
            this.personalCounterForm = {
                entryId: '',
                recordedAt: this.getDefaultCounterDate(),
                value: this.vehicle.currentMileage !== null && this.vehicle.currentMileage !== undefined ? this.vehicle.currentMileage : null,
                comment: ''
            };
        },
        openPersonalCounterModal: function () {
            if (this.vehicle.organizationId) {
                return;
            }

            this.resetPersonalCounterForm();

            if (this.personalCounterModal) {
                this.personalCounterModal.show();
            }

            this.loadPersonalCounterEntry();
        },
        handlePersonalCounterDateChange: function () {
            this.loadPersonalCounterEntry();
        },
        loadPersonalCounterEntry: function () {
            if (this.vehicle.organizationId || !this.personalCounterForm.recordedAt) {
                this.personalCounterLookup = null;
                return;
            }

            var _this = this;
            this.personalCounterLookupLoading = true;

            axios.get('/api/v1/vehicles/' + this.vehicle.id + '/counter-entry', {
                params: {
                    recordedAt: this.personalCounterForm.recordedAt,
                    _ts: Date.now()
                }
            })
                .then(function (response) {
                    _this.personalCounterLookup = response.data || null;

                    if (_this.personalCounterLookup && _this.personalCounterLookup.hasEntry) {
                        _this.personalCounterForm.entryId = _this.personalCounterLookup.sourceType === 'maintenance'
                            ? ''
                            : (_this.personalCounterLookup.entryId || '');
                        _this.personalCounterForm.value = _this.personalCounterLookup.value;
                        _this.personalCounterForm.comment = _this.personalCounterLookup.comment || '';
                        return;
                    }

                    _this.personalCounterForm.entryId = '';
                    _this.personalCounterForm.value = _this.vehicle.currentMileage !== null && _this.vehicle.currentMileage !== undefined
                        ? _this.vehicle.currentMileage
                        : null;
                    _this.personalCounterForm.comment = '';
                })
                .catch(function (error) {
                    _this.personalCounterLookup = null;
                    _this.personalCounterForm.entryId = '';
                    _this.personalCounterForm.value = _this.vehicle.currentMileage !== null && _this.vehicle.currentMileage !== undefined
                        ? _this.vehicle.currentMileage
                        : null;
                    _this.personalCounterForm.comment = '';
                    _this.handleApiError(error, vehicleText.cardErrorCheckCounterDate);
                })
                .finally(function () {
                    _this.personalCounterLookupLoading = false;
                });
        },
        savePersonalCounterEntry: function () {
            if (this.personalCounterSaveInProgress) {
                return;
            }

            if (!this.personalCounterForm.recordedAt) {
                this.showError(vehicleText.cardErrorCounterDateRequired);
                return;
            }

            if (this.personalCounterForm.value === null || this.personalCounterForm.value === undefined || this.personalCounterForm.value === '') {
                this.showError(vehicleText.cardErrorCounterValueRequired);
                return;
            }

            var value = Number(this.personalCounterForm.value);
            if (Number.isNaN(value) || value < 0) {
                this.showError(vehicleText.cardErrorCounterNegative);
                return;
            }

            var _this = this;
            this.personalCounterSaveInProgress = true;

            axios.post('/api/v1/vehicles/' + this.vehicle.id + '/counter-entry', {
                EntryId: this.personalCounterForm.entryId || null,
                VehicleId: this.vehicle.id,
                Value: value,
                RecordedAt: this.personalCounterForm.recordedAt,
                Comment: this.personalCounterForm.comment || null
            })
                .then(function () {
                    if (_this.personalCounterModal) {
                        _this.personalCounterModal.hide();
                    }

                    _this.loadVehicleData();
                })
                .catch(function (error) {
                    _this.handleApiError(error, vehicleText.cardErrorCounterSave);
                })
                .finally(function () {
                    _this.personalCounterSaveInProgress = false;
                });
        },
        refreshTooltips: function () {
            var _this = this;
            if (typeof window.initAppTooltips !== 'function') {
                return;
            }

            this.$nextTick(function () {
                var root = document.getElementById('vehicle-form');
                window.initAppTooltips(root || document);
            });
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
                    vm.organizationClientsEnabled = true;
                    return vm.organizationClients;
                })
                .catch(function (error) {
                    vm.organizationClients = [];
                    vm.organizationClientsEnabled = false;
                    if (!error.response || error.response.status !== 403) {
                        vm.handleApiError(error, vehicleText.cardErrorClients);
                        throw error;
                    }

                    return [];
                })
                .finally(function () {
                    vm.loadOrganizationClients = false;
                });
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
        changeVehicleKind: function () {
            this.selectedBrandId = null;
            this.models = [];
            this.vehicle.modelId = null;

            this.getBrandOptions();
        },
        changeMileageUnit: function () {
            if (this.mileageUnitWarningShown || !this.originalVehicleData) return;

            if (this.vehicle.mileageUnit !== this.originalVehicleData.mileageUnit) {
                this.mileageUnitWarningShown = true;
                this.showModal('#mileageUnitWarningModal');
            }
        },
        getVehicleKindText: function (kind) {
            var item = this.vehicleKindOptions.find(function (option) { return option.value === kind; });
            return item ? item.text : vehicleText.cardKindOther;
        },
        generateVehicleWord: async function (includePhotos = false) {
            if (!this.hasMaintenanceRecordsForExport) {
                this.showError(vehicleText.cardErrorMaintenanceRequired);
                return;
            }

            this.showModal('#loadingModal');

            try {
                var url = '/api/v1/documents/word/vehicle/' + this.vehicle.id;
                if (includePhotos) {
                    url += '?includePhotos=true';
                }
                url = this.appendOrganizationId(url);

                await this.downloadBlobFromUrl(url, 'vehicle-report.docx');
            } catch (error) {
                await this.handleDocumentDownloadError(error, vehicleText.cardErrorWord);
            } finally {
                this.hideModal('#loadingModal');
            }
        },
        generateVehiclePdf: async function (includePhotos = true) {
            if (!this.hasMaintenanceRecordsForExport) {
                this.showError(vehicleText.cardErrorMaintenanceRequired);
                return;
            }

            this.showModal('#loadingModal');

            try {
                var url = this.appendOrganizationId('/api/v1/documents/pdf/vehicle/' + this.vehicle.id + '?includePhotos=' + includePhotos);
                await this.downloadBlobFromUrl(url, 'vehicle-report.pdf');
            } catch (error) {
                await this.handleDocumentDownloadError(error, vehicleText.cardErrorPdf);
            } finally {
                this.hideModal('#loadingModal');
            }
        },


        loadVehicleData: async function () {
            try {
                var response = await axios.get('/api/v1/vehicles/' + this.vehicle.id, { params: this.getWorkspaceParams() });
                var data = response.data;

                // Копируем данные вручную вместо spread оператора
                this.vehicle.id = data.id;
                this.vehicle.kind = data.kind || 1;
                this.vehicle.mileageUnit = data.mileageUnit || 1;
                this.vehicle.currentMileage = data.currentMileage == null ? null : data.currentMileage;
                this.vehicle.currentMileageUpdatedAt = data.currentMileageUpdatedAt == null ? null : data.currentMileageUpdatedAt;
                this.vehicle.brandName = data.brandName;
                this.vehicle.modelName = data.modelName;
                this.vehicle.yearIssue = data.yearIssue;
                this.vehicle.name = data.name;
                this.vehicle.vin = data.vin || '';
                this.vehicle.registrationPlate = data.registrationPlate || '';
                this.vehicle.registrationCountryCode = data.registrationCountryCode || '';
                this.vehicle.bodyNumber = data.bodyNumber || '';
                this.vehicle.engineNumber = data.engineNumber || '';
                this.vehicle.color = data.color || '';
                this.vehicle.registrationCertificate = data.registrationCertificate || '';
                this.vehicle.commissioningDate = data.commissioningDate ? data.commissioningDate.split('T')[0] : '';
                this.vehicle.description = data.description;
                this.vehicle.photoId = data.photoId;
                this.vehicle.brandId = data.brandId;
                this.vehicle.modelId = data.modelId;
                this.vehicle.manualBrandName = data.manualBrandName;
                this.vehicle.manualModelName = data.manualModelName;
                this.vehicle.organizationId = data.organizationId || this.vehicle.organizationId;
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

                this.originalVehicleData = {
                    kind: data.kind || 1,
                    mileageUnit: data.mileageUnit || 1,
                    brandName: data.brandName,
                    modelName: data.modelName,
                    yearIssue: data.yearIssue,
                    name: data.name,
                    vin: data.vin || '',
                    registrationPlate: data.registrationPlate || '',
                    registrationCountryCode: data.registrationCountryCode || '',
                    bodyNumber: data.bodyNumber || '',
                    engineNumber: data.engineNumber || '',
                    color: data.color || '',
                    registrationCertificate: data.registrationCertificate || '',
                    commissioningDate: data.commissioningDate ? data.commissioningDate.split('T')[0] : '',
                    description: data.description,
                    organizationClientId: data.organizationClientId || null,
                    organizationClientName: data.organizationClientName || null,
                    photoId: data.photoId,
                    brandId: data.brandId,
                    modelId: data.modelId,
                    manualBrandName: data.manualBrandName,
                    manualModelName: data.manualModelName,
                    isManualEntry: data.isManualEntry === true,
                    photoUrl: this.vehicle.photoUrl
                };
                this.originalSelectedBrandId = data.brandId;

                await this.preloadVehiclePhoto();
                await this.loadVehiclePhotos();
                await this.loadFeatureAccess();
                await this.loadOrganizationClientOptions();

                await this.getBrandOptions();

                if (!this.isManualIdentity && this.catalogModeAvailable) {
                    this.getModelOptions(data.modelId);
                } else {
                    this.models = [];
                }
                this.loadMaintenanceHistory();
                this.loadMaintenanceAnalytics();
                this.loadCounterHistoryChartData();
            } catch (error) {
                this.handleApiError(error, vehicleText.cardErrorVehicleLoad);
            } finally {
                this.formLoad = false;
            }
        },

        loadMaintenanceAnalytics: function () {
            var _this = this;
            this.maintenanceAnalyticsLoaded = false;

            return axios.get('/api/v1/vehicles/' + this.vehicle.id + '/maintenance/analytics', {
                params: Object.assign({}, this.getWorkspaceParams(), {
                    _ts: Date.now()
                })
            })
                .then(function (response) {
                    _this.maintenanceAnalytics = response.data || [];
                })
                .catch(function (error) {
                    _this.maintenanceAnalytics = [];
                    _this.handleApiError(error, vehicleText.cardErrorAnalytics);
                })
                .finally(function () {
                    _this.maintenanceAnalyticsLoaded = true;
                    _this.refreshTooltips();
                });
        },
        toggleVehicleHistory: function () {
            this.vehicleHistoryVisible = !this.vehicleHistoryVisible;
            if (this.vehicleHistoryVisible && !this.vehicleHistoryLoaded) {
                this.loadVehicleHistory();
            }
        },
        loadVehicleHistory: function () {
            var _this = this;
            this.vehicleHistoryLoading = true;

            return axios.get('/api/v1/vehicles/' + this.vehicle.id + '/history', {
                params: Object.assign({}, this.getWorkspaceParams(), {
                    take: 150,
                    _ts: Date.now()
                })
            })
                .then(function (response) {
                    _this.vehicleHistory = response.data || [];
                    _this.vehicleHistoryLoaded = true;
                })
                .catch(function (error) {
                    _this.vehicleHistory = [];
                    _this.handleApiError(error, vehicleText.cardErrorJournal);
                })
                .finally(function () {
                    _this.vehicleHistoryLoading = false;
                });
        },
        getVehicleHistoryIcon: function (event) {
            var type = event && event.eventType ? event.eventType : '';
            if (type.indexOf('maintenance_') === 0) return 'fa-wrench';
            if (type.indexOf('counter_') === 0) return 'fa-tachometer';
            if (type.indexOf('defect_') === 0) return 'fa-exclamation-triangle';
            if (type.indexOf('vehicle_') === 0) return 'fa-car';
            return 'fa-history';
        },
        loadCounterHistoryChartData: function () {
            var _this = this;
            this.counterHistoryLoaded = false;

            return axios.get('/api/v1/vehicles/' + this.vehicle.id + '/counter-history', {
                params: {
                    take: 200,
                    _ts: Date.now()
                }
            })
                .then(function (response) {
                    _this.counterHistoryChartEntries = response.data || [];
                })
                .catch(function (error) {
                    _this.counterHistoryChartEntries = [];
                    _this.handleApiError(error, vehicleText.cardErrorCounterHistory);
                })
                .finally(function () {
                    _this.counterHistoryLoaded = true;
                    _this.refreshTooltips();
                });
        },

        loadMaintenanceHistory: function (loadMore) {
            if (loadMore === undefined) loadMore = false;

            if (this.pagination.isLoading) return;

            if (!loadMore) {
                this.pagination.page = 1;
                this.maintenanceHistory = [];
            }

            this.pagination.isLoading = true;
            var _this = this;

            axios.get('/api/v1/vehicles/' + this.vehicle.id + '/maintenance/' + this.pagination.page + '/' + this.pagination.pageSize, {
                params: {
                    organizationId: this.vehicle.organizationId || null,
                    sort: this.maintenanceFilters.sort,
                    type: this.maintenanceFilters.type === 'all' ? null : this.maintenanceFilters.type,
                    status: this.maintenanceFilters.status === 'all' ? null : this.maintenanceFilters.status
                }
            })
                .then(async function (response) {
                    var data = response.data;
                    if (loadMore) {
                        _this.maintenanceHistory = _this.maintenanceHistory.concat(data.items);
                    } else {
                        _this.maintenanceHistory = data.items;
                    }

                    await _this.preloadMaintenancePhotos();

                    _this.maintenanceTotalCount = data.totalCount;

                    _this.pagination.hasMore = data.hasMore;
                    _this.pagination.page++;
                })
                .catch(function (error) {
                    _this.handleApiError(error, vehicleText.cardErrorMaintenanceHistory);
                })
                .finally(function () {
                    _this.pagination.isLoading = false;
                    _this.initialMaintenanceLoaded = true;
                });
        },

        changeMaintenanceFilters: function () {
            this.expandedMaintenance = null;
            this.loadMaintenanceHistory(false);
        },

        resetMaintenanceFilters: function () {
            this.maintenanceFilters.sort = 'date_desc';
            this.maintenanceFilters.type = 'all';
            this.maintenanceFilters.status = 'all';
            this.showDeletedMaintenance = false;
            this.changeMaintenanceFilters();
        },

        toggleDeletedMaintenance: function () {
            this.expandedMaintenance = null;

            if (this.showDeletedMaintenance) {
                this.loadDeletedMaintenance(false);
            }
        },

        loadDeletedMaintenance: function (loadMore) {
            if (loadMore === undefined) loadMore = false;
            if (this.deletedPagination.isLoading) return;

            if (!loadMore) {
                this.deletedPagination.page = 1;
                this.deletedMaintenanceHistory = [];
            }

            var _this = this;
            this.deletedMaintenanceLoading = true;
            this.deletedPagination.isLoading = true;

            return axios.get('/api/v1/vehicles/' + this.vehicle.id + '/maintenance/deleted/' + this.deletedPagination.page + '/' + this.deletedPagination.pageSize, {
                params: this.getWorkspaceParams()
            })
                .then(function (response) {
                    var data = response.data || {};
                    var items = (data.items || []).map(function (item) {
                        item.isDeleted = true;
                        return item;
                    });

                    if (loadMore) {
                        _this.deletedMaintenanceHistory = _this.deletedMaintenanceHistory.concat(items);
                    } else {
                        _this.deletedMaintenanceHistory = items;
                    }

                    _this.deletedMaintenanceTotalCount = data.totalCount || 0;
                    _this.deletedPagination.hasMore = data.hasMore;
                    _this.deletedPagination.page++;
                })
                .catch(function (error) {
                    if (!loadMore) {
                        _this.deletedMaintenanceHistory = [];
                        _this.deletedMaintenanceTotalCount = 0;
                    }

                    _this.deletedPagination.hasMore = false;
                    _this.handleApiError(error, vehicleText.cardErrorDeletedMaintenance);
                })
                .finally(function () {
                    _this.deletedMaintenanceLoading = false;
                    _this.deletedPagination.isLoading = false;
                });
        },

        preloadMaintenancePhotos: async function () {
            var _this = this;
            for (var i = 0; i < this.maintenanceHistory.length; i++) {
                var m = this.maintenanceHistory[i];
                m.photoUrls = [];
                m.failedPhotoCount = 0;
                if (m.photoIds && m.photoIds.length > 0) {
                    for (var j = 0; j < m.photoIds.length; j++) {
                        var photoId = m.photoIds[j];
                        try {
                            var photoUrl = await _this.loadPhoto(photoId);
                            m.photoUrls.push(photoUrl);
                        } catch (error) {
                            console.error('Error loading photo:', photoId, error);
                            m.failedPhotoCount++;
                        }
                    }
                }
            }
        },

        loadMoreMaintenance: function () {
            if (this.showDeletedMaintenance) {
                this.loadDeletedMaintenance(true);
                return;
            }

            this.loadMaintenanceHistory(true);
        },

        openEditVehicleModal: function () {
            var url = this.appendOrganizationId('/vehicle/' + this.vehicle.id + '/edit');
            window.location.href = url + (url.indexOf('?') >= 0 ? '&' : '?') + 'returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
        },

        closeEditVehicleModal: function () {
            this.hideModal('#editVehicleModal');
        },

        cancelVehicleEdit: function () {
            if (this.originalVehicleData) {
                this.vehicle.kind = this.originalVehicleData.kind;
                this.vehicle.mileageUnit = this.originalVehicleData.mileageUnit || 1;
                this.vehicle.brandName = this.originalVehicleData.brandName;
                this.vehicle.modelName = this.originalVehicleData.modelName;
                this.vehicle.yearIssue = this.originalVehicleData.yearIssue;
                this.vehicle.name = this.originalVehicleData.name;
                this.vehicle.vin = this.originalVehicleData.vin || '';
                this.vehicle.registrationPlate = this.originalVehicleData.registrationPlate || '';
                this.vehicle.registrationCountryCode = this.originalVehicleData.registrationCountryCode || '';
                this.vehicle.bodyNumber = this.originalVehicleData.bodyNumber || '';
                this.vehicle.engineNumber = this.originalVehicleData.engineNumber || '';
                this.vehicle.color = this.originalVehicleData.color || '';
                this.vehicle.registrationCertificate = this.originalVehicleData.registrationCertificate || '';
                this.vehicle.commissioningDate = this.originalVehicleData.commissioningDate || '';
                this.vehicle.description = this.originalVehicleData.description;
                this.vehicle.organizationClientId = this.originalVehicleData.organizationClientId || null;
                this.vehicle.organizationClientName = this.originalVehicleData.organizationClientName || null;
                this.vehicle.photoId = this.originalVehicleData.photoId;
                this.vehicle.brandId = this.originalVehicleData.brandId;
                this.vehicle.modelId = this.originalVehicleData.modelId;
                this.vehicle.manualBrandName = this.originalVehicleData.manualBrandName;
                this.vehicle.manualModelName = this.originalVehicleData.manualModelName;
                this.vehicle.isManualEntry = this.originalVehicleData.isManualEntry;
                this.vehicle.photoUrl = this.originalVehicleData.photoUrl;

                this.selectedBrandId = this.originalSelectedBrandId;
                this.isManualIdentity = this.originalVehicleData.isManualEntry === true;
                this.manualIdentityDraft = {
                    brandName: this.originalVehicleData.manualBrandName || this.originalVehicleData.brandName || null,
                    modelName: this.originalVehicleData.manualModelName || this.originalVehicleData.modelName || null
                };
                this.catalogIdentityDraft = {
                    brandId: this.originalSelectedBrandId,
                    modelId: this.originalVehicleData.modelId
                };

                if (!this.isManualIdentity && this.selectedBrandId) {
                    this.getModelOptions(this.vehicle.modelId);
                } else {
                    this.models = [];
                }
            }
            this.closeEditVehicleModal();
        },

        saveVehicle: function () {
            if (window.OpenCRMVehiclePlate && !window.OpenCRMVehiclePlate.validate()) {
                return;
            }

            this.saveModal = true;
            var _this = this;

            axios.put('/api/v1/vehicles/' + this.vehicle.id, {
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
                RegistrationCountryCode: this.vehicle.registrationCountryCode,
                AllowOtherRegistrationPlateFormat: this.vehicle.allowOtherRegistrationPlateFormat === true,
                BodyNumber: this.vehicle.bodyNumber,
                EngineNumber: this.vehicle.engineNumber,
                Color: this.vehicle.color,
                RegistrationCertificate: this.vehicle.registrationCertificate,
                CommissioningDate: this.vehicle.commissioningDate || null,
                Description: this.vehicle.description,
                OrganizationClientId: this.vehicle.organizationId ? (this.vehicle.organizationClientId || null) : null
            }, {
                params: this.getWorkspaceParams()
            })
                .then(function (response) {
                    _this.originalVehicleData = {
                        kind: _this.vehicle.kind,
                        mileageUnit: _this.vehicle.mileageUnit || 1,
                        brandName: _this.vehicle.brandName,
                        modelName: _this.vehicle.modelName,
                        yearIssue: _this.vehicle.yearIssue,
                        name: _this.vehicle.name,
                        vin: _this.vehicle.vin,
                        registrationPlate: _this.vehicle.registrationPlate,
                        registrationCountryCode: _this.vehicle.registrationCountryCode,
                        bodyNumber: _this.vehicle.bodyNumber,
                        engineNumber: _this.vehicle.engineNumber,
                        color: _this.vehicle.color,
                        registrationCertificate: _this.vehicle.registrationCertificate,
                        commissioningDate: _this.vehicle.commissioningDate,
                        description: _this.vehicle.description,
                        currentMileage: _this.vehicle.currentMileage,
                        currentMileageUpdatedAt: _this.vehicle.currentMileageUpdatedAt,
                        organizationClientId: _this.vehicle.organizationClientId || null,
                        organizationClientName: (function () {
                            var currentClient = _this.organizationClients.find(function (client) {
                                return client.id === _this.vehicle.organizationClientId;
                            });
                            return currentClient ? currentClient.name : null;
                        })(),
                        photoId: _this.vehicle.photoId,
                        brandId: _this.vehicle.brandId,
                        modelId: _this.vehicle.modelId,
                        manualBrandName: _this.vehicle.manualBrandName,
                        manualModelName: _this.vehicle.manualModelName,
                        isManualEntry: _this.isManualIdentity,
                        photoUrl: _this.vehicle.photoUrl
                    };
                    _this.originalSelectedBrandId = _this.selectedBrandId;
                    _this.closeEditVehicleModal();
                    _this.loadVehicleData();
                })
                .catch(function (error) {
                    _this.handleApiError(error, vehicleText.cardErrorVehicleUpdate);
                })
                .finally(function () {
                    _this.saveModal = false;
                });
        },

        changeYearIssue: function () {
            var currentYear = new Date().getFullYear();
            if (this.vehicle.yearIssue && this.vehicle.yearIssue > currentYear) {
                this.vehicle.yearIssue = currentYear;
            }
            if (this.vehicle.yearIssue == 0 || this.vehicle.yearIssue < 0) {
                this.vehicle.yearIssue = null;
            }
        },

        getModelOptions: function (modelId) {
            if (modelId === undefined) modelId = null;

            this.vehicle.modelId = modelId;
            this.models = [];
            if (!this.selectedBrandId) return;

            this.loadModels = true;
            var _this = this;

            axios.get('/api/v1/catalog/models/options', {
                params: {
                    brandId: this.selectedBrandId,
                    kind: this.vehicle ? this.vehicle.kind : null
                }
            })
                .then(function (response) {
                    _this.models = response.data;
                })
                .catch(function (error) {
                    _this.handleApiError(error, vehicleText.cardErrorModels);
                })
                .finally(function () {
                    _this.loadModels = false;
                });
        },

        getBrandOptions: function () {
            var _this = this;

            return axios.get('/api/v1/catalog/brands/options', {
                params: {
                    kind: _this.vehicle ? _this.vehicle.kind : null
                }
            })
                .then(function (response) {
                    _this.brands = response.data;
                    _this.applyCatalogModeAvailability();
                })
                .catch(function (error) {
                    _this.brands = [];
                    _this.applyCatalogModeAvailability();
                    _this.handleApiError(error, vehicleText.cardErrorBrands);
                });
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
        },

        uploadVehiclePhoto: async function (event) {
            var file = event.target.files[0];
            if (!file) return;

            var originalIsUploading = this.vehicle.isUploading;
            var originalPhotoId = this.vehicle.photoId;
            var originalPhotoUrl = this.vehicle.photoUrl;
            var originalPhotoLoadFailed = this.vehicle.photoLoadFailed;

            this.vehicle.isUploading = true;
            this.vehicle.photoLoadFailed = false;

            var bodyFormData = new FormData();
            bodyFormData.append('formFile', file);

            var _this = this;

            try {
                var response = await axios.post('/api/v1/media/vehicles/' + this.vehicle.id + '/upload-photo', bodyFormData, {
                    params: this.getWorkspaceParams()
                });

                this.vehicle.photoId = response.data;
                await this.preloadVehiclePhoto();

                if (this.originalVehicleData) {
                    this.originalVehicleData.photoId = response.data;
                    this.originalVehicleData.photoUrl = this.vehicle.photoUrl;
                }
            } catch (error) {
                this.vehicle.isUploading = originalIsUploading;
                this.vehicle.photoId = originalPhotoId;
                this.vehicle.photoUrl = originalPhotoUrl;
                this.vehicle.photoLoadFailed = originalPhotoLoadFailed;
                this.handleApiError(error, vehicleText.cardErrorPhotoUpload);
            } finally {
                this.vehicle.isUploading = false;
                event.target.value = '';
            }
        },

        openDeletePhotoModal: function () {
            this.showModal('#deletePhotoModal');
        },

        deleteVehiclePhoto: async function () {
            var _this = this;

            try {
                await axios.delete('/api/v1/media/vehicles/' + this.vehicle.id + '/delete-photo', {
                    params: this.getWorkspaceParams()
                });

                this.vehicle.photoId = '';
                this.vehicle.photoUrl = '';
                this.vehicle.photoLoadFailed = false;

                if (this.originalVehicleData) {
                    this.originalVehicleData.photoId = '';
                    this.originalVehicleData.photoUrl = '';
                }

                this.hideModal('#deletePhotoModal');
            } catch (error) {
                this.handleApiError(error, vehicleText.cardErrorPhotoDelete);
            }
        },

        deletePhoto: function () {
            this.deleteVehiclePhoto();
        },

        openDeleteVehicleModal: function () {
            this.showModal('#deleteVehicleModal');
        },

        openTransferVehicleModal: function () {
            this.selectedTransferOrganizationId = '';
            this.showModal('#transferVehicleModal');
        },

        confirmTransferVehicle: async function () {
            if (!this.selectedTransferOrganizationId) {
                return;
            }

            this.transferSaveInProgress = true;

            try {
                await axios.post('/api/v1/vehicles/' + this.vehicle.id + '/transfer-to-organization', {
                    organizationId: this.selectedTransferOrganizationId
                });

                this.hideModal('#transferVehicleModal');
                window.location.href = '/organizations/' + this.selectedTransferOrganizationId;
            } catch (error) {
                this.handleApiError(error, vehicleText.cardErrorTransfer);
            } finally {
                this.transferSaveInProgress = false;
            }
        },

        getVehicleListUrl: function () {
            return this.vehicle.organizationId
                ? '/organizations/' + this.vehicle.organizationId
                : '/vehicles';
        },

        deleteCurrentVehicle: async function () {
            this.saveModal = true;

            try {
                await axios.delete('/api/v1/vehicles/' + this.vehicle.id, {
                    params: this.getWorkspaceParams()
                });

                this.hideModal('#deleteVehicleModal');
                window.location.href = this.getVehicleListUrl();
            } catch (error) {
                this.handleApiError(error, vehicleText.cardErrorVehicleDelete);
            } finally {
                this.saveModal = false;
            }
        },

        toggleMaintenance: function (index) {
            this.expandedMaintenance = this.expandedMaintenance === index ? null : index;
        },

        getMaintenanceTypeClass: function (type) {
            var typeClasses = {
                1: 'bg-primary',
                2: 'bg-success',
                3: 'bg-info',
                100: 'bg-secondary'
            };
            return typeClasses[type] || 'bg-secondary';
        },

        getMaintenanceStatusText: function (status) {
            var statusTexts = {
                1: vehicleText.cardStatusPlanned,
                2: vehicleText.cardStatusInProgress,
                3: vehicleText.cardStatusCompleted,
                4: vehicleText.cardStatusCancelled
            };
            return statusTexts[status] || vehicleText.cardStatusUnknown;
        },

        getMaintenanceDateDay: function (dateString) {
            if (!dateString) {
                return null;
            }

            var value = String(dateString);
            if (value.indexOf('T') >= 0) {
                var dateTime = moment(value);
                return dateTime.isValid() ? dateTime.startOf('day') : null;
            }

            var datePart = value.length >= 10 ? value.substring(0, 10) : value;
            var parsedDate = moment(datePart, 'YYYY-MM-DD', true);

            return parsedDate.isValid()
                ? parsedDate
                : moment(dateString).startOf('day');
        },

        isMaintenanceOverdue: function (maintenance) {
            if (!maintenance || maintenance.status === 3 || maintenance.status === 4) {
                return false;
            }

            var targetDate = maintenance.plannedDate || maintenance.date;
            if (!targetDate) {
                return false;
            }

            var targetDay = this.getMaintenanceDateDay(targetDate);
            return targetDay ? targetDay.format('YYYY-MM-DD') < window.OpenCRMRegional.today() : false;
        },

        getMaintenanceStatusDisplayText: function (maintenance) {
            return this.getMaintenanceStatusText(maintenance.status);
        },

        getMaintenanceStatusClass: function (status) {
            if (typeof status === 'object' && status !== null) {
                status = status.status;
            }

            var statusClasses = {
                1: 'text-bg-warning',
                2: 'text-bg-primary',
                3: 'text-bg-success',
                4: 'text-bg-secondary'
            };
            return statusClasses[status] || 'text-bg-light border';
        },

        getMaintenancePrimaryDateLabel: function (maintenance) {
            return maintenance.status === 3 ? vehicleText.cardMaintenanceCompletionDate : vehicleText.cardMaintenancePlannedDate;
        },

        getMaintenancePrimaryDateValue: function (maintenance) {
            var value = maintenance.status === 3
                ? (maintenance.completedDate || maintenance.date)
                : (maintenance.plannedDate || maintenance.date);

            return value ? this.formatDate(value) : '—';
        },

        getMaintenanceTypeText: function (type) {
            var typeTexts = {
                1: vehicleText.cardMaintenanceRepair,
                2: vehicleText.cardMaintenancePlannedService,
                3: vehicleText.cardMaintenanceFuel,
                100: vehicleText.cardOther
            };
            return typeTexts[type] || vehicleText.cardChartMaintenance;
        },

        formatDate: function (dateString) {
            return window.OpenCRMRegional.formatDate(dateString);
        },

        formatCounterValue: function (value, mileageUnit) {
            if (value === null || value === undefined) return vehicleText.cardUnspecified;

            return Number(value).toLocaleString(window.OpenCRMRegional.culture) + ' ' + (mileageUnit === 2 ? vehicleText.cardUnitShortHours : vehicleText.cardUnitShortKilometers);
        },

        formatPrice: function (price, currencyCode) {
            const selectedCurrency = currencyCode || this.maintenance?.currencyCode || this.selectedMaintenance?.currencyCode || this.regionalCurrencyCode;

            return window.OpenCRMRegional.formatMoney(price, selectedCurrency);
        },

        formatCompactPrice: function (price) {
            if (typeof price !== 'number' || price <= 0) return '0';

            if (price >= 1000000) {
                return (price / 1000000).toLocaleString(window.OpenCRMRegional.culture, {
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 1
                }) + ' ' + vehicleText.cardPriceMillion;
            }

            if (price >= 1000) {
                return Math.round(price / 1000).toLocaleString(window.OpenCRMRegional.culture) + ' ' + vehicleText.cardPriceThousand;
            }

            return Math.round(price).toLocaleString(window.OpenCRMRegional.culture);
        },

        openPhotoGallery: function (photos, index) {
            this.currentPhotos = photos;
            this.currentPhotoIndex = index;
            this.showModal('#photoModal');
        },

        deleteMaintenance: function (maintenance) {
            this.selectedMaintenance = maintenance;
            this.selectedMaintenanceDate = this.formatDate(maintenance.date);
            this.showModal('#deleteMaintenanceModal');
        },

        confirmDeleteMaintenance: function () {
            if (!this.selectedMaintenance) return;

            var _this = this;
            var deletedIndex = this.maintenanceHistory.findIndex(function (m) {
                return m.id === _this.selectedMaintenance.id;
            });

            axios.delete('/api/v1/maintenance/' + this.selectedMaintenance.id, {
                params: this.getWorkspaceParams()
            })
                .then(function (response) {
                    _this.maintenanceHistory = _this.maintenanceHistory.filter(function (m) {
                        return m.id !== _this.selectedMaintenance.id;
                    });
                    _this.maintenanceTotalCount--;

                    if (_this.expandedMaintenance === deletedIndex) {
                        _this.expandedMaintenance = null;
                    } else if (_this.expandedMaintenance !== null && _this.expandedMaintenance > deletedIndex) {
                        _this.expandedMaintenance--;
                    }

                    _this.hideModal('#deleteMaintenanceModal');
                    _this.selectedMaintenance = null;
                    _this.selectedMaintenanceDate = '';
                    _this.loadDeletedMaintenance(false);
                    _this.loadMaintenanceAnalytics();
                })
                .catch(function (error) {
                    _this.handleApiError(error, vehicleText.cardErrorMaintenanceDelete);
                    _this.hideModal('#deleteMaintenanceModal');
                });
        }
    },
    mounted: function () {
        window.OpenCRMRegional.initializeLegacyVm(this);
        this.loadVehicleData();
        this.loadAlbumPreview();
        this.getBrandOptions();
        this.loadOrganizationClientOptions();

        var _this = this;
        var personalCounterModalElement = document.getElementById('personalCounterModal');
        this.personalCounterModal = personalCounterModalElement ? new bootstrap.Modal(personalCounterModalElement) : null;

        $('#editVehicleModal').on('hidden.bs.modal', function () {
            _this.cancelVehicleEdit();
        });

        $('#photoModal').on('hidden.bs.modal', function () {
            _this.currentPhotos = [];
            _this.currentPhotoIndex = 0;
        });

        $('#personalCounterModal').on('hidden.bs.modal', function () {
            _this.resetPersonalCounterForm();
        });

        this.refreshTooltips();
    },
    beforeDestroy: function () {
        $('#editVehicleModal').off('hidden.bs.modal');
        $('#photoModal').off('hidden.bs.modal');
        $('#personalCounterModal').off('hidden.bs.modal');
    }
});







