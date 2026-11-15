<script setup lang="ts">
import { computed, nextTick, ref, useSlots, watch } from 'vue'
import { UiButton, UiInput, UiSelect, UiTextarea, UiPhoneInput } from '@opencrm/ui'
import type { WorkspaceSettingsData } from '../api/workspaceAdmin'
import WorkspaceNotificationSettings from './WorkspaceNotificationSettings.vue'
import WorkspaceSettingsChoices from './WorkspaceSettingsChoices.vue'
import WorkspaceOrganizationExport from './WorkspaceOrganizationExport.vue'
import WorkspaceProLock from './WorkspaceProLock.vue'
import OrganizationTimeZoneSettings from './OrganizationTimeZoneSettings.vue'
import { supportsMaintenanceWorkflow } from '../utils/maintenanceWorkflow'

const props = defineProps<{
  isReadOnly: boolean
  messagingAvailable?: boolean
  selectedSection: string
  isFreePlan?: boolean
  canViewWiki?: boolean
  settings: WorkspaceSettingsData
  settingsTitle: string
  scenarioTitle: string
  isSaving: boolean
  isEditingProfile: boolean
  isEditingRequisites: boolean
  requisitesErrors?: Record<string, string[]>
  profileForm: {
    name: string
    description: string
    primaryScenarioCode: string
    documentStyleCode: string
    maintenanceWorkflowMode: string
    allowWorkSelfAssignment: boolean
  }
  requisitesForm: {
    legalName: string
    inn: string
    kpp: string
    ogrn: string
    legalAddress: string
    actualAddress: string
    phone: string
    email: string
    signatoryPosition: string
    signatoryName: string
    signatoryBasis: string
  }
}>()

const emit = defineEmits<{
  selectSection: [section: string]
  startProfileEdit: []
  startRequisitesEdit: []
  saveProfile: []
  saveRequisites: []
  cancelProfileEdit: []
  cancelRequisitesEdit: []
  archiveOrganization: []
  timeZoneSaved: []
}>()

type SettingsSection = 'profile' | 'workflow' | 'documents' | 'notifications' | 'requisites' | 'imports' | 'export' | 'archive'
type SettingsMenuItem = { code: SettingsSection, title: string, group: string, description: string, isPro: boolean }
const slots = useSlots()
const canImport = ref(false)
const notificationBusy = ref<Record<string, boolean>>({})
const notificationsVisited = ref(false)
const workflowSection = ref<'rules' | 'hours' | null>(null)
const hoursSaving = ref(false)
const timeZoneBusy = ref(false)
const workflowChoices = ref<InstanceType<typeof WorkspaceSettingsChoices> | null>(null)
const workflowBack = ref<InstanceType<typeof UiButton> | null>(null)
const isOwner = computed(() => props.settings.organization.currentUserRoleCode === 'owner')
const isFreePlan = computed(() => props.isFreePlan === true)
const canEditProfile = computed(() => props.settings.canManageProfile && !props.isReadOnly)
const canEditRequisites = computed(() => props.settings.canManageRequisites && !props.isReadOnly)
const canReadNotifications = computed(() => isOwner.value && props.settings.canManageProfile)
const isSelfAssignmentLocked = computed(() => !hasWorkspaceFeature('workspace.module.members'))
const isDocumentStylingLocked = computed(() => !hasWorkspaceFeature('workspace.documents.styling'))
const isDataImportLocked = computed(() => !hasWorkspaceFeature('workspace.data.import'))
const isMessagingLocked = computed(() => props.messagingAvailable === false)
const isBusy = computed(() => props.isSaving || hoursSaving.value || timeZoneBusy.value || Object.values(notificationBusy.value).some(Boolean))
const isSectionLocked = computed(() => isBusy.value || props.isEditingProfile || props.isEditingRequisites || workflowSection.value === 'hours')
const workflowGroups = computed(() => [{ items: [
  { code: 'rules', title: 'Правила работы', symbol: 'rules', description: 'Режим заказ-нарядов и самостоятельное назначение мастеров.',
    detail: [supportsMaintenanceWorkflow(props.settings.organization) ? (props.settings.organization.maintenanceWorkflowMode === 'simple' ? 'Простой режим' : 'Работа по этапам') : '',
      props.settings.organization.allowWorkSelfAssignment ? 'Мастера могут брать работы' : 'Ручное назначение работ'].filter(Boolean).join(' · ') },
  ...(slots.workingHours ? [{ code: 'hours', title: 'Рабочие часы и перерывы', symbol: 'clock', description: 'Рабочие дни, время открытия и закрытия, перерывы.' }] : [])
] }])
const sections = computed<SettingsMenuItem[]>(() => [
  { code: 'profile', title: 'Общие', group: 'Организация', description: 'Название, описание и часовой пояс', isPro: false },
  { code: 'requisites', title: 'Реквизиты и контакты', group: 'Организация', description: 'Юридические данные и связь', isPro: false },
  { code: 'workflow', title: 'Работа и расписание', group: 'Рабочий процесс', description: 'Режим работы и назначения', isPro: false },
  { code: 'documents', title: 'Документы', group: 'Рабочий процесс', description: 'Оформление печатных форм', isPro: isFreePlan.value && isDocumentStylingLocked.value },
  ...(canReadNotifications.value ? [{ code: 'notifications' as const, title: 'Уведомления', group: 'Связь с клиентами', description: 'Telegram и MAX', isPro: isFreePlan.value && isMessagingLocked.value }] : []),
  ...(slots.imports && (props.isReadOnly || canImport.value || isDataImportLocked.value && props.settings.canManageProfile)
    ? [{ code: 'imports' as const, title: 'Импорт данных', group: 'Данные', description: 'Загрузка в организацию', isPro: isFreePlan.value && isDataImportLocked.value }]
    : []),
  ...(isOwner.value ? [{ code: 'export' as const, title: 'Экспорт данных', group: 'Данные', description: 'Резервная выгрузка', isPro: false }] : []),
])
const menuGroups = computed(() => Array.from(new Set(sections.value.map(section => section.group)))
  .map(title => ({ title, items: sections.value.filter(section => section.group === title) })))
