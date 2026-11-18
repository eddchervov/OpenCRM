var vehicleAlbumMessages = document.getElementById('vehicle-album-detail').dataset;

new Vue({
    el: '#vehicle-album-detail',
    data: function () {
        return {
            album: null,
            albumLoading: false,
            albumPhotoUploading: false,
            currentPhotoIndex: 0,
            photoDownloading: false,
            deleteSaving: false,
            pendingDeleteType: '',
            pendingDeletePhoto: null,
            photoOpenAria: vehicleAlbumMessages.openPhotoAria,
            photoAlt: vehicleAlbumMessages.photoAlt,
            primaryTitle: vehicleAlbumMessages.primaryTitle,
            primarySetTitle: vehicleAlbumMessages.primarySetTitle,
            coverLabel: vehicleAlbumMessages.coverLabel,
            setCoverLabel: vehicleAlbumMessages.setCoverLabel,
            errorMessage: ''
        };
    },
    computed: {
        albumTitle: function () {
            return this.album && this.album.title ? this.album.title : vehicleAlbumMessages.albumFallbackTitle;
        },
        currentViewerPhoto: function () {
            if (!this.album || !this.album.photos || !this.album.photos.length) {
                return null;
            }

            return this.album.photos[this.currentPhotoIndex] || this.album.photos[0];
        },
        hasMultiplePhotos: function () {
            return !!(this.album && this.album.photos && this.album.photos.length > 1);
        },
        deleteModalTitle: function () {
            return this.pendingDeleteType === 'album' ? vehicleAlbumMessages.deleteTitleAlbum : vehicleAlbumMessages.deleteTitlePhoto;
        },
        deleteModalText: function () {
            return this.pendingDeleteType === 'album'
                ? vehicleAlbumMessages.deleteTextAlbum
                : vehicleAlbumMessages.deleteTextPhoto;
        }
    },
    methods: {
        getWorkspaceParams: function () {
            return vehicleOrganizationId ? { organizationId: vehicleOrganizationId } : {};
        },
        getAlbumsUrl: function () {
            var url = '/vehicle/' + vehicleId + '/albums';
            if (vehicleOrganizationId) {
                url += '?organizationId=' + encodeURIComponent(vehicleOrganizationId);
            }

            return url;
        },
        loadAlbum: async function () {
            this.albumLoading = true;
            try {
                var response = await axios.get('/api/v1/media/vehicles/' + vehicleId + '/albums', { params: this.getWorkspaceParams() });
                var albums = response.data.albums || [];
                var expectedAlbumId = (vehicleAlbumId || '').toLowerCase();
                this.album = albums.find(function (album) {
                    return (album.albumId || '').toLowerCase() === expectedAlbumId;
                }) || null;
                if (this.album && !this.album.photos) {
                    this.album.photos = [];
                }
            } catch (error) {
                this.handleApiError(error, vehicleAlbumMessages.errorLoad);
            } finally {
                this.albumLoading = false;
            }
        },
        uploadAlbumPhotos: async function (event) {
            var files = Array.prototype.slice.call(event.target.files || []);
            if (!files.length || !this.album) return;

            this.albumPhotoUploading = true;
            try {
                var uploaded = await this.uploadFiles('/api/v1/media/vehicles/' + vehicleId + '/albums/' + this.album.albumId + '/photos', files);
                if (uploaded) {
                    await this.loadAlbum();
                }
            } finally {
                this.albumPhotoUploading = false;
                event.target.value = '';
            }
        },
        uploadFiles: async function (url, files) {
            var total = files.reduce(function (sum, file) { return sum + file.size; }, 0);
            if (total > 100 * 1024 * 1024) {
                this.showError(vehicleAlbumMessages.errorMaxSize);
                return false;
            }

            var form = new FormData();
            files.forEach(function (file) { form.append('formFiles', file); });

            try {
                await axios.post(url, form, { params: this.getWorkspaceParams() });
                return true;
            } catch (error) {
                this.handleApiError(error, vehicleAlbumMessages.errorUpload);
                return false;
            }
        },
        openDeleteAlbumModal: function () {
            if (!this.album) return;

            this.pendingDeleteType = 'album';
            this.pendingDeletePhoto = null;
            this.showModal('#albumDeleteConfirmModal');
        },
        openDeleteAlbumPhotoModal: function (photo) {
            if (!this.album || !photo) return;

            this.pendingDeleteType = 'photo';
            this.pendingDeletePhoto = photo;
            this.showModal('#albumDeleteConfirmModal');
        },
        confirmDelete: async function () {
            if (this.pendingDeleteType === 'album') {
                await this.deleteAlbum();
                return;
            }

            if (this.pendingDeleteType === 'photo') {
                await this.deleteAlbumPhoto();
            }
        },
        deleteAlbum: async function () {
            if (!this.album) return;

            this.deleteSaving = true;
            try {
                await axios.delete('/api/v1/media/vehicles/' + vehicleId + '/albums/' + this.album.albumId, { params: this.getWorkspaceParams() });
                window.location.href = this.getAlbumsUrl();
            } catch (error) {
                this.handleApiError(error, vehicleAlbumMessages.errorDelete);
            } finally {
                this.deleteSaving = false;
            }
        },
        setPrimaryAlbumPhoto: async function (photo) {
            if (!this.album || !photo) return;

            try {
                await axios.post('/api/v1/media/vehicles/' + vehicleId + '/albums/' + this.album.albumId + '/photos/' + photo.photoId + '/primary', null, { params: this.getWorkspaceParams() });
                await this.loadAlbum();
            } catch (error) {
                this.handleApiError(error, vehicleAlbumMessages.errorPrimary);
            }
        },
        openPhotoViewer: function (index) {
            if (!this.album || !this.album.photos || !this.album.photos.length) return;

            this.currentPhotoIndex = index || 0;
            this.showModal('#albumPhotoViewerModal');
        },
        showPreviousViewerPhoto: function () {
            if (!this.hasMultiplePhotos) return;

            this.currentPhotoIndex = (this.currentPhotoIndex - 1 + this.album.photos.length) % this.album.photos.length;
        },
        showNextViewerPhoto: function () {
            if (!this.hasMultiplePhotos) return;

            this.currentPhotoIndex = (this.currentPhotoIndex + 1) % this.album.photos.length;
        },
        downloadCurrentPhoto: async function () {
            if (!this.currentViewerPhoto || !this.currentViewerPhoto.url) return;

            var photo = this.currentViewerPhoto;
            var fileName = photo.originalFileName || ('album-photo-' + (this.currentPhotoIndex + 1) + '.jpg');
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
        deleteAlbumPhoto: async function () {
            if (!this.album || !this.pendingDeletePhoto) return;

            var photo = this.pendingDeletePhoto;
            this.deleteSaving = true;
            try {
                await axios.delete('/api/v1/media/vehicles/' + vehicleId + '/albums/' + this.album.albumId + '/photos/' + photo.photoId, { params: this.getWorkspaceParams() });
                this.hideModal('#albumDeleteConfirmModal');
                this.pendingDeleteType = '';
                this.pendingDeletePhoto = null;
                await this.loadAlbum();
            } catch (error) {
                this.handleApiError(error, vehicleAlbumMessages.errorDeletePhoto);
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
            var element = document.getElementById('photoManagerErrorModal');
            if (element && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(element).show();
            } else {
                alert(message);
            }
        },
        showModal: function (selector) {
            var element = document.querySelector(selector);
            if (element && window.bootstrap) {
                bootstrap.Modal.getOrCreateInstance(element).show();
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
        this.loadAlbum();
    }
});
