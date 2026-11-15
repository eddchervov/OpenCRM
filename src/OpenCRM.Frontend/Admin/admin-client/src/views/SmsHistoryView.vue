<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { getSmsHistory } from '@/api/adminOperationsApi'
import type { AdminSmsHistory } from '@/models/AdminSmsHistory'
const items = ref<AdminSmsHistory[]>([]), search = ref(''), error = ref('')
const filtered = computed(() => { const q = search.value.trim().toLowerCase(); return q ? items.value.filter(x => x.phone.includes(q) || x.text.toLowerCase().includes(q) || x.smsCode.toLowerCase().includes(q)) : items.value })
onMounted(async () => { try { items.value = (await getSmsHistory()).histories } catch { error.value = 'Не удалось загрузить SMS.' } })
function date(value: string) { return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value)) }
</script>
<template><section class="operations-page"><h1>История SMS</h1><div class="operations-toolbar"><input v-model="search" type="search" placeholder="Телефон или текст"></div><div v-if="error" class="form-message form-message--error">{{ error }}</div><div class="table-wrap"><table class="data-table"><thead><tr><th>Телефон</th><th>Текст</th><th>Код</th><th>Дата</th><th>Ответ</th></tr></thead><tbody><tr v-for="item in filtered" :key="item.id"><td>{{ item.phone }}</td><td>{{ item.text }}</td><td>{{ item.smsCode }}</td><td>{{ date(item.dateTime) }}</td><td>{{ item.response || '—' }}</td></tr></tbody></table></div></section></template>
