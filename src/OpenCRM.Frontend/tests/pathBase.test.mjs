import test from 'node:test'
import assert from 'node:assert/strict'
import { pushHashState, withPathBase } from '../packages/workspace/src/utils/pathBase.mjs'

test('withPathBase preserves a mounted application prefix for local paths', () => {
  assert.equal(withPathBase('/help', '/opencrm'), '/opencrm/help')
  assert.equal(withPathBase('/api/v1/notifications', '/opencrm'), '/opencrm/api/v1/notifications')
  assert.equal(withPathBase('/', '/opencrm'), '/opencrm/')
})

test('withPathBase leaves non-local and already prefixed paths unchanged', () => {
  assert.equal(withPathBase('https://example.com/help', '/opencrm'), 'https://example.com/help')
  assert.equal(withPathBase('#workspace', '/opencrm'), '#workspace')
  assert.equal(withPathBase('/opencrm/help', '/opencrm'), '/opencrm/help')
  assert.equal(withPathBase('/help', ''), '/help')
})

test('pushHashState adds a browser-history entry and preserves path and query', () => {
  const originalWindow = globalThis.window
  const entries = []
  const location = { pathname: '/autoservice/organization', search: '?from=dashboard', hash: '#clients/client-1' }
  globalThis.window = {
    location,
    history: {
      pushState(_state, _title, url) {
        entries.push(url)
        location.hash = new URL(url, 'https://opencrm.example.invalid').hash
      }
    }
  }

  try {
    pushHashState('#vehicles')

    assert.deepEqual(entries, ['/autoservice/organization?from=dashboard#vehicles'])
    assert.equal(location.hash, '#vehicles')
  } finally {
    globalThis.window = originalWindow
  }
})

test('pushHashState does not add a duplicate entry for the current section', () => {
  const originalWindow = globalThis.window
  const entries = []
  globalThis.window = {
    location: { pathname: '/detailing/organization', search: '', hash: '#finance' },
    history: { pushState: (...args) => entries.push(args) }
  }

  try {
    pushHashState('#finance')

    assert.deepEqual(entries, [])
  } finally {
    globalThis.window = originalWindow
  }
})
