<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { changeEmailCampaignStatus, getEmailCampaigns } from '@/api/adminOperationsApi'
import type { AdminEmailCampaign } from '@/models/AdminEmailCampaign'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'
const items = ref<AdminEmailCampaign[]>([]), error = ref('')
const { t } = useLocalization()
onMounted(load)
async function load() { try { items.value = (await getEmailCampaigns()).items } catch { error.value = t('adminOperations.loadCampaignsError') } }
async function change(item: AdminEmailCampaign, action: 'pause' | 'resume' | 'cancel') { try { await changeEmailCampaignStatus(item.id, action); await load() } catch { error.value = t('adminOperations.changeCampaignError') } }
function date(value: string) { return new Intl.DateTimeFormat(formattingCulture.value, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
function status(value: string) {
  const key = value === 'running' ? 'campaignRunning'
    : value === 'paused' ? 'campaignPaused'
      : value === 'completed' ? 'campaignCompleted'
        : value === 'canceled' ? 'campaignCanceled'
          : null

  return key ? t(`adminOperations.${key}`) : value
}
</script>
<template><section class="operations-page"><h1>{{ t('adminOperations.emailCampaigns') }}</h1><div class="operations-toolbar operations-toolbar--end"><RouterLink class="primary-button link-button" to="/communications/email-compose">{{ t('adminOperations.writeEmail') }}</RouterLink></div><div v-if="error" class="form-message form-message--error">{{ error }}</div><div class="table-wrap"><table class="data-table"><thead><tr><th>{{ t('adminOperations.subject') }}</th><th>{{ t('adminOperations.launch') }}</th><th>{{ t('adminOperations.progress') }}</th><th>{{ t('adminOperations.errors') }}</th><th>{{ t('adminOperations.status') }}</th><th></th></tr></thead><tbody><tr v-for="item in items" :key="item.id"><td>{{ item.subject }}</td><td>{{ date(item.startAt) }}</td><td>{{ item.sentCount }} / {{ item.totalCount }}<span v-if="item.simulatedCount">{{ t('adminOperations.simulation', { count: item.simulatedCount }) }}</span></td><td>{{ item.failedCount }}</td><td>{{ status(item.statusCode) }}</td><td><button v-if="item.statusCode === 'running'" class="table-action" @click="change(item, 'pause')">{{ t('adminOperations.pause') }}</button><button v-if="item.statusCode === 'paused'" class="table-action" @click="change(item, 'resume')">{{ t('adminOperations.resume') }}</button><button v-if="!['completed','canceled'].includes(item.statusCode)" class="table-action danger-link" @click="change(item, 'cancel')">{{ t('adminOperations.cancelCampaign') }}</button></td></tr></tbody></table></div></section></template>
