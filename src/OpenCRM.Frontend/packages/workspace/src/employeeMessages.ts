import { registerMessages, type UiLocale } from '@opencrm/ui/i18n'

const messages = {
  ru: {
    employees: {
      rolesPermissions: 'Роли и права', addEmployee: '+ Добавить сотрудника', searchPlaceholder: 'Имя, телефон или email',
      searchLabel: 'Поиск по команде', filterLabel: 'Фильтр команды', loading: 'Загружаем команду…', retry: 'Повторить',
      team: 'Команда', employee: 'Сотрудник', assignment: 'Назначение на работы', crmAccess: 'Доступ к CRM', role: 'Роль',
      action: 'Действие', noMatches: 'Никого не найдено.', noEmployees: 'В команде пока никого нет.',
      assignable: 'Можно назначать', notAssignable: 'Не назначается', open: 'Открыть', close: 'Закрыть',
      addTitle: 'Добавить сотрудника', employeeData: 'Данные сотрудника', name: 'Имя', exampleName: 'Например, Алексей Смирнов',
      phone: 'Телефон', canAssignWork: 'Можно назначать работы', accessTitle: 'Доступ к CRM', roleHelp: 'Роль определяет доступные разделы и действия.',
      roleHelpLabel: 'Подсказка: доступ к CRM', grantAccess: 'Предоставить доступ к CRM',
      assignmentHelp: 'Если параметр включён, сотрудника можно назначать на работы.', assignmentHelpLabel: 'Подсказка: назначение на работы',
      inviteOnlyHint: 'Ваши права позволяют пригласить сотрудника в CRM. Данные для назначения на работы можно добавить позже.',
      emailForLogin: 'Email для входа', inviteHint: 'На этот email отправим приглашение.', account: 'Аккаунт в вашей организации',
      chooseAccount: 'Выберите аккаунт', linkExisting: 'У сотрудника уже есть аккаунт? Связать аккаунт', inviteByEmail: 'Пригласить по email',
      cancel: 'Отмена', saving: 'Сохраняем…', addInvite: 'Отправить приглашение', addAndInvite: 'Добавить и пригласить',
      addAndLink: 'Добавить и связать', addPerson: 'Добавить сотрудника', cardEyebrow: 'Сотрудник',
      saveRole: 'Сохранить роль', unlinkAccount: 'Отвязать аккаунт', invite: 'Отправить приглашение',
      saveDetailsFirst: 'Сначала сохраните изменения данных сотрудника.', assignmentTitle: 'Назначение на работы',
      noAssignmentHint: 'Сейчас сотрудник входит в CRM без назначения на работы.', combineExisting: 'Сотрудник уже есть в команде? Объединить с аккаунтом',
      allowAssignment: 'Разрешить назначение', combine: 'Объединить', accessManagement: 'Управление доступом',
      revokeInvitation: 'Отозвать приглашение', removeAccess: 'Удалить доступ к CRM', transferOwnership: 'Передать владение',
      saveChanges: 'Сохранить изменения', executing: 'Выполняем…', unlinkTitle: 'Отвязать аккаунт?',
      ownerTitle: 'Передать владение?', removeTitle: 'Удалить доступ к CRM?',
      unlinkConfirmation: 'Аккаунт будет отвязан от карточки сотрудника. Сотрудник, его работы и смены сохранятся. Членство аккаунта, роль и доступ к CRM не изменятся.',
      ownerConfirmation: 'Сотрудник станет владельцем организации. Ваша роль изменится.',
      removeConfirmation: 'Сотрудник потеряет вход в эту организацию. История работ сохранится.',
      confirmUnlink: 'Отвязать аккаунт', confirmOwner: 'Передать владение', confirmRemove: 'Удалить доступ',
      roles: { owner: 'Владелец', admin: 'Администратор', employee: 'Сотрудник', performer: 'Исполнитель', viewer: 'Только просмотр' },
      filters: { all: 'Все сотрудники', assignable: 'Можно назначать работы', notAssignable: 'Не назначаются на работы', noAccess: 'Без доступа к CRM', invited: 'Ожидают принятия' },
      connection: { noData: 'Нет данных', connected: 'Подключён', pending: 'Ожидает принятия', notProvided: 'Не предоставлен' },
      errors: {
        loadEmployees: 'Не удалось загрузить сотрудников. Попробуйте ещё раз.',
        loadMembers: 'Не удалось загрузить пользователей CRM. Попробуйте ещё раз.',
        inviteCrmUser: 'Не удалось пригласить пользователя CRM.', addPerson: 'Не удалось добавить человека.',
        saveCard: 'Не удалось сохранить карточку.', inviteUser: 'Не удалось пригласить пользователя.',
        createPerformer: 'Не удалось создать карточку исполнителя.', linkPerson: 'Не удалось связать человека с карточкой.',
        saveRole: 'Не удалось изменить роль.', revokeInvitation: 'Не удалось отозвать приглашение.',
        unlinkAccount: 'Не удалось отвязать аккаунт.', changeAccess: 'Не удалось изменить доступ сотрудника.'
      }
    },
    employeeApi: {
      loadEmployees: 'Не удалось загрузить сотрудников. Попробуйте ещё раз.',
      linkMember: 'Не удалось изменить доступ сотрудника.', saveEmployee: 'Не удалось сохранить сотрудника.',
      inviteEmployee: 'Не удалось добавить человека и пригласить его.', createPerformer: 'Не удалось создать карточку исполнителя.',
      assignEmployee: 'Не удалось назначить сотрудника.'
    }
  },
  en: {
    employees: {
      rolesPermissions: 'Roles and permissions', addEmployee: '+ Add employee', searchPlaceholder: 'Name, phone, or email',
      searchLabel: 'Search team', filterLabel: 'Filter team', loading: 'Loading team…', retry: 'Try again',
      team: 'Team', employee: 'Employee', assignment: 'Work assignment', crmAccess: 'CRM access', role: 'Role',
      action: 'Action', noMatches: 'No matches found.', noEmployees: 'There are no employees in the team yet.',
      assignable: 'Can be assigned', notAssignable: 'Not assignable', open: 'Open', close: 'Close',
      addTitle: 'Add employee', employeeData: 'Employee details', name: 'Name', exampleName: 'For example, Alex Taylor',
      phone: 'Phone', canAssignWork: 'Can be assigned to work', accessTitle: 'CRM access', roleHelp: 'The role determines which sections and actions are available.',
      roleHelpLabel: 'CRM access help', grantAccess: 'Grant CRM access',
      assignmentHelp: 'When enabled, this employee can be assigned to work.', assignmentHelpLabel: 'Work assignment help',
      inviteOnlyHint: 'Your permissions allow you to invite someone to the CRM. You can add their work assignment details later.',
      emailForLogin: 'Email for sign-in', inviteHint: 'We will send an invitation to this email address.', account: 'Account in your organization',
      chooseAccount: 'Choose an account', linkExisting: 'Does this employee already have an account? Link account', inviteByEmail: 'Invite by email',
      cancel: 'Cancel', saving: 'Saving…', addInvite: 'Send invitation', addAndInvite: 'Add and invite',
      addAndLink: 'Add and link', addPerson: 'Add employee', cardEyebrow: 'Employee',
      saveRole: 'Save role', unlinkAccount: 'Unlink account', invite: 'Send invitation',
      saveDetailsFirst: 'Save the employee details first.', assignmentTitle: 'Work assignment',
      noAssignmentHint: 'This person has CRM access but is not set up for work assignments.', combineExisting: 'Is this employee already on the team? Link the account',
      allowAssignment: 'Enable work assignment', combine: 'Link', accessManagement: 'Access management',
      revokeInvitation: 'Revoke invitation', removeAccess: 'Remove CRM access', transferOwnership: 'Transfer ownership',
      saveChanges: 'Save changes', executing: 'Processing…', unlinkTitle: 'Unlink account?',
      ownerTitle: 'Transfer ownership?', removeTitle: 'Remove CRM access?',
      unlinkConfirmation: 'The account will be unlinked from this employee record. The employee, their work, and shifts will remain. Account membership, role, and CRM access will not change.',
      ownerConfirmation: 'This employee will become the organization owner. Your role will change.',
      removeConfirmation: 'This person will lose access to this organization. Their work history will remain.',
      confirmUnlink: 'Unlink account', confirmOwner: 'Transfer ownership', confirmRemove: 'Remove access',
      roles: { owner: 'Owner', admin: 'Administrator', employee: 'Employee', performer: 'Technician', viewer: 'View only' },
      filters: { all: 'All employees', assignable: 'Can be assigned to work', notAssignable: 'Not assignable to work', noAccess: 'No CRM access', invited: 'Invitation pending' },
      connection: { noData: 'No data', connected: 'Connected', pending: 'Invitation pending', notProvided: 'Not linked' },
      errors: {
        loadEmployees: 'Could not load employees. Try again.',
        loadMembers: 'Could not load CRM users. Try again.',
        inviteCrmUser: 'Could not invite the CRM user.', addPerson: 'Could not add the person.',
        saveCard: 'Could not save the employee record.', inviteUser: 'Could not invite the user.',
        createPerformer: 'Could not create the employee record.', linkPerson: 'Could not link the account to the employee record.',
        saveRole: 'Could not change the role.', revokeInvitation: 'Could not revoke the invitation.',
        unlinkAccount: 'Could not unlink the account.', changeAccess: 'Could not change employee access.'
      }
    },
    employeeApi: {
      loadEmployees: 'Could not load employees. Try again.',
      linkMember: 'Could not update employee access.', saveEmployee: 'Could not save the employee.',
      inviteEmployee: 'Could not add the person and send an invitation.', createPerformer: 'Could not create the employee record.',
      assignEmployee: 'Could not assign the employee.'
    }
  },
  'zh-Hans': {
    employees: {
      rolesPermissions: '角色与权限', addEmployee: '+ 添加员工', searchPlaceholder: '姓名、电话或电子邮件',
      searchLabel: '搜索团队', filterLabel: '筛选团队', loading: '正在加载团队…', retry: '重试',
      team: '团队', employee: '员工', assignment: '工作指派', crmAccess: 'CRM 访问权限', role: '角色',
      action: '操作', noMatches: '未找到匹配项。', noEmployees: '团队中还没有员工。',
      assignable: '可指派', notAssignable: '不可指派', open: '打开', close: '关闭',
      addTitle: '添加员工', employeeData: '员工信息', name: '姓名', exampleName: '例如：李明',
      phone: '电话', canAssignWork: '可指派工作', accessTitle: 'CRM 访问权限', roleHelp: '角色决定可访问的部分和可执行的操作。',
      roleHelpLabel: 'CRM 访问权限帮助', grantAccess: '授予 CRM 访问权限',
      assignmentHelp: '启用后，可以将此员工指派到工作。', assignmentHelpLabel: '工作指派帮助',
      inviteOnlyHint: '您可以邀请用户加入 CRM。稍后可以添加其工作指派信息。',
      emailForLogin: '登录电子邮件', inviteHint: '我们会向此电子邮件地址发送邀请。', account: '组织中的账户',
      chooseAccount: '选择账户', linkExisting: '员工已有账户？关联账户', inviteByEmail: '通过电子邮件邀请',
      cancel: '取消', saving: '正在保存…', addInvite: '发送邀请', addAndInvite: '添加并邀请',
      addAndLink: '添加并关联', addPerson: '添加员工', cardEyebrow: '员工',
      saveRole: '保存角色', unlinkAccount: '解除账户关联', invite: '发送邀请',
      saveDetailsFirst: '请先保存员工信息。', assignmentTitle: '工作指派',
      noAssignmentHint: '此人员已加入 CRM，但尚未设置工作指派。', combineExisting: '员工已在团队中？与账户合并',
      allowAssignment: '启用工作指派', combine: '关联', accessManagement: '访问权限管理',
      revokeInvitation: '撤回邀请', removeAccess: '移除 CRM 访问权限', transferOwnership: '转让所有权',
      saveChanges: '保存更改', executing: '正在处理…', unlinkTitle: '解除账户关联？',
      ownerTitle: '转让所有权？', removeTitle: '移除 CRM 访问权限？',
      unlinkConfirmation: '账户将从此员工记录中解除关联。员工、其工作和班次都会保留。账户成员身份、角色和 CRM 访问权限不会更改。',
      ownerConfirmation: '此员工将成为组织所有者。您的角色将发生变化。',
      removeConfirmation: '此人员将无法再访问此组织。其工作记录会保留。',
      confirmUnlink: '解除账户关联', confirmOwner: '转让所有权', confirmRemove: '移除访问权限',
      roles: { owner: '所有者', admin: '管理员', employee: '员工', performer: '技师', viewer: '仅查看' },
      filters: { all: '所有员工', assignable: '可指派工作', notAssignable: '不可指派工作', noAccess: '无 CRM 访问权限', invited: '等待接受邀请' },
      connection: { noData: '无数据', connected: '已关联', pending: '等待接受邀请', notProvided: '未关联' },
      errors: {
        loadEmployees: '无法加载员工。请重试。',
        loadMembers: '无法加载 CRM 用户。请重试。',
        inviteCrmUser: '无法邀请 CRM 用户。', addPerson: '无法添加人员。',
        saveCard: '无法保存员工记录。', inviteUser: '无法邀请用户。',
        createPerformer: '无法创建员工记录。', linkPerson: '无法将账户关联到员工记录。',
        saveRole: '无法更改角色。', revokeInvitation: '无法撤回邀请。',
        unlinkAccount: '无法解除账户关联。', changeAccess: '无法更改员工访问权限。'
      }
    },
    employeeApi: {
      loadEmployees: '无法加载员工。请重试。',
      linkMember: '无法更新员工访问权限。', saveEmployee: '无法保存员工。',
      inviteEmployee: '无法添加人员并发送邀请。', createPerformer: '无法创建员工记录。',
      assignEmployee: '无法指派员工。'
    }
  }
}

for (const [locale, localeMessages] of Object.entries(messages)) {
  registerMessages(locale as UiLocale, localeMessages)
}
