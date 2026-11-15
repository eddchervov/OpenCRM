<script setup lang="ts">
export type WorkspaceBreadcrumbItem = {
  title: string
  href?: string
}

defineProps<{
  items: WorkspaceBreadcrumbItem[]
}>()
</script>

<template>
  <nav class="workspace-breadcrumbs" aria-label="Хлебные крошки">
    <ol>
      <li v-for="(item, index) in items" :key="`${item.title}-${index}`">
        <a v-if="item.href && index < items.length - 1" :href="item.href">{{ item.title }}</a>
        <span v-else>{{ item.title }}</span>
      </li>
    </ol>
  </nav>
</template>
