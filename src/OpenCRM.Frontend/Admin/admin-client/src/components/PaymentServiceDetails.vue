<script setup lang="ts">
import { computed, ref } from 'vue'
import PaymentServiceActions from './PaymentServiceActions.vue'
import type { AdminBillingPayment } from '@/models/AdminBillingPayment'
import {
  formatPaymentDate,
  formatPaymentEvent,
  getPaymentAccessStatus,
  getPaymentMoneyStatus,
  getPaymentMoneySummary,
  getPaymentNotice,
  getVisiblePaymentEvents,
} from '@/paymentDetails'

const props = defineProps<{
  payment: AdminBillingPayment
  loading: boolean
  loadError: string | null
  canManage: boolean
  actorId: string
}>()

const emit = defineEmits<{
  refresh: []
  subscriptions: []
  updated: []
}>()

const showAllEvents = ref(false)
const money = computed(() => getPaymentMoneySummary(props.payment))
const notice = computed(() => getPaymentNotice(props.payment))
const allEvents = computed(() => getVisiblePaymentEvents(props.payment.providerEvents, 20))
const visibleEvents = computed(() => showAllEvents.value ? allEvents.value : allEvents.value.slice(0, 5))
const hasTechnicalIssue = computed(() => props.payment.applicationStatus === 'needs_attention' &&
  Boolean(props.payment.lastErrorCode) && props.payment.lastErrorCode !== 'refund_requires_access_review')
const hasMoneyDetails = computed(() => Number.isFinite(props.payment.amount) &&
  typeof props.payment.refundedAmount === 'number' && Number.isFinite(props.payment.refundedAmount))

function formatMoney(amount: number): string {
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 }).format(amount)
}
</script>

