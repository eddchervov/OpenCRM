<script setup lang="ts">
import type { FooterSection } from '../navigation'
import { getProjectBrand } from '../projectBrand'

withDefaults(defineProps<{
  brand?: string
  sections?: FooterSection[]
  year?: number
}>(), {
  brand: getProjectBrand(),
  sections: () => [],
  year: () => new Date().getFullYear()
})
</script>

<template>
  <footer class="opencrm-footer">
    <div class="opencrm-container opencrm-footer__grid">
      <section v-for="section in sections" :key="section.title" class="opencrm-footer__section">
        <h2>{{ section.title }}</h2>
        <div class="opencrm-footer__links">
          <a v-for="link in section.links" :key="link.href" :href="link.href">{{ link.label }}</a>
        </div>
      </section>
    </div>
    <div class="opencrm-footer__copy">На базе OpenCRM · автор <a href="https://github.com/eddchervov" target="_blank" rel="noopener noreferrer">eddchervov</a> · © {{ brand }}, {{ year }}</div>
  </footer>
</template>