const activeSection = computed<SettingsSection>(() => {
  if (props.isEditingRequisites) return 'requisites'
  if (props.isEditingProfile) return props.selectedSection === 'workflow' || props.selectedSection === 'documents' ? props.selectedSection : 'profile'
  if (props.selectedSection === 'archive') return 'profile'
  return sections.value.find(section => section.code === props.selectedSection)?.code ?? 'profile'
})
const documentOptions = [
  { value: 'auto', label: 'Автоматическое' },
  { value: 'detailing', label: 'Детейлинг' },
  { value: 'fleet', label: 'Универсальное' },
  { value: 'minimal', label: 'Минимальное' }
]
const documentStyleName = computed(() => documentOptions.find(option => option.value === props.settings.organization.documentStyleCode)?.label ?? 'Автоматическое')
const requisitesLimits: Partial<Record<string, number>> = { legalName: 240, legalAddress: 400, actualAddress: 400, phone: 40, email: 320, signatoryPosition: 120, signatoryName: 160, signatoryBasis: 160 }
const requisitesHelp: Record<string, string> = {
  inn: 'ИНН юридического лица — 10 цифр, ИП или физического лица — 12 цифр.',
  kpp: 'КПП организации — 9 знаков. ИП и физические лица оставляют поле пустым.',
  ogrn: 'ОГРН юридического лица — 13 цифр, ОГРНИП индивидуального предпринимателя — 15 цифр. Физические лица оставляют поле пустым.'
}
const requisitesGroups = [
  { title: 'Юридические данные', fields: [{ key: 'legalName', label: 'Юридическое название' }, { key: 'inn', label: 'ИНН' }, { key: 'kpp', label: 'КПП' }, { key: 'ogrn', label: 'ОГРН / ОГРНИП' }] },
  { title: 'Контакты и адреса', fields: [{ key: 'phone', label: 'Телефон' }, { key: 'email', label: 'Email' }, { key: 'legalAddress', label: 'Юридический адрес' }, { key: 'actualAddress', label: 'Фактический адрес' }] },
  { title: 'Подпись в документах', fields: [{ key: 'signatoryPosition', label: 'Должность подписанта' }, { key: 'signatoryName', label: 'Подписант' }, { key: 'signatoryBasis', label: 'Основание подписи' }] }
] as const

function hasWorkspaceFeature(code: string): boolean {
  return props.settings.workspaceFeatures?.some(feature => feature.code === code && feature.boolValue === true) ?? false
}

