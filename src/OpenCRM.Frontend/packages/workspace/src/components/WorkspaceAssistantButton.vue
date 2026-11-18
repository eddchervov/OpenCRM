<script setup lang="ts">
import { translate as t } from '@opencrm/ui/i18n'
import '../interactionMessages'
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { getAssistantUrl, isAssistantScenario, loadAssistantAvailability, type AssistantAvailability } from '../api/assistant'

const props = defineProps<{
  organizationId: string
  scenarioCode: string | null | undefined
  isLocked?: boolean
}>()

const availability = ref<AssistantAvailability | null>(null)
const assistantUrl = ref<string | null>(null)
let pollInterval: ReturnType<typeof setTimeout> | null = null
let requestVersion = 0

async function refresh(): Promise<void> {
  const version = ++requestVersion
  if (pollInterval) clearTimeout(pollInterval)
  availability.value = null
  assistantUrl.value = null
  if (!props.organizationId || !isAssistantScenario(props.scenarioCode) || props.isLocked) return

  try {
    const response = await loadAssistantAvailability(props.organizationId)
    if (version !== requestVersion || props.organizationId !== response.organizationId) return
    availability.value = response
    assistantUrl.value = getAssistantUrl(response, props.organizationId, window.location.origin, window.location.pathname)
    if (Number.isFinite(response.pollingSeconds) && response.pollingSeconds > 0) {
      pollInterval = setTimeout(() => { void refresh() }, response.pollingSeconds * 1000)
    }
  } catch {
    if (version === requestVersion) {
      availability.value = null
      assistantUrl.value = null
    }
  }
}

function handleFocus(): void {
  void refresh()
}

watch(() => [props.organizationId, props.scenarioCode, props.isLocked], () => { void refresh() })

onMounted(() => {
  void refresh()
  window.addEventListener('focus', handleFocus)
})

onBeforeUnmount(() => {
  requestVersion++
  if (pollInterval) clearTimeout(pollInterval)
  window.removeEventListener('focus', handleFocus)
})
</script>

<template>
  <a
    v-if="assistantUrl || isLocked && isAssistantScenario(scenarioCode)"
    class="crm-assistant-link"
    href="#assistant"
    :aria-label="t('interaction.assistant')"
    :title="isLocked ? t('interaction.assistantPro') : t('interaction.assistant')"
  >
    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3.5 14.2 9.8 20.5 12l-6.3 2.2L12 20.5l-2.2-6.3L3.5 12l6.3-2.2L12 3.5Z" /><path d="m19 3 .7 2.3L22 6l-2.3.7L19 9l-.7-2.3L16 6l2.3-.7L19 3Z" /></svg>
    <span>{{ t('interaction.assistant') }}</span>
  </a>
</template>

<style scoped>
.crm-assistant-link {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  min-height: 40px;
  padding: 0 11px;
  border: 1px solid color-mix(in srgb, var(--crm-action-accent) 35%, var(--crm-border, #d8dce2));
  background: color-mix(in srgb, var(--crm-action-accent) 10%, var(--crm-surface, #fff));
  border-radius: 8px;
  color: var(--crm-action-accent);
  font-weight: 600;
  text-decoration: none;
  white-space: nowrap;
}
.crm-assistant-link:hover { background: color-mix(in srgb, var(--crm-action-accent) 18%, var(--crm-surface, #fff)); border-color: var(--crm-action-accent); }
.crm-assistant-link svg { width: 18px; height: 18px; fill: none; stroke: currentColor; stroke-width: 1.7; stroke-linejoin: round; }
@media (max-width: 560px) { .crm-assistant-link { width: 40px; padding: 0; justify-content: center; } .crm-assistant-link span { display: none; } }
</style>
