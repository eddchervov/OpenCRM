import * as clientForm from '../packages/workspace/src/utils/clientForm.ts'
import { Mask } from 'maska'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { createMemberSelectOptions } from '../packages/workspace/src/utils/memberSelectOptions.ts'
import { loadClientMaintenances } from '../packages/workspace/src/api/clients.ts'

test('Маска ИНН ограничивает ввод цифрами по виду организации и сохраняет начальные нули', () => {
  const legalEntity = new Mask({ mask: clientForm.clientInnMask('legal_entity') })
  const entrepreneur = new Mask({ mask: clientForm.clientInnMask('entrepreneur') })
  assert.equal(legalEntity.masked('00 1234abc56789012345'), '0012345678')
  assert.equal(entrepreneur.masked('00 1234abc56789012345'), '001234567890')
  assert.equal(entrepreneur.masked(''), '')
  assert.equal(entrepreneur.masked('00123'), '00123')
  assert.equal(clientForm.clientInnMask(''), undefined)
})

test('Маска КПП допускает латинские буквы только в позициях 5–6 и ограничивает длину', () => {
  const kpp = new Mask(clientForm.clientKppMask)
  assert.equal(kpp.masked('5406 ab 001999'), '5406AB001')
  assert.equal(kpp.masked('540601001'), '540601001')
  assert.equal(kpp.masked('abc5406аб01001'), '540601001')
  assert.equal(kpp.masked('5406a'), '5406A')
  assert.equal(kpp.masked(''), '')
})

test('Сохранение с неполным КПП переводит фокус к ошибке; исправленный КПП сохраняется', async () => {
  const payloads = []
  const focused = []
  const scrolled = []
  const state = card({}, {}, { updateClient: async (_, __, payload) => {
    payloads.push(payload)
    return { client: { ...payload, id: 'client-1', vehicles: [] } }
  } })
  await state.loadCard()
  state.startEdit()
  Object.assign(state.form.value, { partyTypeCode: 'company', companyTypeCode: 'legal_entity', name: 'Организация', inn: '3333333333', kpp: '66665665' })
  state.clientFormElement.value = { querySelector: () => ({
    focus: options => focused.push(options),
    scrollIntoView: options => scrolled.push(options)
  }) }

  await state.saveClient()
  assert.equal(payloads.length, 0)
  assert.equal(focused.length, 1)
  assert.equal(scrolled.length, 1)
  assert.equal(state.isEditing.value, true)
  assert.equal(state.form.value.kpp, '66665665')
  assert.equal(state.identityErrors.value.kpp, 'КПП: нужно 9 знаков.')

  state.form.value.kpp = '666656650'
  await state.saveClient()
  assert.equal(payloads.length, 1)
  assert.equal(payloads[0].kpp, '666656650')
  assert.equal(state.isEditing.value, false)
})

test('ФИО сохраняется по частям; прежнее имя не разбирается автоматически', async () => {
  const payloads = []
  const state = card({}, {}, { updateClient: async (_, __, value) => {
    payloads.push(value)
    return { client: { ...value, id: 'client-1', vehicles: [] } }
  } })
  await state.loadCard()
  state.startEdit()
  assert.equal(state.form.value.firstName, '')
  await state.saveClient()
  assert.equal(payloads[0].name, 'Клиент')
  assert.equal(payloads[0].firstName, null)
  state.startEdit()
  Object.assign(state.form.value, { lastName: ' Иванов ', firstName: ' Пётр ', middleName: 'Иванович' })
  await state.saveClient()
  assert.equal(payloads[1].name, 'Иванов Пётр Иванович')
  assert.equal(payloads[1].lastName, 'Иванов')
  assert.equal(state.form.value.firstName, 'Пётр')
  state.startEdit()
  state.form.value.middleName = 'я'.repeat(61)
  await state.saveClient()
  assert.equal(payloads.length, 2)
  assert.match(state.identityErrors.value.middleName, /60/)
})

