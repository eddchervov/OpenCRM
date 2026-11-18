<script setup lang="ts">
import { computed } from 'vue'
import { formattingCulture, useLocalization } from '@opencrm/ui/i18n'
import '../workdayCardMessages'
import { formatDateOnly, parseDateOnly } from '@opencrm/ui'
import type { WorkspaceWorkdayItem } from '../api/workday'
import { getVehicleBrandLogo } from '../utils/vehicleBrandLogo'
import { getWorkdayProgress, shouldOpenRecord } from '../utils/workdayCard'

const props = withDefaults(defineProps<{
  item: WorkspaceWorkdayItem
  stage: 'planned' | 'inProgress' | 'ready' | 'handedOver'
  today: string
  timeZoneId?: string | null
  canOpen?: boolean
}>(), {
  timeZoneId: 'UTC',
  canOpen: true
})

const emit = defineEmits<{
  open: [id: string]
}>()
const { t } = useLocalization()
const isFinished = computed(() => props.stage === 'ready' || props.stage === 'handedOver')
const handoverTime = computed(() => {
  const value = props.item.handedOverAt ?? props.item.readyForHandoverAt
  return value ? new Intl.DateTimeFormat(formattingCulture.value, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: props.timeZoneId || 'UTC' }).format(new Date(value)) : ''
})
const href = computed(() => `#maintenance/${props.item.id}`)
const brandLogo = computed(() => getVehicleBrandLogo(props.item.vehicleBrandLogoKey))
const progress = computed(() => getWorkdayProgress(props.item.workTotalCount, props.item.workCompletedCount))
const date = computed(() => props.item.date.slice(0, 10))
const endDate = computed(() => props.item.endDate?.slice(0, 10) || date.value)
const formattedEndDate = computed(() => parseDateOnly(endDate.value)?.toLocaleDateString(formattingCulture.value, { year: 'numeric', month: '2-digit', day: '2-digit' }) ?? '')
const plannedEnd = computed(() => [props.item.endDate && endDate.value !== props.today ? formattedEndDate.value : '', props.item.endTime?.slice(0, 5)].filter(Boolean).join(' '))
const isPastBooking = computed(() => props.stage === 'planned' && endDate.value < props.today)
const isContinuing = computed(() => date.value < props.today && endDate.value >= props.today)
const endsToday = computed(() => isContinuing.value && endDate.value === props.today)
const phone = computed(() => props.item.clientPhone?.replace(/[^+\d]/g, '') ?? '')
const dateLabel = computed(() => {
  if (date.value === props.today) return t('workdayCard.today')
  const tomorrow = parseDateOnly(props.today)!
  tomorrow.setDate(tomorrow.getDate() + 1)
  if (date.value === formatDateOnly(tomorrow)) return t('workdayCard.tomorrow')
  return parseDateOnly(date.value)?.toLocaleDateString(formattingCulture.value, { day: 'numeric', month: 'short' }) ?? ''
})

function openRecord(event: MouseEvent) {
  if (!shouldOpenRecord(props.canOpen, event)) return

  event.preventDefault()
  emit('open', props.item.id)
}
</script>

