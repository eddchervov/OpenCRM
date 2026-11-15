<script setup lang="ts">
import { computed, ref } from 'vue'
import { UiButton, UiCard } from '@opencrm/ui'
import WorkspaceCrmShell from '../packages/workspace/src/components/WorkspaceCrmShell.vue'
import MaintenancePayments from '../packages/workspace/src/components/MaintenancePayments.vue'
import MaintenanceServicesEditor from '../packages/workspace/src/components/MaintenanceServicesEditor.vue'
import BookingTimeFields from '../packages/workspace/src/components/BookingTimeFields.vue'
import WorkspaceWorkSection from '../packages/workspace/src/components/WorkspaceWorkSection.vue'
import MaintenanceWorkProgress from '../packages/workspace/src/components/MaintenanceWorkProgress.vue'
import { demo, organizationId } from './reels-preview'

const mode = new URLSearchParams(location.search).get('scene') || 'payment'
const admin = ref(false)
const services = ref<any[]>([])
const start = ref('09:00')
const end = ref('17:00')
const endDate = ref('2026-10-14')
const nextDay = ref(false)
const context: any = {
  user: { id: 'preview', displayName: 'Администратор', email: '' },
  organization: { id: organizationId, name: 'Демо-студия · тестовые данные', primaryScenarioCode: 'detailing', allowWorkSelfAssignment: false },
  workspaceAccess: { isReadOnly: false, isBaseWorkspace: false, hasActiveSubscription: true, roleCode: 'owner', exportsEnabled: false },
  shell: { menu: [], quickActions: [] },
  links: { workspaceUrl: '#', schedulerUrl: '#', quickBookingUrl: '#', api: {} }
}
const sections = [{ code: 'workspace', title: 'Рабочий день' }, { code: 'scheduler', title: 'Расписание' }, { code: 'maintenance', title: 'Заказы' }, { code: 'catalog', title: 'Услуги и наборы' }]
const maintenance = computed<any>(() => ({ id: 'demo-order', canManagePayments: true, status: 2, buyerName: 'Демо-клиент', paidAmount: demo.paid, debtAmount: 10000 - demo.paid }))
const total = computed(() => services.value.reduce((sum, item) => sum + item.price * item.quantity, 0))
const money = (value: number) => value.toLocaleString('ru-RU') + ' ₽'
</script>

<template>
  <WorkspaceCrmShell :context="context" :sections="sections" active-section="maintenance" :breadcrumbs="[]" :profile-links="[]" display-name="Администратор">
    <div class="reel-demo">
      <template v-if="mode === 'payment'">
        <h1>BMW X3 · Полировка кузова</h1><p class="muted">Заказ · В работе</p>
        <UiCard><div class="metrics"><div>Сумма заказа<strong>10 000 ₽</strong></div><div>Оплачено<strong>{{ money(demo.paid) }}</strong></div><div>Осталось оплатить<strong>{{ money(10000 - demo.paid) }}</strong></div></div>
          <MaintenancePayments :organization-id="organizationId" :maintenance="maintenance" />
        </UiCard>
      </template>
      <template v-else-if="mode === 'bundle'">
        <h1>BMW X3 · Новая запись</h1><p class="muted">Состав работ</p>
        <UiCard><MaintenanceServicesEditor v-model="services" :organization-id="organizationId" /><div class="total">Итого: {{ money(total) }}</div></UiCard>
      </template>
      <template v-else-if="mode === 'period'">
        <h1>{{ nextDay ? 'Проверяем запись на завтра' : 'BMW X3 · Керамическое покрытие' }}</h1><p class="muted">Пост 1 · тестовая запись</p>
        <UiCard><div class="period"><label>Дата начала<input readonly :value="nextDay ? '13.10.2026' : '12.10.2026'"></label><label>Дата окончания<input readonly :value="nextDay ? '13.10.2026' : '14.10.2026'"></label></div>
          <BookingTimeFields :key="String(nextDay)" v-model:start="start" v-model:end="end" :end-date="nextDay ? '2026-10-13' : endDate" :date="nextDay ? '2026-10-13' : '2026-10-12'" :organization-id="organizationId" location="Пост 1" :exclude-maintenance-id="nextDay ? undefined : 'ceramic-order'" />
          <div class="total"><UiButton variant="secondary" @click="nextDay = !nextDay">{{ nextDay ? 'Вернуться к заказу' : 'Проверить следующий день' }}</UiButton></div>
        </UiCard>
      </template>
      <template v-else>
        <h1>{{ admin ? 'BMW X3 · Ход работ' : 'Мои работы' }}</h1><p class="muted">{{ admin ? 'Администратор' : 'Мастер Алексей' }} · тестовая смена</p>
        <MaintenanceWorkProgress v-if="admin" :organization-id="organizationId" maintenance-id="demo-order" :can-return="true" :maintenance-status="2" />
        <WorkspaceWorkSection v-else :context="context" />
        <div class="total"><UiButton variant="secondary" @click="admin = !admin">{{ admin ? 'Экран мастера' : 'Экран администратора' }}</UiButton></div>
      </template>
    </div>
  </WorkspaceCrmShell>
</template>

<style scoped>
.reel-demo { max-width:1100px; margin:0 auto; }
h1 { font-size:26px; margin:8px 0; }
.muted { color:var(--crm-text-muted); margin-bottom:24px; }
.metrics { display:grid; grid-template-columns:repeat(3, 1fr); gap:24px; padding-bottom:28px; }
.metrics strong { display:block; font-size:28px; margin-top:12px; }
.total { margin-top:24px; font-size:22px; font-weight:650; }
.period { display:grid; grid-template-columns:1fr 1fr; gap:16px; margin-bottom:20px; }
.period label { display:grid; gap:8px; }
.period input { border:1px solid var(--crm-line); border-radius:6px; padding:12px; font:inherit; }
@media(max-width:600px) { .metrics { grid-template-columns:1fr; gap:14px; } }
</style>
