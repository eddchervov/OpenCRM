<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiInput, UiLoader, UiTextarea } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { archiveMessageTemplate, archiveMessageTemplateGroup, loadMessageTemplateCatalog, messageTemplateVariables, saveMessageTemplate, saveMessageTemplateGroup, type MessageTemplate, type MessageTemplateCatalog, type MessageTemplateGroup } from '../api/messageTemplates'

const props = defineProps<{ organizationId: string; readOnly?: boolean; previewOnly?: boolean }>()
const catalog = ref<MessageTemplateCatalog | null>(null)
const selectedGroupId = ref('')
const search = ref('')
const includeArchived = ref(false)
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const editorError = ref('')
const mode = ref<'group' | 'template' | null>(null)
const editingGroup = ref<MessageTemplateGroup | null>(null)
const editingTemplate = ref<MessageTemplate | null>(null)
const name = ref('')
const body = ref('')
const sortOrder = ref('0')
const activeGroup = computed(() => catalog.value?.groups.find(group => group.id === selectedGroupId.value))
const canManage = computed(() => !props.previewOnly && catalog.value?.canManage && !props.readOnly)
const templates = computed(() => (activeGroup.value?.templates ?? []).filter(template =>
  [template.name, template.body].join(' ').toLocaleLowerCase('ru').includes(search.value.trim().toLocaleLowerCase('ru'))))
let request: AbortController | undefined
let generation = 0
async function load() {
  request?.abort()
  if (props.previewOnly) { catalog.value = null; loading.value = false; return }
  const active = new AbortController()
  request = active
  loading.value = true
  error.value = ''
  try {
    const result = await loadMessageTemplateCatalog(props.organizationId, includeArchived.value, active.signal)
    if (active.signal.aborted) return
    catalog.value = result
    if (!result.groups.some(group => group.id === selectedGroupId.value)) selectedGroupId.value = result.groups[0]?.id ?? ''
  } catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : 'Не удалось загрузить шаблоны.' }
  finally { if (request === active) loading.value = false }
}
function editGroup(group: MessageTemplateGroup | null = null) {
  editingGroup.value = group; editingTemplate.value = null; name.value = group?.name ?? ''; sortOrder.value = String(group?.sortOrder ?? 0)
  editorError.value = ''; mode.value = 'group'
}
function editTemplate(template: MessageTemplate | null = null) {
  editingTemplate.value = template; editingGroup.value = null; name.value = template?.name ?? ''; body.value = template?.body ?? ''; sortOrder.value = String(template?.sortOrder ?? 0)
  editorError.value = ''; mode.value = 'template'
}
async function save() {
  if (saving.value || !canManage.value || !name.value.trim()) return
  const current = generation
  saving.value = true; editorError.value = ''
  try {
    if (mode.value === 'group') {
      const result = await saveMessageTemplateGroup(props.organizationId, editingGroup.value?.id ?? null, { name: name.value.trim(), sortOrder: Number(sortOrder.value), expectedVersion: editingGroup.value?.version })
      if (current === generation) selectedGroupId.value = result.id
    } else if (mode.value === 'template' && activeGroup.value) {
      await saveMessageTemplate(props.organizationId, editingTemplate.value?.id ?? null, { groupId: activeGroup.value.id, name: name.value.trim(), body: body.value, sortOrder: Number(sortOrder.value), expectedVersion: editingTemplate.value?.version })
    }
    if (current !== generation) return
    mode.value = null
    await load()
  } catch (caught) { if (current === generation) editorError.value = caught instanceof Error ? caught.message : 'Не удалось сохранить шаблон.' }
  finally { if (current === generation) saving.value = false }
}
async function archive() {
  if (saving.value || !canManage.value) return
  const current = generation
  saving.value = true; editorError.value = ''
  try {
    if (editingGroup.value) await archiveMessageTemplateGroup(props.organizationId, editingGroup.value.id, editingGroup.value.version)
    else if (editingTemplate.value) await archiveMessageTemplate(props.organizationId, editingTemplate.value.id, editingTemplate.value.version)
    if (current !== generation) return
    mode.value = null
    await load()
  } catch (caught) { if (current === generation) editorError.value = caught instanceof Error ? caught.message : 'Не удалось архивировать.' }
  finally { if (current === generation) saving.value = false }
}
async function restore(item: MessageTemplateGroup | MessageTemplate) {
  if (saving.value || !canManage.value) return
  const current = generation
  saving.value = true; error.value = ''
  try {
    if ('body' in item) await saveMessageTemplate(props.organizationId, item.id, { groupId: item.groupId, name: item.name, body: item.body, sortOrder: item.sortOrder, expectedVersion: item.version, isArchived: false })
    else await saveMessageTemplateGroup(props.organizationId, item.id, { name: item.name, sortOrder: item.sortOrder, expectedVersion: item.version, isArchived: false })
    if (current === generation) await load()
  } catch (caught) { if (current === generation) error.value = caught instanceof Error ? caught.message : 'Не удалось восстановить.' }
  finally { if (current === generation) saving.value = false }
}
watch(() => [props.organizationId, props.previewOnly], () => { generation++; catalog.value = null; selectedGroupId.value = ''; mode.value = null; saving.value = false; void load() }, { immediate: true })
watch(includeArchived, () => { void load() })
onBeforeUnmount(() => { generation++; request?.abort() })
</script>

