<script setup lang="ts">
import AppFooter from './AppFooter.vue'
import { getProjectBrand } from '../projectBrand'
import AppHeader from './AppHeader.vue'
import { useLocalization } from '../i18n'
import {
  defaultFooterSections,
  defaultHeaderLinks,
  defaultProfileLinks,
  type FooterSection,
  type HeaderLink,
  type ProfileLink
} from '../navigation'

const props = withDefaults(defineProps<{
  brand?: string
  brandHref?: string
  displayName?: string
  headerLinks?: HeaderLink[]
  profileLinks?: ProfileLink[]
  footerSections?: FooterSection[]
  year?: number
}>(), {
  brand: getProjectBrand(),
  brandHref: '/',
  displayName: '',
  headerLinks: () => defaultHeaderLinks,
  profileLinks: () => defaultProfileLinks,
  footerSections: () => defaultFooterSections,
  year: () => new Date().getFullYear()
})
const { t } = useLocalization()
</script>

<template>
  <div class="opencrm-app-shell">
    <AppHeader
      :brand="brand"
      :brand-href="brandHref"
      :display-name="displayName || t('common.profile')"
      :links="headerLinks"
      :profile-links="profileLinks"
    />
    <main class="opencrm-app-shell__main">
      <slot />
    </main>
    <AppFooter :brand="brand" :sections="footerSections" :year="year" />
  </div>
</template>
