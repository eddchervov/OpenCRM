<script setup lang="ts">
import { getProjectBrand } from '@opencrm/ui'
import type { ProFeatureKey, ProFeatureVisual } from '../proFeatureDetails'

const projectBrand = getProjectBrand()








defineProps<{
  feature: ProFeatureKey
  visual: Extract<ProFeatureVisual, { type: 'demo' }>
}>()
</script>

<template>
  <section class="pro-workflow" :class="`pro-workflow--${feature}`" :aria-label="visual.heading">
    <header class="pro-workflow__topbar">
      <span class="pro-workflow__brand-mark" aria-hidden="true">O</span>
      <strong>{{ visual.heading }}</strong>
      <span class="pro-workflow__example">Пример</span>
    </header>

    <template v-if="feature === 'shifts'">
      <div class="pro-workflow__schedule">
        <div class="pro-workflow__schedule-head"><span>Команда</span><span>План на день</span></div>
        <div class="pro-workflow__time-ruler"><span></span><div><span>09</span><span>12</span><span>15</span><span>18</span><span>21</span></div></div>
        <div v-for="(row, index) in visual.rows" :key="row.label" class="pro-workflow__shift-row">
          <div class="pro-workflow__person"><span class="pro-workflow__avatar">{{ row.label.slice(0, 1) }}</span><strong>{{ row.label }}</strong></div>
          <div class="pro-workflow__timeline">
            <div v-if="index < 2" class="pro-workflow__shift-block" :class="`pro-workflow__shift-block--${index}`">
              <strong>{{ row.value.split(' · ')[0] }}</strong><small>{{ row.value.split(' · ')[1] }}</small>
            </div>
            <span v-else class="pro-workflow__day-off">{{ row.value }}</span>
          </div>
        </div>
      </div>
      <footer class="pro-workflow__footnote"><span class="pro-workflow__legend-dot"></span> Рабочая смена <span>3 сотрудника</span></footer>
    </template>

    <template v-else-if="feature === 'selfAssignment' || feature === 'myWork'">
      <div class="pro-workflow__work-head"><span>{{ feature === 'myWork' ? 'Мои задачи' : 'Назначения' }}</span><span>3 работы</span></div>
      <div class="pro-workflow__work-list">
        <article v-for="(row, index) in visual.rows" :key="row.label" class="pro-workflow__work-card">
          <span class="pro-workflow__work-index">0{{ index + 1 }}</span>
          <div class="pro-workflow__work-info"><strong>{{ row.label }}</strong><small>{{ row.value }}</small></div>
          <div class="pro-workflow__stage" :class="`pro-workflow__stage--${index}`"><span></span>{{ index === 0 ? (feature === 'myWork' ? 'В работе' : 'Ожидает') : index === 1 ? (feature === 'myWork' ? 'Следом' : 'Свободна') : (feature === 'myWork' ? 'В плане' : 'Назначена') }}</div>
        </article>
      </div>
      <footer class="pro-workflow__progress"><span><i></i></span><small>{{ feature === 'myWork' ? 'В работе' : 'Назначена вам' }}</small><strong>1 задача</strong><small v-if="feature === 'selfAssignment'">Доступность зависит от настроек и прав</small></footer>
    </template>

    <template v-else-if="feature === 'notifications' || feature === 'messageTemplates'">
      <div class="pro-workflow__message-layout">
        <aside class="pro-workflow__message-list">
          <div v-for="(row, index) in visual.rows" :key="row.label" class="pro-workflow__message-item" :class="{ 'is-selected': index === 0 }">
            <span class="pro-workflow__message-icon">{{ index === 0 ? '↗' : index === 1 ? '○' : '✓' }}</span>
            <span><strong>{{ row.label }}</strong><small>{{ row.value }}</small></span>
          </div>
        </aside>
        <div class="pro-workflow__message-detail">
          <div class="pro-workflow__client"><span class="pro-workflow__avatar">АС</span><span><strong>Анна Соколова</strong><small>Клиент · Toyota Camry</small></span></div>
          <div class="pro-workflow__bubble"><small>{{ feature === 'notifications' ? 'Напоминание о визите' : visual.rows[0].label }}</small><p>Анна, {{ feature === 'notifications' ? 'напоминаем о записи на полировку.' : 'ваша запись на полировку подтверждена.' }} Будем ждать вас!</p><span>Пример сообщения</span></div>
          <div class="pro-workflow__message-meta"><span>{{ visual.rows[0].value }}</span><span class="pro-workflow__status">Результат отправки</span></div>
        </div>
      </div>
    </template>

    <template v-else-if="feature === 'documentStyling'">
      <div class="pro-workflow__paper-wrap">
        <article class="pro-workflow__paper">
          <header class="pro-workflow__paper-brand"><span class="pro-workflow__paper-logo">O</span><span><strong>{{ projectBrand }}</strong><small>{{ visual.rows[0].value }}</small></span><span class="pro-workflow__paper-label">ДОКУМЕНТ</span></header>
          <div class="pro-workflow__paper-title"><small>ЗАКАЗ-НАРЯД · ПРИМЕР</small><strong>Выполненные работы</strong></div>
          <div class="pro-workflow__paper-row pro-workflow__paper-row--head"><span>Наименование работы</span><span>Статус</span></div>
          <div class="pro-workflow__paper-row"><span>{{ visual.rows[1].value }}</span><span class="pro-workflow__status">Выполнено</span></div>
          <div class="pro-workflow__paper-total"><span>{{ visual.rows[2].label }}</span><strong>{{ visual.rows[2].value }}</strong></div>
        </article>
      </div>
    </template>

    <template v-else-if="feature === 'dataImport'">
      <div class="pro-workflow__import-head"><span><i></i> clients_october.xlsx</span><span>3 колонки · пример</span></div>
      <div class="pro-workflow__mapping">
        <div class="pro-workflow__mapping-caption"><span>Данные таблицы</span><span></span><span>Поле системы</span></div>
        <div v-for="(row, index) in visual.rows" :key="row.label" class="pro-workflow__mapping-row">
          <span class="pro-workflow__column"><small>СТОЛБЕЦ {{ index + 1 }}</small><strong>{{ row.value }}</strong></span>
          <span class="pro-workflow__map-line"><i></i></span>
          <span class="pro-workflow__column pro-workflow__column--target"><small>СОПОСТАВЛЕНО</small><strong>{{ row.label }}</strong><b>✓</b></span>
        </div>
      </div>
      <footer class="pro-workflow__import-footer"><span><i></i> Сопоставление проверено</span><span>Демонстрация</span></footer>
    </template>

    <template v-else-if="feature === 'accountingExport'">
      <div class="pro-workflow__export">
        <div class="pro-workflow__export-file"><span class="pro-workflow__file-icon">1C</span><span><strong>выгрузка.json</strong><small>{{ visual.rows[2].value }}</small></span><span class="pro-workflow__ready">ПРИМЕР</span></div>
        <div class="pro-workflow__export-summary">
          <div v-for="row in visual.rows" :key="row.label"><small>{{ row.label }}</small><strong>{{ row.value }}</strong></div>
        </div>
        <div class="pro-workflow__export-track"><span><i></i><i></i><i></i></span><small>Далее: .epf, ручная сверка и проведение в 1С</small></div>
      </div>
    </template>
  </section>
