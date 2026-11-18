<script setup lang="ts">
import { useLocalization, setUiLocale, formattingCulture, formattingCultures, uiLanguages, type UiLocale } from '../i18n'
import { ref, watch } from 'vue'

const { t, locale } = useLocalization()
const formatCulture = ref(formattingCulture.value)
const submitError = ref('')
const formatCultures = formattingCultures
watch(formattingCulture, value => { formatCulture.value = value })

async function savePreferences(nextLocale: UiLocale, nextFormat = formatCulture.value) {
  submitError.value = ''
  formatCulture.value = nextFormat
  if (!await setUiLocale(nextLocale, nextFormat)) {
    submitError.value = t('common.languageSaveError')
  }
}

function changeLanguage(event: Event) {
  void savePreferences((event.target as HTMLSelectElement).value as UiLocale)
}

function changeFormat(event: Event) {
  void savePreferences(locale.value, (event.target as HTMLSelectElement).value)
}
</script>

<template>
  <label class="opencrm-language-switcher">
    <span class="opencrm-language-switcher__label">{{ t('common.language') }}</span>
    <select :value="locale" :aria-label="t('common.language')" @change="changeLanguage">
      <option v-for="language in uiLanguages" :key="language.code" :value="language.code">{{ language.name }}</option>
    </select>
    <span class="opencrm-language-switcher__label">{{ t('common.numberFormat') }}</span>
    <select v-model="formatCulture" :aria-label="t('common.numberFormat')" @change="changeFormat">
      <option v-for="culture in formatCultures" :key="culture" :value="culture">{{ culture }}</option>
    </select>
    <span v-if="submitError" class="opencrm-language-switcher__error" role="alert">{{ submitError }}</span>
  </label>
</template>