<template>
  <section class="message-templates" aria-label="Шаблоны сообщений">
    <header class="message-templates__toolbar"><div><h3>Шаблоны сообщений</h3><p>Готовые ответы для переписки в MAX и Telegram.</p></div><UiButton v-if="canManage || previewOnly" :disabled="previewOnly" @click="editGroup()">Добавить группу</UiButton></header>
    <div v-if="previewOnly" class="message-templates__layout message-templates__preview" aria-hidden="true">
      <aside class="message-templates__groups"><div v-for="index in 4" :key="index" class="message-templates__preview-group"><i></i></div></aside>
      <div class="message-templates__content">
        <UiInput placeholder="Название или текст" label="Поиск шаблонов" disabled />
        <article v-for="index in 3" :key="index" class="message-templates__item"><i></i><i></i><i></i></article>
      </div>
    </div>
    <p v-if="!previewOnly && error" role="alert" class="message-templates__error">{{ error }} <UiButton variant="secondary" @click="load">Повторить</UiButton></p>
    <UiLoader v-if="!previewOnly && loading && !catalog" text="Загружаем шаблоны…" />
    <div v-else-if="!previewOnly && catalog" class="message-templates__layout">
      <aside class="message-templates__groups" aria-label="Группы шаблонов">
        <button v-for="group in catalog.groups" :key="group.id" type="button" :aria-current="group.id === selectedGroupId ? 'page' : undefined" @click="selectedGroupId = group.id"><span>{{ group.name }}<small v-if="group.isArchived">В архиве</small></span><span>{{ group.templates.length }}</span></button>
        <p v-if="!catalog.groups.length">Групп пока нет.</p>
        <label v-if="canManage" class="message-templates__archive"><input v-model="includeArchived" type="checkbox" />Показать архив</label>
      </aside>
      <div class="message-templates__content">
        <div v-if="activeGroup" class="message-templates__heading"><h3>{{ activeGroup.name }}</h3><div><UiButton v-if="canManage && activeGroup.isArchived" variant="secondary" :disabled="saving" @click="restore(activeGroup)">Восстановить группу</UiButton><UiButton v-if="canManage && !activeGroup.isArchived" variant="secondary" @click="editGroup(activeGroup)">Изменить группу</UiButton><UiButton v-if="canManage && !activeGroup.isArchived" @click="editTemplate()">Добавить шаблон</UiButton></div></div>
        <UiInput v-if="activeGroup" v-model="search" type="search" placeholder="Название или текст" label="Поиск шаблонов" />
        <p v-if="activeGroup && !templates.length" class="message-templates__empty">{{ search ? 'Шаблоны не найдены.' : 'В этой группе пока нет шаблонов.' }}</p>
        <p v-if="!activeGroup" class="message-templates__empty">Создайте группу, чтобы добавить ответы для клиентов.</p>
        <article v-for="template in templates" :key="template.id" class="message-templates__item"><header><strong>{{ template.name }}</strong><small v-if="template.isArchived">В архиве</small><UiButton v-if="canManage && template.isArchived && !activeGroup?.isArchived" variant="secondary" :disabled="saving" @click="restore(template)">Восстановить</UiButton><UiButton v-if="canManage && !template.isArchived && !activeGroup?.isArchived" variant="secondary" @click="editTemplate(template)">Изменить</UiButton></header><p>{{ template.body }}</p></article>
      </div>
    </div>
    <SchedulerBookingDialog :open="!!mode" :busy="saving" :dismiss-on-backdrop="false" :title="mode === 'group' ? editingGroup ? 'Изменить группу' : 'Новая группа' : editingTemplate ? 'Изменить шаблон' : 'Новый шаблон'" @close="mode = null">
      <form class="message-template-editor" @submit.prevent="save">
        <UiInput v-model="name" label="Название" required :maxlength="100" :disabled="saving" />
        <UiTextarea v-if="mode === 'template'" v-model="body" label="Текст сообщения" required :rows="8" :maxlength="4096" :disabled="saving" />
        <div v-if="mode === 'template'" class="message-template-editor__variables"><span>Подстановки:</span><button v-for="variable in messageTemplateVariables" :key="variable.code" type="button" :title="variable.code" :disabled="saving" @click="body += variable.code">{{ variable.label }}</button></div>
        <p v-if="mode === 'template'" class="message-template-editor__hint">Дата, время и автомобиль подставляются при отправке из конкретного обслуживания. Перед отправкой можно изменить текст.</p>
        <UiInput v-model="sortOrder" type="number" label="Порядок в списке" min="0" max="9999" :disabled="saving" />
        <p v-if="editorError" class="message-templates__error" role="alert">{{ editorError }}</p>
        <footer><UiButton type="submit" :disabled="saving || !name.trim() || (mode === 'template' && !body.trim())">{{ saving ? 'Сохраняем…' : 'Сохранить' }}</UiButton><UiButton variant="secondary" :disabled="saving" @click="mode = null">Отмена</UiButton><button v-if="editingGroup || editingTemplate" class="message-template-editor__archive" type="button" :disabled="saving" @click="archive">В архив</button></footer>
      </form>
    </SchedulerBookingDialog>
  </section>
