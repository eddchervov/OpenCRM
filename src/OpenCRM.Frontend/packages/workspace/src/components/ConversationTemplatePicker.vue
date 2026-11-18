<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../conversationMessages'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { loadMessageTemplateCatalog, previewMessageTemplate, type MessageTemplate, type MessageTemplateGroup, type MessageTemplatePreview } from '../api/messageTemplates'

const { t } = useLocalization()

const props = defineProps<{ organizationId: string; conversationId: string; maintenanceId?: string; textLimit: number }>()
const emit = defineEmits<{ select: [preview: MessageTemplatePreview] }>()
const open = ref(false)
const groups = ref<MessageTemplateGroup[]>([])
const groupId = ref('')
const search = ref('')
const selected = ref<MessageTemplate | null>(null)
const preview = ref<MessageTemplatePreview | null>(null)
const loading = ref(false)
const previewing = ref(false)
const error = ref('')
let request: AbortController | undefined
const filtered = computed(() => groups.value.filter(group => !groupId.value || group.id === groupId.value)
  .map(group => ({ ...group, templates: group.templates.filter(template => `${template.name} ${template.body}`.toLocaleLowerCase('ru').includes(search.value.trim().toLocaleLowerCase('ru'))) }))
  .filter(group => group.templates.length))

async function show() {
  open.value = true; loading.value = true; error.value = ''; preview.value = null; selected.value = null
  request?.abort()
  const active = new AbortController(); request = active
  try {
    const catalog = await loadMessageTemplateCatalog(props.organizationId, false, active.signal)
    if (!active.signal.aborted) groups.value = catalog.groups
  } catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : t('conversations.templatesError') }
  finally { if (request === active) loading.value = false }
}
async function choose(template: MessageTemplate) {
  request?.abort()
  const active = new AbortController(); request = active
  selected.value = template; preview.value = null; previewing.value = true; error.value = ''
  try {
    const result = await previewMessageTemplate(props.organizationId, props.conversationId, template, props.maintenanceId, active.signal)
    if (!active.signal.aborted) preview.value = result
  } catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : t('conversations.prepareError') }
  finally { if (request === active) previewing.value = false }
}
function insert() {
  if (!preview.value || preview.value.text.length > props.textLimit) return
  emit('select', preview.value); open.value = false
}
watch(() => [props.organizationId, props.conversationId, props.maintenanceId], () => { request?.abort(); open.value = false; groups.value = []; groupId.value = ''; search.value = ''; preview.value = null })
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <button type="button" class="template-picker-trigger" @click="show">{{ t('conversations.templates') }}</button>
  <SchedulerBookingDialog :open="open" :title="t('conversations.templatesTitle')" @close="open = false; request?.abort()">
    <div class="template-picker">
      <UiLoader v-if="loading" :text="t('conversations.loadingTemplates')" />
      <template v-else>
        <div class="template-picker__filters"><input v-model="search" type="search" :placeholder="t('conversations.findTemplate')" :aria-label="t('conversations.searchTemplate')"><select v-model="groupId" :aria-label="t('conversations.templateGroup')"><option value="">{{ t('conversations.allGroups') }}</option><option v-for="group in groups" :key="group.id" :value="group.id">{{ group.name }}</option></select></div>
        <div class="template-picker__layout">
          <div class="template-picker__list"><section v-for="group in filtered" :key="group.id"><h4>{{ group.name }}</h4><button v-for="item in group.templates" :key="item.id" type="button" :aria-pressed="selected?.id === item.id" @click="choose(item)">{{ item.name }}</button></section><p v-if="!filtered.length">{{ t('conversations.noTemplatesHint') }}</p></div>
          <div class="template-picker__preview" aria-live="polite"><UiLoader v-if="previewing" :text="t('conversations.preparingData')" /><template v-else-if="preview"><h4>{{ t('conversations.clientText') }}</h4><p>{{ preview.text }}</p><small>{{ t('conversations.editableHint') }}</small><p v-if="preview.text.length > textLimit" role="alert">{{ t('conversations.channelTextLimit', { limit: textLimit }) }}</p></template><p v-else>{{ t('conversations.chooseTemplateHint') }}</p></div>
        </div>
        <p v-if="error" class="template-picker__error" role="alert">{{ error }}</p>
      </template>
      <footer class="template-picker__actions"><UiButton variant="secondary" @click="open = false; request?.abort()">{{ t('conversations.cancel') }}</UiButton><UiButton :disabled="!preview || previewing || preview.text.length > textLimit" @click="insert">{{ t('conversations.insertMessage') }}</UiButton></footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.template-picker-trigger { color:var(--crm-accent, #ad511e); background:none; border:0; font:inherit; font-size:12px; cursor:pointer; padding:6px; }
.template-picker { display:grid; gap:16px; }
.template-picker__filters { display:flex; gap:10px; }
.template-picker__filters input, .template-picker__filters select { box-sizing:border-box; min-width:0; flex:1; padding:10px; border:1px solid var(--crm-line, #d4dce6); border-radius:8px; color:inherit; background:var(--crm-surface, #fff); font:inherit; }
.template-picker__layout { display:grid; grid-template-columns:minmax(160px, 1fr) minmax(0, 1.4fr); gap:16px; min-height:230px; }
.template-picker__list { max-height:340px; overflow:auto; }
.template-picker h4 { margin:0 0 10px; font-size:13px; }
.template-picker__list section + section { margin-top:18px; }
.template-picker__list button { width:100%; padding:10px; text-align:left; border:1px solid var(--crm-line, #d4dce6); border-radius:7px; margin-bottom:6px; color:inherit; background:var(--crm-surface, #fff); cursor:pointer; font:inherit; overflow-wrap:anywhere; }
.template-picker__list button[aria-pressed="true"] { border-color:var(--crm-accent, #ad511e); background:var(--crm-accent-soft, #fff3e9); }
.template-picker__preview { padding:16px; border-radius:10px; background:var(--crm-surface-soft, #f6f7f9); overflow-wrap:anywhere; }
.template-picker__preview p { white-space:pre-wrap; font-size:14px; line-height:1.5; }
.template-picker__preview small { color:var(--crm-text-muted, #667085); }
.template-picker__error { color:var(--crm-danger, #b42318); }
.template-picker__actions { display:flex; justify-content:flex-end; flex-wrap:wrap; gap:10px; padding-top:16px; border-top:1px solid var(--crm-line, #d4dce6); }
@media(max-width:600px) { .template-picker__layout { grid-template-columns:1fr; } .template-picker__list { max-height:170px; } .template-picker__filters { flex-direction:column; } }
</style>
