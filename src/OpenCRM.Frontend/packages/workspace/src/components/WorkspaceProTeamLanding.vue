<script setup lang="ts">
import { getProjectBrand } from '@opencrm/ui'
import { computed, ref } from 'vue'
import { UiButton } from '@opencrm/ui'
import studioImage from '../assets/pro/team-studio.webp'
import teamImage from '../assets/pro/team.png'
import rolePermissionsImage from '../assets/pro/role-permissions.png'

const projectBrand = getProjectBrand()








defineProps<{ titleId: string; title: string; canManageBilling: boolean }>()
const emit = defineEmits<{
  preview: [image: { src: string; alt: string }]
  billing: []
}>()
const accessSection = ref<HTMLElement | null>(null)
const selectedRole = ref<'performer' | 'admin'>('performer')
const accessExample = computed(() => selectedRole.value === 'performer'
  ? ['Просмотр', 'Закрыто', 'Просмотр', 'Закрыто']
  : ['Просмотр и изменение', 'Просмотр и изменение', 'Просмотр и изменение', 'Просмотр'])
const areas = ['Записи и работы', 'База клиентов', 'Склад', 'Финансы']
const examples = [
  { src: teamImage, alt: 'Команда организации: сотрудники и доступ к CRM', title: 'Команда в одном пространстве', description: 'Добавляйте сотрудников, подключайте доступ и назначайте роли.', viewBox: '232 66 1048 590', width: 1280, height: 720 },
  { src: rolePermissionsImage, alt: 'Матрица прав ролей организации', title: 'Доступ под вашу работу', description: 'Отдельно выбирайте права на просмотр и изменение данных.', viewBox: '275 205 1130 750', width: 1425, height: 1267 }
]
function showAccessExample() {
  accessSection.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  accessSection.value?.focus({ preventScroll: true })
}
</script>

<template>
  <div class="pro-team-landing">
    <section class="pro-team-landing__hero">
      <div class="pro-team-landing__pitch">
        <span class="pro-team-landing__eyebrow">{{ title }} в PRO</span>
        <h2 :id="titleId">Ваша команда.<br>Ваши правила работы.</h2>
        <p>Администратор ведёт запись. Мастер выполняет работу. Вы решаете, кому доступны клиенты, склад и финансы.</p>
        <div class="pro-team-landing__actions">
          <UiButton v-if="canManageBilling" href="#billing" @click="emit('billing')">Выбрать PRO →</UiButton>
          <button type="button" class="pro-team-landing__text-button" @click="showAccessExample">Посмотреть пример доступа ↓</button>
        </div>
      </div>
      <button class="pro-team-landing__cover" type="button" aria-label="Увеличить иллюстрацию команды студии" @click="emit('preview', { src: studioImage, alt: 'Иллюстрация совместной работы администратора и мастера в студии' })">
        <img :src="studioImage" alt="Администратор и мастер обсуждают работу у автомобиля в детейлинг-студии" width="1536" height="1024">
        <span class="pro-team-landing__cover-label">Работайте вместе <span>↗</span></span>
      </button>
    </section>
    <section class="pro-team-landing__benefits" aria-label="Возможности команды">
      <article><span>01 / ЗАДАЧИ</span><h3>У каждой работы — исполнитель</h3><p>Назначайте мастеров на работы и отслеживайте выполнение в CRM.</p></article>
      <article><span>02 / ДОСТУП</span><h3>Каждому — своя роль</h3><p>Администратор, сотрудник, исполнитель или только просмотр.</p></article>
      <article><span>03 / КОНТРОЛЬ</span><h3>Финансы — кому разрешите</h3><p>Настраивайте доступ роли к данным и их изменению.</p></article>
    </section>
    <section ref="accessSection" class="pro-team-landing__access" tabindex="-1" aria-label="Пример настройки доступа">
      <div class="pro-team-landing__section-copy">
        <span class="pro-team-landing__eyebrow">ПРАВА ПОД ВАШ ПРОЦЕСС</span>
        <h3>Одна CRM.<br>Разный доступ.</h3>
        <p>Назначьте сотруднику роль и выберите, что ей разрешено. Например, откройте мастеру работы, а финансы оставьте администратору.</p>
        <p class="pro-team-landing__note">Права роли действуют для всех сотрудников с этой ролью.</p>
      </div>
      <div class="pro-team-landing__permission-card">
        <div class="pro-team-landing__permission-header"><strong>Пример настроек роли</strong><span>Демо</span></div>
        <div class="pro-team-landing__role-switch" role="group" aria-label="Выбрать пример роли">
          <button type="button" :aria-pressed="selectedRole === 'performer'" @click="selectedRole = 'performer'">Мастер</button>
          <button type="button" :aria-pressed="selectedRole === 'admin'" @click="selectedRole = 'admin'">Администратор</button>
        </div>
        <div class="pro-team-landing__permissions" aria-live="polite">
          <div v-for="(area, index) in areas" :key="area"><span>{{ area }}</span><strong :class="{ 'is-closed': accessExample[index] === 'Закрыто' }">{{ accessExample[index] }}</strong></div>
        </div>
      </div>
    </section>
    <section class="pro-team-landing__screens" aria-label="Экраны команды и прав доступа">
      <div class="pro-team-landing__screens-heading"><span class="pro-team-landing__eyebrow">ВНУТРИ {{ projectBrand }}</span><h3>Вот как это работает</h3></div>
      <div class="pro-team-landing__screen-grid">
        <article v-for="example in examples" :key="example.title">
          <button type="button" :aria-label="`Увеличить пример: ${example.title}`" @click="emit('preview', example)">
            <svg :viewBox="example.viewBox" preserveAspectRatio="xMidYMin slice" role="img" :aria-label="example.alt"><image :href="example.src" :width="example.width" :height="example.height" /></svg>
            <span>Увеличить пример ↗</span>
          </button>
          <h4>{{ example.title }}</h4><p>{{ example.description }}</p>
        </article>
      </div>
      <p class="pro-team-landing__note">Демонстрационный пример</p>
    </section>
  </div>
