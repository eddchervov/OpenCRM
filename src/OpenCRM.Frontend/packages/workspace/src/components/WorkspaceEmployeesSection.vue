<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { UiButton, UiInput, UiLoader, UiPhoneInput, UiSelect, UiTooltip } from '@opencrm/ui'
import {
  createEmployeeForMember,
  createEmployeeWithInvitation,
  linkEmployeeMember,
  loadEmployees,
  saveEmployee,
  type EmployeeMemberOption,
  type OrganizationEmployee
} from '../api/employees'
import {
  inviteWorkspaceMember,
  loadWorkspaceMembers,
  revokeWorkspaceInvitation,
  updateWorkspaceMemberRole,
  removeWorkspaceMember,
  transferWorkspaceOwnership,
  type WorkspaceMembersData,
  type WorkspaceOrganizationInvitation,
  type WorkspaceOrganizationMember
} from '../api/workspaceAdmin'
import type { WorkspaceContext } from '../types'

type TeamRow = {
  key: string
  employee?: OrganizationEmployee
  member?: WorkspaceOrganizationMember
  invitation?: WorkspaceOrganizationInvitation
  name: string
  email: string
  role: string
  connection: string
}

const props = withDefaults(defineProps<{ context: WorkspaceContext, previewOnly?: boolean }>(), { previewOnly: false })
const emit = defineEmits<{ 'update:count': [count: number] }>()
const employees = ref<OrganizationEmployee[]>([])
const memberOptions = ref<EmployeeMemberOption[]>([])
const access = ref<WorkspaceMembersData | null>(null)
const canManage = ref(false)
const canLinkMembers = ref(false)
const loading = ref(!props.previewOnly)
const saving = ref(false)
const error = ref('')
const formError = ref('')
const search = ref('')
const teamFilter = ref('all')
const confirmationDialog = ref<HTMLDialogElement | null>(null)
const memberAction = ref<'remove' | 'owner' | 'unlink' | null>(null)
const selected = ref<TeamRow | null>(null)
const addDialog = ref<HTMLDialogElement | null>(null)
const cardDialog = ref<HTMLDialogElement | null>(null)
const addAccess = ref<'none' | 'invite' | 'existing'>('none')
const addDraft = reactive({ name: '', phone: '', email: '', roleCode: 'performer', memberId: '', isActive: true })
const cardDraft = reactive({ name: '', phone: '', isActive: true, email: '', roleCode: 'performer', memberId: '' })
const originalEmployee = ref<OrganizationEmployee | null>(null)
const cardAction = ref<'invite' | 'link' | 'create' | 'attach' | null>(null)

const canWrite = computed(() => canManage.value && props.context.workspaceAccess?.isReadOnly === false)
const canInvite = computed(() => access.value?.canInviteMembers === true && props.context.workspaceAccess?.isReadOnly === false)
const canLink = computed(() => canLinkMembers.value && props.context.workspaceAccess?.isReadOnly === false)
const canManageRoles = computed(() => access.value?.canManageRoles === true && props.context.workspaceAccess?.isReadOnly === false)
const roleOptions = computed(() => (access.value?.roleOptions ?? []).map(role => ({ value: role.code, label: role.name })))
const canViewRoles = computed(() => props.context.shell.menu?.some(item => item.code === 'members' && item.isVisible) === true)
const canRemove = computed(() => access.value?.canRemoveMembers === true && props.context.workspaceAccess?.isReadOnly === false)
const canTransfer = computed(() => canManageRoles.value && access.value?.organization.currentUserRoleCode === 'owner')
const addLabel = computed(() => !canWrite.value ? 'Отправить приглашение'
  : addAccess.value === 'invite' ? 'Добавить и пригласить'
    : addAccess.value === 'existing' ? 'Добавить и связать' : 'Добавить сотрудника')
const filterOptions = computed(() => [
  { value: 'all', label: 'Все сотрудники' },
  { value: 'assignable', label: 'Можно назначать работы' },
  { value: 'not-assignable', label: 'Не назначаются на работы' },
  { value: 'no-access', label: 'Без доступа к CRM' },
  { value: 'invited', label: 'Ожидают принятия' }
].filter(option => canViewRoles.value || !['no-access', 'invited'].includes(option.value)))
const availableMembers = computed(() => {
  const linked = new Set(employees.value.map(item => item.memberId).filter(Boolean))
  return memberOptions.value.filter(item => !linked.has(item.id))
})
const availableEmployees = computed(() => employees.value.filter(item => !item.memberId
  && !access.value?.organization.pendingInvitations.some(invitation => invitation.employeeId === item.id)))
