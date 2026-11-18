<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../permissionMessages'
import '../employeeMessages'
import { computed, ref } from 'vue'
import { UiButton, UiInput } from '@opencrm/ui'
import type { WorkspaceRolePermissionMatrix } from '../api/workspaceAdmin'

const { t, te, locale } = useLocalization()

const props = defineProps<{
  preferredRoleCode?: string
  roles: WorkspaceRolePermissionMatrix[]
  canManage: boolean
  isEditing: boolean
  isSaving: boolean
  drafts: Record<string, string[]>
  previewOnly?: boolean
}>()
const emit = defineEmits<{
  startEdit: []
  cancelEdit: []
  toggle: [roleCode: string, permissionCode: string, checked: boolean]
  save: [roleCode: string]
}>()
const selectedCode = ref(props.preferredRoleCode ?? '')
const search = ref('')
const selectedRole = computed(() => props.roles.find(role => role.roleCode === selectedCode.value)
  ?? props.roles.find(role => role.canEdit) ?? props.roles[0])
const canEdit = computed(() => !props.previewOnly && props.canManage && selectedRole.value?.canEdit === true)
const enabledCodes = computed(() => new Set(props.isEditing && canEdit.value
  ? props.drafts[selectedRole.value!.roleCode] ?? []
  : selectedRole.value?.permissions.filter(permission => permission.isEnabled).map(permission => permission.code) ?? []))
const changedCount = computed(() => selectedRole.value?.permissions.filter(permission =>
  enabledCodes.value.has(permission.code) !== permission.isEnabled).length ?? 0)
const enabledCount = computed(() => selectedRole.value?.permissions.filter(permission => enabledCodes.value.has(permission.code)).length ?? 0)
const groups = computed(() => {
  const result = new Map<string, WorkspaceRolePermissionMatrix['permissions']>()
  const query = search.value.trim().toLocaleLowerCase(locale.value)
  for (const original of selectedRole.value?.permissions ?? []) {
    const permission = { ...original, name: permissionText(original, 'name'), description: permissionText(original, 'description'), groupName: permissionText(original, 'group') }
    if (query && ![permission.name, permission.description, permission.groupName]
      .some(value => value.toLocaleLowerCase(locale.value).includes(query))) continue
    const name = permission.groupName || t('rolePermissions.general')
    if (!result.has(name)) result.set(name, [])
    result.get(name)!.push(permission)
  }
  return Array.from(result, ([name, permissions]) => ({ name, permissions }))
})

function permissionText(permission: WorkspaceRolePermissionMatrix['permissions'][number], field: 'name' | 'description' | 'group') {
  const key = `rolePermissions.entries.${permission.code.replaceAll('.', '_')}.${field}`

  return te(key) ? t(key) : field === 'group' ? permission.groupName : permission[field]
}

function roleName(role: WorkspaceRolePermissionMatrix) {
  const key = `employees.roles.${role.roleCode}`

  return te(key) ? t(key) : role.roleName
}

function selectRole(code: string) {
  if (props.previewOnly || props.isSaving || props.isEditing) return
  selectedCode.value = code
  search.value = ''
}

function toggle(code: string, checked: boolean) {
  if (!props.isEditing || !canEdit.value || props.isSaving || code === 'workspace.view') return
  if (!selectedRole.value?.permissions.some(permission => permission.code === code)) return
  emit('toggle', selectedRole.value.roleCode, code, checked)
}

function save() {
  if (!props.isEditing || !canEdit.value || props.isSaving || !changedCount.value) return
  emit('save', selectedRole.value!.roleCode)
}
</script>