</template>

<style scoped>
.pro-team-landing { container-type: inline-size; }
.pro-team-landing__hero { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; align-items: center; }
.pro-team-landing__pitch { padding: 10px 0; }
.pro-team-landing__eyebrow { color: var(--crm-action-accent, #b94a20); font-size: 11px; font-weight: 800; letter-spacing: .08em; }
.pro-team-landing h2 { margin: 14px 0; font-size: clamp(30px, 4cqi, 44px); line-height: 1.08; letter-spacing: -.035em; }
.pro-team-landing p { color: var(--crm-text-muted, #687386); font-size: 14px; line-height: 1.65; }
.pro-team-landing__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 14px; margin-top: 22px; }
.pro-team-landing__actions :deep(.ui-button--primary) { background: var(--crm-action-accent, #b94a20); border-color: var(--crm-action-accent, #b94a20); color: #fff; }
:global(:root[data-crm-theme="dark"] .pro-team-landing__actions .ui-button--primary) { color: var(--crm-canvas, #12161d); }
.pro-team-landing__text-button { padding: 4px 0; border: 0; background: transparent; color: var(--crm-text, #202631); cursor: pointer; font: inherit; font-size: 12px; font-weight: 700; }
.pro-team-landing__cover { position: relative; display: block; min-width: 0; padding: 0; overflow: hidden; border: 0; border-radius: 14px; background: #171c22; cursor: zoom-in; }
.pro-team-landing__cover img { display: block; width: 100%; height: auto; aspect-ratio: 1.18; object-fit: cover; }
.pro-team-landing__cover-label { position: absolute; right: 0; bottom: 0; left: 0; display: flex; justify-content: space-between; padding: 40px 20px 18px; background: linear-gradient(transparent, rgb(0 0 0 / 75%)); color: #fff; font-size: 15px; font-weight: 700; }
.pro-team-landing__benefits { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; margin: 28px 0 36px; }
.pro-team-landing__benefits article { padding: 18px 0 0; border-top: 2px solid var(--crm-line, #dce2e9); }
.pro-team-landing__benefits article > span { color: var(--crm-action-accent, #b94a20); font-size: 10px; font-weight: 800; letter-spacing: .07em; }
.pro-team-landing__benefits h3 { margin: 10px 0 5px; font-size: 16px; line-height: 1.4; }
.pro-team-landing__benefits p { margin: 0; font-size: 13px; }
.pro-team-landing__access { display: grid; grid-template-columns: 1fr 1fr; gap: 30px; align-items: center; padding: 26px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 14px; background: var(--crm-surface-soft, #f6f7f9); scroll-margin: 10px; }
.pro-team-landing__section-copy h3, .pro-team-landing__screens h3 { margin: 10px 0; font-size: 28px; line-height: 1.15; letter-spacing: -.02em; }
.pro-team-landing .pro-team-landing__note { margin-bottom: 0; font-size: 11px; }
.pro-team-landing__permission-card { min-width: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; background: var(--crm-surface, #fff); }
.pro-team-landing__permission-header { display: flex; justify-content: space-between; align-items: center; gap: 8px; padding: 16px; font-size: 12px; }
.pro-team-landing__permission-header > span { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-team-landing__role-switch { display: flex; gap: 5px; padding: 0 14px 14px; }
.pro-team-landing__role-switch button { flex: 1; padding: 9px 7px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; background: var(--crm-surface-soft, #f6f7f9); color: inherit; cursor: pointer; font: inherit; font-size: 12px; }
.pro-team-landing__role-switch button[aria-pressed="true"] { border-color: var(--crm-action-accent, #b94a20); background: color-mix(in srgb, var(--crm-action-accent, #b94a20) 10%, var(--crm-surface, #fff)); color: var(--crm-action-accent, #b94a20); font-weight: 700; }
.pro-team-landing__permissions > div { display: flex; justify-content: space-between; gap: 12px; align-items: center; padding: 13px 16px; border-top: 1px solid var(--crm-line, #dce2e9); font-size: 12px; }
.pro-team-landing__permissions strong { color: var(--crm-action-accent, #b94a20); font-size: 11px; text-align: right; }
.pro-team-landing__permissions .is-closed { color: var(--crm-text-muted, #687386); font-weight: 400; }
.pro-team-landing__screens { margin-top: 36px; }
.pro-team-landing__screens-heading { margin-bottom: 20px; }
.pro-team-landing__screen-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 24px; }
.pro-team-landing__screen-grid button { display: block; width: 100%; padding: 12px 12px 8px; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; background: var(--crm-surface-soft, #f6f7f9); color: inherit; cursor: zoom-in; }
.pro-team-landing__screen-grid svg { display: block; width: 100%; height: auto; aspect-ratio: 1.5; border-radius: 6px; background: #fff; }
.pro-team-landing__screen-grid button > span { display: block; padding-top: 9px; color: var(--crm-text-muted, #687386); font-size: 11px; text-align: right; }
.pro-team-landing__screen-grid h4 { margin: 14px 0 3px; font-size: 16px; }
.pro-team-landing__screen-grid p { margin: 0; font-size: 13px; }
.pro-team-landing button:focus-visible, .pro-team-landing__access:focus-visible { outline: 2px solid var(--crm-action-accent, #b94a20); outline-offset: 3px; }
@container (max-width: 650px) {
  .pro-team-landing__hero, .pro-team-landing__access, .pro-team-landing__screen-grid { grid-template-columns: minmax(0, 1fr); }
  .pro-team-landing__cover img { aspect-ratio: 1.65; }
  .pro-team-landing__benefits { grid-template-columns: minmax(0, 1fr); gap: 14px; }
  .pro-team-landing__benefits article { padding-top: 12px; }
  .pro-team-landing__access { padding: 18px; gap: 20px; }
}
</style>
