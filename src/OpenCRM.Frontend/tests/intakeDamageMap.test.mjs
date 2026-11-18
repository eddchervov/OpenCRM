import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/workspaceApiMessages.ts'

const componentUrl = new URL('../packages/workspace/src/components/IntakeDamageMap.vue', import.meta.url)
const { descriptor } = parse(readFileSync(componentUrl, 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'intake-damage-map-test' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText

const messages = {
  'intakeDamage.unavailableDiagram': 'Схема автомобиля недоступна. Сохранённые повреждения доступны в списке. Повторите загрузку.',
  'intakeDamage.deleteChanged': 'Запись изменилась. Проверьте повреждение и повторите удаление.',
  'intakeDamage.conflictChanged': 'Осмотр обновлён после открытия. Ваши изменения не потеряны. Сравните их с сохранёнными данными перед продолжением.',
  'intakeDamage.diagramChanged': 'Схема автомобиля обновлена. Ваши изменения относятся к прежней схеме. Откройте актуальный осмотр и отметьте повреждение заново.',
  'intakeDamage.damageTitle': 'Повреждение №{number}',
  'intakeDamage.numberPrefix': '№{number}, ',
  'intakeDamage.summary': '{number}{type}, {view}: {description}; фото: {count}',
  'intakeDamage.scratch': 'Царапина',
  'intakeDamage.other': 'Другое',
  'intakeDamage.front': 'Спереди',
  'intakeDamage.rear': 'Сзади',
  'intakeDamage.left': 'Левый борт',
  'intakeDamage.right': 'Правый борт'
}

function translate(key, parameters = {}) {
  return (messages[key] ?? key).replace(/\{([^}]+)\}/g, (_, name) => parameters[name] ?? `{${name}}`)
}

class FakeIntakeDamageApiError extends Error {
  constructor(message, status) {
    super(message)
    this.status = status
  }
}

function createEditor({ saveDamage, deleteDamage, readDamages, uploadPhotos, photos = [], readOnly = false, previewOnly = false, maintenanceVersion, diagramVersion = 'sedan-v1', autoLoadImages = true } = {}) {
  const module = { exports: {} }
  const requests = []
  const server = { version: 5, diagramVersion, damages: [] }
  const emitted = []
  const reads = []
  const resolve = id => {
    if (id === 'vue') return { ...vue, onBeforeUnmount() {} }
    if (id === '@opencrm/ui') return { UiButton: {}, UiConfirmDialog: {}, UiModal: {}, useLocalization: () => ({ t: translate }) }
    if (id === '../maintenanceMediaMessages') return {}
    if (id === './intakeDiagramRegistry') return {
      getIntakeDiagram: version => version === 'sedan-v1' ? {
        version: 'sedan-v1', width: 1619, height: 971, aspectRatio: '1619 / 971',
        views: { front: { imageUrl: '/sedan-v1/front.png' }, rear: { imageUrl: '/sedan-v1/rear.png' }, left: { imageUrl: '/sedan-v1/left.png' }, right: { imageUrl: '/sedan-v1/right.png' } }
      } : null
    }
    if (id === '../api/intakeDamages') return {
      IntakeDamageApiError: FakeIntakeDamageApiError,
      deleteIntakeDamage: async (...args) => {
        if (deleteDamage) return deleteDamage(args, server)
        server.damages = server.damages.filter(damage => damage.id !== args[2])
        server.version++
      },
      loadIntakeDamages: async (...args) => {
        reads.push(args)
        if (readDamages) return readDamages(args, server)
        return { version: server.version, diagramVersion: server.diagramVersion, damages: server.damages }
      },
      saveIntakeDamage: async (...args) => {
        requests.push(args)
        if (saveDamage) return saveDamage(args, server)
        server.damages.push({ id: args[2], number: 1, ...args[5] })
        server.version++
      }
    }
    if (id === '../api/maintenance') return {
      loadMaintenancePhotos: async () => [],
      uploadMaintenancePhotos: async (...args) => uploadPhotos?.(...args)
    }
    return {}
  }
  new Function('require', 'module', 'exports', code)(resolve, module, module.exports)
  const props = vue.reactive({ organizationId: 'org-1', maintenanceId: 'order-1', maintenanceVersion, photos, readOnly, previewOnly })
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => emitted.push(args) })
  const loadDamages = state.loadDamages
  state.loadDamages = async (...args) => {
    const result = await loadDamages(...args)
    if (autoLoadImages) {
      for (const viewCode of ['front', 'rear', 'left', 'right']) {
        state.handleDiagramImageLoad(viewCode, state.diagramVersion.value, state.diagramImageAttempt(viewCode))
      }
    }

    return result
  }

  return { state, props, requests, reads, server, emitted }
}

