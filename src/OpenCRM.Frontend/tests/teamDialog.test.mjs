import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript, compileTemplate } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'

function compileComponent(path, dependencies) {
  const filename = new URL(path, import.meta.url)
  const { descriptor } = parse(readFileSync(filename, 'utf8'))
  const script = compileScript(descriptor, { id: path })
  const code = ts.transpileModule(script.content, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText
  const module = { exports: {} }
  new Function('require', 'module', 'exports', code)(id => {
    if (id === 'vue') return vue
    if (id in dependencies) return dependencies[id]
    throw new Error(`Неизвестная зависимость: ${id}`)
  }, module, module.exports)
  const template = compileTemplate({
    source: descriptor.template.content,
    filename: filename.pathname,
    id: path,
    compilerOptions: { mode: 'function', bindingMetadata: script.bindings }
  }).code
  module.exports.default.render = new Function('Vue', ts.transpileModule(template, {
    compilerOptions: { target: ts.ScriptTarget.ES2022 }
  }).outputText)(vue)

  return module.exports.default
}

function createNode(type, text = '') {
  return {
    type, text, props: {}, children: [], parent: null, open: false,
    addEventListener() {},
    showModal() { this.open = true },
    close() { this.open = false },
    closest(selector) {
      for (let node = this; node; node = node.parent) {
        if (selector === 'dialog[open]' && node.type === 'dialog' && node.open) return node
      }
      return null
    }
  }
}

function find(node, predicate) {
  if (predicate(node)) return node
  for (const child of node.children) {
    const match = find(child, predicate)
    if (match) return match
  }
}

function createTeam() {
  const root = createNode('body')
  const renderer = vue.createRenderer({
    createElement: type => createNode(type),
    createText: text => createNode('#text', text),
    createComment: text => createNode('#comment', text),
    setText: (node, text) => { node.text = text },
    setElementText: (node, text) => { node.text = text; node.children = [] },
    patchProp: (node, key, _previous, value) => { node.props[key] = value },
    insert(node, parent, anchor = null) {
      assert.ok(parent, 'Родитель DOM-узла должен существовать')
      assert.ok(!anchor || parent.children.includes(anchor), 'DOM-якорь должен принадлежать родителю')
      if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1)
      node.parent = parent
      parent.children.splice(anchor ? parent.children.indexOf(anchor) : parent.children.length, 0, node)
    },
    remove(node) {
      if (node.parent) node.parent.children.splice(node.parent.children.indexOf(node), 1)
      node.parent = null
    },
    parentNode: node => node.parent,
    nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] ?? null,
    querySelector: selector => selector === 'body' ? root : find(root, node => node.props.id === selector.slice(1))
  })
  const UiTooltip = compileComponent('../packages/ui/src/components/UiTooltip.vue', {
    '@floating-ui/vue': {
      autoUpdate() {}, flip() {}, offset() {}, shift() {},
      useFloating: () => ({ floatingStyles: vue.ref({}) })
    }
  })
  const placeholder = vue.defineComponent({ setup: () => () => vue.h('input') })
  const ui = Object.fromEntries(['UiButton', 'UiInput', 'UiLoader', 'UiPhoneInput', 'UiSelect'].map(name => [name, placeholder]))
  const employee = { id: 'employee', name: 'Сотрудник', phone: '', memberId: 'owner', isActive: true, version: 1 }
  const member = { id: 'owner', email: 'contact@example.invalid', roleCode: 'owner', roleName: 'Владелец', isOwner: true }
  const calls = []
  const Team = compileComponent('../packages/workspace/src/components/WorkspaceEmployeesSection.vue', {
    '@opencrm/ui': { ...ui, UiTooltip },
    '../api/employees': {
      loadEmployees: async () => ({ items: [employee], members: [member], canManage: true, canLinkMembers: true }),
      linkEmployeeMember: async (...args) => { calls.push(args); employee.memberId = args[2] }
    },
    '../api/workspaceAdmin': {
      loadWorkspaceMembers: async () => ({ organization: { members: [member], pendingInvitations: [], currentUserRoleCode: 'owner' } })
    }
  })
  let state
  const setup = Team.setup
  Team.setup = (props, context) => { state = setup(props, context); return state }
  const app = renderer.createApp(Team, {
    context: { organization: { id: 'organization' }, workspaceAccess: { isReadOnly: false }, shell: { menu: [{ code: 'members', isVisible: true }] } }
  })
  app.mount(root)

  return { app, root, calls, employee, member, get state() { return state } }
}

for (const action of ['close', 'unlink']) {
  test(`Карточка открывается повторно после подсказки и ${action === 'close' ? 'закрытия' : 'отвязки владельца'}`, async () => {
    const team = createTeam()
    await team.state.load()
    team.state.openCard(team.state.rows.value[0])
    await vue.nextTick()
    const dialog = find(team.root, node => node.props.id === 'team-person-card-dialog')
    const tooltip = find(dialog, node => node.props.class === 'ui-tooltip-anchor')
    tooltip.props.onFocusin()
    await vue.nextTick()
    assert.ok(find(dialog, node => node.props.role === 'tooltip'))

    if (action === 'unlink') {
      team.state.requestMemberAction('unlink')
      await team.state.confirmMemberAction()
      assert.equal(team.calls.length, 1)
      assert.equal(team.calls[0][2], null)
    } else {
      team.state.closeCard()
    }
    await vue.nextTick()
    assert.equal(dialog.open, false)
    assert.equal(find(dialog, node => node.props.role === 'tooltip'), undefined)

    team.state.openCard(team.state.rows.value[0])
    await vue.nextTick()
    assert.equal(dialog.open, true)
    assert.ok(find(dialog, node => node.props.id === 'person-card-title'))
    team.app.unmount()
  })
}
