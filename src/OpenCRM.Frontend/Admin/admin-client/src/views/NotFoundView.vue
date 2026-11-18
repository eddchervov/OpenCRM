<template>
  <section class="empty-state">
    <span>404</span>
    <h2>{{ t('adminOperations.sectionNotCreated') }}</h2>
    <p>{{ t('adminOperations.sectionNotCreatedHelp') }}</p>
    <RouterLink to="/">{{ t('adminOperations.backToDashboard') }}</RouterLink>
  </section>
</template>

<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../adminOperationsMessages'

const { t } = useLocalization()
</script>
