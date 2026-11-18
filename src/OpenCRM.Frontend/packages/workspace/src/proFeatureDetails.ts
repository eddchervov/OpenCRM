import { translate } from '@opencrm/ui/i18n'
import '@opencrm/workspace/proDetailsMessages'
import { getProjectBrand } from '@opencrm/ui/projectBrand'
export type ProFeatureKey =
  | 'team'
  | 'shifts'
  | 'statistics'
  | 'messages'
  | 'intake'
  | 'albums'
  | 'notifications'
  | 'messageTemplates'
  | 'selfAssignment'
  | 'documentStyling'
  | 'dataImport'
  | 'accountingExport'
  | 'assistant'
  | 'myWork'
  | 'roles'

export type ProFeatureVisual =
  | { type: 'screenshot', image: 'team' | 'intake' | 'statistics', alt: string, viewBox: string, imageWidth: number, imageHeight: number }
  | { type: 'preview', kind: 'albums' | 'messages' | 'assistant' }
  | { type: 'demo', heading: string, rows: readonly { label: string, value: string }[] }

export type ProFeatureDetail = {
  title: string
  description: string
  beta?: boolean
  note?: string
  points: readonly [string, string, string] | readonly [string, string, string, string]
  visual: ProFeatureVisual
}

export const proFeatureDetails: Record<ProFeatureKey, ProFeatureDetail> = {
  get team(): ProFeatureDetail {
    return {
      title: translate('proDetails.team_title'),
      description: translate('proDetails.team_description'),
      points: [
        translate('proDetails.team_points_item1'),
        translate('proDetails.team_points_item2'),
        translate('proDetails.team_points_item3'),
        translate('proDetails.team_points_item4')
      ],
      visual: { type: 'screenshot', image: 'team', alt: translate('proDetails.team_visual_alt'), viewBox: '232 66 1048 590', imageWidth: 1280, imageHeight: 720 }
    }
  },
  get shifts(): ProFeatureDetail {
    return {
      title: translate('proDetails.shifts_title'),
      description: translate('proDetails.shifts_description'),
      points: [translate('proDetails.shifts_points_item1'), translate('proDetails.shifts_points_item2'), translate('proDetails.shifts_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.shifts_visual_heading'), rows: [{ label: translate('proDetails.shifts_visual_rows_item1_label'), value: translate('proDetails.shifts_visual_rows_item1_value') }, { label: translate('proDetails.shifts_visual_rows_item2_label'), value: translate('proDetails.shifts_visual_rows_item2_value') }, { label: translate('proDetails.shifts_visual_rows_item3_label'), value: translate('proDetails.shifts_visual_rows_item3_value') }] }
    }
  },
  get statistics(): ProFeatureDetail {
    return {
      title: translate('proDetails.statistics_title'),
      description: translate('proDetails.statistics_description'),
      points: [translate('proDetails.statistics_points_item1'), translate('proDetails.statistics_points_item2'), translate('proDetails.statistics_points_item3')],
      visual: { type: 'screenshot', image: 'statistics', alt: translate('proDetails.statistics_visual_alt'), viewBox: '232 66 1033 646', imageWidth: 1265, imageHeight: 712 }
    }
  },
  get messages(): ProFeatureDetail {
    return {
      title: translate('proDetails.messages_title'),
      description: translate('proDetails.messages_description'),
      points: [translate('proDetails.messages_points_item1'), translate('proDetails.messages_points_item2'), translate('proDetails.messages_points_item3'), translate('proDetails.messages_points_item4')],
      visual: { type: 'preview', kind: 'messages' }
    }
  },
  get intake(): ProFeatureDetail {
    return {
      title: translate('proDetails.intake_title'),
      description: translate('proDetails.intake_description'),
      points: [translate('proDetails.intake_points_item1'), translate('proDetails.intake_points_item2'), translate('proDetails.intake_points_item3')],
      visual: { type: 'screenshot', image: 'intake', alt: translate('proDetails.intake_visual_alt'), viewBox: '279 323 619 361', imageWidth: 1265, imageHeight: 712 }
    }
  },
  get albums(): ProFeatureDetail {
    return {
      title: translate('proDetails.albums_title'),
      description: translate('proDetails.albums_description'),
      points: [translate('proDetails.albums_points_item1'), translate('proDetails.albums_points_item2'), translate('proDetails.albums_points_item3')],
      visual: { type: 'preview', kind: 'albums' }
    }
  },
  get notifications(): ProFeatureDetail {
    return {
      title: translate('proDetails.notifications_title'),
      description: translate('proDetails.notifications_description'),
      points: [translate('proDetails.notifications_points_item1'), translate('proDetails.notifications_points_item2'), translate('proDetails.notifications_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.notifications_visual_heading'), rows: [{ label: translate('proDetails.notifications_visual_rows_item1_label'), value: translate('proDetails.notifications_visual_rows_item1_value') }, { label: translate('proDetails.notifications_visual_rows_item2_label'), value: translate('proDetails.notifications_visual_rows_item2_value') }, { label: translate('proDetails.notifications_visual_rows_item3_label'), value: translate('proDetails.notifications_visual_rows_item3_value') }] }
    }
  },
  get messageTemplates(): ProFeatureDetail {
    return {
      title: translate('proDetails.messageTemplates_title'),
      description: translate('proDetails.messageTemplates_description'),
      points: [translate('proDetails.messageTemplates_points_item1'), translate('proDetails.messageTemplates_points_item2'), translate('proDetails.messageTemplates_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.messageTemplates_visual_heading'), rows: [{ label: translate('proDetails.messageTemplates_visual_rows_item1_label'), value: translate('proDetails.messageTemplates_visual_rows_item1_value') }, { label: translate('proDetails.messageTemplates_visual_rows_item2_label'), value: translate('proDetails.messageTemplates_visual_rows_item2_value') }, { label: translate('proDetails.messageTemplates_visual_rows_item3_label'), value: translate('proDetails.messageTemplates_visual_rows_item3_value') }] }
    }
  },
  get selfAssignment(): ProFeatureDetail {
    return {
      title: translate('proDetails.selfAssignment_title'),
      description: translate('proDetails.selfAssignment_description'),
      points: [translate('proDetails.selfAssignment_points_item1'), translate('proDetails.selfAssignment_points_item2'), translate('proDetails.selfAssignment_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.selfAssignment_visual_heading'), rows: [{ label: translate('proDetails.selfAssignment_visual_rows_item1_label'), value: translate('proDetails.selfAssignment_visual_rows_item1_value') }, { label: translate('proDetails.selfAssignment_visual_rows_item2_label'), value: translate('proDetails.selfAssignment_visual_rows_item2_value') }, { label: translate('proDetails.selfAssignment_visual_rows_item3_label'), value: translate('proDetails.selfAssignment_visual_rows_item3_value') }] }
    }
  },
  get documentStyling(): ProFeatureDetail {
    return {
      title: translate('proDetails.documentStyling_title'),
      description: translate('proDetails.documentStyling_description'),
      points: [translate('proDetails.documentStyling_points_item1'), translate('proDetails.documentStyling_points_item2'), translate('proDetails.documentStyling_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.documentStyling_visual_heading'), rows: [{ label: getProjectBrand(), value: translate('proDetails.documentStyling_visual_rows_item1_value') }, { label: translate('proDetails.documentStyling_visual_rows_item2_label'), value: translate('proDetails.documentStyling_visual_rows_item2_value') }, { label: translate('proDetails.documentStyling_visual_rows_item3_label'), value: translate('proDetails.documentStyling_visual_rows_item3_value') }] }
    }
  },
  get dataImport(): ProFeatureDetail {
    return {
      title: translate('proDetails.dataImport_title'),
      description: translate('proDetails.dataImport_description'),
      points: [translate('proDetails.dataImport_points_item1'), translate('proDetails.dataImport_points_item2'), translate('proDetails.dataImport_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.dataImport_visual_heading'), rows: [{ label: translate('proDetails.dataImport_visual_rows_item1_label'), value: translate('proDetails.dataImport_visual_rows_item1_value') }, { label: translate('proDetails.dataImport_visual_rows_item2_label'), value: translate('proDetails.dataImport_visual_rows_item2_value') }, { label: translate('proDetails.dataImport_visual_rows_item3_label'), value: translate('proDetails.dataImport_visual_rows_item3_value') }] }
    }
  },
  get accountingExport(): ProFeatureDetail {
    return {
      title: translate('proDetails.accountingExport_title'),
      description: translate('proDetails.accountingExport_description'),
      points: [translate('proDetails.accountingExport_points_item1'), translate('proDetails.accountingExport_points_item2'), translate('proDetails.accountingExport_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.accountingExport_visual_heading'), rows: [{ label: translate('proDetails.accountingExport_visual_rows_item1_label'), value: translate('proDetails.accountingExport_visual_rows_item1_value') }, { label: translate('proDetails.accountingExport_visual_rows_item2_label'), value: translate('proDetails.accountingExport_visual_rows_item2_value') }, { label: translate('proDetails.accountingExport_visual_rows_item3_label'), value: translate('proDetails.accountingExport_visual_rows_item3_value') }] }
    }
  },
  get assistant(): ProFeatureDetail {
    return {
      title: translate('proDetails.assistant_title'),
      description: translate('proDetails.assistant_description'),
      beta: true,
      note: translate('proDetails.assistant_note'),
      points: [translate('proDetails.assistant_points_item1'), translate('proDetails.assistant_points_item2'), translate('proDetails.assistant_points_item3'), translate('proDetails.assistant_points_item4')],
      visual: { type: 'preview', kind: 'assistant' }
    }
  },
  get myWork(): ProFeatureDetail {
    return {
      title: translate('proDetails.myWork_title'),
      description: translate('proDetails.myWork_description'),
      points: [translate('proDetails.myWork_points_item1'), translate('proDetails.myWork_points_item2'), translate('proDetails.myWork_points_item3')],
      visual: { type: 'demo', heading: translate('proDetails.myWork_visual_heading'), rows: [{ label: translate('proDetails.myWork_visual_rows_item1_label'), value: translate('proDetails.myWork_visual_rows_item1_value') }, { label: translate('proDetails.myWork_visual_rows_item2_label'), value: translate('proDetails.myWork_visual_rows_item2_value') }, { label: translate('proDetails.myWork_visual_rows_item3_label'), value: translate('proDetails.myWork_visual_rows_item3_value') }] }
    }
  },
  get roles(): ProFeatureDetail {
    return {
      title: translate('proDetails.roles_title'),
      description: translate('proDetails.roles_description'),
      points: [
        translate('proDetails.roles_points_item1'),
        translate('proDetails.roles_points_item2'),
        translate('proDetails.roles_points_item3')
      ],
      visual: { type: 'screenshot', image: 'team', alt: translate('proDetails.roles_visual_alt'), viewBox: '232 66 1048 590', imageWidth: 1280, imageHeight: 720 }
    }
  }
}