test('Закрытая приёмка показывает встроенную схему без запроса повреждений и не разрешает изменение', async () => {
  const { state, props, reads, requests } = createEditor({ previewOnly: true })
  await state.loadDamages()
  assert.equal(reads.length, 0)
  assert.equal(state.diagramVersion.value, 'sedan-v1')
  assert.ok(state.diagramDefinition.value)
  assert.equal(state.canEdit.value, false)
  props.maintenanceId = 'other-order'
  await vue.nextTick()
  assert.equal(reads.length, 0)
  assert.equal(requests.length, 0)
  props.previewOnly = false
  await vue.nextTick()
  assert.equal(reads.length, 1)
})

test('Координаты схемы нормализуются и выбранная проекция задаёт ракурс метки', async () => {
  const { state } = createEditor()
  await state.loadDamages()
  const projection = { getBoundingClientRect: () => ({ left: 10, top: 20, width: 200, height: 100 }) }

  const point = state.pointFromEvent({ currentTarget: projection, clientX: 110, clientY: 70 })
  assert.deepEqual(point, { x: 0.5, y: 0.5 })
  assert.deepEqual(state.pointFromEvent({ currentTarget: projection, clientX: -40, clientY: 200 }), { x: 0.03, y: 0.92 })
  state.openProjection('left')
  assert.equal(state.projectionView.value, 'left')
  assert.equal(state.draft.value, null)
  assert.equal(state.isEditorOpen.value, false)
  state.handleProjectionClick({ currentTarget: projection, clientX: 110, clientY: 70 }, 'left')
  assert.equal(state.draft.value.viewCode, 'left')
  assert.equal(state.activeView.value, 'left')
})

test('Перенос метки через схему сохраняет черновик, а клавиатура размещает её стрелками и Enter', async () => {
  const { state } = createEditor()
  await state.loadDamages()
  state.openNewAt({ x: 0.4, y: 0.6 }, 'front')
  state.draft.value.description = 'Скол на крыле'
  state.draft.value.photoIds = ['photo-1']
  const draftId = state.draftId.value

  state.startRepositioning()
  assert.equal(state.isEditorOpen.value, false)
  state.editDamage({ id: 'other', number: 2, viewCode: 'right', damageType: 'dent', x: 0.1, y: 0.2, description: 'Другой дефект', photoIds: [] })
  assert.equal(state.draft.value.description, 'Скол на крыле')
  state.handleProjectionClick({
    currentTarget: { getBoundingClientRect: () => ({ left: 10, top: 20, width: 200, height: 100 }) },
    clientX: 110,
    clientY: 70
  }, 'rear')

  assert.equal(state.isEditorOpen.value, true)
  assert.equal(state.draftId.value, draftId)
  assert.deepEqual(state.draft.value, {
    viewCode: 'rear', damageType: 'scratch', x: 0.5, y: 0.5,
    description: 'Скол на крыле', photoIds: ['photo-1']
  })

  state.closeEditor()
  state.openProjection('left')
  state.handleProjectionFocus('left')
  state.handleProjectionKeydown({ key: 'ArrowRight', preventDefault() {}, shiftKey: false }, 'left')
  state.handleProjectionKeydown({ key: 'Enter', preventDefault() {}, shiftKey: false }, 'left')
  assert.equal(state.draft.value.viewCode, 'left')
  assert.equal(state.draft.value.x, 0.53)
  assert.equal(state.draft.value.y, 0.5)
  assert.equal(state.isEditorOpen.value, true)
})

test('Сохранение акта обновляет версию схемы до добавления следующего повреждения', async () => {
  const { state, props, server, requests } = createEditor({ maintenanceVersion: 5 })
  await state.loadDamages()
  server.version = 6
  props.maintenanceVersion = 6
  await vue.nextTick()

  assert.equal(state.version.value, 6)
  state.openNewAt({ x: 0.4, y: 0.6 })
  await state.saveDamage()

  assert.equal(requests.length, 1)
  assert.equal(requests[0][3], 6)
  assert.equal(state.saveNeedsReview.value, false)
  assert.equal(server.damages.length, 1)
})

