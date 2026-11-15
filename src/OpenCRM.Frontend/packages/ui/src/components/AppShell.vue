<script setup lang="ts">
import AppFooter from './AppFooter.vue'
import { getProjectBrand } from '../projectBrand'
import AppHeader from './AppHeader.vue'
import {
  defaultFooterSections,
  defaultHeaderLinks,
  defaultProfileLinks,
  type FooterSection,
  type HeaderLink,
  type ProfileLink
} from '../navigation'

withDefaults(defineProps<{
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
  displayName: 'Профиль',
  headerLinks: () => defaultHeaderLinks,
  profileLinks: () => defaultProfileLinks,
  footerSections: () => defaultFooterSections,
  year: () => new Date().getFullYear()
})
</script>

<template>
  <div class="opencrm-app-shell">
    <AppHeader
      :brand="brand"
      :brand-href="brandHref"
      :display-name="displayName"
      :links="headerLinks"
      :profile-links="profileLinks"
    />
    <main class="opencrm-app-shell__main">
      <slot />
    </main>
    <AppFooter :brand="brand" :sections="footerSections" :year="year" />
  </div>
</template>