test('Отдельная модалка комментария не включает редактирование клиента, проверяет лимит и сохраняет только комментарий', async () => {
  const calls = []
  const state = card({}, {}, { updateClientComment: async (...args) => {
    calls.push(args)
    return { client: { id: 'client-1', name: 'Клиент', comment: args[2], vehicles: cars } }
  } })
  await state.loadCard()
  state.openComment()
  assert.equal(state.isEditing.value, false)
  assert.equal(state.isCommentOpen.value, true)
  state.commentDraft.value = 'я'.repeat(4001)
  await state.saveComment()
  assert.equal(calls.length, 0)
  assert.match(state.commentError.value, /4000/)
  state.commentDraft.value = 'я'.repeat(4000)
  await state.saveComment()
  assert.equal(calls[0][2].length, 4000)
  assert.equal(state.isCommentOpen.value, false)
  assert.equal(state.isEditing.value, false)
  state.openComment()
  state.commentDraft.value = ''
  await state.saveComment()
  assert.deepEqual(calls[1], ['org-1', 'client-1', null])
})

test('Ошибка модалки сохраняет черновик, отмена не меняет сохранённый комментарий', async () => {
  const state = card({}, { client: { id: 'client-1', name: 'Клиент', comment: 'Сохранённый', vehicles: [] } }, {
    updateClientComment: async () => { throw new Error('Ошибка сервера') }
  })
  await state.loadCard()
  state.openComment()
  state.commentDraft.value = 'Черновик'
  await state.saveComment()
  assert.equal(state.isCommentOpen.value, true)
  assert.equal(state.commentDraft.value, 'Черновик')
  assert.equal(state.commentError.value, 'Ошибка сервера')
  state.closeComment()
  state.openComment()
  assert.equal(state.commentDraft.value, 'Сохранённый')
  assert.equal(state.commentError.value, '')
})

test('Реквизиты ИП используют 12 цифр и не отправляют скрытый КПП; прежние реквизиты остаются доступными', async () => {
  const payloads = []
  const state = card({}, {}, { updateClient: async (_, __, payload) => {
    payloads.push(payload)
    return { client: { ...payload, id: 'client-1', vehicles: [] } }
  } })
  await state.loadCard()
  state.startEdit()
  Object.assign(state.form.value, { partyTypeCode: 'company', companyTypeCode: 'entrepreneur', name: 'ИП Иванов', inn: ' 0012 3456 7890 ', kpp: '540601001' })
  await state.saveClient()
  assert.equal(payloads[0].inn, '001234567890')
  assert.equal(payloads[0].kpp, null)
  assert.equal(payloads[0].companyTypeCode, 'entrepreneur')
  state.startEdit()
  state.form.value.inn = '1234567890'
  await state.saveClient()
  assert.equal(payloads.length, 1)
  assert.match(state.identityErrors.value.inn, /12/)

  const legacy = card({}, { client: { id: 'client-1', name: 'Старые данные', partyTypeCode: 'company', inn: '123', kpp: 'ABC', vehicles: [] } })
  await legacy.loadCard()
  legacy.startEdit()
  assert.equal(legacy.form.value.companyTypeCode, '')
  assert.deepEqual(legacy.identityErrors.value, {})
  legacy.form.value.companyTypeCode = 'legal_entity'
  assert.match(legacy.identityErrors.value.inn, /10/)
  assert.match(legacy.identityErrors.value.kpp, /9/)
})