test('Обновление карточки не подменяет версию открытого черновика и не обходит конфликт', async () => {
  const { state, props, server, requests } = createEditor({
    maintenanceVersion: 5,
    saveDamage: async (args, currentServer) => {
      if (args[3] !== currentServer.version) throw new FakeIntakeDamageApiError('Запись изменена другим пользователем.', 409)
    }
  })
  await state.loadDamages()
  state.openNewAt({ x: 0.4, y: 0.6 })
  state.draft.value.description = 'Не потерять изменения'
  server.version = 6
  props.maintenanceVersion = 6
  await vue.nextTick()

  assert.equal(state.version.value, 5)
  assert.equal(state.draft.value.description, 'Не потерять изменения')
  await state.saveDamage()

  assert.equal(requests[0][3], 5)
  assert.equal(state.saveNeedsReview.value, true)
  assert.equal(state.editorError.value, '')
  assert.match(state.conflictMessage.value, /Ваши изменения не потеряны/)
  assert.doesNotMatch(state.conflictMessage.value, /другим пользователем|сервер|версия/i)
  assert.equal(state.draft.value.description, 'Не потерять изменения')
  state.closeEditor()
  await vue.nextTick()
  assert.equal(state.version.value, 6)
})

test('Обновление карточки Free не загружает повреждения платного предпросмотра', async () => {
  const { props, reads } = createEditor({ previewOnly: true, maintenanceVersion: 5 })
  props.maintenanceVersion = 6
  await vue.nextTick()

  assert.equal(reads.length, 0)
})

test('Обновление карточки не подменяет версию подтверждённого к удалению повреждения', async () => {
  const { state, props, server } = createEditor({
    maintenanceVersion: 5,
    deleteDamage: async (args, currentServer) => {
      assert.equal(args[3], 5)
      assert.equal(currentServer.version, 6)
      throw new FakeIntakeDamageApiError('Конфликт версии', 409)
    }
  })
  server.damages.push({ id: 'damage-1', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.5, y: 0.5, description: 'До изменения', photoIds: [] })
  await state.loadDamages()
  state.askDelete(state.damages.value[0])
  server.damages = [{ ...server.damages[0], description: 'Уточнённый осмотр' }]
  server.version = 6
  props.maintenanceVersion = 6
  await vue.nextTick()

  assert.equal(state.version.value, 5)
  await state.confirmDelete()

  assert.equal(server.damages.length, 1)
  assert.equal(state.deleteCandidate.value, null)
  assert.equal(state.version.value, 6)
  assert.equal(state.damages.value[0].description, 'Уточнённый осмотр')
})

test('Удалённую при конфликте отметку нельзя перезаписать прежним черновиком', async () => {
  const { state, server, requests } = createEditor({
    saveDamage: async (_args, currentServer) => {
      currentServer.damages = []
      currentServer.version++
      throw new FakeIntakeDamageApiError('Конфликт версии', 409)
    }
  })
  const damage = { id: 'deleted', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.5, y: 0.5, description: 'Исходная отметка', photoIds: [] }
  server.damages.push(damage)
  await state.loadDamages()
  state.editDamage(damage)
  state.draft.value.description = 'Не потерять черновик'
  await state.saveDamage()

  assert.equal(state.saveNeedsReview.value, true)
  assert.equal(state.conflictDamage.value, null)
  assert.equal(state.canOverwriteConflict.value, false)
  await state.confirmConflictOverwrite()
  assert.equal(requests.length, 1)
  assert.equal(state.draft.value.description, 'Не потерять черновик')
})

test('Во время обновления осмотра нельзя открыть старую отметку для редактирования или удаления', async () => {
  let delay = false
  let release
  const { state, props, server } = createEditor({
    maintenanceVersion: 5,
    readDamages: async (_args, currentServer) => {
      if (delay) return new Promise(resolve => { release = resolve })
      return { version: currentServer.version, diagramVersion: currentServer.diagramVersion, damages: currentServer.damages }
    }
  })
  const damage = { id: 'existing', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.5, y: 0.5, description: 'Исходная отметка', photoIds: [] }
  server.damages.push(damage)
  await state.loadDamages()
  delay = true
  server.version = 6
  props.maintenanceVersion = 6
  await vue.nextTick()
  assert.equal(state.isLoading.value, true)
  state.editDamage(damage)
  state.askDelete(damage)
  assert.equal(state.draft.value, null)
  assert.equal(state.deleteCandidate.value, null)

  release({ version: 6, diagramVersion: 'sedan-v1', damages: [{ ...damage, description: 'Обновлённая отметка' }] })
  await new Promise(resolve => setImmediate(resolve))
  assert.equal(state.version.value, 6)
  state.editDamage(state.damages.value[0])
  assert.equal(state.draft.value.description, 'Обновлённая отметка')
})

