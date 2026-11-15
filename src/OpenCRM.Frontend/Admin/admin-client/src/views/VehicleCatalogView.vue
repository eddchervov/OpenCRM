<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { deleteCatalogBrand, deleteCatalogModel, getCatalogBrands, getCatalogModels, saveCatalogBrand, saveCatalogModel, seedVehicleCatalog } from '@/api/adminOperationsApi'
import type { AdminCatalogBrand } from '@/models/AdminCatalogBrand'
import type { AdminCatalogModel } from '@/models/AdminCatalogModel'
const brands = ref<AdminCatalogBrand[]>([]), models = ref<AdminCatalogModel[]>([]), selectedBrand = ref<AdminCatalogBrand | null>(null), search = ref(''), kind = ref(''), error = ref('')
const modal = ref<'brand' | 'model' | null>(null), editingId = ref<number | null>(null), name = ref(''), editKind = ref(1), isSaving = ref(false)
const isSeeding = ref(false), success = ref('')
onMounted(loadBrands)
async function loadBrands() { try { brands.value = await getCatalogBrands(kind.value, search.value) } catch { error.value = 'Не удалось загрузить каталог.' } }
async function selectBrand(item: AdminCatalogBrand) { selectedBrand.value = item; models.value = await getCatalogModels(item.id) }
function openBrand(item?: AdminCatalogBrand) { modal.value = 'brand'; editingId.value = item?.id ?? null; name.value = item?.name ?? ''; editKind.value = item?.kind ?? 1 }
function openModel(item?: AdminCatalogModel) { if (!selectedBrand.value) return; modal.value = 'model'; editingId.value = item?.id ?? null; name.value = item?.name ?? '' }
async function save() { isSaving.value = true; try { if (modal.value === 'brand') await saveCatalogBrand(name.value, editKind.value, editingId.value ?? undefined); else if (selectedBrand.value) await saveCatalogModel(selectedBrand.value.id, name.value, editingId.value ?? undefined); modal.value = null; await loadBrands(); if (selectedBrand.value) await selectBrand(selectedBrand.value) } catch { error.value = 'Не удалось сохранить запись.' } finally { isSaving.value = false } }
async function removeBrand(item: AdminCatalogBrand) { if (!confirm(`Удалить марку «${item.name}»?`)) return; try { await deleteCatalogBrand(item.id); if (selectedBrand.value?.id === item.id) { selectedBrand.value = null; models.value = [] }; await loadBrands() } catch { error.value = 'Нельзя удалить используемую марку.' } }
async function removeModel(item: AdminCatalogModel) { if (!confirm(`Удалить модель «${item.name}»?`)) return; try { await deleteCatalogModel(item.id); if (selectedBrand.value) models.value = await getCatalogModels(selectedBrand.value.id) } catch { error.value = 'Нельзя удалить используемую модель.' } }
async function seedCatalog() {
  if (isSeeding.value || !confirm('Дополнить общий каталог марками и моделями остальных видов техники? Легковые не импортируются, удалённые записи не восстанавливаются.')) {
    return
  }

  isSeeding.value = true
  error.value = ''
  success.value = ''

  try {
    const result = await seedVehicleCatalog()
    const messages = [`Добавлено марок: ${result.createdBrands}, моделей: ${result.createdModels}.`]
    if (result.skippedDeletedBrands > 0 || result.skippedDeletedModels > 0) {
      messages.push(`Из-за ранее удалённых записей пропущено марок: ${result.skippedDeletedBrands}, моделей: ${result.skippedDeletedModels}.`)
    }
    success.value = messages.join(' ')
    await loadBrands()
    if (selectedBrand.value) {
      await selectBrand(brands.value.find(item => item.id === selectedBrand.value?.id) ?? selectedBrand.value)
    }
  } catch {
    error.value = 'Не удалось дополнить каталог. Попробуйте ещё раз.'
  } finally {
    isSeeding.value = false
  }
}
</script>
<template><section class="operations-page"><h1>Каталог техники</h1><div class="operations-toolbar"><button class="secondary-button" :disabled="isSeeding" @click="seedCatalog">{{ isSeeding ? 'Заполнение…' : 'Дополнить каталог техники' }}</button></div><div v-if="success" class="form-message form-message--success">{{ success }}</div><div class="operations-toolbar"><input v-model="search" placeholder="Поиск марки" @keyup.enter="loadBrands"><SearchSelect v-model="kind" :options="[{ value: '', label: 'Все типы' }, ...[1,2,3,4,5,6,7,8,9,10,99].map(value => ({ value, label: `Тип ${value}` }))]" @change="loadBrands" /><button class="secondary-button" @click="loadBrands">Найти</button><button class="primary-button" @click="openBrand()">Добавить марку</button><button class="primary-button" :disabled="!selectedBrand" @click="openModel()">Добавить модель</button></div><div v-if="error" class="form-message form-message--error">{{ error }}</div><div class="catalog-columns"><div class="table-wrap"><table class="data-table"><thead><tr><th>Марка</th><th>Тип</th><th></th></tr></thead><tbody><tr v-for="item in brands" :key="item.id" :class="{ selected: selectedBrand?.id === item.id }" @click="selectBrand(item)"><td>{{ item.name }}<small>{{ item.modelsCount }} моделей</small></td><td>{{ item.kindText }}</td><td><button class="table-action" @click.stop="openBrand(item)">Изменить</button> <button class="table-action danger-link" @click.stop="removeBrand(item)">Удалить</button></td></tr></tbody></table></div><div class="table-wrap"><table class="data-table"><thead><tr><th>{{ selectedBrand?.name || 'Модели' }}</th><th>Используется</th><th></th></tr></thead><tbody><tr v-for="item in models" :key="item.id"><td>{{ item.name }}</td><td>{{ item.vehiclesCount }}</td><td><button class="table-action" @click="openModel(item)">Изменить</button> <button class="table-action danger-link" @click="removeModel(item)">Удалить</button></td></tr></tbody></table></div></div><div v-if="modal" class="modal-backdrop" @click.self="modal = null"><form class="modal-card modal-card--compact" @submit.prevent="save"><div class="modal-heading"><strong>{{ modal === 'brand' ? 'Марка' : 'Модель' }}</strong><button type="button" @click="modal = null">×</button></div><label><span>Название</span><input v-model="name" required autofocus></label><label v-if="modal === 'brand'"><span>Тип техники</span><SearchSelect v-model="editKind" :options="[1,2,3,4,5,6,7,8,9,10,99].map(value => ({ value, label: `Тип ${value}` }))" /></label><div class="modal-actions"><button class="primary-button" :disabled="isSaving">Сохранить</button><button class="secondary-button" type="button" @click="modal = null">Отмена</button></div></form></div></section></template>