function setupComponent(name, props, api = {}) {
  const filename = new URL(`../packages/workspace/src/components/${name}.vue`, import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'))
  const script = compileScript(descriptor, { id: 'client-card-test' })
  const code = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const resolve = id => {
    if (id === '../utils/clientForm') return clientForm
    if (id === '../utils/memberSelectOptions') return { createMemberSelectOptions }
    if (id === 'vue') return { ...vue, onMounted() {}, onBeforeUnmount() {}, watch() {}, useId: () => 'test-tags' }
    if (id === '@opencrm/ui' || id.endsWith('.vue')) return {}
    if (id === '../api/clients') return api
    if (id === '../analytics') return { trackWorkspaceGoal() {} }
    if (id === '../utils/vehicleBrandLogo') return { getVehicleBrandLogo: () => null }
    throw new Error('Не задана зависимость: ' + id)
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  return module.exports.default.setup(vue.reactive(props), { expose() {}, emit() {} })
}

const cars = Array.from({ length: 50 }, (_, index) => ({
  id: String(index + 1), displayName: index === 49 ? 'Audi Q5' : 'BMW X3',
  registrationPlate: index === 49 ? 'К055КК 154' : `А${index}АА 154`, maintenanceCount: 0
}))

test('Фотография автомобиля используется при наличии, ошибка загрузки включает запасной вид', () => {
  const state = setupComponent('ClientVehicles', { vehicles: cars })
  const vehicle = { ...cars[0], photoUrl: '/photo.jpg' }
  assert.equal(state.hasPhoto(vehicle), true)
  assert.equal(state.hasPhoto(cars[0]), false)
  state.failedPhotos.value.add('/photo.jpg')
  assert.equal(state.hasPhoto(vehicle), false)
  assert.equal(state.hasPhoto({ ...vehicle, photoUrl: '/new-photo.jpg' }), true)
})

test('Выбор автомобиля охватывает весь список и ищет номер без пробелов', () => {
  const state = setupComponent('ClientVehicles', { vehicles: cars })
  assert.equal(state.visibleVehicles.value.length, 4)
  assert.equal(state.filteredVehicles.value.length, 50)
  state.search.value = 'к055кк154'
  assert.deepEqual(state.filteredVehicles.value.map(car => car.id), ['50'])
  state.search.value = 'AUDI'
  assert.deepEqual(state.filteredVehicles.value.map(car => car.id), ['50'])
  state.search.value = 'несуществующий'
  assert.equal(state.filteredVehicles.value.length, 0)
  state.open(true)
  assert.equal(state.filteredVehicles.value.length, 50)
  assert.equal(state.isChoosing.value, true)
})

function card(context = {}, data = {}, api = {}) {
  return setupComponent('ClientCardSection', {
    context: {
      organization: { id: 'org-1' }, workspaceAccess: {},
      shell: { scenario: { code: 'detailing' }, quickActions: [{code: 'create-maintenance', isVisible: true}], menu: [{ code: 'vehicles', sectionCodes: ['vehicles', 'maintenance'], isVisible: true }] },
      ...context
    }, clientId: 'client-1'
  }, { loadClientDetails: async () => ({
    client: { id: 'client-1', name: 'Клиент', vehicles: cars, createdAt: '2026-01-01' },
    canViewMaintenance: true, recentMaintenances: [], defects: [], ...data
  }), loadClientMaintenances: async (_, __, query) => ({ items: [], totalCount: 0, page: query.page, pageSize: query.pageSize }), ...api })
}

test('Выбор ответственного показывает связанные имена, различает совпадения и сохраняет идентификаторы аккаунтов', () => {
  const state = card({ organization: { id: 'org-1', members: [
    { userId: 'one', employeeName: 'Иван Петров', email: 'contact@example.invalid' },
    { userId: 'two', employeeName: 'Иван Петров', email: 'contact@example.invalid' },
    { userId: 'three', employeeName: 'Анна Иванова', email: 'contact@example.invalid' },
    { userId: 'four', email: 'contact@example.invalid' }
  ] } })

  assert.deepEqual(state.responsibleOptions.value, [
    { value: '', label: 'Без ответственного' },
    { value: 'one', label: 'Иван Петров (contact@example.invalid)' },
    { value: 'two', label: 'Иван Петров (contact@example.invalid)' },
    { value: 'three', label: 'Анна Иванова' },
    { value: 'four', label: 'contact@example.invalid' }
  ])
})

test('Создание записи недоступно в режиме чтения, архиве и без раздела работ', async () => {
  for (const [context, data] of [
    [{ workspaceAccess: { isReadOnly: true } }, {}],
    [{ shell: { scenario: { code: 'detailing' }, menu: [{code:'maintenance',isVisible:true}], quickActions: [] } }, {}],
    [{}, {canViewMaintenance: false}],
    [{ shell: { scenario: { code: 'detailing' }, menu: [] } }, {}],
    [{}, { client: { id: 'client-1', name: 'Архив', isArchived: true, vehicles: cars } }]
  ]) {
    const state = card(context, data)
    await state.loadCard()
    assert.equal(state.canCreateMaintenance.value, false)
  }
})

test('Новая запись при нескольких машинах открывает выбор без случайного выбора первой', async () => {
  const state = card()
  await state.loadCard()
  let choice = null
  state.vehiclePicker.value = { open: value => { choice = value } }
  state.createMaintenance()
  assert.equal(choice, true)
})

test('Фильтры истории передают серверу статус и поисковый запрос', async () => {
  let request
  const state = card({}, {}, {
    loadClientMaintenances: async (_, __, query) => {
      request = query
      return { items: [], totalCount: 0, page: query.page, pageSize: query.pageSize }
    }
  })
  await state.loadCard()
  state.historyStatus.value = 'upcoming'
  await state.loadMaintenanceHistory(1)
  assert.equal(request.status, 'upcoming')
  state.historyStatus.value = 'completed'
  state.historySearch.value = 'ПОЛИРОВКА'
  await state.loadMaintenanceHistory(1)
  assert.equal(request.status, 'completed')
  assert.equal(request.searchQuery, 'ПОЛИРОВКА')
  assert.equal(state.getStatusName({ status: 4, handedOverAt: '2026-01-01' }), 'Отменено')
  assert.equal(state.formatMoney(null), '—')
  assert.match(state.formatMoney(0), /0/)
})

test('Теги добавляются без повторов, удаляются по одному и сбрасываются при отмене', async () => {
  const state = card()
  await state.loadCard()
  state.startEdit()
  state.form.value.tagNamesText = 'Постоянный'
  state.tagDraft.value = 'постоянный, Керамика, Корпоративный'
  let prevented = false
  state.onTagKeydown({ key: 'Enter', isComposing: false, preventDefault() { prevented = true } })
  assert.equal(prevented, true)
  assert.deepEqual(state.formTags.value, ['Постоянный', 'Керамика', 'Корпоративный'])
  assert.equal(state.tagDraft.value, '')
  state.removeTag(1)
  assert.deepEqual(state.formTags.value, ['Постоянный', 'Корпоративный'])
  state.tagDraft.value = 'Не сохранять'
  state.cancelEdit()
  assert.equal(state.tagDraft.value, '')
  assert.equal(state.form.value.tagNamesText, '')
})

test('Сохранение организации передаёт единый комментарий и ещё не подтверждённый тег', async () => {
  let payload
  const state = card({}, {}, { updateClient: async (_, __, value) => {
    payload = value
    return { client: { id: 'client-1', name: value.name, vehicles: [], tags: [] } }
  } })
  await state.loadCard()
  state.startEdit()
  Object.assign(state.form.value, {
    name: ' ООО «Автосфера» ', partyTypeCode: 'company', phone: '+7 000 000-00-01', inn: '5406123456', kpp: '540601001',
    legalAddress: 'г. Новосибирск', statusCode: 'attention', responsibleUserId: 'member-1',
    tagNamesText: 'Постоянный', comment: 'Комментарий'
  })
  state.tagDraft.value = 'Корпоративный'
  await state.saveClient()
  assert.deepEqual(payload, {
    name: 'ООО «Автосфера»', lastName: null, firstName: null, middleName: null, companyTypeCode: 'legal_entity', partyTypeCode: 'company', phone: '+7 000 000-00-01', inn: '5406123456', kpp: '540601001',
    legalAddress: 'г. Новосибирск', statusCode: 'attention', responsibleUserId: 'member-1',
    tagNamesText: 'Постоянный, Корпоративный', comment: 'Комментарий'
  })
  assert.equal(Object.hasOwn(payload, 'notes'), false)
  assert.equal(state.isEditing.value, false)
})

test('Единый комментарий загружается, сохраняется и очищается без поля notes, включая текст длиннее 600 символов', async () => {
  const longComment = `${'Требование клиента. '.repeat(35)}\n${'Согласование дополнительных работ. '.repeat(12)}`
  assert.ok(longComment.length > 600)
  const payloads = []
  const state = card({}, { client: { id: 'client-1', name: 'Клиент', comment: longComment, vehicles: cars } }, {
    updateClient: async (_, __, payload) => {
      payloads.push(payload)
      return { client: { id: 'client-1', name: 'Клиент', comment: payload.comment, vehicles: cars } }
    }
  })
  await state.loadCard()

  assert.equal(state.form.value.comment, longComment)
  assert.equal(Object.hasOwn(state.form.value, 'notes'), false)
  state.startEdit()
  await state.saveClient()

  assert.equal(payloads[0].comment, longComment)
  assert.equal(Object.hasOwn(payloads[0], 'notes'), false)
  assert.equal(state.form.value.comment, longComment)

  state.startEdit()
  state.form.value.comment = ''
  await state.saveClient()

  assert.equal(payloads[1].comment, null)
  assert.equal(Object.hasOwn(payloads[1], 'notes'), false)
  assert.equal(state.form.value.comment, '')

})

test('Комментарий ровно в 4000 символов сохраняется, а 4001 отклоняется до вызова API', async () => {
  const payloads = []
  const state = card({}, {}, {
    updateClient: async (_, __, payload) => {
      payloads.push(payload)
      return { client: { id: 'client-1', name: 'Клиент', comment: payload.comment, vehicles: cars } }
    }
  })
  await state.loadCard()
  state.startEdit()
  state.form.value.comment = 'а'.repeat(4000)
  await state.saveClient()

  assert.equal(payloads.length, 1)
  assert.equal(payloads[0].comment.length, 4000)
  assert.equal(state.errorMessage.value, '')

  state.startEdit()
  state.form.value.comment = 'б'.repeat(4001)
  await state.saveClient()

  assert.equal(payloads.length, 1)
  assert.equal(state.form.value.comment.length, 4001)
  assert.equal(state.errorMessage.value, 'Комментарий не должен превышать 4000 символов.')
  assert.equal(state.isEditing.value, true)

  state.cancelEdit()
  assert.equal(state.errorMessage.value, '')
  assert.equal(state.isEditing.value, false)
  assert.equal(state.form.value.comment, 'а'.repeat(4000))
  assert.equal(payloads.length, 1)
})

test('Ошибка сохранения сохраняет ввод; реквизиты физлица не отправляются', async () => {
  let payload
  const state = card({}, {}, { updateClient: async (_, __, value) => {
    payload = value
    throw new Error('Ошибка сервера')
  } })
  await state.loadCard()
  state.startEdit()
  Object.assign(state.form.value, { partyTypeCode: 'person', inn: '5406123456', kpp: '540601001', legalAddress: 'Адрес' })
  state.tagDraft.value = 'Новый тег'
  await state.saveClient()
  assert.equal(payload.inn, null)
  assert.equal(payload.kpp, null)
  assert.equal(payload.legalAddress, null)
  assert.equal(state.form.value.inn, '5406123456')
  assert.equal(state.form.value.tagNamesText, 'Новый тег')
  assert.equal(state.errorMessage.value, 'Ошибка сервера')
  assert.equal(state.isEditing.value, true)
})

test('Режим чтения не сохраняет изменения клиента', async () => {
  let saves = 0
  const state = card({ workspaceAccess: { isReadOnly: true } }, {}, { updateClient: async () => { saves++ } })
  await state.loadCard()
  state.startEdit()
  assert.equal(state.isEditing.value, false)
  await state.saveClient()
  assert.equal(saves, 0)
})

test('История клиента загружается страницами по 10 записей и показывает диапазон', async () => {
  const requests = []
  const state = card({}, {}, {
    loadClientMaintenances: async (_, __, query) => {
      requests.push(query)
      return {
        items: Array.from({ length: query.page === 3 ? 8 : 10 }, (_, index) => ({
          id: `work-${(query.page - 1) * 10 + index + 1}`,
          vehicleId: 'vehicle-1', vehicleDisplayName: 'BMW X3', type: 2, status: 3,
          date: '2025-01-01', serviceNames: ['Полировка'], detailUrl: '#', vehicleUrl: '#'
        })),
        totalCount: 28, page: query.page, pageSize: query.pageSize
      }
    }
  })

  await state.loadCard()
  assert.deepEqual(requests[0], { page: 1, pageSize: 10, searchQuery: undefined, status: undefined })
  assert.equal(state.historyResult.value.items.length, 10)

  await state.loadMaintenanceHistory(3)
  assert.equal(state.historyPage.value, 3)
  assert.equal(state.historyResult.value.items.length, 8)
  assert.equal(state.historyPaginationInfo.value, '21–28 из 28')
})

test('Поиск с задержкой сбрасывает историю на первую страницу', async () => {
  const requests = []
  const state = card({}, {}, {
    loadClientMaintenances: async (_, __, query) => {
      requests.push(query)
      return { items: [], totalCount: 28, page: query.page, pageSize: query.pageSize }
    }
  })

  await state.loadCard()
  await state.loadMaintenanceHistory(3)
  state.historySearch.value = 'BMW X3'
  state.scheduleHistorySearch()
  assert.equal(state.historyPage.value, 1)
  await new Promise(resolve => setTimeout(resolve, 300))

  assert.deepEqual(requests.at(-1), { page: 1, pageSize: 10, searchQuery: 'BMW X3', status: undefined })
  assert.equal(state.historyPage.value, 1)
})

test('Ошибка истории показывается и повторная загрузка восстанавливает данные', async () => {
  let shouldFail = true
  const state = card({}, {}, {
    loadClientMaintenances: async (_, __, query) => {
      if (shouldFail) throw new Error('История временно недоступна')
      return { items: [{ id: 'work-1', vehicleId: 'vehicle-1', vehicleDisplayName: 'BMW X3', type: 2, status: 3, date: '2025-01-01', detailUrl: '#', vehicleUrl: '#' }], totalCount: 1, page: query.page, pageSize: query.pageSize }
    }
  })

  await state.loadCard()
  assert.equal(state.historyError.value, 'История временно недоступна')
  assert.equal(state.historyResult.value, null)
  shouldFail = false
  await state.loadMaintenanceHistory(state.historyPage.value)
  assert.equal(state.historyError.value, '')
  assert.equal(state.historyResult.value.items[0].id, 'work-1')
})

test('Запоздалый ответ первой страницы не затирает актуальный поиск', async () => {
  const requests = []
  const state = card({}, {}, {
    loadClientMaintenances: (_, __, query) => new Promise(resolve => requests.push({ query, resolve }))
  })

  const initialLoad = state.loadCard()
  await new Promise(resolve => setTimeout(resolve, 0))
  assert.equal(requests.length, 1)

  state.historySearch.value = 'новый запрос'
  const searchLoad = state.loadMaintenanceHistory(1)
  requests[1].resolve({ items: [{ id: 'new-result', vehicleId: 'vehicle-1', vehicleDisplayName: 'Audi', type: 2, status: 3, date: '2025-01-01', detailUrl: '#', vehicleUrl: '#' }], totalCount: 1, page: 1, pageSize: 10 })
  await searchLoad
  requests[0].resolve({ items: [{ id: 'old-result', vehicleId: 'vehicle-1', vehicleDisplayName: 'BMW', type: 2, status: 3, date: '2025-01-01', detailUrl: '#', vehicleUrl: '#' }], totalCount: 1, page: 1, pageSize: 10 })
  await initialLoad

  assert.equal(state.historyResult.value.items[0].id, 'new-result')
})

test('История клиента не запрашивается без права просмотра работ', async () => {
  let requests = 0
  const state = card({}, { canViewMaintenance: false }, {
    loadClientMaintenances: async () => {
      requests++
      return { items: [], totalCount: 0, page: 1, pageSize: 10 }
    }
  })

  await state.loadCard()
  assert.equal(state.canOpenMaintenance.value, false)
  assert.equal(requests, 0)
  assert.equal(state.historyResult.value, null)
})

test('API истории клиента передаёт страницу, поиск и фильтр в query string', async () => {
  const originalWindow = globalThis.window
  const originalFetch = globalThis.fetch
  let requestedUrl
  globalThis.window = { location: { origin: 'https://opencrm.test' } }
  globalThis.fetch = async (url, options) => {
    requestedUrl = new URL(url)
    assert.equal(options.method, 'GET')
    return Response.json({ items: [], totalCount: 0, page: 2, pageSize: 10 })
  }

  try {
    const result = await loadClientMaintenances('org-1', 'client-1', {
      page: 2, pageSize: 10, searchQuery: '  BMW X3  ', status: 'completed'
    })

    assert.equal(requestedUrl.pathname, '/api/v1/organizations/org-1/clients/client-1/maintenances')
    assert.equal(requestedUrl.searchParams.get('page'), '2')
    assert.equal(requestedUrl.searchParams.get('pageSize'), '10')
    assert.equal(requestedUrl.searchParams.get('searchQuery'), 'BMW X3')
    assert.equal(requestedUrl.searchParams.get('status'), 'completed')
    assert.deepEqual(result, { items: [], totalCount: 0, page: 2, pageSize: 10 })
  } finally {
    globalThis.window = originalWindow
    globalThis.fetch = originalFetch
  }
})

test('Новый клиент по умолчанию физлицо, сброс формы восстанавливает этот выбор', async () => {
  const state = setupComponent('ClientCardSection', {
    context: { organization: { id: 'org-1' }, workspaceAccess: {}, shell: { scenario: { code: 'detailing' } } },
    clientId: 'new'
  })
  await state.loadCard()
  assert.equal(state.isEditing.value, true)
  assert.equal(state.form.value.partyTypeCode, 'person')
  state.form.value.partyTypeCode = 'company'
  state.resetForm()
  assert.equal(state.form.value.partyTypeCode, 'person')
})

test('Повторное сохранение клиента во время запроса не отправляет второй запрос', async () => {
  let finish
  let calls = 0
  const state = card({}, {}, { updateClient: async (_, __, payload) => {
    calls++
    return new Promise(resolve => { finish = () => resolve({client:{...payload,id:'client-1',vehicles:[]}}) })
  } })
  await state.loadCard()
  state.startEdit()
  const pending = state.saveClient()
  const repeated = state.saveClient()
  assert.equal(calls, 1)
  finish()
  await Promise.all([pending, repeated])
})

test('Поздний ответ сохранения не заменяет заново открытую карточку клиента', async () => {
  let finish
  let currentName = 'Прежняя карточка'
  const state = card({}, {}, {
    loadClientDetails: async () => ({client:{id:'client-1',name:currentName,vehicles:[]}}),
    updateClient: async () => new Promise(resolve => { finish = resolve })
  })
  await state.loadCard()
  state.startEdit()
  const pending = state.saveClient()
  currentName = 'Заново открытая карточка'
  await state.loadCard()
  finish({client:{id:'client-1',name:'Запоздалый ответ',vehicles:[]}})
  await pending
  assert.equal(state.details.value.client.name, currentName)
  assert.equal(state.isSaving.value, false)
})

test('Ошибка прежнего сохранения не снимает блокировку нового запроса и не показывает чужую ошибку', async () => {
  const requests = []
  const state = card({}, {}, { updateClient: async () => new Promise((resolve, reject) => requests.push({resolve,reject})) })
  await state.loadCard()
  state.startEdit()
  const previous = state.saveClient()
  await state.loadCard()
  state.startEdit()
  const current = state.saveClient()
  assert.equal(requests.length, 2)
  requests[0].reject(new Error('Ошибка прежнего запроса'))
  await previous
  assert.equal(state.errorMessage.value, '')
  assert.equal(state.isSaving.value, true)
  requests[1].resolve({client:{id:'client-1',name:'Актуальный результат',vehicles:[]}})
  await current
  assert.equal(state.isSaving.value, false)
  assert.equal(state.details.value.client.name, 'Актуальный результат')
})