test('Конфликт сохраняет черновик и перезаписывает сервер только после явного подтверждения', async () => {
  let shouldFail = true
  const { state, requests, server, emitted } = createEditor({
    saveDamage: async (args, currentServer) => {
      if (shouldFail) {
        shouldFail = false
        currentServer.version++
        currentServer.damages.push({ id: 'other-id', number: 1, viewCode: 'rear', damageType: 'dent', x: 0.2, y: 0.3, description: null, photoIds: [] })
        throw new FakeIntakeDamageApiError('Конфликт версии', 409)
      }
      currentServer.damages.push({ id: args[2], number: 2, ...args[5] })
      currentServer.version++
    }
  })
  await state.loadDamages()
  state.openNewAt({ x: 0.4, y: 0.6 })
  state.draft.value.description = 'Сохранить этот текст'
  const draftId = state.draftId.value

  await state.saveDamage()

  assert.equal(state.editorError.value, '')
  assert.equal(state.saveNeedsReview.value, true)
  assert.equal(state.draft.value.description, 'Сохранить этот текст')
  assert.equal(state.draftId.value, draftId)
  assert.equal(state.version.value, 5)
  await state.saveDamage()
  assert.equal(requests.length, 1)

  assert.equal(state.conflictSnapshot.value.version, 6)
  assert.equal(state.version.value, 5)
  assert.equal(state.draft.value.description, 'Сохранить этот текст')
  await state.confirmConflictOverwrite()

  assert.equal(requests.length, 2)
  assert.equal(requests[0][2], draftId)
  assert.equal(requests[1][2], draftId)
  assert.equal(requests[0][3], 5)
  assert.equal(requests[1][3], 6)
  assert.equal(state.draft.value, null)
  assert.equal(state.draftId.value, null)
  assert.equal(server.damages.length, 2)
  assert.deepEqual(emitted.filter(([name]) => name === 'changed'), [['changed']])
})

test('Неизвестный результат сверяется по стабильному ID и серверной записи', async () => {
  const { state, requests, server, emitted } = createEditor({
    saveDamage: async (args, currentServer) => {
      currentServer.damages.push({
        id: args[2], number: 1, ...args[5],
        description: args[5].description?.replace(/\s+/g, ' ').trim() ?? null
      })
      currentServer.version++
      throw new TypeError('Failed to fetch')
    }
  })
  await state.loadDamages()
  state.openProjection('front')
  state.openNewAt({ x: 0.4, y: 0.6 })
  state.draft.value.description = 'Не создавать\nдубль'
  const draftId = state.draftId.value

  await state.saveDamage()

  assert.equal(requests.length, 1)
  assert.equal(requests[0][2], draftId)
  assert.equal(requests[0][3], 5)
  assert.equal(state.draft.value, null)
  assert.equal(state.draftId.value, null)
  assert.equal(server.damages.length, 1)
  assert.equal(state.version.value, 6)
  assert.equal(state.projectionView.value, null)
  assert.equal(state.isEditorOpen.value, false)
  assert.deepEqual(emitted.filter(([name]) => name === 'changed'), [['changed']])
})

test('Не применившийся сетевой запрос можно повторить с прежней версией и тем же ID', async () => {
  let shouldFail = true
  const { state, requests, server } = createEditor({
    saveDamage: async (args, currentServer) => {
      if (shouldFail) {
        shouldFail = false
        throw new TypeError('Failed to fetch')
      }
      currentServer.damages.push({ id: args[2], number: 1, ...args[5] })
      currentServer.version++
    }
  })
  await state.loadDamages()
  state.openNewAt({ x: 0.5, y: 0.5 })
  const draftId = state.draftId.value
  await state.saveDamage()

  assert.ok(state.draft.value)
  assert.equal(state.saveNeedsReview.value, false)
  assert.equal(state.version.value, 5)
  await state.saveDamage()

  assert.equal(requests.length, 2)
  assert.deepEqual(requests.map(args => [args[2], args[3]]), [[draftId, 5], [draftId, 5]])
  assert.equal(server.damages.length, 1)
  assert.equal(state.draft.value, null)
})

test('В режиме только чтение показываются только фото повреждения', () => {
  const { state } = createEditor({
    photos: [
      { photoId: 'linked', stage: 2, url: '/linked.jpg' },
      { photoId: 'unrelated', stage: 1, url: '/unrelated.jpg' }
    ],
    readOnly: true
  })
  state.draft.value = { viewCode: 'front', damageType: 'scratch', x: 0.5, y: 0.5, description: '', photoIds: ['linked'] }

  assert.deepEqual(state.displayedPhotos.value.map(photo => photo.photoId), ['linked'])
})

