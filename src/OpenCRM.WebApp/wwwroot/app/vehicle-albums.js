new Vue({
    el: '#vehicle-album-manager',
    data: function () {
        return {
            albums: [],
            albumLimit: null,
            albumsLoading: false,
            albumSaving: false,
            newAlbumTitle: '',
            errorMessage: ''
        };
    },
    computed: {
        canSubmitAlbumTitle: function () {
            return this.newAlbumTitle.length > 0 && this.canCreateAlbum;
        },
        canCreateAlbum: function () {
            return !this.albumLimit || this.albumLimit.canCreateMore;
        }
    },
    methods: {
        getWorkspaceParams: function () {
            return vehicleOrganizationId ? { organizationId: vehicleOrganizationId } : {};
        },
        loadAlbums: async function () {
            this.albumsLoading = true;
            try {
                var response = await axios.get('/api/v1/media/vehicles/' + vehicleId + '/albums', { params: this.getWorkspaceParams() });
                this.albums = response.data.albums || [];
                this.albumLimit = response.data.limit || null;
            } catch (error) {
                this.handleApiError(error, 'Не удалось загрузить альбомы');
            } finally {
                this.albumsLoading = false;
            }
        },
        createAlbum: async function () {
            if (!this.canCreateAlbum) {
                this.showError('На бесплатном тарифе и в демо можно создать только 1 альбом.');
                return;
            }

            this.albumSaving = true;
            try {
                await axios.post('/api/v1/media/vehicles/' + vehicleId + '/albums', { title: this.newAlbumTitle }, { params: this.getWorkspaceParams() });
                this.newAlbumTitle = '';
                await this.loadAlbums();
            } catch (error) {
                this.handleApiError(error, 'Не удалось создать альбом');
            } finally {
                this.albumSaving = false;
            }
        },
        getAlbumCover: function (album) {
            if (!album || !album.photos || !album.photos.length) {
                return '';
            }

            var primary = album.photos.find(function (photo) {
                return photo.isPrimary;
            });

            return (primary || album.photos[0]).url || '';
        },
        getAlbumUrl: function (album) {
            if (!album) {
                return '#';
            }

            var url = '/vehicle/' + vehicleId + '/albums/' + album.albumId;
            if (vehicleOrganizationId) {
                url += '?organizationId=' + encodeURIComponent(vehicleOrganizationId);
            }

            return url;
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
        }
    },
    mounted: function () {
        this.loadAlbums();
    }
});
