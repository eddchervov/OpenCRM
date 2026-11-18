<script setup lang="ts">
import { useLocalization } from '@opencrm/ui'
import { workspaceLabelKey } from '../workspaceChromeMessages'

export type WorkspaceBreadcrumbItem = {
  title: string
  code?: string
  href?: string
}

defineProps<{
  items: WorkspaceBreadcrumbItem[]
}>()
const { t, te } = useLocalization()

function itemTitle(item: WorkspaceBreadcrumbItem): string {
  if (!item.code) return item.title
  const key = workspaceLabelKey(item.code)
  return te(key) ? t(key) : item.title
}
</script>

<template>
  <nav class="workspace-breadcrumbs" :aria-label="t('common.breadcrumbs')">
    <ol>
      <li v-for="(item, index) in items" :key="`${item.title}-${index}`">
        <a v-if="item.href && index < items.length - 1" :href="item.href">{{ itemTitle(item) }}</a>
        <span v-else>{{ itemTitle(item) }}</span>
      </li>
    </ol>
  </nav>
</template>
