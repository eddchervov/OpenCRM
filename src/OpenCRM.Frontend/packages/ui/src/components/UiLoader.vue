<script setup lang="ts">
import { useLocalization } from '../i18n'

const { t } = useLocalization()

withDefaults(defineProps<{
  text?: string
  compact?: boolean
}>(), {
  text: '',
  compact: false
})
</script>

<template>
  <div class="ui-loader" :class="{ 'ui-loader--compact': compact }" role="status" aria-live="polite">
    <span class="ui-loader__spinner" aria-hidden="true"></span>
    <span>{{ text || t('common.loading') }}</span>
  </div>
</template>