test('409 при удалении обновляет список, но требует нового подтверждения пользователя', async () => {
  const { state, server } = createEditor({
    deleteDamage: async (_args, currentServer) => {
      currentServer.version++
      currentServer.damages.push({ id: 'fresh', number: 4, viewCode: 'rear', damageType: 'dent', x: 0.5, y: 0.5, description: null, photoIds: [] })
      throw new FakeIntakeDamageApiError('Конфликт версии', 409)
    }
  })
  server.damages.push({ id: 'old', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.5, y: 0.5, description: null, photoIds: [] })
  await state.loadDamages()
  state.askDelete(state.damages.value[0])
  await state.confirmDelete()

  assert.equal(state.deleteCandidate.value, null)
  assert.equal(state.version.value, 6)
  assert.deepEqual(state.damages.value.map(damage => damage.id), ['old', 'fresh'])
  assert.equal(state.deleteError.value, 'Запись изменилась. Проверьте повреждение и повторите удаление.')
})

test('Ошибка параллельной загрузки фото после смены записи не портит новый черновик', async () => {
  let rejectUpload
  const { state, props } = createEditor({
    uploadPhotos: () => new Promise((resolve, reject) => { rejectUpload = reject })
  })
  await state.loadDamages()
  state.openNewAt({ x: 0.5, y: 0.5 })
  const input = { files: [new File(['photo'], 'photo.jpg')], value: 'photo.jpg' }
  const uploading = state.uploadDraftPhotos({ target: input })
  props.maintenanceId = 'order-2'
  await vue.nextTick()
  state.openNewAt({ x: 0.2, y: 0.3 })
  rejectUpload(new Error('Старая запись недоступна'))
  await uploading

  assert.equal(state.editorError.value, '')
  assert.deepEqual(state.draft.value && { x: state.draft.value.x, y: state.draft.value.y }, { x: 0.2, y: 0.3 })
})


test('После удаления редактор и увеличенный ракурс закрыты, а сохранить удалённую метку нельзя', async () => {
  const { state, requests, server } = createEditor()
  const damage = { id: 'old', number: 1, viewCode: 'rear', damageType: 'scratch', x: 0.5, y: 0.5, description: 'Удалить', photoIds: ['photo-1'] }
  server.damages.push(damage)
  await state.loadDamages()
  state.editDamage(damage)
  state.askDelete(damage)
  await state.saveDamage()
  assert.equal(requests.length, 0)

  await state.confirmDelete()

  assert.equal(state.deleteCandidate.value, null)
  assert.equal(state.draft.value, null)
  assert.equal(state.editingId.value, null)
  assert.equal(state.draftId.value, null)
  assert.equal(state.isEditorOpen.value, false)
  assert.equal(state.projectionView.value, null)
  assert.equal(server.damages.length, 0)
  await state.saveDamage()
  assert.equal(requests.length, 0)
})

test('Ошибка удаления и отмена подтверждения оставляют черновик для просмотра', async () => {
  const { state } = createEditor({ deleteDamage: async () => { throw new Error('Нет соединения') } })
  const damage = { id: 'old', number: 1, viewCode: 'rear', damageType: 'chip', x: 0.3, y: 0.4, description: 'Скол', photoIds: [] }
  await state.loadDamages()
  state.editDamage(damage)
  state.askDelete(damage)
  await state.confirmDelete()

  assert.equal(state.deleteCandidate.value.id, damage.id)
  assert.equal(state.draft.value.description, 'Скол')
  assert.equal(state.isEditorOpen.value, true)
  assert.equal(state.deleteError.value, 'Нет соединения')
  state.deleteCandidate.value = null
  assert.equal(state.editingId.value, damage.id)
  assert.equal(state.draft.value.description, 'Скол')
})

test('Отмена увеличенного ракурса при переносе возвращает исходный черновик', async () => {
  const { state } = createEditor()
  await state.loadDamages()
  state.openProjection('front')
  state.openNewAt({ x: 0.2, y: 0.7 }, 'front')
  state.draft.value.description = 'Не терять описание'
  state.draft.value.photoIds = ['photo-1']
  const original = structuredClone({ ...state.draft.value, photoIds: [...state.draft.value.photoIds] })
  state.startRepositioning()
  assert.equal(state.projectionView.value, 'front')
  assert.equal(state.isEditorOpen.value, false)
  state.closeProjection()
  assert.equal(state.isEditorOpen.value, true)
  assert.equal(state.isRepositioning.value, false)
  assert.deepEqual(state.draft.value, original)
})


