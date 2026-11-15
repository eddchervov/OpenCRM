<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import { isFreePlan, isRequiredFreeLimit } from '@/utils/subscriptionPolicy'
import {
  createAdminSubscriptionPlan,
  deleteAdminSubscriptionPlan,
  getAdminPlanFeatures,
  getAdminSubscriptionPlans,
  saveAdminPlanFeatures,
  toggleAdminSubscriptionPlan,
  updateAdminSubscriptionPlan
} from '@/api/adminSubscriptionPlansApi'
import type { AdminPlanFeature } from '@/models/AdminPlanFeature'
import type { AdminSubscriptionPlan } from '@/models/AdminSubscriptionPlan'
import type { AdminSubscriptionPlanForm } from '@/models/AdminSubscriptionPlanForm'

const plans = ref<AdminSubscriptionPlan[]>([])
const selectedPlan = ref<AdminSubscriptionPlan | null>(null)
const features = ref<AdminPlanFeature[]>([])
const form = ref<AdminSubscriptionPlanForm>(createEmptyForm())
const isCreating = ref(false)
const isLoading = ref(false)
const isSaving = ref(false)
const isLoadingFeatures = ref(false)
let featuresRequest = 0
const errorMessage = ref<string | null>(null)
const successMessage = ref<string | null>(null)

const businessPlans = computed(() => plans.value
  .filter(plan => plan.planTypeCode === 'business' && !plan.isDefaultPlan && !plan.isDemoPlan)
  .sort((first, second) => Number(isFreePlan(second)) - Number(isFreePlan(first))))
const historicalPlans = computed(() => plans.value.filter(plan => plan.planTypeCode !== 'business' || plan.isDefaultPlan || plan.isDemoPlan))
const isSystemFree = computed(() => selectedPlan.value !== null && isFreePlan(selectedPlan.value))
const isReadOnly = computed(() => selectedPlan.value?.planTypeCode !== 'business' && !isCreating.value)

onMounted(() => {
  void loadPlans()
})

async function loadPlans(selectedId?: number): Promise<void> {
  isLoading.value = true
  errorMessage.value = null

  try {
    plans.value = await getAdminSubscriptionPlans()
    const nextPlan = plans.value.find(plan => plan.id === selectedId)
    if (nextPlan) {
      await selectPlan(nextPlan, false)
    } else {
      closeEditor()
    }
  } catch (error) {
    closeEditor()
    errorMessage.value = getErrorMessage(error, 'Не удалось загрузить тарифы.')
  } finally {
    isLoading.value = false
  }
}

async function selectPlan(plan: AdminSubscriptionPlan, clearStatus = true): Promise<void> {
  const requestId = ++featuresRequest
  isLoadingFeatures.value = true
  selectedPlan.value = plan
  isCreating.value = false
  form.value = {
    name: plan.name,
    description: plan.description,
    price: plan.price,
    discountPrice: plan.discountPrice,
    durationDays: plan.durationDays,
    vehicleLimit: plan.vehicleLimit,
    order: plan.order,
    group: plan.group,
    planTypeCode: plan.planTypeCode,
    isActive: plan.isActive,
    isDefaultPlan: plan.isDefaultPlan,
    isDemoPlan: plan.isDemoPlan
  }
  features.value = []
  if (clearStatus) {
    clearMessages()
  }

  try {
    const response = await getAdminPlanFeatures(plan.id)
    if (requestId === featuresRequest) {
      features.value = response
    }
  } catch (error) {
    if (requestId === featuresRequest) {
      errorMessage.value = getErrorMessage(error, 'Не удалось загрузить возможности тарифа.')
    }
  } finally {
    if (requestId === featuresRequest) {
      isLoadingFeatures.value = false
    }
  }
}

function createPlan(): void {
  ++featuresRequest
  isLoadingFeatures.value = false
  selectedPlan.value = null
  isCreating.value = true
  form.value = createEmptyForm()
  features.value = []
  clearMessages()
}

function closeEditor(): void {
  ++featuresRequest
  isLoadingFeatures.value = false
  selectedPlan.value = null
  isCreating.value = false
  features.value = []
}

async function savePlan(): Promise<void> {
  isSaving.value = true
  clearMessages()

  try {
    const saved = isCreating.value
      ? await createAdminSubscriptionPlan(normalizeForm())
      : await updateAdminSubscriptionPlan(selectedPlan.value!.id, normalizeForm())

    await loadPlans(saved.id)
    if (!errorMessage.value) {
      successMessage.value = 'Тариф сохранён.'
    }
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось сохранить тариф.')
  } finally {
    isSaving.value = false
  }
}

