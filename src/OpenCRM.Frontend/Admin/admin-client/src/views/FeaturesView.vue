<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { getFeatures, saveFeature, toggleFeature } from '@/api/adminOperationsApi'
import type { AdminFeatureDefinition } from '@/models/AdminFeatureDefinition'

const items = ref<AdminFeatureDefinition[]>([])
const editingId = ref<number | null>(null)
const isOpen = ref(false)
const isSaving = ref(false)
const error = ref('')
const form = ref(emptyForm())

onMounted(load)

async function load() {
  try { items.value = await getFeatures() } catch { error.value = 'Не удалось загрузить возможности.' }
}

function emptyForm() {
  return { code: '', name: '', description: '', category: null as string | null, scopeCode: 'both', audienceCode: 'user', valueType: 'Boolean', isActive: true }
}

function open(item?: AdminFeatureDefinition) {
  editingId.value = item?.id ?? null
  form.value = item ? { code: item.code, name: item.name, description: item.description, category: item.category, scopeCode: item.scopeCode, audienceCode: item.audienceCode, valueType: item.valueType, isActive: item.isActive } : emptyForm()
  isOpen.value = true
  error.value = ''
}

async function save() {
  isSaving.value = true
  try { await saveFeature(form.value, editingId.value ?? undefined); isOpen.value = false; await load() }
  catch { error.value = 'Не удалось сохранить возможность.' } finally { isSaving.value = false }
}
</script>

<template>
  <section class="operations-page">
    <h1>Возможности продукта</h1>
    <div class="operations-toolbar"><button class="primary-button" type="button" @click="open()">Добавить</button></div>
    <div v-if="error" class="form-message form-message--error">{{ error }}</div>
    <div class="table-wrap"><table class="data-table"><thead><tr><th>Название</th><th>Код</th><th>Область</th><th>Тип</th><th>Статус</th><th></th></tr></thead><tbody>
      <tr v-for="item in items" :key="item.id"><td><strong>{{ item.name }}</strong></td><td>{{ item.code }}</td><td>{{ item.scopeCode }}</td><td>{{ item.valueType }}</td><td>{{ item.isActive ? 'Включена' : 'Отключена' }}</td><td><button class="table-action" @click="open(item)">Изменить</button> <button class="table-action" @click="toggleFeature(item.id).then(load)">{{ item.isActive ? 'Отключить' : 'Включить' }}</button></td></tr>
    </tbody></table></div>
    <div v-if="isOpen" class="modal-backdrop" @click.self="isOpen = false"><form class="modal-card" @submit.prevent="save"><div class="modal-heading"><strong>{{ editingId ? 'Изменить возможность' : 'Новая возможность' }}</strong><button type="button" @click="isOpen = false">×</button></div><div class="modal-grid">
      <label><span>Название</span><input v-model="form.name" required></label><label><span>Код</span><input v-model="form.code" required></label><label class="span-2"><span>Описание</span><textarea v-model="form.description" rows="3"></textarea></label><label><span>Категория</span><input v-model="form.category"></label><label><span>Тип значения</span><SearchSelect v-model="form.valueType" :options="[{ value: 'Boolean', label: 'Boolean' }, { value: 'Integer', label: 'Integer' }]" /></label><label><span>Область</span><SearchSelect v-model="form.scopeCode" :options="[{ value: 'user', label: 'Пользователь' }, { value: 'organization', label: 'Организация' }, { value: 'both', label: 'Обе' }]" /></label><label><span>Аудитория</span><SearchSelect v-model="form.audienceCode" :options="[{ value: 'user', label: 'Пользователь' }, { value: 'organization', label: 'Организация' }]" /></label>
    </div><div class="modal-actions"><button class="primary-button" :disabled="isSaving">Сохранить</button><button class="secondary-button" type="button" @click="isOpen = false">Отмена</button></div></form></div>
  </section>
</template>
