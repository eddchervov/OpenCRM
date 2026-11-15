<script setup lang="ts">
import { computed, ref } from 'vue'
import { autoUpdate, flip, offset, shift, useFloating, type Placement } from '@floating-ui/vue'

const props = withDefaults(defineProps<{
  text: string
  placement?: Placement
  disabled?: boolean
}>(), {
  placement: 'top',
  disabled: false
})

const isOpen = ref(false)
const reference = ref<HTMLElement | null>(null)
const floating = ref<HTMLElement | null>(null)
const tooltipId = `ui-tooltip-${Math.random().toString(36).slice(2)}`
const teleportTarget = ref<Element | string>('body')

const { floatingStyles } = useFloating(reference, floating, {
  placement: props.placement,
  strategy: 'fixed',
  whileElementsMounted: autoUpdate,
  middleware: [offset(8), flip(), shift({ padding: 8 })]
})

const canShow = computed(() => Boolean(props.text) && !props.disabled)

function open() {
  if (canShow.value) {
    teleportTarget.value = reference.value?.closest('dialog[open]') ?? 'body'
    isOpen.value = true
  }
}

function close() {
  isOpen.value = false
}
</script>

<template>
  <span
    ref="reference"
    class="ui-tooltip-anchor"
    :aria-describedby="isOpen ? tooltipId : undefined"
    @mouseenter="open"
    @mouseleave="close"
    @focusin="open"
    @focusout="close"
  >
    <slot />
  </span>

  <Teleport :to="teleportTarget">
    <span
      v-if="isOpen"
      :id="tooltipId"
      ref="floating"
      class="ui-tooltip"
      role="tooltip"
      :style="floatingStyles"
    >
      {{ text }}
    </span>
  </Teleport>
</template>
