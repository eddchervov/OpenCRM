<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ApiError } from '@/api/ApiError'
import { updateAdminRolePermissions } from '@/api/adminPermissionsApi'
import { addAdminRoleMember, getAdminRoleMembers, removeAdminRoleMember } from '@/api/adminRolesApi'
import type { AdminRoleMember } from '@/models/AdminRoleMember'
import type { AdminRoleMembers } from '@/models/AdminRoleMembers'
import { useSystemAccessStore } from '@/stores/systemAccessStore'

const systemAccessStore = useSystemAccessStore()
const selectedRoleId = ref('')
const selectedPermissionCodes = ref<string[]>([])
const isSaving = ref(false)
const successMessage = ref<string | null>(null)
const errorMessage = ref<string | null>(null)
const activeMode = ref<'permissions' | 'members'>('permissions')
const roleMembers = ref<AdminRoleMembers | null>(null)
const memberSearch = ref('')
const selectedUserId = ref('')
const isMembersLoading = ref(false)
const isMemberSaving = ref(false)
const memberToRemove = ref<AdminRoleMember | null>(null)

const permissionDependencies: Record<string, string[]> = {
  'Admin.Users.Manage': ['Admin.Users.View'],
  'Admin.Users.Impersonate': ['Admin.Users.View'],
  'Admin.Billing.Manage': ['Admin.Billing.View'],
  'Admin.Content.Manage': ['Admin.Content.View'],
  'Admin.Communications.Manage': ['Admin.Communications.View']
}

onMounted(() => {
  void systemAccessStore.load()
})

const data = computed(() => systemAccessStore.permissions)
const roles = computed(() => [...(data.value?.roles ?? [])].sort((firstRole, secondRole) => {
  const permissionCountDifference = secondRole.permissionCodes.length - firstRole.permissionCodes.length
  if (permissionCountDifference !== 0) {
    return permissionCountDifference
  }

  if (firstRole.isLocked !== secondRole.isLocked) {
    return firstRole.isLocked ? -1 : 1
  }

  return firstRole.roleName.localeCompare(secondRole.roleName, 'ru')
}))
const selectedRole = computed(() =>
  roles.value.find(role => role.roleId === selectedRoleId.value) ?? null
)
const permissionGroups = computed(() => {
  const groups = new Map<string, NonNullable<typeof data.value>['permissions']>()

  for (const permission of data.value?.permissions ?? []) {
    const permissions = groups.get(permission.group) ?? []
    permissions.push(permission)
    groups.set(permission.group, permissions)
  }

  return [...groups.entries()].map(([name, permissions]) => ({ name, permissions }))
})
const assignedMembers = computed(() => {
  const query = memberSearch.value.trim().toLowerCase()

  return (roleMembers.value?.users ?? [])
    .filter(user => user.hasRole)
    .filter(user => !query || [user.displayName, user.email, user.phone]
      .some(value => value?.toLowerCase().includes(query)))
})
const availableUsers = computed(() => {
  const query = memberSearch.value.trim().toLowerCase()

  return (roleMembers.value?.users ?? [])
    .filter(user => !user.hasRole)
    .filter(user => !query || [user.displayName, user.email, user.phone]
      .some(value => value?.toLowerCase().includes(query)))
})

watch(
  roles,
  roles => {
    if (!roles?.length) {
      selectedRoleId.value = ''
      return
    }

    if (!roles.some(role => role.roleId === selectedRoleId.value)) {
      selectedRoleId.value = roles[0].roleId
    }
  },
  { immediate: true }
)

watch(
  selectedRole,
  role => {
    selectedPermissionCodes.value = [...(role?.permissionCodes ?? [])]
    successMessage.value = null
    errorMessage.value = null
  },
  { immediate: true }
)

watch([selectedRoleId, activeMode], ([roleId, mode]) => {
  selectedUserId.value = ''
  memberSearch.value = ''

  if (mode === 'members' && roleId) {
    void loadMembers()
  }
})