const memberSelectOptions = computed(() => availableMembers.value.map(member => ({
  value: member.id, label: member.email || member.userName || member.id
})))
const employeeSelectOptions = computed(() => availableEmployees.value.map(employee => ({ value: employee.id, label: employee.name })))
const hasUnsavedDetails = computed(() => originalEmployee.value !== null && (
  cardDraft.name !== originalEmployee.value.name
  || cardDraft.phone !== (originalEmployee.value.phone ?? '')
  || cardDraft.isActive !== originalEmployee.value.isActive
))
const hasPendingLink = computed(() => !!selected.value?.employee && !selected.value.employee.memberId
  && !selected.value.invitation && cardAction.value === 'link' && canLink.value && !!cardDraft.memberId)
const canSaveCard = computed(() => canWrite.value && !!cardDraft.name.trim()
  && (hasUnsavedDetails.value || hasPendingLink.value)
  && (cardAction.value !== 'link' || hasPendingLink.value))
const rows = computed<TeamRow[]>(() => {
  const result: TeamRow[] = []
  const members = access.value?.organization.members ?? []
  const invitations = access.value?.organization.pendingInvitations ?? []
  const usedMembers = new Set<string>()
  const usedInvitations = new Set<string>()

  for (const employee of employees.value) {
    const member = members.find(item => item.id === employee.memberId)
    const invitation = invitations.find(item => item.employeeId === employee.id)
    if (member) usedMembers.add(member.id)
    if (invitation) usedInvitations.add(invitation.id)
    result.push({
      key: 'employee-' + employee.id,
      employee,
      member,
      invitation,
      name: employee.name,
      email: member?.email ?? invitation?.email ?? '',
      role: member?.roleName ?? invitation?.roleName ?? '—',
      connection: !canViewRoles.value ? 'Нет данных' : member || employee.memberId ? 'Подключён' : invitation ? 'Ожидает принятия' : 'Не предоставлен'
    })
  }

  for (const member of members) {
    if (usedMembers.has(member.id)) continue
    result.push({
      key: 'member-' + member.id,
      member,
      name: member.email || member.userName || member.userId,
      email: member.email ?? '',
      role: member.roleName,
      connection: 'Подключён'
    })
  }
  for (const invitation of invitations) {
    if (usedInvitations.has(invitation.id)) continue
    result.push({
      key: 'invitation-' + invitation.id,
      invitation,
      name: invitation.email,
      email: invitation.email,
      role: invitation.roleName,
      connection: 'Ожидает принятия'
    })
  }

  const query = search.value.trim().toLocaleLowerCase('ru')
  return result
    .filter(item => !query || [item.name, item.email, item.role, item.connection, item.employee?.phone ?? '']
      .some(value => value.toLocaleLowerCase('ru').includes(query)))
    .filter(item => teamFilter.value === 'all'
      || (teamFilter.value === 'assignable' && item.employee?.isActive === true)
      || (teamFilter.value === 'not-assignable' && item.employee?.isActive !== true)
      || (teamFilter.value === 'no-access' && !item.member && !item.employee?.memberId && !item.invitation)
      || (teamFilter.value === 'invited' && !!item.invitation))
    .sort((left, right) => left.name.localeCompare(right.name, 'ru'))
})

async function load() {
  if (props.previewOnly) return

  loading.value = true
  error.value = ''
  const canViewMembers = props.context.shell.menu?.some(item => item.code === 'members' && item.isVisible) === true
  const [employeesResult, accessResult] = await Promise.allSettled([
    loadEmployees(props.context.organization.id),
    canViewMembers ? loadWorkspaceMembers(props.context.organization.id) : Promise.resolve(null)
  ])
  if (employeesResult.status === 'fulfilled') {
    employees.value = employeesResult.value.items
    memberOptions.value = employeesResult.value.members
    canManage.value = employeesResult.value.canManage
    canLinkMembers.value = employeesResult.value.canLinkMembers
  } else {
    employees.value = []
    memberOptions.value = []
    canManage.value = false
    canLinkMembers.value = false
    const status = (employeesResult.reason as Error & { status?: number })?.status
    if (status !== 403 || !canViewMembers) error.value = 'Не удалось загрузить сотрудников. Попробуйте ещё раз.'
  }
  access.value = accessResult.status === 'fulfilled' ? accessResult.value : null
  if (accessResult.status === 'rejected') error.value = 'Не удалось загрузить пользователей CRM. Попробуйте ещё раз.'
  loading.value = false
}

function openAdd() {
  if (saving.value || (!canWrite.value && !canInvite.value)) return
  addAccess.value = canWrite.value ? 'none' : 'invite'
  Object.assign(addDraft, { name: '', phone: '', email: '', roleCode: canWrite.value ? 'performer' : 'employee', memberId: '', isActive: true })
  formError.value = ''
  addDialog.value?.showModal()
}

function changeAddAccess(enabled: boolean) {
  if (saving.value) return
  addAccess.value = enabled ? (canInvite.value ? 'invite' : 'existing') : 'none'
}

