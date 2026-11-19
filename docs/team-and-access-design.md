# Organization Team and Access

**English** | [Русский](team-and-access-design.ru.md) | [简体中文](team-and-access-design.zh-CN.md)

## Unified list

The workspace menu has one item, “Team.” The list brings together employees, CRM users, and pending invitations. Each row shows a name (or an email address when no employee profile exists), the CRM role, relationship status, and a “Profile” button. Search covers name, phone, email, role, and status. Archived employees can be shown separately.

A person may be represented by an employee profile, a CRM account, or both. Records are linked only through an explicit EmployeeId/MemberId relationship or an invitation with an EmployeeId. Matching names or email addresses do not create a link automatically.

The employee profile lets an authorized user edit the name, phone number, and active status. If the person has no CRM access, the owner can invite them by email or link them to an existing user. A CRM user without an employee profile is offered the option to create one or link an existing profile. An invitation has its own status card and revoke action. A linked profile shows the CRM role and allows it to be changed when the current user has permission.

## Adding a person

The “Add person” button offers two choices:

- “Employee”: a name, optional phone number, and one access option—no CRM access, an email invitation, or a link to an existing CRM user.
- “CRM user”: an email address and role; no employee profile is created yet.

Creating an employee with an invitation, and creating an employee for an existing user, are atomic operations. The server checks permissions, organization membership, duplicate links, and invitation limits. Creating an employee profile does not change the linked CRM user's role.

## Permissions and navigation

Managing employee profiles requires `employees.manage`; inviting someone requires `members.invite`; changing a role or creating a manual link requires `members.roles.manage`. The server enforces permissions independently of whether buttons are shown. An employee profile alone does not grant CRM access. When a user can only view the employee list, CRM details and access actions are hidden.

The `#employees` route opens “Team.” The legacy `#members` route retains expanded member and permission management; there is no separate item for it in the workspace menu. Shifts, schedules, and totals are expected to appear in this same section later. Employees use “My work.”
