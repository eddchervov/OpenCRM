import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  "ru": {
    "vehicleApi": {
      "vehicle": "Автомобиль",
      "brands": "Не удалось загрузить марки техники.",
      "models": "Не удалось загрузить модели техники.",
      "vehicles": "Не удалось загрузить автомобили.",
      "card": "Не удалось загрузить карточку автомобиля.",
      "history": "Не удалось загрузить историю обслуживания.",
      "album": "Не удалось загрузить альбом.",
      "uploadPhoto": "Не удалось загрузить фото.",
      "deletePhoto": "Не удалось удалить фото.",
      "mainPhoto": "Не удалось назначить главное фото.",
      "createAlbum": "Не удалось создать альбом.",
      "deleteAlbum": "Не удалось удалить альбом.",
      "uploadAlbumPhoto": "Не удалось загрузить фото в альбом.",
      "deleteAlbumPhoto": "Не удалось удалить фото из альбома.",
      "mainAlbumPhoto": "Не удалось назначить главное фото альбома.",
      "save": "Не удалось сохранить авто."
    }
  },
  "en": {
    "vehicleApi": {
      "vehicle": "Vehicle",
      "brands": "Could not load vehicle makes.",
      "models": "Could not load vehicle models.",
      "vehicles": "Could not load vehicles.",
      "card": "Could not load the vehicle profile.",
      "history": "Could not load service history.",
      "album": "Could not load the album.",
      "uploadPhoto": "Could not upload the photo.",
      "deletePhoto": "Could not delete the photo.",
      "mainPhoto": "Could not set the main photo.",
      "createAlbum": "Could not create the album.",
      "deleteAlbum": "Could not delete the album.",
      "uploadAlbumPhoto": "Could not upload the photo to the album.",
      "deleteAlbumPhoto": "Could not remove the photo from the album.",
      "mainAlbumPhoto": "Could not set the album cover photo.",
      "save": "Could not save the vehicle."
    }
  },
  "zh-Hans": {
    "vehicleApi": {
      "vehicle": "车辆",
      "brands": "无法加载车辆品牌。",
      "models": "无法加载车型。",
      "vehicles": "无法加载车辆。",
      "card": "无法加载车辆资料。",
      "history": "无法加载服务记录。",
      "album": "无法加载相册。",
      "uploadPhoto": "无法上传照片。",
      "deletePhoto": "无法删除照片。",
      "mainPhoto": "无法设置主照片。",
      "createAlbum": "无法创建相册。",
      "deleteAlbum": "无法删除相册。",
      "uploadAlbumPhoto": "无法将照片上传到相册。",
      "deleteAlbumPhoto": "无法从相册中移除照片。",
      "mainAlbumPhoto": "无法设置相册封面。",
      "save": "无法保存车辆。"
    }
  }
}

for (const [locale, value] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, value)
}
