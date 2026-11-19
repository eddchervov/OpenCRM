<script setup lang="ts">
import { RegionalPreferences } from '@opencrm/ui'
import { useLocalization } from '@opencrm/ui/i18n'
import '@opencrm/ui/styles.css'

const { t } = useLocalization()
</script>

<template>
  <section class="admin-personal-settings">
    <h1>{{ t('common.personalSettings') }}</h1>
    <RegionalPreferences />
  </section>
</template>

<style scoped>
.admin-personal-settings { --crm-text: inherit; --crm-text-muted: inherit; --crm-surface: transparent; }
:global(:root[data-admin-theme='dark']) .admin-personal-settings { --crm-line: #466061; --crm-danger-text: #ff9999; }
</style>