test('Конфликт удаления не переносит свежую версию на старый черновик редактора', async () => {
  const { state, requests, server } = createEditor({
    deleteDamage: async (_args, currentServer) => {
      currentServer.version++
      currentServer.damages = currentServer.damages.map(damage => ({ ...damage, description: 'Параллельное уточнение' }))
      throw new FakeIntakeDamageApiError('Конфликт версии', 409)
    }
  })
  const damage = { id: 'old', number: 1, viewCode: 'rear', damageType: 'chip', x: 0.3, y: 0.4, description: 'Исходное описание', photoIds: [] }
  server.damages.push(damage)
  await state.loadDamages()
  state.editDamage(damage)
  state.askDelete(damage)
  await state.confirmDelete()

  assert.equal(state.deleteCandidate.value, null)
  assert.equal(state.version.value, 5)
  assert.equal(state.conflictSnapshot.value.version, 6)
  assert.equal(state.draft.value.description, 'Исходное описание')
  assert.equal(state.saveNeedsReview.value, true)
  await state.saveDamage()
  assert.equal(requests.length, 0)
  assert.equal(server.damages[0].description, 'Параллельное уточнение')
})


test('Сохранение отправляет версию схемы и закрывает оба окна', async () => {
  const { state, requests } = createEditor()
  await state.loadDamages()
  state.openProjection('rear')
  state.openNewAt({ x: 0.4, y: 0.6 }, 'rear')
  await state.saveDamage()

  assert.equal(state.projectionView.value, null)
  assert.equal(state.isEditorOpen.value, false)
  assert.equal(state.draft.value, null)

  assert.equal(requests[0][4], 'sedan-v1')
  assert.equal(state.diagramVersion.value, 'sedan-v1')
})

test('Тестовая модель схемы считает загруженными все четыре отдельные проекции', async () => {
  const { state } = createEditor()
  await state.loadDamages()

  for (const viewCode of ['front', 'rear', 'left', 'right']) {
    assert.equal(state.diagramImageStatus(viewCode), 'loaded')
    assert.equal(state.isDiagramViewReady(viewCode), true)
  }
})

test('Изображения схемы блокируют размещение при загрузке и ошибке, после повтора разрешают его', async () => {
  const { state } = createEditor({ autoLoadImages: false })
  await state.loadDamages()

  assert.equal(state.diagramImageStatus('front'), 'loading')
  assert.equal(state.isDiagramViewReady('front'), false)
  state.openProjection('front')
  assert.equal(state.projectionView.value, null)

  state.handleDiagramImageError('front', 'sedan-v1', 0)
  assert.equal(state.diagramImageStatus('front'), 'error')
  state.retryDiagramImage('front')
  assert.equal(state.diagramImageStatus('front'), 'loading')
  assert.equal(state.diagramImageAttempt('front'), 1)
  state.handleDiagramImageLoad('front', 'sedan-v1', 0)
  assert.equal(state.isDiagramViewReady('front'), false)

  state.handleDiagramImageLoad('front', 'sedan-v1', 1)
  assert.equal(state.isDiagramViewReady('front'), true)
  state.openProjection('front')
  assert.equal(state.projectionView.value, 'front')
})

test('Неизвестная или отсутствующая версия схемы закрывает изменения, сохраняя просмотр повреждений', async () => {
  for (const diagramVersion of ['future-diagram-v9', null]) {
    const { state, server } = createEditor({ diagramVersion })
    const damage = { id: 'old', number: 3, viewCode: 'rear', damageType: 'dent', x: 0.4, y: 0.5, description: 'Сохранённый дефект', photoIds: [] }
    server.damages.push(damage)
    await state.loadDamages()

    assert.equal(state.diagramDefinition.value, null)
    assert.equal(state.canEdit.value, false)
    assert.match(state.diagramError.value, /Схема|версию схемы/)
    assert.equal(state.damages.value.length, 1)
    state.editDamage(damage)
    assert.equal(state.isEditorOpen.value, true)
    assert.equal(state.isReadOnly.value, true)
    state.openProjection('rear')
    assert.equal(state.projectionView.value, null)
    state.openNewAt({ x: 0.5, y: 0.5 }, 'front')
    assert.equal(state.draft.value.description, 'Сохранённый дефект')
  }
})

