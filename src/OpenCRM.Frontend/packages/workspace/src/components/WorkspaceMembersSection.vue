<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../permissionMessages'
const { t } = useLocalization()
import WorkspaceRolePermissions from './WorkspaceRolePermissions.vue'
import type { WorkspaceMembersData } from '../api/workspaceAdmin'

defineProps<{
  preferredRoleCode: string
  isReadOnly: boolean
  members: Pick<WorkspaceMembersData, 'rolePermissionMatrix' | 'canManageRoles'>
  isSaving: boolean
  isEditingPermissions: boolean
  permissionDrafts: Record<string, string[]>
  previewOnly?: boolean
}>()

defineEmits<{
  startPermissionsEdit: []
  cancelPermissionsEdit: []
  togglePermission: [roleCode: string, permissionCode: string, checked: boolean]
  savePermissions: [roleCode: string]
}>()
</script>

<template>
  <div class="workspace-common-stack">
    <a href="#employees">{{ t('rolePermissions.backToTeam') }}</a>
    <WorkspaceRolePermissions :roles="members.rolePermissionMatrix" :preferred-role-code="preferredRoleCode"
      :can-manage="members.canManageRoles && !isReadOnly" :is-editing="isEditingPermissions"
      :is-saving="isSaving" :drafts="permissionDrafts" :preview-only="previewOnly"
      @start-edit="$emit('startPermissionsEdit')" @cancel-edit="$emit('cancelPermissionsEdit')"
      @toggle="(role, code, checked) => $emit('togglePermission', role, code, checked)"
      @save="$emit('savePermissions', $event)" />
  </div>
</template>