async function savePermissions(): Promise<void> {
  const role = selectedRole.value
  if (!role || role.isLocked) {
    return
  }

  isSaving.value = true
  successMessage.value = null
  errorMessage.value = null

  try {
    const response = await updateAdminRolePermissions(role.roleId, selectedPermissionCodes.value)
    role.permissionCodes = [...selectedPermissionCodes.value]
    successMessage.value = response.message
  } catch (error) {
    errorMessage.value = error instanceof ApiError
      ? error.message
      : 'Не удалось сохранить права роли.'
  } finally {
    isSaving.value = false
  }
}

function togglePermission(permissionCode: string, event: Event): void {
  const isChecked = (event.target as HTMLInputElement).checked
  const permissionCodes = new Set(selectedPermissionCodes.value)

  if (isChecked) {
    addPermissionWithDependencies(permissionCodes, permissionCode)
  } else {
    permissionCodes.delete(permissionCode)

    for (const code of [...permissionCodes]) {
      if (getPermissionDependencies(code).includes(permissionCode)) {
        permissionCodes.delete(code)
      }
    }
  }

  selectedPermissionCodes.value = [...permissionCodes]
}

function addPermissionWithDependencies(permissionCodes: Set<string>, permissionCode: string): void {
  permissionCodes.add(permissionCode)

  for (const dependency of getPermissionDependencies(permissionCode)) {
    permissionCodes.add(dependency)
  }
}

function getPermissionDependencies(permissionCode: string): string[] {
  if (permissionCode === 'Admin.Access') {
    return []
  }

  return ['Admin.Access', ...(permissionDependencies[permissionCode] ?? [])]
}

async function loadMembers(): Promise<void> {
  if (!selectedRoleId.value) {
    return
  }

  isMembersLoading.value = true
  errorMessage.value = null

  try {
    roleMembers.value = await getAdminRoleMembers(selectedRoleId.value)
  } catch (error) {
    roleMembers.value = null
    errorMessage.value = error instanceof ApiError ? error.message : 'Не удалось загрузить участников роли.'
  } finally {
    isMembersLoading.value = false
  }
}

async function addMember(): Promise<void> {
  if (!selectedRoleId.value || !selectedUserId.value) {
    return
  }

  await runMemberOperation(() => addAdminRoleMember(selectedRoleId.value, selectedUserId.value))
  selectedUserId.value = ''
}

async function removeMember(userId: string): Promise<void> {
  await runMemberOperation(() => removeAdminRoleMember(selectedRoleId.value, userId))
  memberToRemove.value = null
}

async function runMemberOperation(operation: () => Promise<{ message: string }>): Promise<void> {
  isMemberSaving.value = true
  successMessage.value = null
  errorMessage.value = null

  try {
    const response = await operation()
    await loadMembers()
    successMessage.value = response.message
  } catch (error) {
    errorMessage.value = error instanceof ApiError ? error.message : 'Не удалось изменить участников роли.'
  } finally {
    isMemberSaving.value = false
  }
}
</script>