test('Смена версии схемы при конфликте запрещает перезапись старых координат', async () => {
  const { state, requests, server } = createEditor({
    saveDamage: async (_args, currentServer) => {
      currentServer.diagramVersion = 'future-diagram-v9'
      currentServer.version++
      throw new FakeIntakeDamageApiError('Конфликт версии', 409)
    }
  })
  await state.loadDamages()
  state.openNewAt({ x: 0.3, y: 0.7 }, 'left')
  state.draft.value.description = 'Не терять черновик'
  await state.saveDamage()

  assert.equal(state.diagramVersion.value, 'sedan-v1')
  assert.equal(state.conflictSnapshot.value.diagramVersion, 'future-diagram-v9')
  assert.equal(state.canOverwriteConflict.value, false)
  assert.match(state.conflictMessage.value, /прежней схеме/)
  assert.equal(state.draft.value.description, 'Не терять черновик')
  await state.confirmConflictOverwrite()
  assert.equal(requests.length, 1)
  assert.equal(server.damages.length, 0)
  state.projectionView.value = 'left'
  state.confirmConflictRefresh()
  assert.equal(state.projectionView.value, null)
  assert.equal(state.draft.value, null)
  assert.equal(state.diagramDefinition.value, null)
})

test('Сравнение конфликта показывает номер из соответствующего снимка осмотра', async () => {
  const { state, server } = createEditor()
  const first = { id: 'first', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.4, y: 0.5, description: 'Первое', photoIds: [] }
  const second = { ...first, id: 'second', number: 3, viewCode: 'left', description: 'Второе' }
  const edited = { ...first, id: 'edited', number: 4, viewCode: 'rear', description: 'Третье' }
  server.damages.push(first, second, edited)
  await state.loadDamages()
  state.editDamage(edited)
  state.conflictSnapshot.value = { version: 6, diagramVersion: 'sedan-v1', damages: [second, edited] }

  assert.equal(state.editingDamageNumber.value, 3)
  assert.match(state.damageSummary(state.draft.value), /^№3,/)
  assert.equal(state.conflictDamage.value.displayNumber, 2)
  assert.match(state.damageSummary(state.conflictDamage.value), /^№2,/)
  assert.equal(server.damages[2].number, 4)
})