function openCard(row: TeamRow) {
  selected.value = row
  originalEmployee.value = row.employee ? { ...row.employee } : null
  Object.assign(cardDraft, {
    name: row.employee?.name ?? '',
    phone: row.employee?.phone ?? '',
    isActive: row.employee?.isActive ?? true,
    email: row.email,
    roleCode: row.member?.roleCode ?? row.invitation?.roleCode ?? 'performer',
    memberId: row.employee?.memberId ?? ''
  })
  cardAction.value = null
  formError.value = ''
  cardDialog.value?.showModal()
}

function closeCard() {
  cardDialog.value?.close()
  selected.value = null
}

async function complete(action: () => Promise<unknown>, close: () => void, fallback: string) {
  if (props.previewOnly || saving.value) return
  saving.value = true
  formError.value = ''
  try {
    await action()
    close()
    await load()
  } catch (cause) {
    formError.value = cause instanceof Error ? cause.message : fallback
  } finally {
    saving.value = false
  }
}

async function addPerson() {
  if (!canWrite.value) {
    if (!canInvite.value || !addDraft.email.trim()) return
    await complete(
      () => inviteWorkspaceMember(props.context.organization.id, {
        email: addDraft.email.trim(), roleCode: addDraft.roleCode
      }),
      () => addDialog.value?.close(),
      'Не удалось пригласить пользователя CRM.'
    )
    return
  }
  if (!canWrite.value || !addDraft.name.trim()) return
  if (addAccess.value === 'invite' && (!canInvite.value || !addDraft.email.trim())) return
  if (addAccess.value === 'existing' && (!canLink.value || !addDraft.memberId)) return
  const employee = {
    id: '', name: addDraft.name.trim(), phone: addDraft.phone,
    memberId: null, isActive: addDraft.isActive, version: 0
  }
  await complete(async () => {
    if (addAccess.value === 'invite') {
      await createEmployeeWithInvitation(props.context.organization.id, {
        name: employee.name, phone: employee.phone, email: addDraft.email.trim(), roleCode: addDraft.roleCode, isActive: employee.isActive
      })
    } else if (addAccess.value === 'existing') {
      await createEmployeeForMember(props.context.organization.id, {
        memberId: addDraft.memberId, name: employee.name, phone: employee.phone, isActive: employee.isActive
      })
    } else {
      await saveEmployee(props.context.organization.id, employee)
    }
  }, () => addDialog.value?.close(), 'Не удалось добавить человека.')
}

async function saveCard() {
  const row = selected.value
  if (!row?.employee || !canSaveCard.value) return
  await complete(
    () => hasUnsavedDetails.value ? saveEmployee(props.context.organization.id, {
      ...row.employee!, name: cardDraft.name.trim(), phone: cardDraft.phone, isActive: cardDraft.isActive,
      ...(hasPendingLink.value ? { memberIdToLink: cardDraft.memberId } : {})
    }) : linkEmployeeMember(props.context.organization.id, row.employee!, cardDraft.memberId),
    closeCard,
    'Не удалось сохранить карточку.'
  )
}

async function applyCardAction() {
  const row = selected.value
  if (!row || !cardAction.value) return
  if (row.employee) {
    if (hasUnsavedDetails.value) {
      formError.value = 'Сначала сохраните изменения карточки.'
      return
    }
    if (cardAction.value === 'invite' && canInvite.value && cardDraft.email.trim()) {
      const emailInput = cardDialog.value?.querySelector<HTMLInputElement>('input[type="email"]')
      if (emailInput && !emailInput.reportValidity()) return
      await complete(
        () => inviteWorkspaceMember(props.context.organization.id, {
          email: cardDraft.email.trim(), roleCode: cardDraft.roleCode, employeeId: row.employee!.id
        }),
        closeCard,
        'Не удалось пригласить пользователя.'
      )
    }
    return
  }
  if (row.member && canLink.value) {
    if (cardAction.value === 'create' && cardDraft.name.trim()) {
      await complete(
        () => createEmployeeForMember(props.context.organization.id, {
          memberId: row.member!.id, name: cardDraft.name.trim(), phone: cardDraft.phone
        }),
        closeCard,
        'Не удалось создать карточку исполнителя.'
      )
    } else if (cardAction.value === 'attach' && cardDraft.memberId) {
      const employee = availableEmployees.value.find(item => item.id === cardDraft.memberId)
      if (!employee) return
      await complete(
        () => linkEmployeeMember(props.context.organization.id, employee, row.member!.id),
        closeCard,
        'Не удалось связать человека с карточкой.'
      )
    }
  }
}

async function saveRole() {
  const row = selected.value
  if (!row?.member || !canManageRoles.value || row.member.isOwner) return
  if (hasUnsavedDetails.value) {
    formError.value = 'Сначала сохраните изменения карточки.'
    return
  }
  await complete(
    () => updateWorkspaceMemberRole(props.context.organization.id, row.member!.id, { roleCode: cardDraft.roleCode }),
    closeCard,
    'Не удалось изменить роль.'
  )
}

