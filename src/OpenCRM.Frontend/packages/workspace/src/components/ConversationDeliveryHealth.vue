<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { UiButton, UiLoader } from '@opencrm/ui'
import SchedulerBookingDialog from './SchedulerBookingDialog.vue'
import { loadConversationDeliveryHealth, type ConversationDeliveryHealth } from '../api/conversations'

const props = defineProps<{ organizationId: string }>()
const open = ref(false)
const loading = ref(false)
const error = ref('')
const health = ref<ConversationDeliveryHealth | null>(null)
let request: AbortController | undefined
async function load() {
  open.value = true; loading.value = true; error.value = ''
  request?.abort()
  const active = new AbortController(); request = active
  try { const result = await loadConversationDeliveryHealth(props.organizationId, active.signal); if (!active.signal.aborted) health.value = result }
  catch (caught) { if (!active.signal.aborted) error.value = caught instanceof Error ? caught.message : 'Не удалось проверить доставку.' }
  finally { if (request === active) loading.value = false }
}
onBeforeUnmount(() => request?.abort())
</script>

<template>
  <UiButton variant="secondary" class="delivery-health-trigger" @click="load">Доставка</UiButton>
  <SchedulerBookingDialog :open="open" title="Доставка сообщений организации" @close="open = false; request?.abort()">
    <div class="delivery-health">
      <UiLoader v-if="loading" text="Проверяем состояние…" />
      <p v-if="error" role="alert">{{ error }}</p>
      <template v-if="health">
        <dl><div><dt>Ожидают отправки</dt><dd>{{ health.queuedUnpublishedCount + health.queuedPublishedCount }}</dd></div><div><dt>Ожидание дольше 15 минут</dt><dd>{{ health.staleQueuedCount }}</dd></div><div><dt>Восстанавливается результат отправки</dt><dd>{{ health.expiredSendingCount }}</dd></div><div><dt>Завершились ошибкой</dt><dd>{{ health.failedCount }}</dd></div><div><dt>Результат не подтверждён</dt><dd>{{ health.uncertainCount }}</dd></div></dl>
        <p v-if="health.uncertainCount">Если ответ мессенджера потерян, сообщение не отправляется повторно автоматически: оно уже могло дойти клиенту. Проверьте переписку перед повторной отправкой.</p>
        <p v-if="health.staleQueuedCount">Ожидающие сообщения сохранены. Сервис повторно проверит доставку после восстановления соединения.</p>
        <small>Ошибки и неподтверждённые результаты включают историю организации.</small>
      </template>
      <footer><UiButton variant="secondary" :disabled="loading" @click="load">Обновить</UiButton><UiButton @click="open = false; request?.abort()">Закрыть</UiButton></footer>
    </div>
  </SchedulerBookingDialog>
</template>

<style scoped>
.delivery-health-trigger { font-size:12px; }
:global(.scheduler-booking-dialog:has(.delivery-health)) { width:min(680px, calc(100vw - 32px)); height:fit-content; max-height:calc(100dvh - 32px); border:1px solid var(--crm-line, #dce2e9); border-radius:12px; }
.delivery-health { display:grid; gap:16px; padding:20px 24px; }
.delivery-health dl { display:grid; gap:0; margin:0; }
.delivery-health dl div { display:flex; justify-content:space-between; gap:16px; padding:12px 0; border-bottom:1px solid var(--crm-line, #d4dce6); }
.delivery-health dt { font-size:14px; }
.delivery-health dd { flex-shrink:0; min-width:2ch; margin:0; font-weight:650; text-align:right; }
.delivery-health p { margin:0; line-height:1.5; font-size:13px; }
.delivery-health small { color:var(--crm-text-muted, #667085); }
.delivery-health footer { display:flex; justify-content:flex-end; flex-wrap:wrap; gap:10px; padding-top:16px; border-top:1px solid var(--crm-line, #d4dce6); }
@media (max-width:550px) { .delivery-health { padding:16px; } }
</style>
