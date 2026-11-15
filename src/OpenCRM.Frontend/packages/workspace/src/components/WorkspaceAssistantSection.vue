<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { UiButton, UiCard, UiLoader } from '@opencrm/ui'
import { getAssistantFrameUrl, loadAssistantAvailability } from '../api/assistant'
import type { WorkspaceContext } from '../types'
import WorkspaceProLock from './WorkspaceProLock.vue'
import WorkspaceProFeaturePreview from './WorkspaceProFeaturePreview.vue'

const props = defineProps<{ context: WorkspaceContext }>()
const emit = defineEmits<{ changed: [] }>()
const isVisible = computed(() => props.context.shell.scenario?.modules?.assistantVisible !== false)
const previewOnly = computed(() => isVisible.value && props.context.shell.scenario?.modules?.assistant === false)
const frame = ref<HTMLIFrameElement | null>(null)
const frameUrl = ref('')
const loading = ref(true)
const error = ref('')
const frameHeight = ref(540)
let requestVersion = 0

function updateHeight() {
  if (!frame.value) return
  const content = frame.value.closest('.crm-content')
  const bottomPadding = content ? Number.parseFloat(getComputedStyle(content).paddingBottom) : 0
  frameHeight.value = Math.max(420, Math.floor(window.innerHeight - frame.value.getBoundingClientRect().top - bottomPadding - 2))
}

async function load() {
  const version = ++requestVersion
  frameUrl.value = ''
  loading.value = true
  error.value = ''
  if (!isVisible.value) {
    loading.value = false
    error.value = 'ИИ-помощник временно отключён администратором.'
    return
  }
  if (previewOnly.value) {
    loading.value = false
    return
  }
  try {
    const availability = await loadAssistantAvailability(props.context.organization.id)
    if (version !== requestVersion) return
    const url = getAssistantFrameUrl(availability, props.context.organization.id, window.location.origin, window.location.pathname)
    if (!url) throw new Error('ИИ-помощник сейчас недоступен для этой организации.')
    frameUrl.value = url
    await nextTick()
    updateHeight()
  } catch (exception) {
    if (version !== requestVersion) return
    error.value = exception instanceof Error ? exception.message : 'Не удалось открыть ИИ-помощника.'
    loading.value = false
  }
}

function loaded() {
  if (!frame.value) return
  try {
    if (!frame.value.contentDocument?.querySelector('[data-ai-pilot]')) {
      error.value = 'Не удалось открыть ИИ-помощника. Проверьте доступ и повторите попытку.'
      frameUrl.value = ''
    }
  } catch {
    error.value = 'Не удалось открыть ИИ-помощника.'
    frameUrl.value = ''
  }
  loading.value = false
  void nextTick().then(updateHeight)
}

function receiveUpdate(event: MessageEvent) {
  if (event.origin !== window.location.origin || event.source !== frame.value?.contentWindow) return
  if (event.data?.type !== 'opencrm:assistant-updated' || event.data.organizationId !== props.context.organization.id) return
  emit('changed')
}

watch(() => [props.context.organization.id, previewOnly.value, isVisible.value], () => { void load() }, { immediate: true })
onMounted(() => {
  window.addEventListener('resize', updateHeight)
  window.addEventListener('message', receiveUpdate)
})
onBeforeUnmount(() => {
  requestVersion++
  window.removeEventListener('resize', updateHeight)
  window.removeEventListener('message', receiveUpdate)
})
</script>

<template>
  <UiCard page title="ИИ - Голосовой помощник" class="workspace-assistant">
    <WorkspaceProLock v-if="previewOnly" locked centered feature="assistant" message="ИИ помощник доступен в PRO" class="workspace-assistant__preview">
      <WorkspaceProFeaturePreview kind="assistant" />
    </WorkspaceProLock>
    <UiLoader v-if="loading" text="Открываем помощника…" />
    <div v-if="error" class="workspace-assistant__error" role="alert">
      <p>{{ error }}</p>
      <UiButton v-if="isVisible" variant="secondary" @click="load">Повторить</UiButton>
    </div>
    <iframe
      v-if="frameUrl"
      ref="frame"
      :src="frameUrl"
      :style="{ height: `${frameHeight}px` }"
      class="workspace-assistant__frame"
      title="ИИ - Голосовой помощник"
      allow="microphone 'self'"
      referrerpolicy="same-origin"
      @load="loaded"
    />
  </UiCard>
</template>

<style scoped>
.workspace-assistant :deep(.ui-card__body) { padding: 0; }
.workspace-assistant__frame { display: block; width: 100%; border: 0; background: var(--crm-canvas, #f5f7fa); }
.workspace-assistant__error { padding: 20px; color: var(--crm-text, #182230); }
.workspace-assistant__error p { margin: 0 0 12px; }
.workspace-assistant__preview { padding: 24px; }
.workspace-assistant__preview :deep(.pro-feature-preview) { max-width: 680px; margin: 0 auto; }
</style>