function selectSection(section: SettingsSection) {
  if (isSectionLocked.value) return
  if (!sections.value.some(item => item.code === section)) return
  emit('selectSection', section)
}

async function selectWorkflowSection(section: 'rules' | 'hours' | null) {
  if (isBusy.value) return
  if (section === 'hours' && !slots.workingHours) return
  const previousSection = workflowSection.value
  workflowSection.value = section
  if (section === 'rules' && canEditProfile.value) emit('startProfileEdit')
  if (!section && props.isEditingProfile) emit('cancelProfileEdit')
  await nextTick()
  if (section) {
    workflowBack.value?.$el.focus()
    return
  }
  workflowChoices.value?.$el.querySelector(`[data-section="${previousSection}"]`)?.focus()
}

watch(() => props.isEditingProfile, (editing, previous) => {
  if (activeSection.value !== 'workflow') return
  if (editing) workflowSection.value = 'rules'
  if (!editing && previous && workflowSection.value === 'rules') void selectWorkflowSection(null)
}, { immediate: true })

watch(activeSection, section => { if (section === 'notifications') notificationsVisited.value = true }, { immediate: true })
watch(() => props.selectedSection, section => { if (section === 'archive') emit('selectSection', 'profile') }, { immediate: true })
watch(() => props.settings.organization.id, () => {
  canImport.value = false
  notificationBusy.value = {}
  notificationsVisited.value = activeSection.value === 'notifications'
  workflowSection.value = null
  hoursSaving.value = false
  timeZoneBusy.value = false
})
</script>