async function saveFeatures(): Promise<void> {
  if (!selectedPlan.value || isLoadingFeatures.value || features.value.length === 0) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    const planId = selectedPlan.value.id
    const result = await saveAdminPlanFeatures(planId, features.value)
    await loadPlans(planId)
    if (!errorMessage.value) {
      successMessage.value = result.message || 'Возможности тарифа сохранены.'
    }
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось сохранить возможности тарифа.')
  } finally {
    isSaving.value = false
  }
}

async function togglePlan(plan: AdminSubscriptionPlan): Promise<void> {
  clearMessages()

  try {
    const result = await toggleAdminSubscriptionPlan(plan.id)
    successMessage.value = result.message
    await loadPlans()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось изменить статус тарифа.')
  }
}

async function deletePlan(): Promise<void> {
  if (!selectedPlan.value || !window.confirm(`Удалить тариф «${selectedPlan.value.name}»?`)) {
    return
  }

  isSaving.value = true
  clearMessages()

  try {
    await deleteAdminSubscriptionPlan(selectedPlan.value.id)
    selectedPlan.value = null
    successMessage.value = 'Тариф удалён.'
    await loadPlans()
  } catch (error) {
    errorMessage.value = getErrorMessage(error, 'Не удалось удалить тариф.')
  } finally {
    isSaving.value = false
  }
}

function normalizeForm(): AdminSubscriptionPlanForm {
  return {
    ...form.value,
    name: form.value.name.trim(),
    description: form.value.description.trim(),
    discountPrice: form.value.discountPrice || null,
    group: form.value.group?.trim() || null,
    planTypeCode: 'business'
  }
}

function setDefaultPlan(): void {
  if (form.value.isDefaultPlan) {
    form.value.isDemoPlan = false
  }
}

function setDemoPlan(): void {
  if (form.value.isDemoPlan) {
    form.value.isDefaultPlan = false
  }
}

function createEmptyForm(): AdminSubscriptionPlanForm {
  return {
    name: '',
    description: '',
    price: 0,
    discountPrice: null,
    durationDays: 30,
    vehicleLimit: 0,
    order: 0,
    group: null,
    planTypeCode: 'business',
    isActive: true,
    isDefaultPlan: false,
    isDemoPlan: false
  }
}

function clearMessages(): void {
  errorMessage.value = null
  successMessage.value = null
}

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.message : fallback
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB' }).format(value)
}
</script>

