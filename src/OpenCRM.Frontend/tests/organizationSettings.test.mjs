import { regionalTestDependencies } from './regionalTestDependencies.mjs'
import '../packages/workspace/src/settingsMessages.ts'
import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as maintenanceWorkflow from '../packages/workspace/src/utils/maintenanceWorkflow.ts'
import { renderToString } from '@vue/server-renderer'

const { descriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceSettingsSection.vue', import.meta.url), 'utf8'))
const code = ts.transpileModule(compileScript(descriptor, { id: 'organization-settings' }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
const { descriptor: notificationDescriptor } = parse(readFileSync(new URL('../packages/workspace/src/components/WorkspaceNotificationSettings.vue', import.meta.url), 'utf8'))
const notificationCode = ts.transpileModule(compileScript(notificationDescriptor, { id: 'organization-notification-settings', inlineTemplate: true }).content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText
function setup(hasWorkingHours = false) {
  const props = vue.reactive({
    settingsTitle: 'Настройки организации', scenarioTitle: 'Автосервис',
    isReadOnly: false, isFreePlan: false, selectedSection: 'profile', isSaving: false, isEditingProfile: false, isEditingRequisites: false,
    settings: {
      organization: { id: 'org', currentUserRoleCode: 'owner', documentStyleCode: 'minimal' },
      workspaceFeatures: [
        { code: 'workspace.module.members', boolValue: true },
        { code: 'workspace.documents.styling', boolValue: true },
        { code: 'workspace.data.import', boolValue: true }
      ],
      canManageProfile: true, canManageRequisites: true, canArchive: true
    }
  })
  const events = []
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => id === 'vue' ? { ...vue, useSlots: () => ({ setup() {}, imports() {}, ...(hasWorkingHours ? { workingHours() {} } : {}) }) } : id === '../utils/maintenanceWorkflow' ? maintenanceWorkflow : regionalTestDependencies[id] ?? {}, module, module.exports)
  const state = module.exports.default.setup(props, { expose() {}, emit: (...args) => events.push(args) })
  return { state, props, events }
}

test('Уведомления — один раздел, каналы не дублируют меню; импорт зависит от своего доступа', () => {
  const { state } = setup()
  assert.equal(state.sections.value.some(section => section.code === 'notifications'), true)
  assert.equal(state.sections.value.some(section => section.code === 'telegram' || section.code === 'max'), false)
  assert.equal(state.sections.value.find(section => section.code === 'documents').isPro, false)
  assert.equal(state.sections.value.some(section => section.code === 'imports'), false)
  state.canImport.value = true
  assert.equal(state.sections.value.some(section => section.code === 'imports'), true)
})

test('Free показывает отметки PRO и раздел импорта, не запуская импорт до разрешённого тарифа', () => {
  const { state, props } = setup()
  props.isFreePlan = true
  props.messagingAvailable = false
  props.settings.workspaceFeatures.forEach(feature => { feature.boolValue = false })
  assert.equal(state.sections.value.find(section => section.code === 'documents').isPro, true)
  assert.equal(state.sections.value.find(section => section.code === 'notifications').isPro, true)
  assert.equal(state.sections.value.find(section => section.code === 'imports').isPro, true)
  assert.equal(state.isSelfAssignmentLocked.value, true)
  assert.equal(state.isDocumentStylingLocked.value, true)
  assert.equal(state.isDataImportLocked.value, true)
  props.settings.canManageProfile = false
  assert.equal(state.sections.value.some(section => section.code === 'imports'), false)
})

test('Предпросмотр уведомлений Free не монтирует компоненты, загружающие настройки', async () => {
  const module = { exports: {} }
  let settingsMounts = 0
  const choices = vue.defineComponent({
    props: ['title', 'groups'],
    setup: props => () => vue.h('section', [
      vue.h('h2', props.title),
      ...props.groups.flatMap(group => group.items.map(item => vue.h('div', item.title)))
    ])
  })
  const channelSettings = vue.defineComponent({
    setup: () => {
      settingsMounts++
      return () => vue.h('div', 'Рабочие настройки канала')
    }
  })
  const button = vue.defineComponent({ setup: (_, { slots }) => () => vue.h('button', slots.default?.()) })
  new Function('require', 'module', 'exports', notificationCode)(id => {
    if (id === 'vue') return vue
    if (id === '@opencrm/ui') return { UiButton: button, useLocalization: () => ({ t: key => ({
      'notificationSettings.title': 'Уведомления клиентам',
      'notificationSettings.visitReminder': 'Напоминание о визите',
      'notificationChannel.telegram': 'Telegram',
      'notificationChannel.max': 'MAX'
    })[key] ?? key }) }
    if (id === '../notificationMessages') return {}
    if (id === './WorkspaceSettingsChoices.vue') return { default: choices }
    return { default: channelSettings }
  }, module, module.exports)

  const html = await renderToString(vue.createSSRApp({
    render: () => vue.h(module.exports.default, { organizationId: 'org', canWrite: false, previewOnly: true })
  }))

  assert.match(html, /Уведомления клиентам/)
  assert.match(html, /Напоминание о визите/)
  assert.match(html, /Telegram/)
  assert.match(html, /MAX/)
  assert.equal(settingsMounts, 0)
})

test('Архив скрыт, прямой переход возвращает к профилю; чужие каналы скрыты', async () => {
  const { state, props, events } = setup()
  props.isReadOnly = true
  assert.equal(state.canEditProfile.value, false)
  assert.equal(state.canEditRequisites.value, false)
  props.selectedSection = 'archive'
  await vue.nextTick()
  assert.equal(state.sections.value.some(section => section.code === 'archive'), false)
  assert.equal(state.activeSection.value, 'profile')
  assert.deepEqual(events, [['selectSection', 'profile']])
  props.settings.organization.currentUserRoleCode = 'employee'
  props.selectedSection = 'notifications'
  assert.equal(state.canReadNotifications.value, false)
  assert.equal(state.activeSection.value, 'profile')
  assert.equal(state.sections.value.some(section => section.code === 'export'), false)
})

test('Редактирование документов и режима работы не переключает экран на общие настройки', () => {
  const { state, props } = setup()
  props.selectedSection = 'documents'
  props.isEditingProfile = true
  assert.equal(state.activeSection.value, 'documents')
  props.selectedSection = 'workflow'
  assert.equal(state.activeSection.value, 'workflow')
  props.isEditingRequisites = true
  assert.equal(state.activeSection.value, 'requisites')
})

test('Переключение разделов блокируется при несохранённом профиле или отправке любого канала', () => {
  const { state, props, events } = setup()
  props.isEditingProfile = true
  state.selectSection('documents')
  props.isEditingProfile = false
  state.notificationBusy.value.telegram = true
  state.selectSection('requisites')
  assert.equal(events.length, 0)
  state.notificationBusy.value.telegram = false
  state.selectSection('requisites')
  assert.deepEqual(events[0], ['selectSection', 'requisites'])
})

test('Каналы монтируются после открытия; смена организации сбрасывает доступ импорта и старую занятость', async () => {
  const { state, props } = setup()
  assert.equal(state.notificationsVisited.value, false)
  props.selectedSection = 'notifications'
  await vue.nextTick()
  assert.equal(state.notificationsVisited.value, true)
  props.selectedSection = 'profile'
  await vue.nextTick()
  assert.equal(state.notificationsVisited.value, true)
  state.canImport.value = true
  state.notificationBusy.value.max = true
  props.settings.organization.id = 'other-org'
  await vue.nextTick()
  assert.equal(state.canImport.value, false)
  assert.deepEqual(state.notificationBusy.value, {})
  assert.equal(state.notificationsVisited.value, false)
})

test('Импорт виден в режиме чтения без права записи; архив скрыт', async () => {
  const { state, props, events } = setup()
  props.isReadOnly = true
  props.settings.canManageProfile = false
  props.settings.canManageRequisites = false
  props.settings.canArchive = false
  assert.equal(state.canImport.value, false)
  assert.equal(state.sections.value.some(section => section.code === 'imports'), true)
  state.selectSection('imports')
  assert.deepEqual(events, [['selectSection', 'imports']])
  props.selectedSection = 'archive'
  await vue.nextTick()
  assert.equal(state.activeSection.value, 'profile')
  assert.equal(state.sections.value.some(section => section.code === 'archive'), false)
  assert.deepEqual(events, [['selectSection', 'imports'], ['selectSection', 'profile']])
})

test('Правила открывают редактирование; ошибка сохраняет форму, успешное сохранение возвращает к выбору', async () => {
  const { state, props, events } = setup()
  props.selectedSection = 'workflow'
  await state.selectWorkflowSection('rules')
  assert.equal(state.workflowSection.value, 'rules')
  assert.deepEqual(events, [['startProfileEdit']])
  props.isEditingProfile = true
  await vue.nextTick()
  props.isSaving = true
  await state.selectWorkflowSection(null)
  assert.equal(state.workflowSection.value, 'rules')
  props.isSaving = false
  await vue.nextTick()
  assert.equal(state.workflowSection.value, 'rules')
  props.isEditingProfile = false
  await vue.nextTick()
  assert.equal(state.workflowSection.value, null)
})

test('Открытый график блокирует смену вкладки; сохранение блокирует возврат, отмена освобождает переходы', async () => {
  const { state, props, events } = setup(true)
  props.selectedSection = 'workflow'
  await state.selectWorkflowSection('hours')
  state.selectSection('documents')
  assert.deepEqual(events, [])
  state.hoursSaving.value = true
  await state.selectWorkflowSection(null)
  assert.equal(state.workflowSection.value, 'hours')
  state.hoursSaving.value = false
  await state.selectWorkflowSection(null)
  state.selectSection('documents')
  assert.deepEqual(events, [['selectSection', 'documents']])
})

test('Без редактора графика переход к нему недоступен; правила в режиме чтения не начинают изменение', async () => {
  const { state, props, events } = setup()
  props.isReadOnly = true
  await state.selectWorkflowSection('hours')
  assert.equal(state.workflowSection.value, null)
  await state.selectWorkflowSection('rules')
  assert.equal(state.workflowSection.value, 'rules')
  assert.deepEqual(events, [])
})

test('Free показывает структуру импорта без монтирования рабочей формы', async () => {
  const compiled = ts.transpileModule(compileScript(descriptor, { id: 'organization-settings-render', inlineTemplate: true }).content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  const button = vue.defineComponent({
    inheritAttrs: false,
    props: ['disabled', 'variant'],
    setup: (props, { slots, attrs }) => () => vue.h('button', { ...attrs, disabled: props.disabled }, slots.default?.())
  })
  const placeholder = vue.defineComponent({ setup: () => () => null })
  const proLock = vue.defineComponent({
    props: ['locked', 'message'],
    setup: (lockProps, { slots }) => () => vue.h('div', { class: 'workspace-pro-lock' }, [
      vue.h('div', { class: 'workspace-pro-lock__content' }, slots.default?.()),
      lockProps.locked ? vue.h('a', { href: '#billing' }, lockProps.message) : null
    ])
  })
  new Function('require', 'module', 'exports', compiled)(id => {
    if (id in regionalTestDependencies) return regionalTestDependencies[id]
    if (id === 'vue') return vue
    if (id === '../utils/maintenanceWorkflow') return maintenanceWorkflow
    if (id === '@opencrm/ui') return { UiButton: button, UiInput: placeholder, UiSelect: placeholder, UiTextarea: placeholder, UiPhoneInput: placeholder }
    if (id === './WorkspaceProLock.vue') return { default: proLock }
    return { default: placeholder }
  }, module, module.exports)
  const { props } = setup()
  Object.assign(props, { isReadOnly: true, isFreePlan: true, messagingAvailable: false, selectedSection: 'imports', profileForm: {}, requisitesForm: {} })
  props.settings.workspaceFeatures.forEach(feature => { feature.boolValue = false })
  Object.assign(props.settings, { canManageProfile: false, canManageRequisites: false })
  let importMounts = 0
  const imports = vue.defineComponent({ setup() { importMounts++; return () => vue.h('p', 'Рабочая форма импорта') } })
  const render = () => renderToString(vue.createSSRApp({ render: () => vue.h(module.exports.default, props, { imports: () => vue.h(imports) }) }))
  const html = await render()
  assert.match(html, /Импорт данных доступен в PRO/)
  assert.match(html, /CSV, XLSX или ZIP/)
  assert.match(html, /<input[^>]*type="file"[^>]*disabled/)
  assert.match(html, /<button[^>]*disabled[^>]*>Выбрать файл<\/button>/)
  assert.match(html, /organization-settings__pro-badge/)
  assert.doesNotMatch(html, /Архив организации/)
  assert.equal(importMounts, 0)
  props.settings.canManageProfile = true
  props.selectedSection = 'notifications'
  await vue.nextTick()
  assert.match(await render(), /Уведомления доступны в PRO/)
  assert.equal(importMounts, 0)
})