<template>
  <div class="organization-settings">
    <div class="organization-settings__mobile-menu"><UiSelect :model-value="activeSection" label="Раздел настроек" :options="sections.map(section => ({ value: section.code, label: section.isPro ? `${section.title} · PRO` : section.title }))" required :searchable="false" :disabled="isSectionLocked" @update:model-value="selectSection($event as SettingsSection)" /></div>
    <nav class="organization-settings__menu" aria-label="Разделы настроек организации">
      <section v-for="group in menuGroups" :key="group.title">
        <h3>{{ group.title }}</h3>
        <button v-for="section in group.items" :key="section.code" type="button"
          :title="section.description" :class="{ 'is-active': activeSection === section.code }" :aria-current="activeSection === section.code ? 'page' : undefined"
          :disabled="isSectionLocked" @click="selectSection(section.code)">
          <strong>{{ section.title }}</strong>
          <small v-if="section.isPro" class="organization-settings__pro-badge">PRO</small>
        </button>
      </section>
    </nav>
    <div :key="settings.organization.id" class="organization-settings__content">
      <section v-show="activeSection === 'profile'" class="organization-panel">
        <header class="ui-section-header"><div><h3>Общие настройки</h3><p>Основные данные вашей организации</p></div><UiButton v-if="canEditProfile && !isEditingProfile" variant="secondary" :disabled="isBusy" @click="$emit('startProfileEdit')">Редактировать</UiButton></header>
        <form v-if="canEditProfile && isEditingProfile" class="organization-form" @submit.prevent="$emit('saveProfile')">
          <UiInput v-model="profileForm.name" label="Название организации" required :disabled="isBusy" />
          <UiTextarea v-model="profileForm.description" label="Описание" :disabled="isBusy" />
          <footer><UiButton variant="secondary" :disabled="isBusy" @click="$emit('cancelProfileEdit')">Отмена</UiButton><UiButton type="submit" :disabled="isBusy || !profileForm.name.trim()">{{ isSaving ? 'Сохраняем…' : 'Сохранить изменения' }}</UiButton></footer>
        </form>
        <dl v-else class="organization-info"><div><dt>Название</dt><dd>{{ settings.organization.name }}</dd></div><div><dt>Направление</dt><dd>{{ settings.organization.primaryScenarioName || scenarioTitle }}</dd></div><div v-if="settings.organization.description"><dt>Описание</dt><dd>{{ settings.organization.description }}</dd></div></dl>
        <OrganizationTimeZoneSettings v-if="activeSection === 'profile'" :key="settings.organization.id" :organization-id="settings.organization.id" :disabled="isReadOnly || isSaving || isEditingProfile || isEditingRequisites" @busy-changed="timeZoneBusy = $event" @saved="emit('timeZoneSaved')" />
      </section>

      <section v-show="activeSection === 'workflow'" class="workflow-settings">
        <WorkspaceSettingsChoices v-show="!workflowSection" ref="workflowChoices" title="Работа и расписание" :groups="workflowGroups" :disabled="isBusy" @select="selectWorkflowSection($event as 'rules' | 'hours')" />
        <nav v-if="workflowSection" class="workflow-settings__back" aria-label="Раздел работы и расписания"><UiButton ref="workflowBack" variant="secondary" :disabled="isBusy" @click="selectWorkflowSection(null)">← Работа и расписание</UiButton></nav>
        <section v-if="workflowSection === 'rules'" class="organization-panel" aria-labelledby="workflow-rules-title">
          <header class="ui-section-header"><h3 id="workflow-rules-title">Правила работы</h3></header>
          <form v-if="canEditProfile && isEditingProfile" class="organization-form" @submit.prevent="$emit('saveProfile')">
            <fieldset v-if="supportsMaintenanceWorkflow(settings.organization)" class="workflow-settings__modes">
              <legend>Режим заказ-нарядов</legend>
              <label :class="{ 'is-selected': profileForm.maintenanceWorkflowMode === 'simple' }"><input v-model="profileForm.maintenanceWorkflowMode" type="radio" name="maintenance-workflow-mode" value="simple" :disabled="isBusy" /><span><strong>Простой</strong><small>Запись и выдача автомобиля без промежуточных этапов.</small></span></label>
              <label :class="{ 'is-selected': profileForm.maintenanceWorkflowMode === 'staged' }"><input v-model="profileForm.maintenanceWorkflowMode" type="radio" name="maintenance-workflow-mode" value="staged" :disabled="isBusy" /><span><strong>По этапам</strong><small>Пошаговое выполнение работ и подготовка автомобиля к выдаче.</small></span></label>
            </fieldset>
            <WorkspaceProLock v-if="isSelfAssignmentLocked" compact :locked="true" message="Самостоятельное назначение доступно в PRO" feature="selfAssignment">
              <label class="organization-check workflow-settings__assignment"><input v-model="profileForm.allowWorkSelfAssignment" type="checkbox" disabled /><span><strong>Мастера могут брать свободные работы</strong><small>Включено — мастер сам берёт работу. Выключено — работы назначаются вручную.</small></span></label>
            </WorkspaceProLock>
            <label v-else class="organization-check workflow-settings__assignment"><input v-model="profileForm.allowWorkSelfAssignment" type="checkbox" :disabled="isBusy" /><span><strong>Мастера могут брать свободные работы</strong><small>Включено — мастер сам берёт работу. Выключено — работы назначаются вручную.</small></span></label>
            <footer><UiButton variant="secondary" :disabled="isBusy" @click="selectWorkflowSection(null)">Отмена</UiButton><UiButton type="submit" :disabled="isBusy">{{ isSaving ? 'Сохраняем…' : 'Сохранить правила' }}</UiButton></footer>
          </form>
          <dl v-else class="organization-info"><div v-if="!isSelfAssignmentLocked"><dt>Самостоятельное назначение</dt><dd>{{ settings.organization.allowWorkSelfAssignment ? 'Разрешено' : 'Выключено' }}</dd></div><div v-if="supportsMaintenanceWorkflow(settings.organization)"><dt>Режим заказ-нарядов</dt><dd>{{ settings.organization.maintenanceWorkflowMode === 'simple' ? 'Простой' : 'По этапам' }}</dd></div></dl>
          <WorkspaceProLock v-if="isSelfAssignmentLocked && !(canEditProfile && isEditingProfile)" compact :locked="true" message="Самостоятельное назначение доступно в PRO" feature="selfAssignment">
            <label class="organization-check workflow-settings__assignment"><input type="checkbox" :checked="settings.organization.allowWorkSelfAssignment" disabled /><span><strong>Мастера могут брать свободные работы</strong><small>Включено — мастер сам берёт работу. Выключено — работы назначаются вручную.</small></span></label>
          </WorkspaceProLock>
        </section>
        <slot v-if="workflowSection === 'hours'" name="workingHours" :on-close="() => selectWorkflowSection(null)" :on-busy-change="(value: boolean) => hoursSaving = value" />
      </section>

      <section v-show="activeSection === 'documents'" class="organization-panel">
        <header class="ui-section-header"><div><h3>Документы</h3><p>Оформление печатных PDF и Word</p></div><UiButton v-if="canEditProfile && !isEditingProfile && !isDocumentStylingLocked" variant="secondary" :disabled="isBusy" @click="$emit('startProfileEdit')">Редактировать</UiButton></header>
        <WorkspaceProLock centered :locked="isDocumentStylingLocked" message="Оформление документов доступно в PRO" feature="documentStyling">
          <form v-if="(canEditProfile && isEditingProfile) || isDocumentStylingLocked" class="organization-form" @submit.prevent="$emit('saveProfile')"><UiSelect v-model="profileForm.documentStyleCode" label="Оформление документов" :options="documentOptions" required :searchable="false" :disabled="isBusy" /><p class="organization-hint">Выбранный стиль применяется к формируемым документам организации.</p><footer><UiButton variant="secondary" :disabled="isBusy" @click="$emit('cancelProfileEdit')">Отмена</UiButton><UiButton type="submit" :disabled="isBusy">{{ isSaving ? 'Сохраняем…' : 'Сохранить оформление' }}</UiButton></footer></form>
          <dl v-else class="organization-info"><div><dt>Стиль оформления</dt><dd>{{ documentStyleName }}</dd></div><div><dt>Используется в</dt><dd>Печатных документах организации</dd></div></dl>
        </WorkspaceProLock>
        <a v-if="canViewWiki && !isEditingProfile" href="#wiki?slug=materials-and-documents" class="organization-link">Посмотреть примеры оформления →</a>
      </section>

      <section v-show="activeSection === 'requisites'" class="organization-panel">
        <header class="ui-section-header"><div><h3>Реквизиты и контакты</h3><p>Данные организации и подпись в документах</p></div><UiButton v-if="canEditRequisites && !isEditingRequisites" variant="secondary" :disabled="isBusy" @click="$emit('startRequisitesEdit')">Редактировать</UiButton></header>
        <form v-if="canEditRequisites && isEditingRequisites" class="organization-form organization-form--wide" @submit.prevent="$emit('saveRequisites')">
          <fieldset v-for="group in requisitesGroups" :key="group.title">
            <legend class="ui-section-header">{{ group.title }}</legend>
            <div class="organization-fields">
              <div v-for="field in group.fields" :key="field.key">
                <UiPhoneInput v-if="field.key === 'phone'" :model-value="requisitesForm.phone" @update:model-value="requisitesForm.phone = $event" :label="field.label" :disabled="isBusy" />
                <UiInput v-else v-model="requisitesForm[field.key]" :label="field.label" :type="field.key === 'email' ? 'email' : 'text'" :label-help="requisitesHelp[field.key]" :maxlength="requisitesLimits[field.key]" :error="requisitesErrors?.[field.key]?.join(' ')" :disabled="isBusy" />
                <span v-if="field.key === 'phone' && requisitesErrors?.phone" class="ui-field__error" role="alert">{{ requisitesErrors.phone.join(' ') }}</span>
              </div>
            </div>
          </fieldset>
          <p v-if="requisitesErrors && Object.keys(requisitesErrors).some(key => !(key in requisitesForm))" class="ui-field__error" role="alert">Не удалось проверить реквизиты. Проверьте заполнение формы и повторите сохранение.</p>
          <footer><UiButton variant="secondary" :disabled="isBusy" @click="$emit('cancelRequisitesEdit')">Отмена</UiButton><UiButton type="submit" :disabled="isBusy">{{ isSaving ? 'Сохраняем…' : 'Сохранить реквизиты' }}</UiButton></footer>
        </form>
        <div v-else class="organization-requisites"><section v-for="group in requisitesGroups" :key="group.title"><h4 class="ui-section-header">{{ group.title }}</h4><dl class="organization-info"><div v-for="field in group.fields" :key="field.key"><dt>{{ field.label }}</dt><dd>{{ settings.organization[field.key] || 'Не указано' }}</dd></div></dl></section></div>
      </section>

      <section v-if="canReadNotifications && notificationsVisited" v-show="activeSection === 'notifications'" class="organization-notifications">
        <WorkspaceProLock v-if="isMessagingLocked" centered :locked="true" message="Уведомления доступны в PRO" feature="notifications">
          <WorkspaceNotificationSettings preview-only :organization-id="settings.organization.id" :can-write="false" @busy-changed="notificationBusy.notifications = $event" />
        </WorkspaceProLock>
        <WorkspaceNotificationSettings v-else :organization-id="settings.organization.id" :can-write="canEditProfile && messagingAvailable !== false" @busy-changed="notificationBusy.notifications = $event" />
      </section>
      <div v-show="activeSection === 'imports'" class="organization-imports">
        <WorkspaceProLock v-if="isDataImportLocked" centered :locked="true" message="Импорт данных доступен в PRO" feature="dataImport">
          <section class="organization-panel organization-import-preview">
            <header class="ui-section-header"><div><h3>Загрузка данных</h3><p>Импорт клиентов, автомобилей и истории работ.</p></div></header>
            <div class="organization-import-preview__upload" aria-hidden="true">
              <strong>Выберите файл для импорта</strong>
              <span>CSV, XLSX или ZIP</span>
              <input type="file" accept=".csv,.xlsx,.zip" disabled aria-label="Файл для импорта" />
              <UiButton variant="secondary" disabled>Выбрать файл</UiButton>
            </div>
          </section>
        </WorkspaceProLock>
        <section v-else-if="isReadOnly" class="organization-panel">
          <header class="ui-section-header"><div><h3>Импорт данных</h3><p>Загрузка клиентов, автомобилей и истории работ.</p></div></header>
          <p class="organization-hint" role="status">Организация доступна только для чтения. Загрузка файлов и применение импорта недоступны до подключения тарифа с возможностью изменения данных.</p>
          <UiButton variant="secondary" disabled>Загрузить файл</UiButton>
        </section>
        <slot v-else name="imports" :on-access-change="(value: boolean) => canImport = value" />
      </div>
      <WorkspaceOrganizationExport v-if="isOwner" v-show="activeSection === 'export'" :organization-id="settings.organization.id" />
      <section v-if="settings.canArchive && activeSection === 'archive'" class="organization-panel organization-panel--archive">
        <header class="ui-section-header"><div><h3>Архив организации</h3><p>Организация исчезнет из рабочих разделов.</p></div></header>
        <p>Данные сохранятся. Восстановление будет доступно в списке организаций в течение срока хранения архива.</p>
        <UiButton variant="secondary" :disabled="isBusy" @click="$emit('archiveOrganization')">Архивировать организацию</UiButton>
      </section>
    </div>
  </div>