<template>
  <article class="day-job" :class="[`day-job--${stage}`, { 'day-job--past': isPastBooking }]">
    <div class="day-job__timing">
      <span v-if="stage === 'planned'" class="day-job__time">{{ item.startTime?.slice(0, 5) || t('workdayCard.noTime') }}</span>
      <span v-else-if="stage === 'inProgress'" class="day-job__state"><i></i>{{ t('workdayCard.inProgress') }}</span>
      <span v-else class="day-job__state">{{ stage === 'ready' ? t('workdayCard.ready') : t('workdayCard.handedOver') }}</span>
      <span v-if="isFinished" class="day-job__date">{{ handoverTime }}</span>
      <span v-if="!isFinished" class="day-job__date">{{ dateLabel }}</span>
    </div>
    <div class="day-job__vehicle">
      <img
        v-if="brandLogo"
        class="day-job__brand-logo"
        :src="brandLogo.src"
        :alt="t('workdayCard.logo', { name: brandLogo.name })"
      >
      <a v-if="canOpen" :href="href" @click="openRecord">{{ item.vehicleName }}</a>
      <span v-else class="day-job__vehicle-name">{{ item.vehicleName }}</span>
      <span v-if="item.registrationPlate && !item.vehicleName.includes(item.registrationPlate)" class="day-job__plate">{{ item.registrationPlate }}</span>
    </div>
    <p class="day-job__services">{{ item.services.slice(0, 3).join(' · ') || t('workdayCard.noServices') }}<span v-if="item.services.length > 3"> · {{ t('workdayCard.more', { count: item.services.length - 3 }) }}</span></p>
    <div v-if="item.hasTrackedWork !== false && progress.total && stage !== 'planned'" class="day-job__progress">
      <div
        class="day-job__progressbar"
        :class="{ 'day-job__progressbar--complete': progress.isComplete }"
        role="progressbar"
        :aria-label="t('workdayCard.progressLabel', progress)"
        :aria-valuemin="0"
        :aria-valuemax="progress.total"
        :aria-valuenow="progress.completed"
        :aria-valuetext="t('workdayCard.progressValue', progress)"
      >
        <span :style="{ width: `${progress.percent}%` }"></span>
      </div>
      <p class="day-job__progress-label">
        {{ progress.isComplete && stage === 'inProgress' ? t('workdayCard.awaitingReview') : t('workdayCard.progress', progress) }}
        <span v-if="item.workPausedCount"> · {{ t('workdayCard.paused', { count: item.workPausedCount }) }}</span>
      </p>
    </div>
    <div v-if="item.clientName || phone" class="day-job__client">
      <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="3.5"/><path d="M5 21v-3a7 7 0 0 1 14 0v3"/></svg>
      <span>{{ item.clientName || t('workdayCard.client') }}</span>
      <a v-if="phone" :href="`tel:${phone}`" :aria-label="t('workdayCard.call', { phone: item.clientPhone ?? '' })" class="day-job__call" :title="t('workdayCard.callClient')">
        {{ item.clientPhone }}
      </a>
    </div>
    <div v-if="item.location || item.performer || (item.endTime && !isFinished)" class="day-job__details">
      <span v-if="item.location">{{ item.location }}</span>
      <span v-if="item.performer">{{ item.performer }}</span>
      <span v-if="item.endTime && !isFinished">{{ t('workdayCard.plannedUntil', { time: plannedEnd }) }}</span>
    </div>
    <div class="day-job__footer">
      <span v-if="isPastBooking" class="day-job__attention">{{ t('workdayCard.past') }}</span>
      <span v-else-if="isContinuing && !isFinished">{{ endsToday ? t('workdayCard.endsToday') : t('workdayCard.continuing') }}</span>
      <a v-if="canOpen" :href="href" @click="openRecord">{{ isPastBooking ? t('workdayCard.clarify') : stage === 'planned' ? t('workdayCard.openBooking') : t('workdayCard.openWork') }}</a>
    </div>
  </article>
</template>

