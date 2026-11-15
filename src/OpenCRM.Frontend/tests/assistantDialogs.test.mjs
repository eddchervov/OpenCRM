import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import vm from 'node:vm'

const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot.js', import.meta.url), 'utf8')

function assistant() {
  const nodes = new Map()
  function node(selector) {
    if (!nodes.has(selector)) {
      nodes.set(selector, {
        value: '', open: false, hidden: false, disabled: false, checked: false,
        dataset: {}, listeners: new Map(), classList: { toggle() {} },
        addEventListener(name, listener) { this.listeners.set(name, listener) },
        setAttribute() {}, querySelector: node,
        closest() { return node('[data-composer-dialog]') },
        close() { this.open = false }, showModal() { this.open = true }, focus() {}
      })
    }
    return nodes.get(selector)
  }
  const root = node('root')
  root.dataset = { apiUrl: '/api/v1/assistant', organizationId: 'org-1' }
  root.querySelectorAll = selector => {
    if (selector === '[data-close-dialog]') return [node('close-composer')]
    if (selector === '[data-tab], [data-example]') return [node('mode-tab'), node('phrase-example')]
    return []
  }
  const sandbox = {
    document: { querySelector: () => root },
    window: { addEventListener() {}, clearInterval() {} }
  }
  const initialization = '    initialize();'
  assert.equal(source.split(initialization).length, 2)
  const instrumented = source.replace(initialization, `
    globalThis.pilot = {
      updateControls, closeDialog,
      setState(state) {
        context = { telegramConfigured: true };
        contextAvailable = state.available ?? true;
        busy = state.busy ?? false;
        transcriptionCount = state.transcribing ? 1 : 0;
        microphonePending = state.microphonePending ?? false;
        recordingStopping = state.recordingStopping ?? false;
        recorder = state.recorder ?? null;
        activeDraft = state.draft ?? null;
        maximumMessageCharacters = state.maximumMessageCharacters ?? 1000;
      }
    };`)
  vm.runInNewContext(instrumented, sandbox)
  return { pilot: sandbox.pilot, node }
}

test('Остановить запись можно и после отключения доступа к помощнику', () => {
  const { pilot, node } = assistant()
  const recorder = { state: 'recording', stop() { this.state = 'inactive' } }
  pilot.setState({ available: false, recorder })
  pilot.updateControls()
  assert.equal(node('[data-record]').disabled, false)
  assert.equal(node('close-composer').disabled, true)
  node('[data-record]').listeners.get('click')()
  assert.equal(recorder.state, 'inactive')
  pilot.updateControls()
  assert.equal(node('[data-record]').disabled, true)
  assert.equal(node('close-composer').disabled, true)
  pilot.setState({ available: false, recorder })
  pilot.updateControls()
  assert.equal(node('close-composer').disabled, false)
})

test('Ожидание микрофона можно отменить после отключения помощника', () => {
  const { pilot, node } = assistant()
  pilot.setState({ available: false, microphonePending: true })
  pilot.updateControls()
  assert.equal(node('[data-record]').disabled, false)
  node('[data-record]').listeners.get('click')()
  assert.equal(node('[data-record]').disabled, true)
  assert.equal(node('close-composer').disabled, false)
})

test('Escape не скрывает окно во время отправки, распознавания или записи', () => {
  for (const state of [{ busy: true }, { transcribing: true }, { recordingStopping: true }, { recorder: { state: 'recording' } }]) {
    const { pilot, node } = assistant()
    pilot.setState(state)
    const event = { prevented: false, preventDefault() { this.prevented = true } }
    node('[data-composer-dialog]').listeners.get('cancel')(event)
    assert.equal(event.prevented, true)
  }
})

test('Окно остаётся защищено между stop и запуском распознавания', () => {
  const { pilot, node } = assistant()
  const recorder = { state: 'recording', stop() { this.state = 'inactive' } }
  pilot.setState({ recorder })
  node('[data-composer-dialog]').open = true
  node('[data-record]').listeners.get('click')()
  pilot.closeDialog(node('[data-composer-dialog]'))
  assert.equal(node('[data-composer-dialog]').open, true)
  assert.equal(node('[data-record]').disabled, true)
  assert.equal(node('close-composer').disabled, true)
})

test('Завершение записи блокирует отправку, смену режима и выбор другого аудио', async () => {
  const { pilot, node } = assistant()
  pilot.setState({ recordingStopping: true })
  node('[data-transcript]').value = 'Предыдущий текст'
  pilot.updateControls()
  for (const selector of ['[data-prepare]', '[data-new-conversation]', '[data-audio-file]', 'mode-tab', 'phrase-example']) {
    assert.equal(node(selector).disabled, true, selector)
  }
  await node('[data-prepare]').listeners.get('click')()
  assert.equal(node('[data-transcript]').value, 'Предыдущий текст')
})

test('Обычное закрытие окна сохраняет набранный текст', () => {
  const { pilot, node } = assistant()
  pilot.setState({})
  node('[data-composer-dialog]').open = true
  node('[data-transcript]').value = 'А123ВС154, замена масла'
  pilot.closeDialog(node('[data-composer-dialog]'))
  assert.equal(node('[data-composer-dialog]').open, false)
  assert.equal(node('[data-transcript]').value, 'А123ВС154, замена масла')
})

test('Закрытое подтверждение можно открыть повторно без выполнения действия', () => {
  const { pilot, node } = assistant()
  pilot.setState({ draft: { id: 'draft-1' } })
  node('[data-open-draft]').listeners.get('click')()
  assert.equal(node('[data-draft-dialog]').open, true)
  pilot.closeDialog(node('[data-draft-dialog]'))
  assert.equal(node('[data-draft-dialog]').open, false)
  node('[data-open-draft]').listeners.get('click')()
  assert.equal(node('[data-draft-dialog]').open, true)
})

test('Опасное действие требует галочку и не подтверждается во время другого запроса', () => {
  const { pilot, node } = assistant()
  const draft = { id: 'draft-1', destructive: true }
  pilot.setState({ draft })
  pilot.updateControls()
  assert.equal(node('[data-execute]').disabled, true)
  node('[data-confirm-destructive]').checked = true
  pilot.updateControls()
  assert.equal(node('[data-execute]').disabled, false)
  pilot.setState({ draft, busy: true })
  pilot.updateControls()
  assert.equal(node('[data-execute]').disabled, true)
})
