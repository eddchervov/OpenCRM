<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../billingMessages'
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import { UiButton } from '@opencrm/ui'
import { loadOrganizationExportStatus, organizationExportDownloadUrl, requestOrganizationExport, type OrganizationExportStatus } from '../api/organizationExport'

const { t } = useLocalization()

const props = defineProps<{ organizationId: string }>()
const status = ref<OrganizationExportStatus>({ id: null, state: 'idle' })
const isRequesting = ref(false)
const errorMessage = ref('')
const downloadTarget = `organization-export-${useId()}`
const downloadLink = ref<HTMLAnchorElement | null>(null)
const isPreparing = computed(() => status.value.state === 'queued' || status.value.state === 'running')
const buttonLabel = computed(() => {
  if (status.value.state === 'queued') return t('billing.queued')
  if (status.value.state === 'running') return t('billing.archiving')
  return status.value.state === 'ready' ? t('billing.downloadZip') : t('billing.prepareZip')
})
let requestId = 0
let pollTimer: ReturnType<typeof setTimeout> | undefined
let shouldDownload = false

function clearPoll() {
  if (pollTimer !== undefined) clearTimeout(pollTimer)
  pollTimer = undefined
}

function showError(error: unknown) {
  errorMessage.value = error instanceof Error ? error.message : t('billing.exportError')
}

async function applyStatus(result: OrganizationExportStatus, request: number) {
  if (request !== requestId) return
  status.value = result
  errorMessage.value = result.state === 'failed' ? result.errorMessage || t('billing.archiveError') : ''
  if (result.state === 'ready' && result.id && shouldDownload) {
    shouldDownload = false
    if (downloadLink.value) {
      downloadLink.value.href = organizationExportDownloadUrl(props.organizationId, result.id)
      downloadLink.value.click()
    }
  }
  if (isPreparing.value) {
    pollTimer = setTimeout(() => { void refresh(request) }, 3000)
  }
}

async function refresh(request: number) {
  try {
    await applyStatus(await loadOrganizationExportStatus(props.organizationId), request)
  } catch (error) {
    if (request === requestId) {
      showError(error)
      if (isPreparing.value) pollTimer = setTimeout(() => { void refresh(request) }, 10000)
    }
  }
}

async function download() {
  if (isRequesting.value || isPreparing.value) return
  clearPoll()
  const request = ++requestId
  isRequesting.value = true
  shouldDownload = true
  errorMessage.value = ''
  try {
    await applyStatus(await requestOrganizationExport(props.organizationId), request)
  } catch (error) {
    if (request === requestId) showError(error)
  } finally {
    if (request === requestId) isRequesting.value = false
  }
}

function downloadLoaded(event: Event) {
  const frame = event.target as HTMLIFrameElement
  try {
    const body = frame.contentDocument?.body
    if (!body?.textContent?.trim()) return
    const result = JSON.parse(body.textContent)
    errorMessage.value = typeof result.message === 'string' && result.message.trim()
      ? result.message : t('billing.downloadError')
  } catch {
    errorMessage.value = t('billing.downloadError')
  }
}

watch(() => props.organizationId, () => {
  clearPoll()
  const request = ++requestId
  shouldDownload = false
  isRequesting.value = false
  status.value = { id: null, state: 'idle' }
  errorMessage.value = ''
  void refresh(request)
}, { immediate: true })
onBeforeUnmount(() => { requestId++; clearPoll() })
</script>

<template>
  <section class="workspace-common-subpanel workspace-export">
    <h3 class="ui-section-header">{{ t('billing.exportTitle') }}</h3>
    <p>{{ t('billing.exportHint') }}</p>
    <UiButton variant="secondary" :disabled="isRequesting || isPreparing" :aria-busy="isRequesting || isPreparing" @click="download">
      {{ buttonLabel }}
    </UiButton>
    <p v-if="errorMessage" class="workspace-export__error" role="alert">{{ errorMessage }}</p>
    <a ref="downloadLink" :target="downloadTarget" hidden aria-hidden="true" tabindex="-1">{{ t('billing.downloadZip') }}</a>
    <iframe :name="downloadTarget" hidden :title="t('billing.exportDownload')" @load="downloadLoaded" />
  </section>
</template>

<style scoped>
.workspace-export {
  display: grid;
  justify-items: start;
}

.workspace-export p {
  margin: 0;
}

.workspace-export__error {
  color: var(--crm-danger-text, #a12719);
}
</style>