<template>
  <section class="role-permissions" aria-labelledby="role-permissions-title">
    <div class="role-permissions__heading ui-form-header">
      <div><h3 id="role-permissions-title">{{ t('rolePermissions.title') }}</h3><p>{{ t('rolePermissions.hint') }}</p></div>
    </div>
    <div v-if="previewOnly" class="role-permissions__layout" aria-hidden="true">
      <nav class="role-permissions__roles" :aria-label="t('rolePermissions.chooseRole')">
        <button v-for="index in 4" :key="index" type="button" disabled><i class="role-permissions__preview-bar"></i><i class="role-permissions__preview-bar"></i></button>
      </nav>
      <div class="role-permissions__content">
        <div class="role-permissions__toolbar ui-section-header"><h4>{{ t('rolePermissions.rolePermissions') }}</h4><UiButton variant="secondary" disabled>{{ t('rolePermissions.configure') }}</UiButton></div>
        <UiInput v-model="search" :aria-label="t('rolePermissions.search')" :placeholder="t('rolePermissions.searchPlaceholder')" disabled />
        <section v-for="group in [t('rolePermissions.bookings'), t('rolePermissions.clients'), t('rolePermissions.vehicles')]" :key="group" class="role-permissions__group">
          <h5 class="ui-section-header">{{ group }}</h5>
          <div v-for="index in 3" :key="index" class="role-permissions__row">
            <span class="role-permissions__description"><i class="role-permissions__preview-bar"></i><i class="role-permissions__preview-bar"></i></span>
            <input type="checkbox" disabled />
          </div>
        </section>
      </div>
    </div>
    <p v-else-if="!roles.length" class="role-permissions__empty">{{ t('rolePermissions.noRoles') }}</p>
    <div v-else class="role-permissions__layout">
      <nav class="role-permissions__roles" :aria-label="t('rolePermissions.chooseRole')">
        <button v-for="role in roles" :key="role.roleCode" type="button"
          :class="{ 'is-selected': selectedRole?.roleCode === role.roleCode }"
          :aria-pressed="selectedRole?.roleCode === role.roleCode"
          :disabled="isSaving || (isEditing && selectedRole?.roleCode !== role.roleCode)"
          @click="selectRole(role.roleCode)">
          <strong>{{ roleName(role) }}</strong>
          <span>{{ t('rolePermissions.count', { count: role.permissions.filter(permission => permission.isEnabled).length, total: role.permissions.length }) }}</span>
        </button>
        <p v-if="isEditing" class="role-permissions__hint">{{ t('rolePermissions.editingHint') }}</p>
      </nav>
      <div v-if="selectedRole" class="role-permissions__content">
        <div class="role-permissions__toolbar ui-section-header">
          <div><h4>{{ roleName(selectedRole) }}</h4><span>{{ t('rolePermissions.enabledCount', { count: enabledCount, total: selectedRole.permissions.length }) }}</span></div>
          <UiButton v-if="canEdit && !isEditing" variant="secondary" :disabled="isSaving" @click="emit('startEdit')">{{ t('rolePermissions.configure') }}</UiButton>
          <span v-else-if="!selectedRole.canEdit" class="role-permissions__badge">{{ t('rolePermissions.systemRole') }}</span>
          <span v-else-if="!canManage" class="role-permissions__badge">{{ t('rolePermissions.readOnly') }}</span>
        </div>
        <UiInput v-model="search" :aria-label="t('rolePermissions.search')" :placeholder="t('rolePermissions.searchPlaceholder')" />
        <p v-if="!groups.length" class="role-permissions__empty">{{ t('rolePermissions.noResults') }}</p>
        <section v-for="group in groups" :key="group.name" class="role-permissions__group">
          <h5 class="ui-section-header">{{ group.name }} <span>{{ group.permissions.filter(permission => enabledCodes.has(permission.code)).length }} / {{ group.permissions.length }}</span></h5>
          <div v-for="permission in group.permissions" :key="permission.code" class="role-permissions__row">
            <label v-if="isEditing && canEdit" class="role-permissions__check">
              <input type="checkbox" :checked="enabledCodes.has(permission.code)" :disabled="isSaving || permission.code === 'workspace.view'"
                @change="toggle(permission.code, ($event.target as HTMLInputElement).checked)" />
              <span><strong>{{ permission.name }}</strong><small v-if="permission.description">{{ permission.description }}</small><small v-if="permission.code === 'workspace.view'">{{ t('rolePermissions.required') }}</small></span>
            </label>
            <div v-else class="role-permissions__description"><strong>{{ permission.name }}</strong><small v-if="permission.description">{{ permission.description }}</small></div>
            <span v-if="!isEditing || !canEdit" class="role-permissions__status" :class="{ 'is-enabled': enabledCodes.has(permission.code) }">{{ enabledCodes.has(permission.code) ? t('rolePermissions.allowed') : t('rolePermissions.denied') }}</span>
          </div>
        </section>
        <footer v-if="isEditing && canEdit" class="role-permissions__footer">
          <span>{{ changedCount ? t('rolePermissions.changed', { count: changedCount }) : t('rolePermissions.noChanges') }}</span>
          <div><UiButton variant="secondary" :disabled="isSaving" @click="emit('cancelEdit')">{{ t('rolePermissions.cancel') }}</UiButton><UiButton :disabled="isSaving || !changedCount" @click="save">{{ isSaving ? t('rolePermissions.saving') : t('rolePermissions.save') }}</UiButton></div>
        </footer>
      </div>
    </div>
  </section>
</template>

