<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { UiButton, UiTextarea } from '@opencrm/ui'
import type { WorkspaceContext } from '../types'
import { addInquiryNote, InquiryRequestError, loadInquiry, type Inquiry } from '../api/inquiries'

const props = defineProps<{ context: WorkspaceContext; inquiry: Inquiry; disabled?: boolean }>()
const emit = defineEmits<{ updated: [inquiry: Inquiry]; busy: [value: boolean] }>()
const composing = ref(false)
const saving = ref(false)
const note = ref('')
const composer = ref<HTMLFormElement | null>(null)
const error = ref('')
const conflict = ref(false)
const canManage = computed(() => props.inquiry.canManage && props.context.workspaceAccess?.isReadOnly !== true)

async function startNote() {
  composing.value = true
  await nextTick()
  composer.value?.querySelector('textarea')?.focus()
}

async function saveNote() {
  if (saving.value || props.disabled || !canManage.value || conflict.value || !note.value.trim()) return
  saving.value = true
  error.value = ''
  try {
    emit('updated', await addInquiryNote(props.context.organization.id, props.inquiry.id, props.inquiry.version, note.value.trim()))
    note.value = ''
    composing.value = false
    conflict.value = false
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : 'Не удалось сохранить заметку.'
    conflict.value = caught instanceof InquiryRequestError && caught.status === 409
  } finally { saving.value = false }
}

async function refreshInquiry() {
  if (saving.value || props.disabled) return
  saving.value = true
  error.value = ''
  try {
    emit('updated', await loadInquiry(props.context.organization.id, props.inquiry.id))
    conflict.value = false
  } catch (caught) { error.value = caught instanceof Error ? caught.message : 'Не удалось обновить обращение.' }
  finally { saving.value = false }
}

watch(saving, value => emit('busy', value), { flush: 'sync' })
onBeforeUnmount(() => emit('busy', false))
</script>

<template>
  <div v-if="canManage" class="inquiry-note">
    <UiButton v-if="!composing" variant="secondary" :disabled="disabled" @click="startNote">Добавить заметку</UiButton>
    <form v-else ref="composer" class="inquiry-note__form" @submit.prevent="saveNote">
      <UiTextarea v-model="note" label="Новая заметка" placeholder="Договорённости, результат звонка или важные детали" required :maxlength="1000" :disabled="saving || disabled" />
      <div><UiButton type="submit" :disabled="saving || disabled || conflict || !note.trim()">{{ saving ? 'Сохраняем…' : 'Сохранить заметку' }}</UiButton><UiButton variant="ghost" :disabled="saving || disabled" @click="composing = false">Отмена</UiButton></div>
    </form>
    <div v-if="error" class="inquiry-note__error" role="alert"><p>{{ error }}</p><UiButton v-if="conflict" variant="secondary" :disabled="saving || disabled" @click="refreshInquiry">Обновить обращение</UiButton></div>
  </div>
</template>

<style scoped>
.inquiry-note__form { display: grid; gap: 12px; padding: 16px; border: 1px solid var(--crm-line, #dfe3ea); border-radius: 10px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-note__form > div { display: flex; gap: 8px; flex-wrap: wrap; }
.inquiry-note__error { color: var(--crm-danger-text, #c64a4a); font-size: 13px; }
</style>
