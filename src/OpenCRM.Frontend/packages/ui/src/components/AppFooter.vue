<script setup lang="ts">
import type { FooterSection } from '../navigation'
import { getProjectBrand } from '../projectBrand'
import { useLocalization } from '../i18n'

const { t } = useLocalization()

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
        <h2>{{ section.titleKey ? t(section.titleKey) : section.title }}</h2>
        <div class="opencrm-footer__links">
          <a v-for="link in section.links" :key="link.href" :href="link.href">{{ link.labelKey ? t(link.labelKey) : link.label }}</a>
        </div>
      </section>
    </div>
    <div class="opencrm-footer__copy">На базе <a href="https://github.com/eddchervov/OpenCRM" target="_blank" rel="noopener noreferrer">OpenCRM</a> · © {{ brand }}, {{ year }}</div>
  </footer>
</template>