<style scoped>
.role-permissions { border: 1px solid var(--crm-line, #dce2e9); border-radius: 10px; background: var(--crm-surface, #fff); min-width: 0; }
.role-permissions__heading { padding: 18px 20px; border-bottom: 1px solid var(--crm-form-header-border, #dce2e9); }
.role-permissions h3, .role-permissions h4, .role-permissions h5, .role-permissions p { margin: 0; }
.role-permissions h3 { font-size: 16px; }
.role-permissions__heading p, .role-permissions__hint { margin-top: 6px; font-size: 12px; color: var(--crm-text-muted, #687386); }
.role-permissions__layout { display: grid; grid-template-columns: 220px minmax(0, 1fr); }
.role-permissions__roles { display: flex; flex-direction: column; gap: 8px; padding: 16px; border-right: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f4f6f8); }
.role-permissions__roles button { display: grid; gap: 5px; padding: 12px; text-align: left; color: var(--crm-text, #202631); background: transparent; border: 1px solid transparent; border-radius: 7px; font: inherit; cursor: pointer; overflow-wrap: anywhere; }
.role-permissions__roles button.is-selected { border-color: #e87532; background: var(--crm-surface, #fff); }
.role-permissions__roles strong { font-size: 13px; }
.role-permissions__roles span { color: var(--crm-text-muted, #687386); font-size: 12px; }
.role-permissions__roles button:disabled:not(.is-selected) { opacity: .55; cursor: default; }
.role-permissions__content { display: grid; align-content: start; gap: 18px; padding: 20px; min-width: 0; }
.role-permissions__toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.role-permissions h4 { font-size: 16px; margin-bottom: 5px; }
.role-permissions__toolbar span { font-size: 12px; color: var(--crm-text-muted, #687386); }
.role-permissions__badge { border: 1px solid var(--crm-line, #dce2e9); border-radius: 20px; padding: 5px 9px; }
.role-permissions__group { border: 1px solid var(--crm-line, #dce2e9); border-radius: 8px; overflow: hidden; }
.role-permissions h5 { display: flex; justify-content: space-between; gap: 10px; padding: 12px 14px; border: 1px solid var(--crm-section-header-border, #dde3eb); border-radius: 6px; background: var(--crm-section-header-bg, #f4f6f8); font-size: 13px; }
.role-permissions h5 span { color: var(--crm-text-muted, #687386); font-size: 12px; font-weight: 400; white-space: nowrap; }
.role-permissions__row { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 12px 14px; border-top: 1px solid var(--crm-line, #dce2e9); }
.role-permissions__description, .role-permissions__check span { display: grid; gap: 4px; min-width: 0; }
.role-permissions__row strong { font-size: 13px; font-weight: 500; overflow-wrap: anywhere; }
.role-permissions__row small { font-size: 12px; color: var(--crm-text-muted, #687386); overflow-wrap: anywhere; }
.role-permissions__check { display: flex; align-items: start; gap: 10px; width: fit-content; cursor: pointer; }
.role-permissions__check input { margin-top: 3px; flex-shrink: 0; accent-color: #e87532; }
.role-permissions__status { color: var(--crm-text-muted, #687386); font-size: 12px; white-space: nowrap; }
.role-permissions__status.is-enabled { color: var(--crm-success-text, #237269); }
.role-permissions__preview-bar { display: block; width: min(160px, 100%); height: 11px; margin: 5px 0; border-radius: 4px; background: var(--crm-line, #dce2e9); }
.role-permissions__preview-bar + .role-permissions__preview-bar { width: min(100px, 100%); height: 8px; }
.role-permissions__empty { padding: 20px; color: var(--crm-text-muted, #687386); font-size: 13px; }
.role-permissions__footer { position: sticky; bottom: 0; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 0 0; background: var(--crm-surface, #fff); border-top: 1px solid var(--crm-line, #dce2e9); z-index: 1; }
.role-permissions__footer > span { font-size: 12px; color: var(--crm-text-muted, #687386); }
.role-permissions__footer > div { display: flex; gap: 8px; flex-wrap: wrap; }
.role-permissions__footer :deep(.ui-button--primary) { border-color: #e87532; background: #e87532; color: #fff; }
@media (max-width: 900px) {
  .role-permissions__layout { grid-template-columns: minmax(0, 1fr); }
  .role-permissions__roles { flex-direction: row; flex-wrap: wrap; border-right: 0; border-bottom: 1px solid var(--crm-line, #dce2e9); }
  .role-permissions__roles button { flex: 1 1 140px; }
  .role-permissions__hint { flex-basis: 100%; }
}
@media (max-width: 500px) {
  .role-permissions__content, .role-permissions__heading, .role-permissions__roles { padding: 12px; }
  .role-permissions__row { gap: 8px; padding: 10px; align-items: start; }
  .role-permissions__status { font-size: 11px; }
  .role-permissions__footer { flex-wrap: wrap; }
}
</style>
