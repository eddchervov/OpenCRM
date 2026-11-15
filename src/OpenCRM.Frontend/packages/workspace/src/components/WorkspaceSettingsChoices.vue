<script setup lang="ts">
defineProps<{
  title: string
  groups: { title?: string, items: { code: string, title: string, description: string, symbol: string, detail?: string }[] }[]
  disabled?: boolean
}>()

defineEmits<{ select: [code: string] }>()
</script>

<template>
  <section class="settings-choices" :aria-label="title">
    <header class="settings-choices__header ui-section-header">
      <h3>{{ title }}</h3>
      <p>Выберите, что хотите настроить.</p>
    </header>
    <section v-for="(group, index) in groups" :key="index" class="settings-choices__group">
      <h4 v-if="group.title">{{ group.title }}</h4>
      <div class="settings-choices__list">
        <button v-for="item in group.items" :key="item.code" type="button" :data-section="item.code"
          class="settings-choices__item" :disabled="disabled" @click="$emit('select', item.code)">
          <span class="settings-choices__icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">
              <template v-if="item.symbol === 'clock'"><circle cx="12" cy="12" r="8" /><path d="M12 7v5l3 2" /></template>
              <path v-else-if="item.symbol === 'send'" d="m21 3-7 18-4-7-7-4 18-7ZM10 14l5-5" />
              <template v-else-if="item.symbol === 'rules'"><path d="M4 7h16M4 17h16" /><circle cx="9" cy="7" r="3" /><circle cx="15" cy="17" r="3" /></template>
              <path v-else d="M20 11a8 8 0 0 1-8 8 9 9 0 0 1-4-.9L3 20l1.9-5A9 9 0 0 1 4 11a8 8 0 0 1 16 0Z" />
            </svg>
          </span>
          <span class="settings-choices__label"><strong>{{ item.title }}</strong><span>{{ item.description }}</span><small v-if="item.detail">{{ item.detail }}</small></span>
          <span class="settings-choices__arrow" aria-hidden="true">›</span>
        </button>
      </div>
    </section>
  </section>
</template>

<style scoped>
.settings-choices { min-width: 0; padding: 18px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); }
.settings-choices__header { margin-bottom: 20px; }
.settings-choices__header h3 { margin: 0; font-size: 16px; }
.settings-choices__header p { margin: 5px 0 0; color: var(--crm-text-muted, #687386); font-size: 13px; }
.settings-choices__group + .settings-choices__group { margin-top: 22px; }
.settings-choices__group h4 { margin: 0 0 9px; color: var(--crm-text-muted, #687386); font-size: 12px; font-weight: 500; }
.settings-choices__list { overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; }
.settings-choices__item { display: flex; align-items: center; gap: 12px; width: 100%; padding: 15px 14px; border: 0; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font: inherit; text-align: left; cursor: pointer; }
.settings-choices__item + .settings-choices__item { border-top: 1px solid var(--crm-line, #dce2e9); }
.settings-choices__item:hover { background: var(--crm-surface-soft, #f5f6f8); }
.settings-choices__item:focus-visible { outline: 2px solid var(--crm-header-accent, #e87532); outline-offset: -2px; }
.settings-choices__icon { display: grid; place-items: center; flex: 0 0 34px; height: 34px; border-radius: 8px; background: var(--crm-surface-soft, #f5f6f8); color: var(--crm-header-accent, #c65315); }
.settings-choices__icon svg { width: 21px; height: 21px; }
.settings-choices__label { display: grid; gap: 5px; min-width: 0; }
.settings-choices__label strong { font-size: 13px; font-weight: 600; }
.settings-choices__label > span { color: var(--crm-text-muted, #687386); font-size: 12px; line-height: 1.45; }
.settings-choices__label small { color: var(--crm-text, #202631); font-size: 12px; line-height: 1.45; }
.settings-choices__arrow { margin-left: auto; padding-left: 6px; color: var(--crm-text-muted, #687386); font-size: 22px; }
@media (max-width: 600px) {
  .settings-choices { padding: 14px; }
  .settings-choices__item { gap: 10px; padding: 12px; }
}
</style>
