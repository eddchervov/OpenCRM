<script setup lang="ts">
import LanguageSwitcher from './LanguageSwitcher.vue'
import { useLocalization } from '../i18n'

const { t } = useLocalization()
</script>

<template>
  <section class="opencrm-regional-preferences" :aria-label="t('common.regionalPreferences')">
    <h3>{{ t('common.regionalPreferences') }}</h3>
    <p>{{ t('common.regionalPreferencesHint') }}</p>
    <LanguageSwitcher />
  </section>
</template>

<style scoped>
.opencrm-regional-preferences h3 { margin: 0 0 8px; font-size: 16px; }
.opencrm-regional-preferences p { margin: 0 0 16px; color: var(--crm-text-muted, #687386); font-size: 13px; line-height: 1.5; }
</style>
