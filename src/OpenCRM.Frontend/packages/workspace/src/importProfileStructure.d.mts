import type { OrganizationImportMapping, OrganizationImportProfile } from './api/imports.js'

export function remapImportProfileMappings(
  profile: OrganizationImportProfile,
  tables: OrganizationImportProfile['tables']
): OrganizationImportMapping[] | null