<style scoped>
.day-job {
  background: var(--crm-surface, #fff);
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 9px;
  padding: 12px;
  min-width: 0;
  transition: border-color .15s, box-shadow .15s;
}

.day-job:hover {
  border-color: var(--opencrm-accent, #e67337);
  box-shadow: 0 3px 12px var(--crm-shadow, rgba(16, 24, 40, .05));
}

.day-job--inProgress {
  border-top: 3px solid var(--opencrm-accent, #e67337);
  padding-top: 10px;
}

.day-job__timing, .day-job__vehicle, .day-job__client, .day-job__footer {
  display: flex;
  align-items: center;
  gap: 10px;
}

.day-job__timing {
  justify-content: space-between;
  margin-bottom: 9px;
  font-size: 12px;
}

.day-job__time {
  font-size: 15px;
  font-weight: 750;
  font-variant-numeric: tabular-nums;
  color: var(--crm-text, #202733);
}

.day-job__date, .day-job__details, .day-job__client {
  color: var(--crm-text-muted, #667085);
}

.day-job__state {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-weight: 650;
  color: var(--crm-text-muted, #667085);
}

.day-job__state i {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--opencrm-accent, #e67337);
}

.day-job--ready .day-job__state, .day-job--handedOver .day-job__state {
  color: var(--crm-text, #247564);
}

.day-job__vehicle {
  flex-wrap: wrap;
  gap: 8px;
}

.day-job__brand-logo {
  display: inline-block;
  flex: 0 0 22px;
  width: 22px;
  height: 22px;
  padding: 2px;
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 50%;
  background: #fff;
  object-fit: contain;
}

.day-job__vehicle a, .day-job__vehicle-name {
  color: var(--crm-text, #202733);
  font-size: 14px;
  font-weight: 750;
  text-decoration: none;
  overflow-wrap: anywhere;
}

.day-job__vehicle a:hover {
  text-decoration: underline;
}

.day-job__plate {
  border: 1px solid var(--crm-line, #e3e6eb);
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 11px;
  font-weight: 650;
  color: var(--crm-text-muted, #667085);
  text-transform: uppercase;
}

.day-job__services {
  margin: 7px 0 10px;
  font-size: 12px;
  line-height: 1.45;
  color: var(--crm-text, #344054);
  overflow-wrap: anywhere;
}

.day-job__progress {
  margin: 0 0 10px;
  color: var(--crm-text-muted, #667085);
  font-size: 11px;
  font-weight: 650;
}

.day-job__progressbar {
  height: 5px;
  overflow: hidden;
  border-radius: 999px;
  background: var(--crm-line, #e3e6eb);
}

.day-job__progressbar span {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: var(--opencrm-accent, #e67337);
  transition: width .2s ease;
}

.day-job__progressbar--complete span {
  background: #389b82;
}

.day-job__progress-label {
  margin: 5px 0 0;
}

.day-job__client {
  display: grid;
  grid-template-columns: 15px minmax(0, 1fr);
  gap: 4px 8px;
  font-size: 11px;
  min-height: 24px;
}

.day-job__client > span {
  overflow-wrap: anywhere;
  min-width: 0;
}

.day-job svg {
  width: 15px;
  height: 15px;
  flex-shrink: 0;
  fill: none;
  stroke: currentColor;
  stroke-width: 1.6;
  stroke-linecap: round;
  stroke-linejoin: round;
}

.day-job__call {
  grid-column: 2;
  justify-self: start;
  color: var(--crm-text, #202733);
  font-size: 12px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  text-decoration: underline;
  text-decoration-color: var(--crm-line, #e3e6eb);
  text-underline-offset: 3px;
  overflow-wrap: anywhere;
  user-select: text;
}

.day-job__call:hover {
  color: var(--opencrm-accent);
  text-decoration-color: currentColor;
}

.day-job__details {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  margin-top: 8px;
  font-size: 10px;
}

.day-job__footer {
  margin-top: 10px;
  padding-top: 9px;
  border-top: 1px solid var(--crm-line, #eef0f3);
  justify-content: flex-end;
}

.day-job__footer a {
  color: var(--crm-text, #344054);
  text-decoration: none;
  font-size: 12px;
  font-weight: 650;
  display: flex;
  gap: 8px;
  align-items: center;
}

.day-job__footer a:hover {
  color: var(--opencrm-accent);
}

.day-job__attention {
  margin-right: auto;
  font-size: 11px;
  color: var(--crm-text-muted, #9e4e18);
}

.day-job a:focus-visible {
  outline: 2px solid var(--opencrm-accent);
  outline-offset: 3px;
}

@media (max-width: 420px) {
  .day-job {
    padding: 15px;
  }
}
</style>
