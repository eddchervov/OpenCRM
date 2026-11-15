<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { changeEmailCampaignStatus, getEmailCampaigns } from '@/api/adminOperationsApi'
import type { AdminEmailCampaign } from '@/models/AdminEmailCampaign'
const items = ref<AdminEmailCampaign[]>([]), error = ref('')
onMounted(load)
async function load() { try { items.value = (await getEmailCampaigns()).items } catch { error.value = 'Не удалось загрузить рассылки.' } }
async function change(item: AdminEmailCampaign, action: 'pause' | 'resume' | 'cancel') { try { await changeEmailCampaignStatus(item.id, action); await load() } catch { error.value = 'Не удалось изменить рассылку.' } }
function date(value: string) { return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
</script>
<template><section class="operations-page"><h1>Email-рассылки</h1><div class="operations-toolbar operations-toolbar--end"><RouterLink class="primary-button link-button" to="/communications/email-compose">Написать письмо</RouterLink></div><div v-if="error" class="form-message form-message--error">{{ error }}</div><div class="table-wrap"><table class="data-table"><thead><tr><th>Тема</th><th>Запуск</th><th>Прогресс</th><th>Ошибки</th><th>Статус</th><th></th></tr></thead><tbody><tr v-for="item in items" :key="item.id"><td>{{ item.subject }}</td><td>{{ date(item.startAt) }}</td><td>{{ item.sentCount }} / {{ item.totalCount }}<span v-if="item.simulatedCount"> (имитация: {{ item.simulatedCount }})</span></td><td>{{ item.failedCount }}</td><td>{{ item.statusCode }}</td><td><button v-if="item.statusCode === 'running'" class="table-action" @click="change(item, 'pause')">Пауза</button><button v-if="item.statusCode === 'paused'" class="table-action" @click="change(item, 'resume')">Продолжить</button><button v-if="!['completed','canceled'].includes(item.statusCode)" class="table-action danger-link" @click="change(item, 'cancel')">Отменить</button></td></tr></tbody></table></div></section></template>