async function revokeInvitation() {
  const row = selected.value
  if (!row?.invitation || !canInvite.value) return
  await complete(
    () => revokeWorkspaceInvitation(props.context.organization.id, row.invitation!.id),
    closeCard,
    'Не удалось отозвать приглашение.'
  )
}

function requestMemberAction(action: 'remove' | 'owner' | 'unlink') {
  if (saving.value || hasUnsavedDetails.value) return
  if (action === 'unlink') {
    if (!selected.value?.employee?.memberId || !canLink.value) return
  } else {
    if (!selected.value?.member || selected.value.member.isOwner) return
    if (action === 'remove' ? !canRemove.value : !canTransfer.value) return
  }
  memberAction.value = action
  formError.value = ''
  confirmationDialog.value?.showModal()
}

async function confirmMemberAction() {
  const row = selected.value
  const action = memberAction.value
  if (saving.value || !row || !action || hasUnsavedDetails.value) return
  if (action === 'unlink') {
    if (!row.employee?.memberId || !canLink.value) return
    await complete(
      () => linkEmployeeMember(props.context.organization.id, row.employee!, null),
      () => { confirmationDialog.value?.close(); memberAction.value = null; closeCard() },
      'Не удалось отвязать аккаунт.'
    )
    return
  }
  if (!row.member || row.member.isOwner) return
  if (action === 'remove' ? !canRemove.value : !canTransfer.value) return
  await complete(
    () => action === 'remove'
      ? removeWorkspaceMember(props.context.organization.id, row.member!.id)
      : transferWorkspaceOwnership(props.context.organization.id, row.member!.id),
    () => { confirmationDialog.value?.close(); memberAction.value = null; closeCard() },
    'Не удалось изменить доступ сотрудника.'
  )
}

watch(() => rows.value.length, count => emit('update:count', count), { immediate: true })
watch(() => props.previewOnly, isPreview => {
  if (isPreview) {
    employees.value = []
    memberOptions.value = []
    access.value = null
    loading.value = false
    return
  }

  void load()
})
onMounted(() => {
  if (!props.previewOnly) void load()
})
</script>