</template>

<style scoped>
.pro-workflow { --workflow-accent: var(--crm-action-accent, var(--crm-accent, #c8561b)); --workflow-soft: var(--crm-accent-soft, var(--crm-surface-soft, #f6f7f9)); box-sizing: border-box; width: 100%; min-width: 0; overflow: hidden; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 14px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font-size: 12px; line-height: 1.45; }
.pro-workflow * { box-sizing: border-box; }
.pro-workflow__topbar { display: flex; min-width: 0; align-items: center; gap: 10px; padding: 13px 16px; border-bottom: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); background: var(--crm-surface-soft, #f6f7f9); }
.pro-workflow__topbar > strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 12px; }
.pro-workflow__brand-mark, .pro-workflow__paper-logo { display: grid; width: 25px; height: 25px; flex: none; place-items: center; border-radius: 8px; background: var(--workflow-accent); color: var(--crm-text-on-accent, #fff); font-family: Georgia, serif; font-size: 16px; font-weight: 700; }
.pro-workflow__example { margin-left: auto; padding: 3px 7px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 5px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__schedule, .pro-workflow__work-list, .pro-workflow__export { padding: 15px 16px; }
.pro-workflow__schedule-head, .pro-workflow__time-ruler, .pro-workflow__shift-row { display: grid; grid-template-columns: minmax(90px, .9fr) minmax(0, 2.1fr); align-items: center; gap: 12px; }
.pro-workflow__time-ruler > div { display: flex; justify-content: space-between; }
.pro-workflow__schedule-head { margin-bottom: 9px; color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-workflow__schedule-head span:last-child { text-align: center; }
.pro-workflow__time-ruler { padding: 0 0 5px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__shift-row { min-height: 47px; border-top: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__person { display: flex; min-width: 0; align-items: center; gap: 8px; }
.pro-workflow__person strong { overflow: hidden; text-overflow: ellipsis; font-size: 10px; white-space: nowrap; }
.pro-workflow__avatar { display: grid; width: 27px; height: 27px; flex: none; place-items: center; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 50%; background: var(--crm-surface-soft, #f6f7f9); color: var(--crm-text-muted, #687386); font-size: 9px; font-weight: 700; }
.pro-workflow__timeline { position: relative; display: flex; min-width: 0; height: 31px; align-items: center; background: repeating-linear-gradient(to right, transparent 0, transparent calc(25% - 1px), var(--crm-line, var(--crm-border, #dce2e9)) calc(25% - 1px), var(--crm-line, var(--crm-border, #dce2e9)) 25%); }
.pro-workflow__shift-block { display: grid; width: 75%; min-width: 0; gap: 1px; padding: 4px 8px; overflow: hidden; border: 1px solid color-mix(in srgb, var(--workflow-accent) 35%, transparent); border-radius: 6px; background: var(--workflow-soft); color: var(--crm-text, #202631); }
.pro-workflow__shift-block--1 { width: 75%; margin-left: 8.333%; }
.pro-workflow__shift-block strong, .pro-workflow__shift-block small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__shift-block strong { font-size: 9px; }
.pro-workflow__shift-block small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__day-off { padding-left: 8px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__footnote { display: flex; align-items: center; gap: 6px; padding: 0 16px 13px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__footnote > span:last-child { margin-left: auto; }
.pro-workflow__legend-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--workflow-accent); }
.pro-workflow__work-head { display: flex; justify-content: space-between; padding: 14px 16px 0; font-size: 10px; font-weight: 700; }
.pro-workflow__work-head span:last-child { color: var(--crm-text-muted, #687386); font-size: 9px; font-weight: 400; }
.pro-workflow__work-list { display: grid; gap: 8px; }
.pro-workflow__work-card { display: flex; min-width: 0; align-items: center; gap: 10px; padding: 10px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 9px; background: var(--crm-surface, #fff); }
.pro-workflow__work-index { color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__work-info { display: grid; min-width: 0; flex: 1; gap: 2px; }
.pro-workflow__work-info strong, .pro-workflow__work-info small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__work-info strong { font-size: 10px; }
.pro-workflow__work-info small { color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__stage { display: flex; flex: none; align-items: center; gap: 5px; color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__stage span { width: 6px; height: 6px; border-radius: 50%; background: var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__stage--0 { color: var(--workflow-accent); }
.pro-workflow__stage--0 span { background: var(--workflow-accent); }
.pro-workflow__progress { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; padding: 0 16px 14px; }
.pro-workflow__progress > span { width: 72px; height: 4px; overflow: hidden; border-radius: 5px; background: var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__progress i { display: block; width: 38%; height: 100%; border-radius: inherit; background: var(--workflow-accent); }
.pro-workflow__progress small, .pro-workflow__progress strong { color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__progress strong { margin-left: auto; }
.pro-workflow__message-layout { display: grid; grid-template-columns: minmax(135px, 0.8fr) minmax(0, 1.2fr); min-height: 208px; }
.pro-workflow__message-list { display: grid; align-content: start; gap: 5px; padding: 12px 9px; border-right: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__message-item { display: flex; min-width: 0; gap: 7px; padding: 8px 7px; border-radius: 7px; }
.pro-workflow__message-item.is-selected { background: var(--workflow-soft); }
.pro-workflow__message-icon { display: grid; width: 20px; height: 20px; flex: none; place-items: center; border-radius: 6px; background: var(--crm-surface-soft, #f6f7f9); color: var(--workflow-accent); }
.pro-workflow__message-item > span:last-child, .pro-workflow__client > span:last-child { display: grid; min-width: 0; gap: 2px; }
.pro-workflow__message-item strong, .pro-workflow__message-item small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__message-item strong { font-size: 9px; }
.pro-workflow__message-item small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__message-detail { min-width: 0; padding: 13px; }
.pro-workflow__client { display: flex; align-items: center; gap: 8px; }
.pro-workflow__client strong { font-size: 9px; }
.pro-workflow__client small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__bubble { margin: 12px 0 8px; padding: 10px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 9px 9px 9px 2px; background: var(--crm-surface-soft, #f6f7f9); }
.pro-workflow__bubble > small, .pro-workflow__bubble > span { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__bubble p { margin: 5px 0 7px; font-size: 10px; }
.pro-workflow__message-meta { display: flex; justify-content: space-between; gap: 5px; color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__status { color: var(--workflow-accent); }
.pro-workflow__paper-wrap { padding: 16px; background: var(--crm-surface-soft, #f6f7f9); }
.pro-workflow__paper { max-width: 560px; margin: 0 auto; padding: 15px 17px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 5px; background: var(--crm-surface, #fff); box-shadow: 0 5px 16px color-mix(in srgb, var(--crm-text, #202631) 7%, transparent); }
.pro-workflow__paper-brand { display: flex; align-items: center; gap: 8px; padding-bottom: 11px; border-bottom: 2px solid var(--workflow-accent); }
.pro-workflow__paper-brand > span:nth-child(2) { display: grid; gap: 2px; }
.pro-workflow__paper-brand strong { font-size: 10px; letter-spacing: .07em; }
.pro-workflow__paper-brand small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__paper-brand .pro-workflow__paper-label { margin-left: auto; color: var(--crm-text-muted, #687386); font-size: 8px; letter-spacing: .08em; }
.pro-workflow__paper-title { display: grid; gap: 3px; padding: 12px 0 8px; }
.pro-workflow__paper-title small { color: var(--workflow-accent); font-size: 8px; letter-spacing: .06em; }
.pro-workflow__paper-title strong { font-size: 12px; }
.pro-workflow__paper-row { display: flex; justify-content: space-between; gap: 8px; padding: 7px 0; border-top: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); font-size: 9px; }
.pro-workflow__paper-row--head { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__paper-total { display: flex; justify-content: space-between; gap: 8px; margin-top: 5px; padding-top: 8px; border-top: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); font-size: 9px; }
.pro-workflow__paper-total strong { color: var(--workflow-accent); }
.pro-workflow__import-head { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 8px; padding: 14px 16px 11px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-workflow__import-head > span:first-child { color: var(--crm-text, #202631); font-weight: 700; }
.pro-workflow__import-head i, .pro-workflow__import-footer i { display: inline-block; width: 7px; height: 9px; margin-right: 5px; border-radius: 2px; background: var(--workflow-accent); }
.pro-workflow__mapping { padding: 0 16px; }
.pro-workflow__mapping-caption, .pro-workflow__mapping-row { display: grid; grid-template-columns: minmax(0, 1fr) 28px minmax(0, 1fr); align-items: center; gap: 7px; }
.pro-workflow__mapping-caption { padding: 0 2px 6px; color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__mapping-caption span:last-child { text-align: right; }
.pro-workflow__mapping-row { padding: 7px 0; border-top: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__column { position: relative; display: grid; min-width: 0; gap: 3px; padding: 7px 8px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 6px; }
.pro-workflow__column small { overflow: hidden; color: var(--crm-text-muted, #687386); font-size: 7px; letter-spacing: .04em; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__column strong { overflow: hidden; font-size: 9px; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__column--target { padding-right: 23px; border-color: color-mix(in srgb, var(--workflow-accent) 36%, var(--crm-line, #dce2e9)); background: var(--workflow-soft); }
.pro-workflow__column b { position: absolute; top: 50%; right: 7px; color: var(--workflow-accent); font-size: 11px; transform: translateY(-50%); }
.pro-workflow__map-line { height: 1px; background: var(--crm-line, var(--crm-border, #dce2e9)); }
.pro-workflow__map-line i { display: block; width: 5px; height: 5px; margin: -2px auto 0; border: 1px solid var(--workflow-accent); border-radius: 50%; background: var(--crm-surface, #fff); }
.pro-workflow__import-footer { display: flex; justify-content: space-between; gap: 8px; padding: 11px 16px 13px; color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__import-footer > span:first-child { color: var(--crm-text, #202631); }
.pro-workflow__import-footer i { width: 6px; height: 6px; border-radius: 50%; background: var(--workflow-accent); }
.pro-workflow__export { display: grid; gap: 12px; }
.pro-workflow__export-file { display: flex; min-width: 0; align-items: center; gap: 10px; padding: 12px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 9px; background: var(--crm-surface-soft, #f6f7f9); }
.pro-workflow__file-icon { display: grid; width: 36px; height: 39px; flex: none; place-items: center; border: 1px solid color-mix(in srgb, var(--workflow-accent) 35%, transparent); border-radius: 7px; background: var(--workflow-soft); color: var(--workflow-accent); font-size: 10px; font-weight: 800; }
.pro-workflow__export-file > span:nth-child(2) { display: grid; min-width: 0; flex: 1; gap: 3px; }
.pro-workflow__export-file strong, .pro-workflow__export-file small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__export-file strong { font-size: 10px; }
.pro-workflow__export-file small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__ready { padding: 3px 6px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 5px; color: var(--workflow-accent); font-size: 7px; letter-spacing: .04em; }
.pro-workflow__export-summary { display: grid; grid-template-columns: 1fr 1fr; gap: 7px; }
.pro-workflow__export-summary > div { display: grid; min-width: 0; gap: 4px; padding: 9px; border: 1px solid var(--crm-line, var(--crm-border, #dce2e9)); border-radius: 7px; }
.pro-workflow__export-summary small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-workflow__export-summary strong { overflow: hidden; font-size: 9px; text-overflow: ellipsis; white-space: nowrap; }
.pro-workflow__export-track { display: flex; flex-wrap: wrap; align-items: center; gap: 9px; padding-top: 2px; }
.pro-workflow__export-track > span { display: flex; flex: 1; gap: 3px; }
.pro-workflow__export-track i { height: 3px; flex: 1; border-radius: 4px; background: var(--workflow-accent); opacity: .8; }
.pro-workflow__export-track small { color: var(--crm-text-muted, #687386); font-size: 8px; }
@media (max-width: 360px) {
  .pro-workflow__topbar { padding-right: 11px; padding-left: 11px; }
  .pro-workflow__schedule, .pro-workflow__work-list, .pro-workflow__export { padding-right: 10px; padding-left: 10px; }
  .pro-workflow__schedule-head, .pro-workflow__time-ruler, .pro-workflow__shift-row { grid-template-columns: minmax(72px, .8fr) minmax(0, 2fr); gap: 6px; }
  .pro-workflow__time-ruler { grid-template-columns: minmax(72px, .8fr) minmax(0, 2fr); }
  .pro-workflow__person { gap: 5px; }
  .pro-workflow__message-layout { grid-template-columns: minmax(102px, .8fr) minmax(0, 1.2fr); }
  .pro-workflow__message-detail { padding: 9px; }
  .pro-workflow__message-list { padding-right: 5px; padding-left: 5px; }
  .pro-workflow__stage { max-width: 53px; overflow: hidden; white-space: nowrap; }
  .pro-workflow__progress small:last-child { flex-basis: 100%; margin-left: 0; }
  .pro-workflow__export-track small { flex-basis: 100%; }
}
</style>
