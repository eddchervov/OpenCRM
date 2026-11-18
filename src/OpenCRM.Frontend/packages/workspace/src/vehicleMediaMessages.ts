import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    vehicleMedia: {
      album: 'Альбом', photosTitle: 'Управление фото техники', albumsTitle: 'Альбомы техники', vehicle: 'Техника',
      delete: 'Удалить', back: 'Назад', uploadPhoto: 'Загрузить фото', loadingPhotos: 'Загружаем фото…', uploadingPhotos: 'Загружаем фотографии…',
      proAlbums: 'Фотоальбомы доступны в PRO', carPhotos: 'Фото автомобиля', noVehiclePhotos: 'Фото техники пока нет.',
      photo: 'Фото техники', mainPhoto: 'Главное', setMainPhoto: 'Сделать главным', albumTitle: 'Название альбома',
      albumTitleExample: 'Например, до и после полировки', create: 'Создать', noAlbums: 'Альбомов пока нет.', albumPlaceholder: 'Альбом',
      photoCountOne: '{count} фото', photoCountMany: '{count} фото', noAlbumPhotos: 'В альбоме пока нет фото.', albumCover: 'Обложка',
      setAlbumCover: 'Сделать обложкой', savedAlbums: 'Показать сохранённые альбомы', hideSavedAlbums: 'Скрыть сохранённые альбомы',
      deletePhotoTitle: 'Удалить фото', deletePhotoMessage: 'Фото техники будет удалено без возможности восстановления.',
      albumNameRequired: 'Укажите название альбома.', deleteAlbumTitle: 'Удалить альбом',
      deleteAlbumMessage: 'Альбом «{title}» и его фото будут удалены.', deleteAlbumPhotoTitle: 'Удалить фото из альбома',
      deleteAlbumPhotoMessage: 'Фото будет удалено из этого альбома.', openPhoto: 'Открыть фото {fileName}',
      validation: {
        batchSize: 'За одну загрузку можно отправить фото суммарно до {maxMb} МБ.',
        photoCount: 'В карточке техники может быть не больше {maxPhotos} фото.'
      },
      errors: {
        load: 'Не удалось загрузить фото.', upload: 'Не удалось загрузить фото.', uploadAlbum: 'Не удалось загрузить фото в альбом.',
        setMainPhoto: 'Не удалось назначить главное фото.', deletePhoto: 'Не удалось удалить фото.', createAlbum: 'Не удалось создать альбом.',
        deleteAlbum: 'Не удалось удалить альбом.', setAlbumCover: 'Не удалось назначить обложку альбома.',
        deleteAlbumPhoto: 'Не удалось удалить фото из альбома.'
      }
    }
  },
  en: {
    vehicleMedia: {
      album: 'Album', photosTitle: 'Manage vehicle photos', albumsTitle: 'Vehicle albums', vehicle: 'Vehicle',
      delete: 'Delete', back: 'Back', uploadPhoto: 'Upload photos', loadingPhotos: 'Loading photos…', uploadingPhotos: 'Uploading photos…',
      proAlbums: 'Photo albums are available in PRO', carPhotos: 'Vehicle photos', noVehiclePhotos: 'No vehicle photos yet.',
      photo: 'Vehicle photo', mainPhoto: 'Main photo', setMainPhoto: 'Set as main photo', albumTitle: 'Album name',
      albumTitleExample: 'For example, before and after polishing', create: 'Create', noAlbums: 'No albums yet.', albumPlaceholder: 'Album',
      photoCountOne: '{count} photo', photoCountMany: '{count} photos', noAlbumPhotos: 'This album has no photos yet.', albumCover: 'Cover photo',
      setAlbumCover: 'Set as cover photo', savedAlbums: 'Show saved albums', hideSavedAlbums: 'Hide saved albums',
      deletePhotoTitle: 'Delete photo', deletePhotoMessage: 'This vehicle photo will be permanently deleted.',
      albumNameRequired: 'Enter an album name.', deleteAlbumTitle: 'Delete album',
      deleteAlbumMessage: 'Album “{title}” and its photos will be deleted.', deleteAlbumPhotoTitle: 'Remove photo from album',
      deleteAlbumPhotoMessage: 'This photo will be removed from the album.', openPhoto: 'Open photo {fileName}',
      validation: {
        batchSize: 'You can upload up to {maxMb} MB of photos at a time.',
        photoCount: 'A vehicle profile can have no more than {maxPhotos} photos.'
      },
      errors: {
        load: 'Could not load photos.', upload: 'Could not upload photos.', uploadAlbum: 'Could not upload photos to the album.',
        setMainPhoto: 'Could not set the main photo.', deletePhoto: 'Could not delete the photo.', createAlbum: 'Could not create the album.',
        deleteAlbum: 'Could not delete the album.', setAlbumCover: 'Could not set the album cover photo.',
        deleteAlbumPhoto: 'Could not remove the photo from the album.'
      }
    }
  },
  'zh-Hans': {
    vehicleMedia: {
      album: '相册', photosTitle: '管理车辆照片', albumsTitle: '车辆相册', vehicle: '车辆',
      delete: '删除', back: '返回', uploadPhoto: '上传照片', loadingPhotos: '正在加载照片…', uploadingPhotos: '正在上传照片…',
      proAlbums: 'PRO 版提供照片相册', carPhotos: '车辆照片', noVehiclePhotos: '暂无车辆照片。',
      photo: '车辆照片', mainPhoto: '主照片', setMainPhoto: '设为主照片', albumTitle: '相册名称',
      albumTitleExample: '例如：抛光前后', create: '创建', noAlbums: '暂无相册。', albumPlaceholder: '相册',
      photoCountOne: '{count} 张照片', photoCountMany: '{count} 张照片', noAlbumPhotos: '此相册中暂无照片。', albumCover: '封面照片',
      setAlbumCover: '设为封面照片', savedAlbums: '显示已保存的相册', hideSavedAlbums: '隐藏已保存的相册',
      deletePhotoTitle: '删除照片', deletePhotoMessage: '此车辆照片将被永久删除。',
      albumNameRequired: '请输入相册名称。', deleteAlbumTitle: '删除相册',
      deleteAlbumMessage: '相册“{title}”及其中的照片将被删除。', deleteAlbumPhotoTitle: '从相册中移除照片',
      deleteAlbumPhotoMessage: '此照片将从相册中移除。', openPhoto: '打开照片 {fileName}',
      validation: {
        batchSize: '每次最多可上传 {maxMb} MB 的照片。',
        photoCount: '每辆车最多可保存 {maxPhotos} 张照片。'
      },
      errors: {
        load: '无法加载照片。', upload: '无法上传照片。', uploadAlbum: '无法将照片上传到相册。',
        setMainPhoto: '无法设置主照片。', deletePhoto: '无法删除照片。', createAlbum: '无法创建相册。',
        deleteAlbum: '无法删除相册。', setAlbumCover: '无法设置相册封面照片。',
        deleteAlbumPhoto: '无法从相册中移除照片。'
      }
    }
  }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}
