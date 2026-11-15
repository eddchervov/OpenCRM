<script setup lang="ts">
withDefaults(defineProps<{
  text?: string
  compact?: boolean
}>(), {
  text: 'Загружаем...',
  compact: false
})
</script>

<template>
  <div class="ui-loader" :class="{ 'ui-loader--compact': compact }" role="status" aria-live="polite">
    <span class="ui-loader__spinner" aria-hidden="true"></span>
    <span>{{ text }}</span>
  </div>
</template>
