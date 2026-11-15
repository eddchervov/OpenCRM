<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError } from '@/api/ApiError'
import {
  getAdminAssistant,
  getAdminAssistantUsage,
  saveAdminAssistantOrganizationAccess,
  saveAdminAssistantSettings,
  settleAdminAssistantUsage,
  type AdminAssistantOrganization,
  type AdminAssistantResponse,
  type AdminAssistantSettings,
  type AdminAssistantUsageItem,
  type AdminAssistantUsageResponse
} from '@/api/assistantApi'
import { useAdminCurrentUserStore } from '@/stores/adminCurrentUserStore'

const currentUserStore = useAdminCurrentUserStore()
const assistant = ref<AdminAssistantResponse | null>(null)
const settingsDraft = ref<AdminAssistantSettings | null>(null)
const settingsLoading = ref(false)
const settingsSaving = ref(false)
const settingsError = ref('')
const settingsMessage = ref('')
const organizationSearch = ref('')
const organizationPage = ref(1)
const organizationLoading = ref(false)
const organizationError = ref('')
const editingOrganization = ref<AdminAssistantOrganization | null>(null)
const organizationDraft = ref<AdminAssistantOrganization | null>(null)
const organizationSaving = ref(false)
const usage = ref<AdminAssistantUsageResponse | null>(null)
const usageLoading = ref(false)
const usageError = ref('')
const usagePage = ref(1)
const settlementItem = ref<AdminAssistantUsageItem | null>(null)
const settlementCost = ref<number | null>(null)
const settlementReason = ref('')
const settlementSaving = ref(false)
const canViewBilling = computed(() => currentUserStore.isSystem
  || currentUserStore.currentUser?.permissions.includes('Admin.Billing.View') === true)
