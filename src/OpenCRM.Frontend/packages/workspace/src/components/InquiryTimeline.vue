<script setup lang="ts">
import { inquiryStages, type InquiryHistoryEvent } from '../api/inquiries'
import type { WorkspaceContext } from '../types'
import { formatInquiryTime } from '../utils/inquiryTime'

const props = defineProps<{ events: InquiryHistoryEvent[]; timeZoneId: string | null; members?: WorkspaceContext['organization']['members']; compact?: boolean }>()

function eventDescription(event: InquiryHistoryEvent) {
  if (event.description && event.eventType !== 'stage_changed') return event.description
  if (event.eventType === 'action_completed') return event.previousValue || ''
  if (event.eventType === 'closed') return event.newValue || ''
  if (['client', 'vehicle', 'converted'].includes(event.eventType) || (!event.previousValue && !event.newValue)) return ''
  const displayValue = (value?: string | null) => {
    if (!value) return 'Не указано'
    if (event.eventType === 'stage_changed') return inquiryStages.find(stage => stage.value === value)?.label ?? value
    if (event.eventType === 'responsible') {
      const member = props.members?.find(item => item.userId === value)
      return member?.employeeName || member?.userName || member?.email || 'Участник организации'
    }
    return value
  }
  return event.previousValue ? `${displayValue(event.previousValue)} → ${displayValue(event.newValue)}` : displayValue(event.newValue)
}
</script>

<template>
  <div class="inquiry-timeline" :class="{ 'inquiry-timeline--compact': compact }">
    <article v-for="event in events" :key="event.id" class="inquiry-event">
      <header><strong>{{ event.title }}</strong><time :datetime="event.createdAt">{{ formatInquiryTime(event.createdAt, timeZoneId) }}</time></header>
      <p v-if="eventDescription(event)" class="inquiry-event__content">{{ eventDescription(event) }}</p>
      <p v-if="event.eventType === 'stage_changed' && event.description" class="inquiry-event__content inquiry-event__comment">{{ event.description }}</p>
      <small v-if="event.actorName">{{ event.actorName }}</small>
    </article>
    <p v-if="!events.length" class="inquiry-timeline__empty">История пока пуста</p>
  </div>
</template>

<style scoped>
.inquiry-event { position: relative; display: grid; gap: 7px; padding: 0 0 24px 24px; margin-left: 5px; border-left: 1px solid var(--crm-line, #dfe3ea); font-size: 13px; }
.inquiry-event:last-child { padding-bottom: 0; border-left-color: transparent; }
.inquiry-event::before { position: absolute; content: ''; left: -5px; top: 4px; width: 9px; height: 9px; border-radius: 50%; background: var(--crm-accent, #df6c22); box-shadow: 0 0 0 4px var(--crm-surface, #fff); }
.inquiry-event header { display: flex; justify-content: space-between; align-items: baseline; gap: 8px 16px; flex-wrap: wrap; }
.inquiry-event p { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; line-height: 1.55; }
.inquiry-event .inquiry-event__comment { padding: 9px 12px; border-radius: 6px; background: var(--crm-surface-soft, #f5f6f8); }
.inquiry-event time, .inquiry-event small, .inquiry-timeline__empty { color: var(--crm-text-muted, #667085); font-size: 12px; overflow-wrap: anywhere; }
.inquiry-timeline--compact .inquiry-event__content { display: -webkit-box; -webkit-box-orient: vertical; -webkit-line-clamp: 4; overflow: hidden; }
</style>