</template>

<style scoped>
.message-templates { color:var(--crm-text); min-width:0; }
.message-templates__toolbar, .message-templates__heading { display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:12px; margin-bottom:18px; }
.message-templates__toolbar h3, .message-templates__heading h3 { font-size:16px; margin:0; }
.message-templates__toolbar p { font-size:12px; color:var(--crm-text-muted); margin:6px 0 0; }
.message-templates__layout { display:grid; grid-template-columns:minmax(180px, 230px) minmax(0, 1fr); gap:24px; }
.message-templates__preview { min-height: 360px; }
.message-templates__preview-group { padding: 20px 12px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.message-templates__preview i { display: block; width: 80%; height: 12px; margin: 8px 0; border-radius: 5px; background: var(--crm-line, #dce2e9); }
.message-templates__preview .message-templates__item i:first-child { width: 45%; height: 15px; }
.message-templates__preview .message-templates__item i:last-child { width: 65%; }
.message-templates__groups { display:grid; align-content:start; gap:6px; border-right:1px solid var(--crm-line); padding-right:16px; }
.message-templates__groups > button { display:flex; justify-content:space-between; gap:10px; padding:12px; border:0; border-radius:7px; background:none; color:inherit; font:inherit; font-size:13px; text-align:left; cursor:pointer; overflow-wrap:anywhere; }
.message-templates__groups > button[aria-current] { background:var(--crm-accent-soft, #fff1e6); color:var(--crm-accent, #ae511e); }
.message-templates__groups small { display:block; font-size:10px; margin-top:4px; }
.message-templates__groups p, .message-templates__archive { color:var(--crm-text-muted); font-size:12px; }
.message-templates__archive { display:flex; align-items:center; gap:8px; margin-top:16px; }
.message-templates__content { min-width:0; }
.message-templates__heading > div { display:flex; gap:8px; flex-wrap:wrap; }
.message-templates__item { padding:18px; border:1px solid var(--crm-line); border-radius:8px; margin-top:14px; }
.message-templates__item header { display:flex; align-items:center; gap:12px; flex-wrap:wrap; font-size:14px; }
.message-templates__item header :deep(.ui-button) { margin-left:auto; }
.message-templates__item p { font-size:13px; white-space:pre-wrap; line-height:1.6; margin:12px 0 0; overflow-wrap:anywhere; }
.message-templates__empty { padding:28px 0; font-size:13px; color:var(--crm-text-muted); }
.message-templates__error { color:var(--crm-danger, #b42318); font-size:13px; }
.message-template-editor { display:grid; gap:16px; padding:20px 24px; }
.message-template-editor footer, .message-template-editor__variables { display:flex; flex-wrap:wrap; gap:8px; align-items:center; }
.message-template-editor__variables { font-size:11px; color:var(--crm-text-muted); }
.message-template-editor__variables button { padding:5px 8px; border:1px solid var(--crm-line); border-radius:6px; background:var(--crm-surface); color:inherit; cursor:pointer; }
.message-template-editor__hint { margin:0; font-size:12px; color:var(--crm-text-muted); line-height:1.5; }
.message-template-editor__archive { margin-left:auto; border:0; background:none; color:var(--crm-danger, #b42318); font:inherit; font-size:12px; cursor:pointer; }
:global(.scheduler-booking-dialog:has(.message-template-editor)) { width:min(680px, calc(100vw - 32px)); height:fit-content; max-height:calc(100dvh - 32px); border:1px solid var(--crm-line); border-radius:12px; }
@media (max-width:650px) { .message-templates__layout { grid-template-columns:minmax(0, 1fr); gap:16px; } .message-templates__groups { padding:0 0 12px; border-right:0; border-bottom:1px solid var(--crm-line); } .message-template-editor { padding:16px; } }
</style>