test('Registry ссылается только на четыре отдельных изображения схемы sedan-v1', () => {
  const manifestPath = new URL('../packages/workspace/src/assets/intake/sedan-v1/diagram.json', import.meta.url)
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
  const registryPath = new URL('../packages/workspace/src/components/intakeDiagramRegistry.ts', import.meta.url)
  const registryCode = ts.transpileModule(readFileSync(registryPath, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const assetUrls = {
    '../assets/intake/sedan-v1/front.png': '/sedan-v1/front.png',
    '../assets/intake/sedan-v1/rear.png': '/sedan-v1/rear.png',
    '../assets/intake/sedan-v1/left.png': '/sedan-v1/left.png',
    '../assets/intake/sedan-v1/right.png': '/sedan-v1/right.png'
  }
  new Function('require', 'module', 'exports', registryCode)((id) => {
    if (id === '../assets/intake/sedan-v1/diagram.json') return { default: manifest }
    assert.ok(assetUrls[id], 'Unexpected registry asset import')
    return { default: assetUrls[id] }
  }, module, module.exports)

  assert.deepEqual(Object.keys(module.exports.intakeDiagramRegistry), ['sedan-v1'])
  const diagram = module.exports.getIntakeDiagram('sedan-v1')
  assert.equal(diagram.version, 'sedan-v1')
  assert.equal(diagram.width, 1619)
  assert.equal(diagram.height, 971)
  assert.equal(diagram.aspectRatio, '1619 / 971')
  assert.deepEqual(Object.fromEntries(Object.entries(diagram.views).map(([view, item]) => [view, item.file])), {
    front: 'front.png', rear: 'rear.png', left: 'left.png', right: 'right.png'
  })
  assert.deepEqual(Object.fromEntries(Object.entries(diagram.views).map(([view, item]) => [view, item.imageUrl])), {
    front: '/sedan-v1/front.png', rear: '/sedan-v1/rear.png', left: '/sedan-v1/left.png', right: '/sedan-v1/right.png'
  })
  assert.deepEqual(Object.fromEntries(Object.entries(diagram.views).map(([view, item]) => [view, item.sha256])), {
    front: manifest.views.front.sha256, rear: manifest.views.rear.sha256,
    left: manifest.views.left.sha256, right: manifest.views.right.sha256
  })
  for (const version of ['unknown', 'constructor', 'toString', '__proto__', '', null]) {
    assert.equal(module.exports.getIntakeDiagram(version), null)
  }
})

test('Manifest sedan-v1 соответствует размерам четырёх отдельных PNG с SHA-256', () => {
  const manifestPath = new URL('../packages/workspace/src/assets/intake/sedan-v1/diagram.json', import.meta.url)
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'))
  assert.equal(manifest.version, 'sedan-v1')
  assert.equal(manifest.width, 1619)
  assert.equal(manifest.height, 971)
  assert.deepEqual(Object.keys(manifest.views), ['front', 'rear', 'left', 'right'])
  const hashes = []
  for (const view of ['front', 'rear', 'left', 'right']) {
    const entry = manifest.views[view]
    assert.equal(entry.file, view + '.png')
    const image = readFileSync(new URL('../packages/workspace/src/assets/intake/sedan-v1/' + entry.file, import.meta.url))
    assert.equal(image.readUInt32BE(16), manifest.width)
    assert.equal(image.readUInt32BE(20), manifest.height)
    const digest = createHash('sha256').update(image).digest('hex').toUpperCase()
    assert.equal(digest, entry.sha256)
    hashes.push(digest)
  }
  assert.equal(new Set(hashes).size, 4)
})

test('API передаёт закреплённую версию схемы в JSON PUT', async () => {
  const apiPath = new URL('../packages/workspace/src/api/intakeDamages.ts', import.meta.url)
  const apiCode = ts.transpileModule(readFileSync(apiPath, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const apiModule = { exports: {} }
  new Function('require', 'module', 'exports', apiCode)((id) => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === '@opencrm/workspace/workspaceApiMessages') return {}
    assert.equal(id, './maintenance')
    return { getMutationHeaders: () => ({ 'X-Test-Token': 'token', 'Content-Type': 'application/json' }) }
  }, apiModule, apiModule.exports)

  const previousWindow = globalThis.window
  const previousFetch = globalThis.fetch
  let request
  globalThis.window = { location: { origin: 'http://localhost' } }
  globalThis.fetch = async (_url, options) => {
    request = options
    return { ok: true }
  }
  try {
    await apiModule.exports.saveIntakeDamage('org-1', 'maintenance-1', 'damage-1', 12, 'sedan-v1', {
      viewCode: 'left', damageType: 'dent', x: 0.3, y: 0.7, description: 'Вмятина', photoIds: ['photo-1']
    })
  } finally {
    globalThis.window = previousWindow
    globalThis.fetch = previousFetch
  }
  assert.equal(request.method, 'PUT')
  assert.deepEqual(JSON.parse(request.body), {
    version: 12, diagramVersion: 'sedan-v1', viewCode: 'left', damageType: 'dent',
    x: 0.3, y: 0.7, description: 'Вмятина', photoIds: ['photo-1']
  })
})


test('Номера остаются последовательными между ракурсами и после удаления, фото сохраняют исходные ID', async () => {
  const { state, server } = createEditor()
  server.damages.push(
    { id: 'damage-a', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.2, y: 0.3, description: null, photoIds: ['photo-uuid-a'] },
    { id: 'damage-b', number: 3, viewCode: 'left', damageType: 'dent', x: 0.4, y: 0.5, description: null, photoIds: ['photo-uuid-b'] },
    { id: 'damage-c', number: 4, viewCode: 'rear', damageType: 'chip', x: 0.6, y: 0.7, description: null, photoIds: [] },
    { id: 'damage-d', number: 5, viewCode: 'right', damageType: 'other', x: 0.8, y: 0.9, description: null, photoIds: ['photo-uuid-d'] }
  )
  await state.loadDamages()

  assert.deepEqual(state.displayedDamages.value.map(damage => [damage.id, damage.displayNumber]), [
    ['damage-a', 1], ['damage-b', 2], ['damage-c', 3], ['damage-d', 4]
  ])
  assert.equal(state.damagesForView('right')[0].displayNumber, 4)
  assert.equal(state.linkedPhotoIds.value.includes('photo-uuid-d'), true)

  state.askDelete(server.damages[1])
  await state.confirmDelete()

  assert.deepEqual(state.displayedDamages.value.map(damage => [damage.id, damage.displayNumber]), [
    ['damage-a', 1], ['damage-c', 2], ['damage-d', 3]
  ])
  assert.deepEqual(server.damages.find(damage => damage.id === 'damage-d').photoIds, ['photo-uuid-d'])
  state.editDamage(server.damages.find(damage => damage.id === 'damage-d'))
  assert.equal(state.editorTitle.value, 'Повреждение №3')
})
