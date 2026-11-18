import { importMessage } from '@opencrm/workspace/importMessages'
import { importBusinessMessage, knownImportBusinessMessageCodes } from '@opencrm/workspace/importBusinessMessages'

export type ImportRowMessageSource = {
  message?: string | null
  messageCode?: string | null
  messageParameters?: Record<string, string | number> | null
  messageDetails?: Array<{
    message: string
    messageCode?: string | null
    messageParameters?: Record<string, string | number> | null
  }> | null
}

export const knownMessageCodes = new Set([
  'rowExcluded', 'rowWillCreate', 'createSeparateClient', 'clientInvalid', 'selectedClientUnavailable', 'selectedClientLinked',
  'clientPossibleDuplicate', 'duplicateClientExternalId', 'duplicateClientInFile', 'registrationCountryInvalid', 'foreignPlateInvalid',
  'vehicleInvalid', 'vehicleIdentityRequired', 'vehicleIdentityConflict', 'vehicleAlreadyExists', 'duplicateVehicleInFile',
  'vehicleClientReferenceInvalid', 'vehicleClientAmbiguous', 'clientLimitExceeded', 'vehicleLimitExceeded', 'duplicateVehicleExternalId',
  'vehicleNotReady', 'externalIdentityTooLong', 'crmClientInvalid', 'crmClientDuplicate',
  'crmVehicleInvalid', 'crmVehicleDuplicate', 'crmVehicleClientAmbiguous', 'alreadyImported', 'identityConflict',
  'identityExternalIdDuplicate', 'sourceExternalIdRequired', 'childIdentityAlreadyImported', 'childIdentityConflict', 'statusCreated'
])

const legacyMessageCodes = new Map<string, string>([
  ['Исключено из импорта.', 'rowExcluded'],
  ['Будет создано.', 'rowWillCreate'],
  ['Будет создан отдельный клиент по вашему выбору.', 'createSeparateClient'],
  ['Укажите имя до 160 символов, телефон до 40, комментарий до 600 и исходный ID до 100 символов.', 'clientInvalid'],
  ['Выбранный клиент недоступен в этой организации.', 'selectedClientUnavailable'],
  ['Будет использован выбранный клиент; его поля не изменятся.', 'selectedClientLinked'],
  ['Возможный дубль: выберите существующего клиента, подтвердите отдельную запись или исключите строку.', 'clientPossibleDuplicate'],
  ['Повторяется исходный ID клиента. Оставьте одну строку или исправьте ID.', 'duplicateClientExternalId'],
  ['Повтор клиента внутри файла. Оставьте одну строку или явно подтвердите отдельные записи; проверьте связи автомобилей.', 'duplicateClientInFile'],
  ['Укажите корректный двухбуквенный код страны регистрации или ZZ.', 'registrationCountryInvalid'],
  ['Проверьте иностранный регистрационный номер.', 'foreignPlateInvalid'],
  ['Проверьте название (до 160), марку и модель (до 100), VIN (5–32 латинских букв/цифр), код страны, номер и год (1900–2100). HTML не допускается.', 'vehicleInvalid'],
  ['Проверьте название (до 160), марку и модель (до 100), VIN (5–32 латинских букв/цифр), код страны, номер и год (1900–{maxYear}). HTML не допускается.', 'vehicleInvalid'],
  ['Для автомобиля требуется VIN или госномер.', 'vehicleIdentityRequired'],
  ['VIN и госномер противоречат существующим автомобилям. Проверьте идентификаторы или исключите строку.', 'vehicleIdentityConflict'],
  ['Автомобиль уже существует. Строка будет пропущена без изменения его данных и владельца.', 'vehicleAlreadyExists'],
  ['Повтор автомобиля внутри файла. Исключите лишнюю строку или исправьте идентификаторы.', 'duplicateVehicleInFile'],
  ['Исходный ID должен соответствовать одному включённому и проверенному клиенту из файла.', 'vehicleClientReferenceInvalid'],
  ['Телефон владельца неоднозначен или клиент не найден. Укажите исходный ID проверенного клиента.', 'vehicleClientAmbiguous'],
  ['Укажите имя клиента до 160 символов и телефон до 40 символов.', 'crmClientInvalid'],
  ['Клиент с таким телефоном или именем уже есть.', 'crmClientDuplicate'],
  ['Проверьте год, VIN, госномер, марку и модель.', 'crmVehicleInvalid'],
  ['VIN и страна или регистрационный номер противоречат друг другу.', 'vehicleIdentityConflict'],
  ['Автомобиль с таким VIN или госномером уже есть.', 'crmVehicleDuplicate'],
  ['Телефон клиента не соответствует ровно одному клиенту.', 'crmVehicleClientAmbiguous'],
  ['Проверьте год, VIN, госномер, марку и модель.', 'crmVehicleInvalid'],
  ['Создано.', 'statusCreated'],
  ['Уже перенесено из этой базы CRM; существующие данные сохранятся.', 'alreadyImported'],
  ['Этот ID уже переносили, но исходные данные изменились или запись удалена. Автоматическое обновление запрещено; проверьте расхождения.', 'identityConflict'],
  ['Внешний ID повторяется внутри пакета. Оставьте одну запись для каждого ID.', 'identityExternalIdDuplicate'],
  ['Для защиты между файлами сопоставьте столбец с постоянным внешним ID исходной CRM. Номер строки и телефон не заменяют этот ID.', 'sourceExternalIdRequired'],
  ['Строка входит в ранее перенесённый заказ; повторное создание не требуется.', 'childIdentityAlreadyImported'],
  ['Состав ранее перенесённого заказа изменился. Проверьте заказ целиком.', 'childIdentityConflict'],
  ['Повторяется внешний ID автомобиля. Исправьте ID перед переносом заказов.', 'duplicateVehicleExternalId'],
  ['Автомобиль заказа исключён или не прошёл проверку.', 'vehicleNotReady'],
  ['Внешние идентификаторы ограничены 100 символами.', 'externalIdentityTooLong']
])

export function importRowMessage(row: ImportRowMessageSource): string {
  if (row.messageDetails?.length) {
    return row.messageDetails.map(detail => detail.messageCode && knownImportBusinessMessageCodes.has(detail.messageCode)
      ? importBusinessMessage(detail.messageCode, detail.messageParameters ?? undefined)
      : detail.message).join(' · ')
  }

  if (!row.message) return ''

  const messageCode = row.messageCode && knownMessageCodes.has(row.messageCode)
    ? row.messageCode
    : legacyMessageCodes.get(row.message)

  return messageCode
    ? importMessage(`rowMessages.${messageCode}`, row.messageParameters ?? undefined)
    : row.message
}