<template>
  <section class="roles-page">
    <header class="page-heading">
      <h1>Административные роли</h1>
    </header>

    <div
      v-if="systemAccessStore.accessState === 'loading'"
      class="panel-state"
    >
      Загружаем роли и разрешения…
    </div>

    <div
      v-else-if="systemAccessStore.errorMessage"
      class="panel-state panel-state--error"
    >
      {{ systemAccessStore.errorMessage }}
    </div>

    <div
      v-else-if="data"
      class="roles-editor"
    >
      <aside class="roles-list">
        <div class="roles-list__title">Роли</div>
        <button
          v-for="role in roles"
          :key="role.roleId"
          type="button"
          class="role-button"
          :class="{ 'role-button--active': role.roleId === selectedRoleId }"
          @click="selectedRoleId = role.roleId"
        >
          <span>{{ role.roleName }}</span>
          <small v-if="role.isLocked">Системная</small>
          <small v-else>{{ role.permissionCodes.length }} разрешений</small>
        </button>
      </aside>

      <form
        v-if="selectedRole"
        class="permissions-form"
        @submit.prevent="activeMode === 'permissions' ? savePermissions() : addMember()"
      >
        <div class="role-editor-toolbar">
          <div class="role-editor-tabs">
            <button type="button" :class="{ active: activeMode === 'permissions' }" @click="activeMode = 'permissions'">Права</button>
            <button type="button" :class="{ active: activeMode === 'members' }" @click="activeMode = 'members'">Участники</button>
          </div>
          <button
            v-if="activeMode === 'permissions'"
            type="submit"
            class="primary-button"
            :disabled="selectedRole.isLocked || isSaving"
          >
            {{ isSaving ? 'Сохраняем…' : 'Сохранить' }}
          </button>
        </div>

        <p
          v-if="successMessage"
          class="form-message form-message--success"
        >
          {{ successMessage }}
        </p>
        <p
          v-if="errorMessage"
          class="form-message form-message--error"
        >
          {{ errorMessage }}
        </p>

        <fieldset
          v-if="activeMode === 'permissions'"
          v-for="group in permissionGroups"
          :key="group.name"
          class="permission-group"
          :disabled="selectedRole.isLocked"
        >
          <legend>{{ group.name }}</legend>
          <label
            v-for="permission in group.permissions"
            :key="permission.code"
            class="permission-option"
          >
            <input
              type="checkbox"
              :value="permission.code"
              :checked="selectedPermissionCodes.includes(permission.code)"
              @change="togglePermission(permission.code, $event)"
            >
            <strong>{{ permission.name }}</strong>
          </label>
        </fieldset>

        <div v-if="activeMode === 'members'" class="role-members">
          <div class="role-members__toolbar">
            <input v-model="memberSearch" type="search" placeholder="Email или телефон">
            <SearchSelect
              v-model="selectedUserId"
              :disabled="isMembersLoading || availableUsers.length === 0"
              :options="availableUsers.map(user => ({ value: user.userId, label: user.displayName }))"
              placeholder="Выберите пользователя"
            />
            <button class="primary-button" type="submit" :disabled="isMembersLoading || isMemberSaving || !selectedUserId">Назначить</button>
          </div>

          <div v-if="isMembersLoading" class="inline-empty">Загрузка…</div>
          <div v-else-if="assignedMembers.length" class="table-wrap">
            <table class="data-table">
              <thead><tr><th>Пользователь</th><th>Телефон</th><th></th></tr></thead>
              <tbody>
                <tr v-for="user in assignedMembers" :key="user.userId">
                  <td><strong>{{ user.email || user.displayName }}</strong></td>
                  <td>{{ user.phone || '—' }}</td>
                  <td><button class="table-action danger-link" type="button" :disabled="isMemberSaving || (user.isCurrentUser && roleMembers?.isProtected)" @click="memberToRemove = user">Снять роль</button></td>
                </tr>
              </tbody>
            </table>
          </div>
          <div v-else class="inline-empty">В этой роли нет пользователей.</div>
        </div>
      </form>
    </div>

    <div v-else class="panel-state">
      Нет данных о ролях и разрешениях.
    </div>

    <div v-if="memberToRemove && selectedRole" class="modal-backdrop" @click.self="memberToRemove = null">
      <div class="modal-card modal-card--compact">
        <div class="modal-heading">
          <strong>Снять роль</strong>
          <button type="button" :disabled="isMemberSaving" @click="memberToRemove = null">×</button>
        </div>
        <p>{{ memberToRemove.displayName }} · {{ selectedRole.roleName }}</p>
        <div class="modal-actions">
          <button class="danger-button" type="button" :disabled="isMemberSaving" @click="removeMember(memberToRemove.userId)">Снять роль</button>
          <button class="secondary-button" type="button" :disabled="isMemberSaving" @click="memberToRemove = null">Отмена</button>
        </div>
      </div>
    </div>

  </section>
</template>
