<script setup lang="ts">
import { computed, useAttrs } from 'vue'

defineOptions({
  inheritAttrs: false
})

const props = withDefaults(defineProps<{
  type?: 'button' | 'submit' | 'reset'
  variant?: 'primary' | 'secondary' | 'ghost'
  disabled?: boolean
  href?: string
}>(), {
  type: 'button',
  variant: 'primary',
  disabled: false,
  href: undefined
})

const attrs = useAttrs()
const isLink = computed(() => Boolean(props.href))
</script>

<template>
  <a
    v-if="isLink"
    class="ui-button btn"
    :class="[`ui-button--${variant}`, `btn-${variant === 'primary' ? 'dark' : variant === 'secondary' ? 'outline-dark' : 'link'}`]"
    :href="disabled ? undefined : href"
    :aria-disabled="disabled || undefined"
    :tabindex="disabled ? -1 : undefined"
    v-bind="attrs"
  >
    <slot />
  </a>
  <button
    v-else
    class="ui-button btn"
    :class="[`ui-button--${variant}`, `btn-${variant === 'primary' ? 'dark' : variant === 'secondary' ? 'outline-dark' : 'link'}`]"
    :type="type"
    :disabled="disabled"
    v-bind="attrs"
  >
    <slot />
  </button>
</template>