<template>
  <div class="employees">
    <div class="employees__heading">
      <div class="employees__heading-actions">
        <UiButton v-if="canViewRoles" href="#members" variant="secondary" :disabled="previewOnly">Роли и права</UiButton>
        <UiButton v-if="canWrite || canInvite || previewOnly" :disabled="previewOnly" @click="openAdd">+ Добавить сотрудника</UiButton>
      </div>
    </div>
    <div class="employees__filters">
      <UiInput v-model="search" placeholder="Имя, телефон или email" aria-label="Поиск по команде" :disabled="previewOnly" />
      <UiSelect v-model="teamFilter" label="" aria-label="Фильтр команды" :options="filterOptions" required :searchable="false" :disabled="previewOnly" />
    </div>
    <UiLoader v-if="loading" text="Загружаем команду…" />
    <p v-else-if="error" role="alert">{{ error }} <UiButton variant="secondary" @click="load">Повторить</UiButton></p>
    <template v-else-if="previewOnly">
      <div class="employees__table" role="table" aria-label="Команда">
        <div class="employees__table-head" role="row">
          <span role="columnheader">Сотрудник</span><span role="columnheader">Назначение на работы</span><span role="columnheader">Доступ к CRM</span><span role="columnheader">Роль</span><span role="columnheader" class="employees__action-head">Действие</span>
        </div>
        <div v-for="index in 4" :key="index" class="employees__row employees__preview-row" role="row" aria-hidden="true">
          <div class="employees__identity" role="cell"><i></i><small></small></div>
          <div role="cell"><i></i></div><div role="cell"><i></i></div><div role="cell"><i></i></div><div role="cell"><i></i></div>
        </div>
      </div>
    </template>
    <template v-else>
      <p v-if="!rows.length" class="employees__empty">{{ search || teamFilter !== 'all' ? 'Никого не найдено.' : 'В команде пока никого нет.' }}</p>
      <div v-else class="employees__table" role="table" aria-label="Команда">
        <div class="employees__table-head" role="row">
          <span role="columnheader">Сотрудник</span><span role="columnheader">Назначение на работы</span><span role="columnheader">Доступ к CRM</span><span role="columnheader">Роль</span><span role="columnheader" class="employees__action-head">Действие</span>
        </div>
        <div v-for="row in rows" :key="row.key" class="employees__row" role="row">
          <div class="employees__identity" role="cell"><strong>{{ row.name }}</strong><small v-if="row.employee?.phone">{{ row.employee.phone }}</small></div>
          <div class="employees__assignment" role="cell"><span class="team-status" :class="{ 'team-status--enabled': row.employee?.isActive }">{{ row.employee?.isActive ? 'Можно назначать' : 'Не назначается' }}</span></div>
          <div class="employees__connection" role="cell"><span class="team-status" :class="{ 'team-status--enabled': row.member || row.employee?.memberId, 'team-status--pending': row.invitation }">{{ row.connection }}</span><small v-if="row.email && row.employee">{{ row.email }}</small></div>
          <span class="employees__role" role="cell">{{ row.role }}</span>
          <div class="employees__row-action" role="cell"><UiButton variant="secondary" @click="openCard(row)">Открыть</UiButton></div>
        </div>
      </div>
    </template>

    <dialog id="team-add-person-dialog" ref="addDialog" class="employee-dialog" aria-labelledby="add-person-title" @cancel="saving && $event.preventDefault()">
      <form @submit.prevent="addPerson">
        <header class="ui-form-header"><h3 id="add-person-title">Добавить сотрудника</h3><button type="button" :disabled="saving" aria-label="Закрыть" @click="addDialog?.close()">×</button></header>
        <div class="employee-dialog__body">
          <section v-if="canWrite" class="employee-dialog__details">
            <h4 class="ui-section-header">Данные сотрудника</h4>
            <UiInput v-model="addDraft.name" label="Имя" placeholder="Например, Алексей Смирнов" required maxlength="250" :disabled="saving" />
            <UiPhoneInput :model-value="addDraft.phone" @update:model-value="addDraft.phone = $event" label="Телефон" :disabled="saving" />
            <label class="employee-dialog__checkbox"><input v-model="addDraft.isActive" type="checkbox" :disabled="saving" /> Можно назначать работы</label>
          </section>
          <section v-if="canInvite || (canLink && availableMembers.length)" class="employee-dialog__section">
            <h4 class="ui-section-header">Доступ к CRM <UiTooltip text="Роль определяет доступные разделы и действия."><button type="button" class="ui-field-label__help" aria-label="Подсказка: доступ к CRM">?</button></UiTooltip></h4>
            <label v-if="canWrite" class="employee-dialog__checkbox"><input type="checkbox" :checked="addAccess !== 'none'" :disabled="saving" @change="changeAddAccess(($event.target as HTMLInputElement).checked)" /> Предоставить доступ к CRM</label>
            <p v-else class="employee-dialog__hint">Ваши права позволяют пригласить сотрудника в CRM. Данные для назначения на работы можно добавить позже.</p>
            <template v-if="addAccess === 'invite'">
              <UiInput v-model="addDraft.email" type="email" label="Email для входа" required :disabled="saving" />
              <UiSelect append-to="#team-add-person-dialog" v-model="addDraft.roleCode" label="Роль" :options="roleOptions" required :searchable="false" :disabled="saving" />
              <p class="employee-dialog__hint">На этот email отправим приглашение.</p>
            </template>
            <UiSelect v-if="addAccess === 'existing'" append-to="#team-add-person-dialog" v-model="addDraft.memberId" label="Аккаунт в вашей организации" placeholder="Выберите аккаунт" :options="memberSelectOptions" required :disabled="saving" />
            <button v-if="canWrite && canLink && availableMembers.length && addAccess !== 'existing'" type="button" class="employees__quiet-action" :disabled="saving" @click="addAccess = 'existing'">У сотрудника уже есть аккаунт? Связать аккаунт</button>
            <button v-if="canInvite && addAccess === 'existing'" type="button" class="employees__quiet-action" :disabled="saving" @click="addAccess = 'invite'">Пригласить по email</button>
          </section>
          <p v-if="formError" class="employee-dialog__error" role="alert">{{ formError }}</p>
        </div>
        <footer><UiButton variant="secondary" :disabled="saving" @click="addDialog?.close()">Отмена</UiButton><UiButton type="submit" :disabled="saving || (canWrite && !addDraft.name.trim()) || (addAccess === 'invite' && !addDraft.email.trim()) || (addAccess === 'existing' && !addDraft.memberId)">{{ saving ? 'Сохраняем…' : addLabel }}</UiButton></footer>
      </form>
    </dialog>

    <dialog id="team-person-card-dialog" ref="cardDialog" class="employee-dialog" aria-labelledby="person-card-title" @cancel="saving && $event.preventDefault()">
      <form novalidate @submit.prevent="selected?.employee ? saveCard() : applyCardAction()">
        <template v-if="selected">
          <header class="ui-form-header"><div><p class="employee-dialog__eyebrow">Сотрудник</p><h3 id="person-card-title">{{ selected.name }}</h3></div><button type="button" :disabled="saving" aria-label="Закрыть" @click="closeCard">×</button></header>
          <div class="employee-dialog__body">
            <section v-if="selected.employee" class="employee-dialog__details">
              <h4 class="ui-section-header">Данные сотрудника</h4>
              <UiInput v-model="cardDraft.name" label="Имя" required maxlength="250" :disabled="saving || !canWrite" />
              <UiPhoneInput :model-value="cardDraft.phone" @update:model-value="cardDraft.phone = $event" label="Телефон" :disabled="saving || !canWrite" />
              <div class="employee-dialog__checkbox-line">
                <label class="employee-dialog__checkbox"><input v-model="cardDraft.isActive" type="checkbox" :disabled="saving || !canWrite" /> Можно назначать работы</label>
                <UiTooltip text="Доступность для новых работ и смен. Не меняет вход в CRM."><button type="button" class="ui-field-label__help" aria-label="Подсказка: назначение на работы">?</button></UiTooltip>
              </div>
            </section>
            <section class="employee-dialog__section">
              <div class="employee-dialog__section-heading ui-section-header"><h4>Доступ к CRM <UiTooltip text="Роль определяет доступные разделы и действия."><button type="button" class="ui-field-label__help" aria-label="Подсказка: доступ к CRM">?</button></UiTooltip></h4><span class="team-status" :class="{ 'team-status--enabled': selected.member || selected.employee?.memberId, 'team-status--pending': selected.invitation }">{{ selected.connection }}</span></div>
              <p v-if="selected.email" class="employee-dialog__email">{{ selected.email }}</p>
              <template v-if="selected.member && canManageRoles && !selected.member.isOwner">
                <UiSelect append-to="#team-person-card-dialog" v-model="cardDraft.roleCode" label="Роль" :options="roleOptions" required :searchable="false" :disabled="saving" />
                <UiButton v-if="cardDraft.roleCode !== selected.member.roleCode" variant="secondary" :disabled="saving || hasUnsavedDetails" @click="saveRole">Сохранить роль</UiButton>
              </template>
              <p v-else-if="selected.member || selected.invitation" class="employee-dialog__hint">Роль: {{ selected.role }}</p>
              <UiButton v-if="selected.employee?.memberId && canLink" variant="secondary" :disabled="saving || hasUnsavedDetails" @click="requestMemberAction('unlink')">Отвязать аккаунт</UiButton>
              <template v-if="selected.employee && !selected.member && !selected.employee.memberId && !selected.invitation && (canInvite || canLink)">
                <label class="employee-dialog__checkbox"><input type="checkbox" :checked="cardAction === 'invite' || cardAction === 'link'" :disabled="saving" @change="cardAction = ($event.target as HTMLInputElement).checked ? (canInvite ? 'invite' : 'link') : null" /> Предоставить доступ к CRM</label>
                <template v-if="cardAction === 'invite'">
                  <UiInput v-model="cardDraft.email" type="email" label="Email для входа" required :disabled="saving" />
                  <UiSelect append-to="#team-person-card-dialog" v-model="cardDraft.roleCode" label="Роль" :options="roleOptions" required :searchable="false" :disabled="saving" />
                </template>
                <UiSelect v-if="cardAction === 'link'" append-to="#team-person-card-dialog" v-model="cardDraft.memberId" label="Аккаунт в вашей организации" placeholder="Выберите аккаунт" :options="memberSelectOptions" required :disabled="saving" />
                <button v-if="canLink && availableMembers.length && cardAction !== 'link'" type="button" class="employees__quiet-action" :disabled="saving" @click="cardAction = 'link'">У сотрудника уже есть аккаунт? Связать аккаунт</button>
                <button v-if="canInvite && cardAction === 'link'" type="button" class="employees__quiet-action" :disabled="saving" @click="cardAction = 'invite'">Пригласить по email</button>
                <UiButton v-if="cardAction === 'invite'" variant="secondary" :disabled="saving || hasUnsavedDetails || !cardDraft.email.trim()" @click="applyCardAction">Отправить приглашение</UiButton>
              </template>
              <p v-if="hasUnsavedDetails && (cardAction === 'invite' || (selected.member && cardDraft.roleCode !== selected.member.roleCode))" class="employee-dialog__hint">Сначала сохраните изменения данных сотрудника.</p>
            </section>
            <section v-if="!selected.employee && selected.member && canLink" class="employee-dialog__section">
              <h4 class="ui-section-header">Назначение на работы</h4>
              <p class="employee-dialog__hint">Сейчас сотрудник входит в CRM без назначения на работы.</p>
              <label class="employee-dialog__checkbox"><input type="checkbox" :checked="cardAction === 'create'" :disabled="saving" @change="cardAction = ($event.target as HTMLInputElement).checked ? 'create' : null" /> Можно назначать работы</label>
              <template v-if="cardAction === 'create'">
                <UiInput v-model="cardDraft.name" label="Имя" required maxlength="250" :disabled="saving" />
                <UiPhoneInput :model-value="cardDraft.phone" @update:model-value="cardDraft.phone = $event" label="Телефон" :disabled="saving" />
              </template>
              <button v-if="availableEmployees.length && cardAction !== 'attach'" type="button" class="employees__quiet-action" :disabled="saving" @click="cardAction = 'attach'">Сотрудник уже есть в команде? Объединить с аккаунтом</button>
              <UiSelect v-if="cardAction === 'attach'" append-to="#team-person-card-dialog" v-model="cardDraft.memberId" label="Сотрудник" :options="employeeSelectOptions" required :disabled="saving" />
              <UiButton v-if="cardAction === 'create' || cardAction === 'attach'" variant="secondary" :disabled="saving || (cardAction === 'create' ? !cardDraft.name.trim() : !cardDraft.memberId)" @click="applyCardAction">{{ cardAction === 'create' ? 'Разрешить назначение' : 'Объединить' }}</UiButton>
            </section>
            <details v-if="(selected.member && !selected.member.isOwner && (canRemove || canTransfer)) || (selected.invitation && canInvite)" class="employee-dialog__section">
              <summary>Управление доступом</summary>
              <UiButton v-if="selected.invitation && canInvite" variant="secondary" :disabled="saving || hasUnsavedDetails" @click="revokeInvitation">Отозвать приглашение</UiButton>
              <UiButton v-if="selected.member && canRemove" variant="secondary" :disabled="saving || hasUnsavedDetails" @click="requestMemberAction('remove')">Удалить доступ к CRM</UiButton>
              <UiButton v-if="selected.member && canTransfer" variant="secondary" :disabled="saving || hasUnsavedDetails" @click="requestMemberAction('owner')">Передать владение</UiButton>
            </details>
            <p v-if="formError" class="employee-dialog__error" role="alert">{{ formError }}</p>
          </div>
          <footer><UiButton variant="secondary" :disabled="saving" @click="closeCard">Закрыть</UiButton><UiButton v-if="selected.employee && canWrite" type="submit" :disabled="saving || !canSaveCard">{{ saving ? 'Сохраняем…' : 'Сохранить изменения' }}</UiButton></footer>
        </template>
      </form>
    </dialog>
    <dialog ref="confirmationDialog" class="employee-dialog" aria-labelledby="team-confirm-title" @cancel="saving && $event.preventDefault()">
      <form @submit.prevent="confirmMemberAction">
        <header class="ui-form-header"><h3 id="team-confirm-title">{{ memberAction === 'unlink' ? 'Отвязать аккаунт?' : memberAction === 'owner' ? 'Передать владение?' : 'Удалить доступ к CRM?' }}</h3></header>
        <div class="employee-dialog__body"><p>{{ memberAction === 'unlink' ? 'Аккаунт будет отвязан от карточки сотрудника. Сотрудник, его работы и смены сохранятся. Членство аккаунта, роль и доступ к CRM не изменятся.' : memberAction === 'owner' ? 'Сотрудник станет владельцем организации. Ваша роль изменится.' : 'Сотрудник потеряет вход в эту организацию. История работ сохранится.' }}</p><p v-if="formError" class="employee-dialog__error" role="alert">{{ formError }}</p></div>
        <footer><UiButton variant="secondary" :disabled="saving" @click="confirmationDialog?.close()">Отмена</UiButton><UiButton type="submit" :disabled="saving">{{ saving ? 'Выполняем…' : memberAction === 'unlink' ? 'Отвязать аккаунт' : memberAction === 'owner' ? 'Передать владение' : 'Удалить доступ' }}</UiButton></footer>
      </form>
    </dialog>
  </div>
