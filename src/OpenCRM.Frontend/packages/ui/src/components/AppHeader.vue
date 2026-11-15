<script setup lang="ts">
import { computed } from 'vue'
import { getProjectBrand } from '../projectBrand'
import type { HeaderLink, ProfileLink } from '../navigation'
import UiTooltip from './UiTooltip.vue'

const props = withDefaults(defineProps<{
  brand?: string
  brandHref?: string
  displayName?: string
  links?: HeaderLink[]
  profileLinks?: ProfileLink[]
}>(), {
  brand: getProjectBrand(),
  brandHref: '/',
  displayName: 'Профиль',
  links: () => [],
  profileLinks: () => []
})

const hasProfileMenu = computed(() => props.profileLinks.length > 0)
const requestToken = document.querySelector<HTMLMetaElement>('meta[name="request-verification-token"]')?.content ?? ''
</script>

<template>
  <header class="opencrm-header">
    <div class="opencrm-container">
      <nav class="opencrm-header__nav" aria-label="Главное меню">
        <a class="opencrm-header__brand" :href="brandHref">{{ brand }}</a>

        <div class="opencrm-header__menu">
          <a v-for="link in links" :key="link.href" :href="link.href" class="opencrm-header__link">
            {{ link.label }}
          </a>
          <div v-if="hasProfileMenu" class="opencrm-profile">
            <UiTooltip :text="displayName" placement="left">
              <button class="opencrm-icon-button opencrm-profile__button" type="button" aria-label="Профиль">
                <svg viewBox="0 0 24 24" aria-hidden="true">
                  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path>
                  <circle cx="12" cy="7" r="4"></circle>
                </svg>
                <svg class="opencrm-profile__chevron" viewBox="0 0 24 24" aria-hidden="true">
                  <path d="m6 9 6 6 6-6"></path>
                </svg>
              </button>
            </UiTooltip>
            <div class="opencrm-profile__menu">
              <div class="opencrm-profile__name">{{ displayName }}</div>
              <template v-for="link in profileLinks" :key="`${link.label}-${link.href}`">
                <hr v-if="link.kind === 'divider'" class="opencrm-profile__divider">
                <form v-else-if="link.kind === 'logout'" method="post" :action="link.href">
                  <input type="hidden" name="__RequestVerificationToken" :value="requestToken">
                  <button type="submit">{{ link.label }}</button>
                </form>
                <a v-else :href="link.href">{{ link.label }}</a>
              </template>
            </div>
          </div>
        </div>
      </nav>
    </div>
  </header>
</template>
