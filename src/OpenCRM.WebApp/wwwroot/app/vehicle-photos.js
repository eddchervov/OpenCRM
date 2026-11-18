var vehiclePhotoMessages = document.getElementById('vehicle-photo-manager').dataset;

new Vue({
    el: '#vehicle-photo-manager',
    data: function () {
        return {
            photos: [],
            photosLoading: false,
            photosUploading: false,
            currentPhotoIndex: 0,
            photoDownloading: false,
            deleteSaving: false,
            pendingDeletePhoto: null,
            photoOpenAria: vehiclePhotoMessages.openPhotoAria,
            photoAlt: vehiclePhotoMessages.photoAlt,
            primaryTitle: vehiclePhotoMessages.primaryTitle,
            primarySetTitle: vehiclePhotoMessages.primarySetTitle,
            coverLabel: vehiclePhotoMessages.coverLabel,
            setCoverLabel: vehiclePhotoMessages.setCoverLabel,
            errorMessage: ''
        };
    },
    computed: {
        currentViewerPhoto: function () {
            if (!this.photos || !this.photos.length) {
                return null;
            }

            return this.photos[this.currentPhotoIndex] || this.photos[0];
        },
        hasMultiplePhotos: function () {
            return this.photos && this.photos.length > 1;
        }
    },
    methods: {
        getWorkspaceParams: function () {
            return vehicleOrganizationId ? { organizationId: vehicleOrganizationId } : {};
        },
        loadPhotos: async function () {
            this.photosLoading = true;
            try {
                var response = await axios.get('/api/v1/media/vehicles/' + vehicleId + '/photos', { params: this.getWorkspaceParams() });
                this.photos = response.data || [];
            } catch (error) {
                this.handleApiError(error, vehiclePhotoMessages.errorLoad);
            } finally {
                this.photosLoading = false;
            }
        },
        uploadVehiclePhotos: async function (event) {
            var files = Array.prototype.slice.call(event.target.files || []);
            if (!files.length) return;

            if (this.photos.length + files.length > 10) {
                this.showError(vehiclePhotoMessages.errorMaxCount);
                event.target.value = '';
                return;
            }

            this.photosUploading = true;
            try {
                var uploaded = await this.uploadFiles('/api/v1/media/vehicles/' + vehicleId + '/photos', files);
                if (uploaded) {
                    await this.loadPhotos();
                }
            } finally {
                this.photosUploading = false;
                event.target.value = '';
            }
        },
        uploadFiles: async function (url, files) {
            var total = files.reduce(function (sum, file) { return sum + file.size; }, 0);
            if (total > 100 * 1024 * 1024) {
                this.showError(vehiclePhotoMessages.errorMaxSize);
                return false;
            }

            var form = new FormData();
            files.forEach(function (file) { form.append('formFiles', file); });

            try {
                await axios.post(url, form, { params: this.getWorkspaceParams() });
                return true;
            } catch (error) {
                this.handleApiError(error, vehiclePhotoMessages.errorUpload);
                return false;
            }
        },
        setPrimaryPhoto: async function (photo) {
            try {
                await axios.post('/api/v1/media/vehicles/' + vehicleId + '/photos/' + photo.photoId + '/primary', null, { params: this.getWorkspaceParams() });
                await this.loadPhotos();
            } catch (error) {
                this.handleApiError(error, vehiclePhotoMessages.errorPrimary);
            }
        },
        openPhotoViewer: function (index) {
            if (!this.photos || !this.photos.length) return;

            this.currentPhotoIndex = index || 0;
            this.showModal('#vehiclePhotoViewerModal');
        },
        showPreviousViewerPhoto: function () {
            if (!this.hasMultiplePhotos) return;

            this.currentPhotoIndex = (this.currentPhotoIndex - 1 + this.photos.length) % this.photos.length;
        },
        showNextViewerPhoto: function () {
            if (!this.hasMultiplePhotos) return;

            this.currentPhotoIndex = (this.currentPhotoIndex + 1) % this.photos.length;
        },
        downloadCurrentPhoto: async function () {
            if (!this.currentViewerPhoto || !this.currentViewerPhoto.url) return;

            var photo = this.currentViewerPhoto;
            var fileName = photo.originalFileName || ('vehicle-photo-' + (this.currentPhotoIndex + 1) + '.jpg');
            this.photoDownloading = true;

            try {
                var response = await axios.get(photo.url, { responseType: 'blob' });
                var blobUrl = window.URL.createObjectURL(response.data);
                var link = document.createElement('a');

                link.href = blobUrl;
                link.download = fileName;
                document.body.appendChild(link);
                link.click();
                link.remove();

                window.setTimeout(function () {
                    window.URL.revokeObjectURL(blobUrl);
                }, 1000);
            } catch (error) {
                window.open(photo.url, '_blank');
            } finally {
                this.photoDownloading = false;
            }
        },
        openDeletePhotoModal: function (photo) {
            if (!photo) return;

            this.pendingDeletePhoto = photo;
            this.showModal('#vehiclePhotoDeleteConfirmModal');
        },
        confirmDeletePhoto: async function () {
            if (!this.pendingDeletePhoto) return;

            var photo = this.pendingDeletePhoto;
            this.deleteSaving = true;
            try {
                await axios.delete('/api/v1/media/vehicles/' + vehicleId + '/photos/' + photo.photoId, { params: this.getWorkspaceParams() });
                this.hideModal('#vehiclePhotoDeleteConfirmModal');
                this.pendingDeletePhoto = null;
                await this.loadPhotos();
            } catch (error) {
                this.handleApiError(error, vehiclePhotoMessages.errorDelete);
            } finally {
                this.deleteSaving = false;
            }
        },
        handleApiError: function (error, defaultMessage) {
            var message = error.response && error.response.data && error.response.data.message
                ? error.response.data.message
                : defaultMessage;
            this.showError(message);
        },
        showError: function (message) {
            this.errorMessage = message;
            this.showModal('#photoManagerErrorModal');
        },
        showModal: function (selector) {
            var element = document.querySelector(selector);
            if (element && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(element).show();
            } else if (this.errorMessage) {
                alert(this.errorMessage);
            }
        },
        hideModal: function (selector) {
            var element = document.querySelector(selector);
            if (element && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(element).hide();
            }
        }
    },
    mounted: function () {
        this.loadPhotos();
    }
});
