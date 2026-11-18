import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/proLandingMessages'
import type { ProFeatureKey } from './proFeatureDetails'

export type ProFeatureLandingContent = {
  headline: readonly [string, string]
  tags: string
  benefitTitles: readonly string[]
  steps: readonly { title: string; text: string }[]
}

export const proFeatureLandingContent: Record<Exclude<ProFeatureKey, 'team' | 'roles'>, ProFeatureLandingContent> = {
  get shifts(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.shifts_headline_item1'), translate('proLanding.shifts_headline_item2')], tags: translate('proLanding.shifts_tags'),
      benefitTitles: [translate('proLanding.shifts_benefitTitles_item1'), translate('proLanding.shifts_benefitTitles_item2'), translate('proLanding.shifts_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.shifts_steps_item1_title'), text: translate('proLanding.shifts_steps_item1_text') }, { title: translate('proLanding.shifts_steps_item2_title'), text: translate('proLanding.shifts_steps_item2_text') }, { title: translate('proLanding.shifts_steps_item3_title'), text: translate('proLanding.shifts_steps_item3_text') }]
    }
  },
  get statistics(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.statistics_headline_item1'), translate('proLanding.statistics_headline_item2')], tags: translate('proLanding.statistics_tags'),
      benefitTitles: [translate('proLanding.statistics_benefitTitles_item1'), translate('proLanding.statistics_benefitTitles_item2'), translate('proLanding.statistics_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.statistics_steps_item1_title'), text: translate('proLanding.statistics_steps_item1_text') }, { title: translate('proLanding.statistics_steps_item2_title'), text: translate('proLanding.statistics_steps_item2_text') }, { title: translate('proLanding.statistics_steps_item3_title'), text: translate('proLanding.statistics_steps_item3_text') }]
    }
  },
  get messages(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.messages_headline_item1'), translate('proLanding.messages_headline_item2')], tags: translate('proLanding.messages_tags'),
      benefitTitles: [translate('proLanding.messages_benefitTitles_item1'), translate('proLanding.messages_benefitTitles_item2'), translate('proLanding.messages_benefitTitles_item3'), translate('proLanding.messages_benefitTitles_item4')],
      steps: [{ title: translate('proLanding.messages_steps_item1_title'), text: translate('proLanding.messages_steps_item1_text') }, { title: translate('proLanding.messages_steps_item2_title'), text: translate('proLanding.messages_steps_item2_text') }, { title: translate('proLanding.messages_steps_item3_title'), text: translate('proLanding.messages_steps_item3_text') }]
    }
  },
  get intake(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.intake_headline_item1'), translate('proLanding.intake_headline_item2')], tags: translate('proLanding.intake_tags'),
      benefitTitles: [translate('proLanding.intake_benefitTitles_item1'), translate('proLanding.intake_benefitTitles_item2'), translate('proLanding.intake_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.intake_steps_item1_title'), text: translate('proLanding.intake_steps_item1_text') }, { title: translate('proLanding.intake_steps_item2_title'), text: translate('proLanding.intake_steps_item2_text') }, { title: translate('proLanding.intake_steps_item3_title'), text: translate('proLanding.intake_steps_item3_text') }]
    }
  },
  get albums(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.albums_headline_item1'), translate('proLanding.albums_headline_item2')], tags: translate('proLanding.albums_tags'),
      benefitTitles: [translate('proLanding.albums_benefitTitles_item1'), translate('proLanding.albums_benefitTitles_item2'), translate('proLanding.albums_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.albums_steps_item1_title'), text: translate('proLanding.albums_steps_item1_text') }, { title: translate('proLanding.albums_steps_item2_title'), text: translate('proLanding.albums_steps_item2_text') }, { title: translate('proLanding.albums_steps_item3_title'), text: translate('proLanding.albums_steps_item3_text') }]
    }
  },
  get notifications(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.notifications_headline_item1'), translate('proLanding.notifications_headline_item2')], tags: translate('proLanding.notifications_tags'),
      benefitTitles: [translate('proLanding.notifications_benefitTitles_item1'), translate('proLanding.notifications_benefitTitles_item2'), translate('proLanding.notifications_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.notifications_steps_item1_title'), text: translate('proLanding.notifications_steps_item1_text') }, { title: translate('proLanding.notifications_steps_item2_title'), text: translate('proLanding.notifications_steps_item2_text') }, { title: translate('proLanding.notifications_steps_item3_title'), text: translate('proLanding.notifications_steps_item3_text') }]
    }
  },
  get messageTemplates(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.messageTemplates_headline_item1'), translate('proLanding.messageTemplates_headline_item2')], tags: translate('proLanding.messageTemplates_tags'),
      benefitTitles: [translate('proLanding.messageTemplates_benefitTitles_item1'), translate('proLanding.messageTemplates_benefitTitles_item2'), translate('proLanding.messageTemplates_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.messageTemplates_steps_item1_title'), text: translate('proLanding.messageTemplates_steps_item1_text') }, { title: translate('proLanding.messageTemplates_steps_item2_title'), text: translate('proLanding.messageTemplates_steps_item2_text') }, { title: translate('proLanding.messageTemplates_steps_item3_title'), text: translate('proLanding.messageTemplates_steps_item3_text') }]
    }
  },
  get selfAssignment(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.selfAssignment_headline_item1'), translate('proLanding.selfAssignment_headline_item2')], tags: translate('proLanding.selfAssignment_tags'),
      benefitTitles: [translate('proLanding.selfAssignment_benefitTitles_item1'), translate('proLanding.selfAssignment_benefitTitles_item2'), translate('proLanding.selfAssignment_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.selfAssignment_steps_item1_title'), text: translate('proLanding.selfAssignment_steps_item1_text') }, { title: translate('proLanding.selfAssignment_steps_item2_title'), text: translate('proLanding.selfAssignment_steps_item2_text') }, { title: translate('proLanding.selfAssignment_steps_item3_title'), text: translate('proLanding.selfAssignment_steps_item3_text') }]
    }
  },
  get documentStyling(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.documentStyling_headline_item1'), translate('proLanding.documentStyling_headline_item2')], tags: translate('proLanding.documentStyling_tags'),
      benefitTitles: [translate('proLanding.documentStyling_benefitTitles_item1'), translate('proLanding.documentStyling_benefitTitles_item2'), translate('proLanding.documentStyling_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.documentStyling_steps_item1_title'), text: translate('proLanding.documentStyling_steps_item1_text') }, { title: translate('proLanding.documentStyling_steps_item2_title'), text: translate('proLanding.documentStyling_steps_item2_text') }, { title: translate('proLanding.documentStyling_steps_item3_title'), text: translate('proLanding.documentStyling_steps_item3_text') }]
    }
  },
  get dataImport(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.dataImport_headline_item1'), translate('proLanding.dataImport_headline_item2')], tags: translate('proLanding.dataImport_tags'),
      benefitTitles: [translate('proLanding.dataImport_benefitTitles_item1'), translate('proLanding.dataImport_benefitTitles_item2'), translate('proLanding.dataImport_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.dataImport_steps_item1_title'), text: translate('proLanding.dataImport_steps_item1_text') }, { title: translate('proLanding.dataImport_steps_item2_title'), text: translate('proLanding.dataImport_steps_item2_text') }, { title: translate('proLanding.dataImport_steps_item3_title'), text: translate('proLanding.dataImport_steps_item3_text') }]
    }
  },
  get accountingExport(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.accountingExport_headline_item1'), translate('proLanding.accountingExport_headline_item2')], tags: translate('proLanding.accountingExport_tags'),
      benefitTitles: [translate('proLanding.accountingExport_benefitTitles_item1'), translate('proLanding.accountingExport_benefitTitles_item2'), translate('proLanding.accountingExport_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.accountingExport_steps_item1_title'), text: translate('proLanding.accountingExport_steps_item1_text') }, { title: translate('proLanding.accountingExport_steps_item2_title'), text: translate('proLanding.accountingExport_steps_item2_text') }, { title: translate('proLanding.accountingExport_steps_item3_title'), text: translate('proLanding.accountingExport_steps_item3_text') }]
    }
  },
  get assistant(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.assistant_headline_item1'), translate('proLanding.assistant_headline_item2')], tags: translate('proLanding.assistant_tags'),
      benefitTitles: [translate('proLanding.assistant_benefitTitles_item1'), translate('proLanding.assistant_benefitTitles_item2'), translate('proLanding.assistant_benefitTitles_item3'), translate('proLanding.assistant_benefitTitles_item4')],
      steps: [{ title: translate('proLanding.assistant_steps_item1_title'), text: translate('proLanding.assistant_steps_item1_text') }, { title: translate('proLanding.assistant_steps_item2_title'), text: translate('proLanding.assistant_steps_item2_text') }, { title: translate('proLanding.assistant_steps_item3_title'), text: translate('proLanding.assistant_steps_item3_text') }]
    }
  },
  get myWork(): ProFeatureLandingContent {
    return {
      headline: [translate('proLanding.myWork_headline_item1'), translate('proLanding.myWork_headline_item2')], tags: translate('proLanding.myWork_tags'),
      benefitTitles: [translate('proLanding.myWork_benefitTitles_item1'), translate('proLanding.myWork_benefitTitles_item2'), translate('proLanding.myWork_benefitTitles_item3')],
      steps: [{ title: translate('proLanding.myWork_steps_item1_title'), text: translate('proLanding.myWork_steps_item1_text') }, { title: translate('proLanding.myWork_steps_item2_title'), text: translate('proLanding.myWork_steps_item2_text') }, { title: translate('proLanding.myWork_steps_item3_title'), text: translate('proLanding.myWork_steps_item3_text') }]
    }
  }
}