<template>
  <section class="payment-service" aria-labelledby="payment-service-title">
    <header class="payment-service__header">
      <div class="payment-service__heading">
        <div class="payment-service__eyebrow">Детали платежа</div>
        <h2 id="payment-service-title">Платёж №{{ payment.id }}</h2>
        <p>{{ payment.user }}<span v-if="payment.userEmail && payment.userEmail !== payment.user"> · {{ payment.userEmail }}</span></p>
      </div>
      <span v-if="payment.isTest" class="payment-service__test-badge">Тестовый платёж</span>
    </header>

    <div v-if="loading" class="payment-service__state" role="status">Обновляем сведения о платеже…</div>
    <div v-else-if="loadError || !hasMoneyDetails" class="payment-service__state payment-service__state--error" role="alert">
      <span>{{ loadError || 'Данные платежа неполные. Обновите карточку или обратитесь к специалисту.' }}</span>
      <button type="button" class="payment-service__button" @click="emit('refresh')">Повторить загрузку</button>
    </div>

    <template v-else>
      <div class="payment-service__notice" :class="`payment-service__notice--${notice.tone}`" role="status">
        <span class="payment-service__notice-mark" aria-hidden="true">{{ notice.tone === 'success' ? '✓' : notice.tone === 'danger' ? '!' : 'i' }}</span>
        <div>
          <h3>{{ notice.title }}</h3>
          <p>{{ notice.message }}</p>
          <p v-if="notice.followUp" class="payment-service__follow-up">{{ notice.followUp }}</p>
          <p v-if="hasTechnicalIssue" class="payment-service__technical-note">Обработка платежа также требует технической проверки. Передайте ID платежа специалисту.</p>
        </div>
      </div>

      <section class="payment-service__section" aria-label="Суммы платежа">
        <div class="payment-service__money-grid">
          <article class="payment-service__metric">
            <span>Сумма покупки</span>
            <strong>{{ formatMoney(money.amount) }}</strong>
          </article>
          <article class="payment-service__metric">
            <span>{{ money.confirmed ? 'Оплата подтверждена' : 'Подтверждено' }}</span>
            <strong>{{ money.confirmed ? formatMoney(money.amount) : 'Пока нет' }}</strong>
          </article>
          <article class="payment-service__metric">
            <span>Возвращено</span>
            <strong>{{ formatMoney(money.refunded) }}</strong>
          </article>
          <article class="payment-service__metric" :class="{ 'payment-service__metric--muted': money.remaining === null }">
            <span>Осталось после возвратов</span>
            <strong>{{ money.remaining === null ? '—' : formatMoney(money.remaining) }}</strong>
          </article>
        </div>
      </section>

      <div class="payment-service__columns">
        <section class="payment-service__section">
          <h3>Покупка</h3>
          <dl class="payment-service__facts">
            <div><dt>Тариф</dt><dd>{{ payment.planName || '—' }}</dd></div>
            <div v-if="payment.organizationName"><dt>Организация</dt><dd>{{ payment.organizationName }}</dd></div>
            <div><dt>Состояние денег</dt><dd>{{ getPaymentMoneyStatus(payment) }}</dd></div>
            <div><dt>Способ оплаты</dt><dd>{{ payment.paymentMethodName || '—' }}</dd></div>
            <div><dt>Создан</dt><dd>{{ formatPaymentDate(payment.createdAt) }}</dd></div>
            <div v-if="payment.paidAt"><dt>Оплачен</dt><dd>{{ formatPaymentDate(payment.paidAt) }}</dd></div>
            <div v-if="payment.externalPaymentId"><dt>ID платежа ЮKassa</dt><dd class="payment-service__id">{{ payment.externalPaymentId }}</dd></div>
          </dl>
        </section>

        <section class="payment-service__section">
          <h3>Доступ по этой покупке</h3>
          <dl class="payment-service__facts">
            <div><dt>Результат</dt><dd>{{ getPaymentAccessStatus(payment) }}</dd></div>
            <div v-if="payment.grantedFrom || payment.grantedUntil"><dt>Выданный период</dt><dd>{{ formatPaymentDate(payment.grantedFrom) }} — {{ formatPaymentDate(payment.grantedUntil) }}</dd></div>
            <div v-if="payment.appliedAt"><dt>Выдан</dt><dd>{{ formatPaymentDate(payment.appliedAt) }}</dd></div>
          </dl>
          <p v-if="payment.grantedFrom || payment.grantedUntil || payment.appliedAt" class="payment-service__access-note">Период этой покупки не показывает, действует ли доступ сейчас. Текущий статус смотрите в подписках.</p>
          <p v-else class="payment-service__access-note">Текущий статус доступа можно проверить в подписках.</p>
          <button type="button" class="payment-service__button payment-service__button--primary" title="Открывает текущие подписки получателя. Здесь можно продлить или остановить доступ с указанием причины." @click="emit('subscriptions')">Открыть подписки</button>
        </section>
      </div>

      <PaymentServiceActions :key="payment.id" :payment-id="payment.id" :can-manage="canManage" :actor-id="actorId" @updated="emit('updated')" />

      <details class="payment-service__journal">
        <summary>Технический журнал <span>{{ allEvents.length }}</span></summary>
        <p class="payment-service__journal-caption">Последние технические события: до 20 записей. Повторные служебные проверки скрыты.</p>
        <details class="payment-service__journal-ids">
          <summary>Идентификаторы и коды</summary>
          <dl>
            <div><dt>ID платежа ЮKassa</dt><dd>{{ payment.externalPaymentId || '—' }}</dd></div>
            <div><dt>ID покупки</dt><dd>{{ payment.purchaseId || '—' }}</dd></div>
            <div><dt>Код провайдера</dt><dd>{{ payment.paymentProviderCode || '—' }}</dd></div>
            <div><dt>Состояние провайдера</dt><dd>{{ payment.providerStatus || '—' }}</dd></div>
            <div><dt>Последний код ошибки</dt><dd>{{ payment.lastErrorCode || '—' }}</dd></div>
          </dl>
        </details>
        <ol v-if="visibleEvents.length" class="payment-service__events">
          <li v-for="event in visibleEvents" :key="event.id">
            <div>
              <strong>{{ formatPaymentEvent(event)?.title }}</strong>
              <span v-if="formatPaymentEvent(event)?.detail">{{ formatPaymentEvent(event)?.detail }}</span>
            </div>
            <time :datetime="event.createdAt">{{ formatPaymentDate(event.createdAt) }}</time>
            <details class="payment-service__event-ids">
              <summary>Коды и ID события</summary>
              <dl>
                <div><dt>Этап</dt><dd>{{ event.stage }}</dd></div>
                <div><dt>Код события</dt><dd>{{ event.code }}</dd></div>
                <div><dt>ID события</dt><dd>{{ event.id }}</dd></div>
                <div v-if="event.providerOrderId"><dt>Внутренний ID платёжного сервиса</dt><dd>{{ event.providerOrderId }}</dd></div>
                <div v-if="event.messageId"><dt>ID сообщения</dt><dd>{{ event.messageId }}</dd></div>
                <div v-if="event.commandId"><dt>ID команды</dt><dd>{{ event.commandId }}</dd></div>
                <div v-if="event.purchaseId"><dt>ID покупки</dt><dd>{{ event.purchaseId }}</dd></div>
                <div v-if="event.providerVersion != null"><dt>Версия провайдера</dt><dd>{{ event.providerVersion }}</dd></div>
              </dl>
            </details>
          </li>
        </ol>
        <p v-else class="payment-service__journal-empty">Событий для отображения пока нет.</p>
        <button v-if="allEvents.length > 5" type="button" class="payment-service__journal-more" @click="showAllEvents = !showAllEvents">
          {{ showAllEvents ? 'Свернуть' : `Показать ещё ${allEvents.length - 5}` }}
        </button>
      </details>

      <footer class="payment-service__footer">
        <span v-if="payment.statusUpdatedAt">Последнее обновление: {{ formatPaymentDate(payment.statusUpdatedAt) }}</span>
        <button type="button" class="payment-service__button" :disabled="loading" title="Перечитывает сохранённые данные. Для новой сверки с провайдером используйте «Проверить платёж»." @click="emit('refresh')">{{ loading ? 'Обновляем…' : 'Обновить данные' }}</button>
      </footer>
    </template>
  </section>