</template>

<style scoped>
.organization-settings { display: grid; grid-template-columns: 235px minmax(0, 1fr); gap: 22px; align-items: start; min-width: 0; }
.organization-settings__mobile-menu { display: none; }
.organization-settings__menu { display: grid; gap: 18px; }
.organization-settings__menu section { display: grid; gap: 4px; }
.organization-settings__menu h3 { margin: 0 0 4px; padding-left: 12px; color: var(--crm-text-muted, #687386); font-size: 11px; text-transform: uppercase; letter-spacing: .04em; font-weight: 500; }
.organization-settings__menu button { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 11px 12px; text-align: left; font: inherit; background: transparent; border: 1px solid transparent; border-radius: 8px; color: var(--crm-text, #202631); cursor: pointer; }
.organization-settings__menu button.is-active { border-color: #e87532; background: var(--crm-surface-soft, #fff4eb); }
.organization-settings__menu button strong { font-size: 13px; font-weight: 600; }
.organization-settings__menu button span { font-size: 12px; color: var(--crm-text-muted, #687386); }
.organization-settings__pro-badge { justify-self: start; padding: 2px 6px; border-radius: 999px; background: color-mix(in srgb, #e87532 12%, transparent); color: #bc4c10; font-size: 10px; font-weight: 700; letter-spacing: .03em; }
.organization-settings__menu button:disabled { cursor: default; opacity: .65; }
.organization-settings__content { min-width: 0; }
.organization-panel { padding: 20px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); min-width: 0; }
.organization-panel header { display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 12px; padding-bottom: 18px; margin-bottom: 18px; border-bottom: 1px solid var(--crm-section-header-border, #dce2e9); }
.organization-panel h3 { margin: 0; font-size: 16px; }
.organization-panel h4 { margin: 0 0 12px; font-size: 14px; }
.organization-panel header p, .organization-panel--intro p, .organization-hint { margin: 5px 0 0; color: var(--crm-text-muted, #687386); font-size: 13px; }
.organization-form { display: grid; gap: 18px; max-width: 720px; }
.organization-form--wide { max-width: none; }
.organization-form fieldset { border: 0; padding: 0; margin: 0; min-width: 0; }
.organization-form legend { padding: 0; margin: 0 0 14px; font-size: 14px; font-weight: 600; }
.organization-fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; }
.organization-form footer { display: flex; justify-content: flex-end; gap: 10px; flex-wrap: wrap; border-top: 1px solid var(--crm-line, #dce2e9); padding-top: 16px; }
.organization-form footer :deep(.ui-button--primary) { background: #e87532; border-color: #e87532; color: #fff; }
.organization-info { margin: 0; display: grid; }
.organization-info > div { display: grid; grid-template-columns: minmax(150px, .7fr) minmax(0, 1.4fr); gap: 18px; padding: 12px 0; border-bottom: 1px solid var(--crm-line, #dce2e9); font-size: 13px; }
.organization-info > div:first-child { padding-top: 0; }
.organization-info > div:last-child { border-bottom: 0; padding-bottom: 0; }
.organization-info dt { color: var(--crm-text-muted, #687386); font-weight: 400; }
.organization-info dd { margin: 0; color: var(--crm-text, #202631); overflow-wrap: anywhere; }
.organization-requisites { display: grid; gap: 26px; }
.workflow-settings { display: grid; gap: 14px; min-width: 0; }
.workflow-settings__back { display: flex; }
.workflow-settings__modes label { display: flex; align-items: start; gap: 10px; margin-top: 10px; padding: 14px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; cursor: pointer; }
.workflow-settings__modes label.is-selected { border-color: var(--crm-header-accent, #e87532); background: var(--crm-surface-soft, #f5f6f8); }
.workflow-settings__modes input { margin-top: 3px; accent-color: var(--crm-header-accent, #e87532); }
.workflow-settings__modes span { display: grid; gap: 5px; }
.workflow-settings__modes strong { font-size: 13px; font-weight: 600; }
.workflow-settings__modes small { color: var(--crm-text-muted, #687386); font-size: 12px; line-height: 1.5; }
.workflow-settings__assignment { padding: 14px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; }
.organization-check { display: flex; align-items: start; gap: 10px; width: fit-content; cursor: pointer; }
.organization-check input { margin-top: 3px; accent-color: #e87532; }
.organization-check span { display: grid; gap: 5px; }
.organization-check strong { font-size: 13px; font-weight: 600; }
.organization-check small { font-size: 12px; color: var(--crm-text-muted, #687386); }
.organization-notifications { display: grid; gap: 16px; }
.organization-panel--archive > p { margin: 0 0 20px; font-size: 13px; line-height: 1.5; color: var(--crm-text-muted, #687386); }
.organization-link { display: inline-block; margin-top: 16px; font-size: 13px; color: var(--crm-text, #202631); }
.organization-import-preview__upload { display: grid; justify-items: center; gap: 10px; padding: 28px 16px; border: 1px dashed var(--crm-line, #cbd3dc); border-radius: 8px; background: var(--crm-surface-soft, #f7f8fa); text-align: center; }
.organization-import-preview__upload strong { font-size: 13px; }
.organization-import-preview__upload span { color: var(--crm-text-muted, #687386); font-size: 12px; }
.organization-settings__content :deep(.workspace-export), .organization-imports :deep(.workspace-common-subpanel) { padding: 20px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); }
@container (max-width: 850px) {
  .organization-settings { grid-template-columns: minmax(0, 1fr); gap: 18px; }
  .organization-settings__menu { grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 12px; }
}
@media (max-width: 600px) {
  .organization-settings__mobile-menu { display: block; }
  .organization-settings__menu { display: none; }
  .organization-panel { padding: 14px; }
  .organization-fields { grid-template-columns: minmax(0, 1fr); }
  .organization-info > div { grid-template-columns: minmax(0, 1fr); gap: 5px; }
  .organization-settings__menu button span { display: none; }
  .organization-settings__menu h3 { font-size: 10px; }
  .organization-form footer :deep(.ui-button) { flex: 1 1 auto; }
}
</style>
