import * as localization from '@opencrm/ui/i18n'
import { computed } from 'vue'

export const localizationTestContext = {
  useLocalization: () => ({
    t: localization.translate,
    te: localization.i18n.global.te,
    locale: computed(() => localization.i18n.global.locale.value),
    formattingCulture: localization.formattingCulture
  })
}

export const regionalTestDependencies = {
  '@opencrm/ui/i18n': { ...localization, ...localizationTestContext },
  '../i18n': { ...localization, ...localizationTestContext },
  '../workspaceCurrency': {
    useWorkspaceCurrency: () => computed(() => 'RUB'),
    provideWorkspaceCurrency: currency => currency
  }
}