</template>

<style scoped>
.payment-service {
  --payment-border: #dce4e3;
  --payment-surface: #fff;
  --payment-soft: #f4f7f7;
  --payment-text: #202c32;
  --payment-muted: #607078;
  --payment-green: #216b4d;
  --payment-orange: #815114;
  --payment-red: #9e3932;
  display: grid;
  gap: 18px;
  padding: 20px;
  border: 1px solid var(--payment-border);
  border-radius: 14px;
  color: var(--payment-text);
  background: var(--payment-surface);
}

.payment-service__header,
.payment-service__footer {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.payment-service__eyebrow,
.payment-service__metric > span,
.payment-service__facts dt,
.payment-service__footer > span {
  color: var(--payment-muted);
  font-size: 12px;
}

.payment-service h2,
.payment-service h3,
.payment-service p {
  margin: 0;
}

.payment-service__heading h2 {
  margin-top: 3px;
  font-size: 20px;
}

.payment-service__heading p {
  margin-top: 5px;
  color: var(--payment-muted);
  font-size: 13px;
  overflow-wrap: anywhere;
}

.payment-service__test-badge {
  flex: none;
  padding: 6px 9px;
  border-radius: 999px;
  color: #714918;
  background: #fff0d7;
  font-size: 11px;
  font-weight: 700;
}

.payment-service__notice {
  display: grid;
  grid-template-columns: 30px minmax(0, 1fr);
  gap: 12px;
  padding: 15px;
  border: 1px solid;
  border-radius: 11px;
}

.payment-service__notice--success { border-color: #b6dac7; color: var(--payment-green); background: #eef8f2; }
.payment-service__notice--warning { border-color: #ead4a4; color: var(--payment-orange); background: #fff8e8; }
.payment-service__notice--danger { border-color: #edc3be; color: var(--payment-red); background: #fff1ef; }
.payment-service__notice--neutral { border-color: var(--payment-border); color: var(--payment-text); background: var(--payment-soft); }

.payment-service__notice-mark {
  display: grid;
  width: 28px;
  height: 28px;
  place-items: center;
  border-radius: 50%;
  background: rgb(0 0 0 / 6%);
  font-size: 15px;
  font-weight: 800;
}

.payment-service__notice h3 { margin: 2px 0 6px; font-size: 15px; }
.payment-service__notice p { font-size: 13px; line-height: 1.5; }
.payment-service__follow-up { margin-top: 7px !important; font-weight: 600; }
.payment-service__technical-note { margin-top: 8px !important; color: var(--payment-muted); font-size: 12px !important; }

.payment-service__section {
  min-width: 0;
  padding: 15px;
  border: 1px solid var(--payment-border);
  border-radius: 11px;
}

.payment-service__money-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.payment-service__metric {
  display: grid;
  min-width: 0;
  gap: 6px;
}

.payment-service__metric strong { font-size: 17px; overflow-wrap: anywhere; }
.payment-service__metric--muted strong { color: var(--payment-muted); }

.payment-service__columns {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.payment-service__section h3 { margin-bottom: 12px; font-size: 14px; }
.payment-service__facts { display: grid; margin: 0; gap: 9px; }
.payment-service__facts > div { display: grid; grid-template-columns: minmax(120px, .7fr) minmax(0, 1.3fr); gap: 12px; }
.payment-service__facts dd { margin: 0; font-size: 13px; overflow-wrap: anywhere; }
.payment-service__id { font-family: ui-monospace, SFMono-Regular, Consolas, monospace; font-size: 12px !important; }
.payment-service__access-note { margin: 12px 0 !important; color: var(--payment-muted); font-size: 12px; line-height: 1.45; }

.payment-service__button,
.payment-service__journal-more {
  min-height: 36px;
  padding: 8px 12px;
  border: 1px solid var(--payment-border);
  border-radius: 8px;
  color: var(--payment-text);
  background: var(--payment-surface);
  font: inherit;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
}

.payment-service__button:hover,
.payment-service__journal-more:hover { background: var(--payment-soft); }
.payment-service__button:disabled { opacity: .6; cursor: wait; }
.payment-service__button--primary { border-color: #cf6c31; color: #fff; background: #cf6c31; }
.payment-service__button--primary:hover { background: #b95724; }

.payment-service__journal { border-top: 1px solid var(--payment-border); padding-top: 13px; }
.payment-service__journal summary { display: flex; width: fit-content; align-items: center; gap: 8px; cursor: pointer; font-size: 13px; font-weight: 700; }
.payment-service__journal summary::before { content: '›'; flex: none; font-size: 17px; line-height: 1; transition: transform .15s ease; }
.payment-service__journal details[open] > summary::before,
.payment-service__journal[open] > summary::before { transform: rotate(90deg); }
.payment-service__journal summary span { padding: 2px 7px; border-radius: 99px; color: var(--payment-muted); background: var(--payment-soft); font-size: 11px; }
.payment-service__journal-caption,
.payment-service__journal-empty { margin-top: 8px !important; color: var(--payment-muted); font-size: 12px; }
.payment-service__events { display: grid; margin: 10px 0; padding: 0; list-style: none; }
.payment-service__events li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 4px 12px; padding: 10px 0; border-bottom: 1px solid var(--payment-border); }
.payment-service__events li > div { display: grid; gap: 3px; }
.payment-service__events strong { font-size: 12px; }
.payment-service__events span,
.payment-service__events time,
.payment-service__events small { color: var(--payment-muted); font-size: 11px; }
.payment-service__events small { grid-column: 1 / -1; overflow-wrap: anywhere; }
.payment-service__journal-more { margin-top: 4px; }
.payment-service__journal-ids,
.payment-service__event-ids { margin-top: 10px; color: var(--payment-muted); font-size: 11px; }
.payment-service__journal-ids summary,
.payment-service__event-ids summary { cursor: pointer; }
.payment-service__journal-ids dl,
.payment-service__event-ids dl { display: grid; margin: 8px 0 0; gap: 5px; }
.payment-service__journal-ids dl > div,
.payment-service__event-ids dl > div { display: grid; grid-template-columns: minmax(140px, .7fr) minmax(0, 1.3fr); gap: 10px; }
.payment-service__journal-ids dt,
.payment-service__event-ids dt { color: var(--payment-muted); }
.payment-service__journal-ids dd,
.payment-service__event-ids dd { margin: 0; overflow-wrap: anywhere; font-family: ui-monospace, SFMono-Regular, Consolas, monospace; }
.payment-service__footer { align-items: center; padding-top: 2px; }
.payment-service__state { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px; border-radius: 9px; background: var(--payment-soft); font-size: 13px; }
.payment-service__state--error { color: var(--payment-red); background: #fff1ef; }

:global(:root[data-admin-theme='dark'] .payment-service) {
  --payment-border: #3d514e;
  --payment-surface: #1c292b;
  --payment-soft: #223234;
  --payment-text: #e5efeb;
  --payment-muted: #afc0ba;
  --payment-green: #a8dec0;
  --payment-orange: #f1d08b;
  --payment-red: #ffb4ac;
}

:global(:root[data-admin-theme='dark'] .payment-service__notice--success) { border-color: #426a55; background: #20382d; }
:global(:root[data-admin-theme='dark'] .payment-service__notice--warning) { border-color: #705a32; background: #3a3323; }
:global(:root[data-admin-theme='dark'] .payment-service__state--error) { background: #3a2828; }
:global(:root[data-admin-theme='dark'] .payment-service__notice--danger) { border-color: #754440; background: #3a2828; }
:global(:root[data-admin-theme='dark'] .payment-service__test-badge) { color: #f3d7a8; background: #493a26; }
:global(:root[data-admin-theme='dark'] .payment-service__button--primary) { border-color: #f19859; color: #1c292b; background: #f19859; }

@media (max-width: 760px) {
  .payment-service { gap: 13px; padding: 14px; }
  .payment-service__money-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .payment-service__columns { grid-template-columns: minmax(0, 1fr); }
  .payment-service__facts > div { grid-template-columns: minmax(100px, .65fr) minmax(0, 1.35fr); }
}

@media (max-width: 420px) {
  .payment-service__header { flex-direction: column; gap: 8px; }
  .payment-service__events li { grid-template-columns: minmax(0, 1fr); }
  .payment-service__footer { align-items: flex-start; flex-direction: column; }
}
</style>