const dateNow = new Date()
const localDate = (value: Date) => `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
const usageFrom = ref(localDate(new Date(dateNow.getFullYear(), dateNow.getMonth(), 1)))
const usageTo = ref(localDate(dateNow))
const usageOrganizationId = ref('')
const usageChannel = ref('')
const usageModel = ref('')
const usageStatus = ref('')
const organizationPageCount = computed(() => Math.max(1, Math.ceil((assistant.value?.totalCount ?? 0) / (assistant.value?.pageSize ?? 20))))
const usagePageCount = computed(() => Math.max(1, Math.ceil((usage.value?.totalCount ?? 0) / (usage.value?.pageSize ?? 50))))

onMounted(async () => {
  await currentUserStore.load()
  await loadAssistant()
  if (canViewBilling.value) await loadUsage()
})

async function loadAssistant(): Promise<void> {
  organizationLoading.value = true
  settingsError.value = ''
  organizationError.value = ''
  try {
    const response = await getAdminAssistant({ search: organizationSearch.value, page: organizationPage.value, pageSize: 20 })
    assistant.value = response
    settingsDraft.value = { ...response.settings }
  } catch (error) {
    settingsError.value = errorMessage(error, 'Не удалось загрузить настройки помощника.')
  } finally {
    organizationLoading.value = false
  }
}

async function saveSettings(): Promise<void> {
  if (!settingsDraft.value) return
  settingsSaving.value = true
  settingsError.value = ''
  settingsMessage.value = ''
  try {
    const draft = settingsDraft.value
    const saved = await saveAdminAssistantSettings({
      isEnabled: draft.isEnabled,
      webEnabled: draft.webEnabled,
      telegramEnabled: draft.telegramEnabled,
      speechRepliesEnabled: draft.speechRepliesEnabled,
      textModel: draft.textModel,
      speechModel: draft.speechModel,
      voiceModel: draft.voiceModel,
      voiceName: draft.voiceName,
      dailyBudgetRub: draft.dailyBudgetRub,
      monthlyBudgetRub: draft.monthlyBudgetRub,
      textReservationRub: draft.textReservationRub,
      audioReservationRub: draft.audioReservationRub,
      maximumOutputTokens: draft.maximumOutputTokens,
      revision: draft.revision
    })
    settingsDraft.value = { ...saved }
    if (assistant.value) assistant.value.settings = saved
    settingsMessage.value = 'Настройки сохранены.'
  } catch (error) {
    settingsError.value = errorMessage(error, 'Не удалось сохранить настройки помощника.')
  } finally {
    settingsSaving.value = false
  }
}

async function searchOrganizations(): Promise<void> {
  organizationPage.value = 1
  await loadAssistant()
}

function editOrganization(organization: AdminAssistantOrganization): void {
  editingOrganization.value = organization
  organizationDraft.value = { ...organization }
  organizationError.value = ''
}

function closeOrganizationEditor(): void {
  editingOrganization.value = null
  organizationDraft.value = null
}

async function saveOrganization(): Promise<void> {
  if (!organizationDraft.value) return
  organizationSaving.value = true
  organizationError.value = ''
  const draft = organizationDraft.value
  try {
    await saveAdminAssistantOrganizationAccess(draft.organizationId, {
      isEnabled: draft.isEnabled,
      enabledUntil: draft.enabledUntil || null,
      webEnabled: draft.webEnabled,
      telegramEnabled: draft.telegramEnabled,
      dailyBudgetRub: draft.dailyBudgetRub,
      monthlyBudgetRub: draft.monthlyBudgetRub
    })
    closeOrganizationEditor()
    await loadAssistant()
  } catch (error) {
    organizationError.value = errorMessage(error, 'Не удалось сохранить доступ организации.')
  } finally {
    organizationSaving.value = false
  }
}

async function loadUsage(): Promise<void> {
  if (!canViewBilling.value) return
  usageLoading.value = true
  usageError.value = ''
  try {
    usage.value = await getAdminAssistantUsage({
      from: usageFrom.value,
      to: usageTo.value,
      organizationId: usageOrganizationId.value || undefined,
      channel: usageChannel.value || undefined,
      model: usageModel.value || undefined,
      status: usageStatus.value || undefined,
      page: usagePage.value,
      pageSize: 50
    })
  } catch (error) {
    usageError.value = errorMessage(error, 'Не удалось загрузить расходы помощника.')
  } finally {
    usageLoading.value = false
  }
}

function openSettlement(item: AdminAssistantUsageItem): void {
  settlementItem.value = item
  settlementCost.value = null
  settlementReason.value = ''
  usageError.value = ''
}

async function settleUsage(): Promise<void> {
  if (!settlementItem.value || settlementCost.value === null || !settlementReason.value.trim()) return
  settlementSaving.value = true
  usageError.value = ''
  try {
    await settleAdminAssistantUsage(settlementItem.value.id, settlementCost.value, settlementReason.value.trim())
    settlementItem.value = null
    await loadUsage()
  } catch (error) {
    usageError.value = errorMessage(error, 'Не удалось сверить расход.')
  } finally {
    settlementSaving.value = false
  }
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.status === 403
    ? 'Недостаточно прав для этого действия.'
    : fallback
}

function money(value: number | null | undefined): string {
  return value == null ? 'Неизвестно' : `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(value)} ₽`
}

function number(value: number | null | undefined): string {
  return value == null ? '—' : new Intl.NumberFormat('ru-RU').format(value)
}

function dateTime(value: string): string {
  return new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(value))
}

function displayStatus(item: AdminAssistantUsageItem): string {
  if (item.status === 'unknown' || item.costRub == null) return 'Неизвестно'
  return item.status
}

function canSettle(item: AdminAssistantUsageItem): boolean {
  return currentUserStore.isSystem
    || (currentUserStore.currentUser?.permissions.includes('Admin.Configuration.Manage') === true
      && (item.status === 'unknown' || item.costRub == null)
      && item.reservedCostRub > 0)
}
</script>

<template>
  <section class="operations-page assistant-page">
    <h1>ИИ-помощник</h1>

    <div v-if="settingsError" class="form-message form-message--error" role="alert">{{ settingsError }}</div>
    <div v-if="settingsMessage" class="form-message form-message--success" role="status">{{ settingsMessage }}</div>
    <div v-if="!settingsDraft && !settingsError" class="form-message">Загрузка настроек…</div>

    <form v-if="settingsDraft" class="standalone-form modal-grid assistant-settings" @submit.prevent="saveSettings">
      <div class="assistant-settings__master">
        <label class="assistant-settings__toggle"><input v-model="settingsDraft.isEnabled" type="checkbox"><strong>Помощник включён</strong></label>
        <p>При выключении помощник скрывается в рабочих пространствах, а новые команды сайта и Telegram блокируются.</p>
      </div>
      <label><span>Сайт</span><input v-model="settingsDraft.webEnabled" type="checkbox"></label>
      <label><span>Telegram</span><input v-model="settingsDraft.telegramEnabled" type="checkbox"></label>
      <label><span>Голосовые ответы</span><input v-model="settingsDraft.speechRepliesEnabled" type="checkbox"></label>
      <label><span>Текстовая модель</span><input v-model.trim="settingsDraft.textModel" required maxlength="120"></label>
      <label><span>Модель распознавания речи</span><input v-model.trim="settingsDraft.speechModel" maxlength="120"></label>
      <label><span>Модель озвучивания</span><input v-model.trim="settingsDraft.voiceModel" maxlength="120"></label>
      <label><span>Голос</span><input v-model.trim="settingsDraft.voiceName" maxlength="100"></label>
      <label><span>Дневной лимит, ₽</span><input v-model.number="settingsDraft.dailyBudgetRub" type="number" min="0" step="0.01" required></label>
      <label><span>Месячный лимит, ₽</span><input v-model.number="settingsDraft.monthlyBudgetRub" type="number" min="0" step="0.01" required></label>
      <label><span>Резерв на текстовый запрос, ₽</span><input v-model.number="settingsDraft.textReservationRub" type="number" min="0" step="0.01" required></label>
      <label><span>Резерв на аудиозапрос, ₽</span><input v-model.number="settingsDraft.audioReservationRub" type="number" min="0" step="0.01" required></label>
      <label><span>Максимум токенов ответа</span><input v-model.number="settingsDraft.maximumOutputTokens" type="number" min="1" :max="assistant?.limits.maximumOutputTokensAllowed" step="1" required></label>
      <div class="assistant-settings__provider">
        <strong>Подключения</strong>
        <span>RouterAI: {{ assistant?.provider.routerConfigured ? 'настроен' : 'не настроен' }}</span>
        <span>Telegram: {{ assistant?.provider.telegramConfigured ? 'настроен' : 'не настроен' }}<template v-if="assistant?.provider.telegramBotUsername"> · @{{ assistant.provider.telegramBotUsername }}</template></span>
        <small>Секретные ключи здесь не отображаются.</small>
      </div>
      <div class="assistant-settings__actions">
        <button class="primary-button" :disabled="settingsSaving">{{ settingsSaving ? 'Сохранение…' : 'Сохранить настройки' }}</button>
        <small v-if="assistant?.settings.updatedAt">Изменено {{ dateTime(assistant.settings.updatedAt) }} · ревизия {{ settingsDraft.revision }}</small>
      </div>
    </form>

    <section class="assistant-section">
      <div class="assistant-section__heading"><h2>Доступ организаций</h2><form class="operations-toolbar" @submit.prevent="searchOrganizations"><input v-model="organizationSearch" type="search" placeholder="Найти организацию"><button class="secondary-button">Найти</button></form></div>
      <div v-if="organizationError" class="form-message form-message--error" role="alert">{{ organizationError }}</div>
      <div v-if="organizationLoading" class="form-message">Загрузка организаций…</div>
      <div v-else class="table-wrap"><table class="data-table"><thead><tr><th>Организация</th><th>Доступ</th><th>До</th><th>Каналы</th><th>Лимиты в день / месяц</th><th></th></tr></thead><tbody>
        <tr v-for="organization in assistant?.organizations ?? []" :key="organization.organizationId">
          <td><strong>{{ organization.name }}</strong></td>
          <td>{{ organization.isEnabled ? 'Включён' : 'Выключен' }}</td>
          <td>{{ organization.enabledUntil ? new Date(organization.enabledUntil).toLocaleDateString('ru-RU') : 'Без срока' }}</td>
          <td>{{ [organization.webEnabled ? 'Сайт' : '', organization.telegramEnabled ? 'Telegram' : ''].filter(Boolean).join(', ') || 'Нет' }}</td>
          <td>{{ money(organization.dailyBudgetRub) }} / {{ money(organization.monthlyBudgetRub) }}</td>
          <td><button class="table-action" type="button" @click="editOrganization(organization)">Настроить</button></td>
        </tr>
        <tr v-if="!assistant?.organizations.length"><td colspan="6">Организации не найдены.</td></tr>
      </tbody></table></div>
      <div class="assistant-pagination"><button class="secondary-button" :disabled="organizationPage <= 1 || organizationLoading" @click="organizationPage--; loadAssistant()">Назад</button><span>Страница {{ organizationPage }} из {{ organizationPageCount }}</span><button class="secondary-button" :disabled="organizationPage >= organizationPageCount || organizationLoading" @click="organizationPage++; loadAssistant()">Дальше</button></div>
    </section>

    <section v-if="assistant?.audits.length" class="assistant-section">
      <h2>Журнал изменений</h2>
      <div class="table-wrap"><table class="data-table"><thead><tr><th>Дата</th><th>Действие</th><th>Описание</th><th>Администратор</th></tr></thead><tbody>
        <tr v-for="audit in assistant.audits" :key="audit.id"><td>{{ dateTime(audit.createdAt) }}</td><td>{{ audit.action }}</td><td>{{ audit.description }}</td><td>{{ audit.adminUserId }}</td></tr>
      </tbody></table></div>
    </section>

    <section class="assistant-section">
      <h2>Расходы и использование</h2>
      <div v-if="!canViewBilling" class="form-message">Для просмотра расходов требуется право «Просмотр финансов».</div>
      <template v-else>
        <form class="operations-toolbar assistant-usage-filters" @submit.prevent="usagePage = 1; loadUsage()">
          <label><span>С</span><input v-model="usageFrom" type="date" required></label>
          <label><span>По</span><input v-model="usageTo" type="date" required></label>
          <label><span>Организация ID</span><input v-model.trim="usageOrganizationId" type="search"></label>
          <label><span>Канал</span><select v-model="usageChannel"><option value="">Все</option><option value="web">Сайт</option><option value="telegram">Telegram</option></select></label>
          <label><span>Модель</span><input v-model.trim="usageModel"></label>
          <label><span>Статус</span><input v-model.trim="usageStatus"></label>
          <button class="secondary-button" :disabled="usageLoading">Применить</button>
        </form>
        <div v-if="usageError" class="form-message form-message--error" role="alert">{{ usageError }}</div>
        <div v-if="usageLoading" class="form-message">Загрузка расходов…</div>
        <template v-else-if="usage">
          <dl class="assistant-usage-totals">
            <div><dt>Фактические расходы</dt><dd>{{ money(usage.totals.costRub) }}</dd></div>
            <div><dt>Зарезервировано</dt><dd>{{ money(usage.totals.reservedRub) }}</dd></div>
            <div><dt>Неизвестные расходы</dt><dd>{{ number(usage.totals.unknownCount) }}</dd></div>
            <div><dt>Запросы</dt><dd>{{ number(usage.totals.requestCount) }}</dd></div>
            <div><dt>Входные токены</dt><dd>{{ number(usage.totals.inputTokens) }}</dd></div>
            <div><dt>Выходные токены</dt><dd>{{ number(usage.totals.outputTokens) }}</dd></div>
          </dl>
          <div class="table-wrap"><table class="data-table"><thead><tr><th>Дата</th><th>Факт</th><th>Резерв</th></tr></thead><tbody><tr v-for="day in usage.daily" :key="day.date"><td>{{ day.date }}</td><td>{{ money(day.costRub) }}</td><td>{{ money(day.reservedRub) }}</td></tr></tbody></table></div>
          <div class="table-wrap"><table class="data-table"><thead><tr><th>Организация</th><th>Факт</th><th>Резерв</th><th>Запросы</th></tr></thead><tbody><tr v-for="organization in usage.byOrganization" :key="organization.organizationId"><td>{{ organization.name }}</td><td>{{ money(organization.costRub) }}</td><td>{{ money(organization.reservedRub) }}</td><td>{{ number(organization.requestCount) }}</td></tr></tbody></table></div>
          <div class="table-wrap"><table class="data-table"><thead><tr><th>Дата</th><th>Организация / пользователь</th><th>Канал / тип</th><th>Модель</th><th>Токены: вход / выход / кэш</th><th>Статус</th><th>Факт</th><th>Резерв</th><th>Ошибка</th><th></th></tr></thead><tbody>
            <tr v-for="item in usage.items" :key="item.id"><td>{{ dateTime(item.createdAt) }}</td><td>{{ item.organizationName }}<small>{{ item.userEmail || item.userId }}</small></td><td>{{ item.channel }} / {{ item.kind }}</td><td>{{ item.model }}</td><td>{{ number(item.inputTokens) }} / {{ number(item.outputTokens) }} / {{ number(item.cachedTokens) }}</td><td>{{ displayStatus(item) }}</td><td>{{ money(item.costRub) }}</td><td>{{ money(item.reservedCostRub) }}</td><td>{{ item.errorCode || '—' }}</td><td><button v-if="canSettle(item)" class="table-action" type="button" @click="openSettlement(item)">Сверить</button></td></tr>
            <tr v-if="!usage.items.length"><td colspan="10">Запросов за период нет.</td></tr>
          </tbody></table></div>
          <div class="assistant-pagination"><button class="secondary-button" :disabled="usagePage <= 1 || usageLoading" @click="usagePage--; loadUsage()">Назад</button><span>Страница {{ usagePage }} из {{ usagePageCount }}</span><button class="secondary-button" :disabled="usagePage >= usagePageCount || usageLoading" @click="usagePage++; loadUsage()">Дальше</button></div>
        </template>
      </template>
    </section>

    <div v-if="organizationDraft" class="modal-backdrop" @click.self="closeOrganizationEditor">
      <form class="modal-card" @submit.prevent="saveOrganization">
        <div class="modal-heading"><strong>Доступ: {{ organizationDraft.name }}</strong><button type="button" :disabled="organizationSaving" @click="closeOrganizationEditor">×</button></div>
        <div class="modal-grid">
          <label><span>Доступ включён</span><input v-model="organizationDraft.isEnabled" type="checkbox"></label>
          <label><span>Доступ до (необязательно)</span><input v-model="organizationDraft.enabledUntil" type="date"></label>
          <label><span>Сайт</span><input v-model="organizationDraft.webEnabled" type="checkbox"></label>
          <label><span>Telegram</span><input v-model="organizationDraft.telegramEnabled" type="checkbox"></label>
          <label><span>Лимит в день, ₽</span><input v-model.number="organizationDraft.dailyBudgetRub" type="number" min="0" step="0.01" required></label>
          <label><span>Лимит в месяц, ₽</span><input v-model.number="organizationDraft.monthlyBudgetRub" type="number" min="0" step="0.01" required></label>
        </div>
        <div class="modal-actions"><button class="primary-button" :disabled="organizationSaving">{{ organizationSaving ? 'Сохранение…' : 'Сохранить доступ' }}</button><button class="secondary-button" type="button" :disabled="organizationSaving" @click="closeOrganizationEditor">Отмена</button></div>
      </form>
    </div>

    <div v-if="settlementItem" class="modal-backdrop" @click.self="settlementItem = null">
      <form class="modal-card" @submit.prevent="settleUsage">
        <div class="modal-heading"><strong>Сверка неизвестного расхода</strong><button type="button" :disabled="settlementSaving" @click="settlementItem = null">×</button></div>
        <p>{{ settlementItem.organizationName }} · {{ dateTime(settlementItem.createdAt) }} · резерв {{ money(settlementItem.reservedCostRub) }}</p>
        <div class="modal-grid"><label class="span-2"><span>Фактическая стоимость, ₽</span><input v-model.number="settlementCost" type="number" min="0" step="0.0001" required></label><label class="span-2"><span>Причина сверки</span><textarea v-model.trim="settlementReason" rows="3" maxlength="500" required></textarea></label></div>
        <div class="modal-actions"><button class="primary-button" :disabled="settlementSaving || settlementCost === null || !settlementReason.trim()">{{ settlementSaving ? 'Сохранение…' : 'Сохранить сверку' }}</button><button class="secondary-button" type="button" :disabled="settlementSaving" @click="settlementItem = null">Отмена</button></div>
      </form>
    </div>
  </section>
</template>

<style scoped>
.assistant-settings { margin-top: 16px; }
.assistant-settings > label:has(input[type="checkbox"]) { display: flex; align-items: center; gap: 8px; }
.assistant-settings__master, .assistant-settings__provider, .assistant-settings__actions { grid-column: 1 / -1; }
.assistant-settings__master { padding: 14px; border: 1px solid var(--crm-border, #d8dce2); border-radius: 8px; }
.assistant-settings__master p { margin: 8px 0 0; color: var(--crm-text-muted, #667080); }
.assistant-settings__toggle { display: flex; align-items: center; gap: 9px; }
.assistant-settings__provider { display: grid; gap: 6px; padding: 14px; background: var(--crm-surface, #fff); border: 1px solid var(--crm-border, #d8dce2); border-radius: 8px; }
.assistant-settings__provider small, .assistant-settings__actions small, .data-table td small { display: block; color: var(--crm-text-muted, #667080); }
.assistant-settings__actions { display: flex; align-items: center; gap: 12px; }
.assistant-section { margin-top: 24px; }
.assistant-section h2 { margin: 0 0 12px; font-size: 18px; }
.assistant-section__heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.assistant-pagination { display: flex; align-items: center; justify-content: flex-end; gap: 12px; padding: 12px 0; }
.assistant-usage-filters { flex-wrap: wrap; align-items: end; }
.assistant-usage-filters label { display: grid; gap: 4px; }
.assistant-usage-totals { display: grid; grid-template-columns: repeat(auto-fit, minmax(150px, 1fr)); gap: 8px; margin: 12px 0; }
.assistant-usage-totals > div { padding: 10px 12px; border: 1px solid var(--crm-border, #d8dce2); border-radius: 8px; }
.assistant-usage-totals dt { color: var(--crm-text-muted, #667080); font-size: 12px; }
.assistant-usage-totals dd { margin: 4px 0 0; font-weight: 700; }
@media (max-width: 700px) { .assistant-section__heading, .assistant-settings__actions { align-items: stretch; flex-direction: column; } }
</style>