</template>

<style scoped>
:global(body:has(.employee-dialog[open])) { overflow: hidden; }
.employees { width: 100%; }
.employees__heading { display: flex; align-items: center; justify-content: flex-end; gap: 20px; padding-bottom: 16px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.employees__heading-actions { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
.employees__access-link { color: var(--crm-text, #202631); font-size: 13px; }
.employees__filters { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 14px 0; }
.employees__filters :deep(.ui-field) { width: min(100%, 320px); }
.employees__quiet-action { border: 0; padding: 6px 0; background: transparent; color: var(--crm-text-muted, #687386); font: inherit; font-size: 13px; cursor: pointer; }
.employees__quiet-action:hover { color: var(--crm-text, #202631); }
.employees__table-head, .employees__row { display: grid; grid-template-columns: minmax(150px, 1.3fr) minmax(135px, 1fr) minmax(160px, 1.2fr) minmax(100px, .8fr) auto; align-items: center; gap: 16px; }
.employees__table-head { padding: 10px 14px; color: var(--crm-text-muted, #687386); background: var(--crm-surface-soft, #f4f6f8); font-size: 12px; }
.employees__row { min-height: 64px; padding: 10px 14px; border-bottom: 1px solid var(--crm-line, #dce2e9); }
.employees__preview-row i { display: block; width: min(100%, 120px); height: 12px; border-radius: 5px; background: var(--crm-line, #dce2e9); opacity: .65; }
.employees__preview-row .employees__identity i { width: 150px; height: 14px; }
.employees__preview-row .employees__identity small { width: 90px; height: 9px; border-radius: 5px; background: var(--crm-line, #dce2e9); opacity: .5; }
.employees__identity { display: grid; gap: 3px; min-width: 0; }
.employees__identity strong { overflow-wrap: anywhere; color: var(--crm-text, #202631); }
.employees__identity small { overflow-wrap: anywhere; color: var(--crm-text-muted, #687386); }
.employees__role, .employees__connection { color: var(--crm-text, #202631); font-size: 13px; }
.employees__connection { color: var(--crm-text-muted, #687386); }
.employees__action-head { text-align: right; }
.employees__row-action { justify-self: end; }
.employees__empty { padding: 20px 0; color: var(--crm-text-muted, #687386); }
.employee-dialog__checkbox-line { display: flex; align-items: center; gap: 6px; }
.employee-dialog { width: min(600px, calc(100vw - 32px)); max-height: 85dvh; overflow: hidden; padding: 0; border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); }
.employee-dialog form { display: flex; flex-direction: column; max-height: calc(85dvh - 2px); }
.employee-dialog::backdrop { background: var(--crm-backdrop, rgb(0 0 0 / .55)); }
.employee-dialog header, .employee-dialog footer { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-shrink: 0; padding: 16px 20px; }
.employee-dialog header { border-bottom: 1px solid var(--crm-form-header-border, #dce2e9); }
.employee-dialog h3 { margin: 0; font-size: 17px; overflow-wrap: anywhere; }
.employee-dialog header button { border: 0; background: transparent; color: inherit; font-size: 24px; cursor: pointer; }
.employee-dialog__body { display: grid; gap: 16px; overflow-y: auto; min-height: 0; padding: 20px; }
.employee-dialog__hint { margin: 0; color: var(--crm-text-muted, #687386); font-size: 13px; }

.employee-dialog__checkbox { display: flex; align-items: center; gap: 8px; width: fit-content; cursor: pointer; font-size: 13px; }
.employee-dialog__section { display: grid; gap: 12px; padding-top: 16px; border-top: 1px solid var(--crm-line, #dce2e9); }
.employee-dialog__section :deep(.ui-button), .employee-dialog__body > :deep(.ui-button) { justify-self: start; }
.employee-dialog__error { margin: 0; color: var(--crm-danger-text, #a12622); }
.employee-dialog footer { flex-wrap: wrap; justify-content: flex-end; border-top: 1px solid var(--crm-line, #dce2e9); }
.employee-dialog footer :deep(.ui-button--primary), .employees__heading-actions :deep(.ui-button--primary) { border-color: #e87532; background: #e87532; color: #fff; }
.employee-dialog__eyebrow { margin: 0 0 5px; font-size: 12px; color: var(--crm-text-muted, #687386); }
.employee-dialog h4 { margin: 0; font-size: 14px; }
.employee-dialog__details { display: grid; gap: 14px; }
.employee-dialog input { accent-color: var(--crm-accent, #ed732e); }
.employee-dialog :deep(.multiselect-dropdown) { max-height: 180px; }
@media (max-width: 1100px) {
  .employees__table-head { display: none; }
  .employees__row { grid-template-columns: minmax(0, 1fr) auto; gap: 7px 12px; }
  .employees__identity { grid-column: 1; grid-row: 1; }
  .employees__assignment { grid-column: 1; grid-row: 2; }
  .employees__role { grid-column: 1; grid-row: 4; }
  .employees__role::before { content: 'Роль: '; color: var(--crm-text-muted, #687386); }
  .employees__connection { grid-column: 1; grid-row: 3; }
  .employees__row-action { grid-column: 2; grid-row: 1 / 5; }
}
@media (max-width: 440px) {
  .employees__heading, .employees__filters { display: grid; }
  .employees__heading :deep(.ui-button) { justify-self: start; }
  .employees__row { padding-inline: 0; }
  .employee-dialog footer :deep(.ui-button) { flex: 1 1 auto; }
}
.employees__connection { display: grid; gap: 5px; justify-items: start; min-width: 0; }
.employees__connection small { overflow-wrap: anywhere; font-size: 12px; }
.employees__filters :deep(.ui-select-field) { max-width: 260px; }
.team-status { display: inline-flex; align-items: center; gap: 6px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 20px; padding: 5px 9px; color: var(--crm-text-muted, #687386); background: var(--crm-surface-soft, #f4f6f8); font-size: 12px; }
.team-status::before { content: ''; width: 6px; height: 6px; flex-shrink: 0; border-radius: 50%; background: currentColor; }
.team-status--enabled { color: var(--crm-success-text, #237269); }
.team-status--pending { color: var(--crm-warning-text, #ac7313); }
.employee-dialog__section-heading { display: flex; justify-content: space-between; align-items: center; gap: 10px; flex-wrap: wrap; }
.employee-dialog__email { margin: 0; overflow-wrap: anywhere; font-size: 14px; }
.employee-dialog summary { cursor: pointer; font-size: 13px; }
.employee-dialog details[open] :deep(.ui-button) { margin-top: 10px; }
</style>