<template>
  <section class="finance-page">
    <h1>Тарифы Free и PRO</h1>
    <p class="muted">Free включается автоматически и действует бессрочно. Варианты PRO задают срок, цену и возможности конкретной организации. Прежние тарифы сохранены отдельно.</p>

    <div class="finance-toolbar finance-toolbar--end">
      <button class="primary-button" type="button" @click="createPlan">Новый вариант PRO</button>
    </div>

    <div v-if="errorMessage" class="form-message form-message--error">{{ errorMessage }}</div>
    <div v-if="successMessage" class="form-message form-message--success">{{ successMessage }}</div>
    <div v-if="isLoading && plans.length === 0" class="panel-state">Загрузка…</div>

    <div v-else class="finance-workspace">
      <div class="finance-table-panel">
        <div class="table-wrap">
          <table class="data-table finance-table">
            <thead><tr><th>Тариф</th><th>Цена</th><th>Срок</th><th>Статус</th><th></th></tr></thead>
            <tbody>
              <tr v-for="plan in businessPlans" :key="plan.id" :class="{ selected: selectedPlan?.id === plan.id }" @click="selectPlan(plan)">
                <td><strong>{{ plan.name }}</strong><small>{{ isFreePlan(plan) ? 'Free · системный' : 'PRO' }}</small></td>
                <td>{{ isFreePlan(plan) ? 'Бесплатно' : formatMoney(plan.effectivePrice) }}</td>
                <td>{{ isFreePlan(plan) ? 'Бессрочно' : `${plan.durationDays} дней` }}</td>
                <td><span class="status-badge" :class="plan.isActive ? 'status-badge--success' : 'status-badge--problem'">{{ plan.isActive ? 'Активен' : 'Отключён' }}</span></td>
                <td><button v-if="!isFreePlan(plan)" class="table-action" type="button" @click.stop="togglePlan(plan)">{{ plan.isActive ? 'Отключить' : 'Включить' }}</button></td>
              </tr>
              <tr v-for="plan in historicalPlans" :key="plan.id" class="historical-row" :class="{ selected: selectedPlan?.id === plan.id }" @click="selectPlan(plan)">
                <td><strong>{{ plan.name }}</strong><small>{{ plan.isDemoPlan ? 'Демо · прежняя модель' : plan.isDefaultPlan ? 'Базовый · прежняя модель' : 'Личный · прежняя модель' }}</small></td>
                <td>{{ formatMoney(plan.effectivePrice) }}</td><td>{{ plan.durationDays }} дней</td><td>{{ plan.planTypeCode === 'business' ? 'Совместимость' : 'Только просмотр' }}</td><td></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div v-if="isCreating || selectedPlan" class="finance-editor finance-editor--wide">
        <form class="finance-form" @submit.prevent="savePlan">
          <div class="finance-editor__heading"><strong>{{ isCreating ? 'Новый тариф' : selectedPlan?.name }}</strong><button type="button" aria-label="Закрыть" @click="closeEditor">×</button></div>
          <fieldset :disabled="isReadOnly || isSaving">
            <label class="span-2"><span>Название</span><input v-model="form.name" maxlength="100" required></label>
            <label class="span-2"><span>Описание</span><textarea v-model="form.description" maxlength="1000" rows="3"></textarea></label>
            <label v-if="!isSystemFree"><span>Цена</span><input v-model.number="form.price" type="number" min="0" step="0.01" required></label>
            <label v-if="!isSystemFree"><span>Цена со скидкой</span><input v-model.number="form.discountPrice" type="number" min="0" step="0.01"></label>
            <label v-if="!isSystemFree"><span>Срок, дней</span><input v-model.number="form.durationDays" type="number" min="1" required></label>
            <label><span>Лимит автомобилей</span><input v-model.number="form.vehicleLimit" type="number" min="0" max="1000000" step="1" required></label>
            <label><span>Порядок</span><input v-model.number="form.order" type="number" required></label>
            <label class="span-2"><span>Группа</span><input v-model="form.group" maxlength="100" :disabled="isSystemFree"></label>
            <label class="checkbox-field"><input v-model="form.isActive" type="checkbox" :disabled="isSystemFree"><span>Активен</span></label>
            <label v-if="selectedPlan?.isDefaultPlan || selectedPlan?.isDemoPlan" class="checkbox-field"><input v-model="form.isDefaultPlan" type="checkbox" @change="setDefaultPlan"><span>По умолчанию</span></label>
            <label v-if="selectedPlan?.isDefaultPlan || selectedPlan?.isDemoPlan" class="checkbox-field"><input v-model="form.isDemoPlan" type="checkbox" @change="setDemoPlan"><span>Демо-тариф</span></label>
          </fieldset>
          <div v-if="!isReadOnly" class="finance-actions">
            <button class="primary-button" type="submit" :disabled="isSaving">Сохранить тариф</button>
            <button class="secondary-button" type="button" :disabled="isSaving" @click="closeEditor">Отмена</button>
            <button v-if="selectedPlan?.canDelete" class="danger-button" type="button" :disabled="isSaving" @click="deletePlan">Удалить</button>
          </div>
        </form>

        <form v-if="selectedPlan && !isReadOnly" class="features-form" @submit.prevent="saveFeatures">
          <strong>Возможности и лимиты</strong>
          <span v-if="isLoadingFeatures" role="status">Загрузка возможностей…</span>
          <p v-if="isSystemFree" class="muted">Бесплатно и бессрочно. Лимиты клиентов и автомобилей обязательны; их значения можно менять. Изменения ниже сохраняются кнопкой «Сохранить возможности».</p>
          <label v-for="feature in features" :key="feature.featureId" class="feature-row">
            <input v-model="feature.isAssigned" type="checkbox" :disabled="isSaving || isRequiredFreeLimit(selectedPlan, feature.code)">
            <span><strong>{{ feature.name }}</strong><small>{{ feature.description }}</small></span>
            <input v-if="feature.valueType === 'Boolean'" v-model="feature.boolValue" type="checkbox" :disabled="isSaving || !feature.isAssigned">
            <input v-else-if="feature.valueType === 'Integer'" v-model.number="feature.intValue" type="number" min="0" :disabled="isSaving || !feature.isAssigned">
          </label>
          <button class="secondary-button" type="submit" :disabled="isSaving || isLoadingFeatures || features.length === 0">Сохранить возможности</button>
        </form>
      </div>
    </div>
  </section>
</template>
