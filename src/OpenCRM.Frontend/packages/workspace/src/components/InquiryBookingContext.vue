<script setup lang="ts">
import { computed, ref } from 'vue'
import { UiInput, UiPhoneInput } from '@opencrm/ui'
import type { Inquiry } from '../api/inquiries'

const props = defineProps<{ inquiry: Inquiry; selectedClientId: string }>()
const createClient = ref(false)
const name = ref(props.inquiry.contactName)
const phone = ref(props.inquiry.contactPhone ?? '')
const showCreate = computed(() => props.inquiry.canCreateClient && !props.selectedClientId)

function getNewClient() {
  if (!showCreate.value || !createClient.value) return undefined
  if (!name.value.trim()) throw new Error('Укажите имя нового клиента.')
  return { name: name.value.trim(), phone: phone.value.trim() || null, partyTypeCode: 'person' }
}
defineExpose({ getNewClient })
</script>

<template>
  <section class="inquiry-booking">
    <div><a :href="`#inquiries/${inquiry.id}`">Обращение · {{ inquiry.contactName }}</a><span v-if="inquiry.contactPhone">{{ inquiry.contactPhone }}</span></div>
    <p>{{ inquiry.interestText }}</p>
    <p v-if="inquiry.vehicleDescription">{{ inquiry.vehicleDescription }}</p>
    <label v-if="showCreate" class="inquiry-booking__toggle"><input v-model="createClient" type="checkbox" /> Создать нового клиента вместе с записью</label>
    <div v-if="showCreate && createClient" class="inquiry-booking__fields"><UiInput v-model="name" label="Имя нового клиента" required :maxlength="200" /><UiPhoneInput v-model="phone" label="Телефон нового клиента" /></div>
  </section>
</template>

<style scoped>
.inquiry-booking { display: grid; gap: 10px; padding: 14px; border: 1px solid var(--crm-line, #e3e6eb); border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); overflow-wrap: anywhere; }
.inquiry-booking > div:first-child { display: flex; gap: 12px; flex-wrap: wrap; }
.inquiry-booking a { color: var(--crm-accent, #df6c22); }
.inquiry-booking p { margin: 0; font-size: 13px; white-space: pre-wrap; }
.inquiry-booking__toggle { display: flex; align-items: center; gap: 8px; font-size: 13px; }
.inquiry-booking__fields { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
@media (max-width: 550px) { .inquiry-booking__fields { grid-template-columns: minmax(0, 1fr); } }
</style>
