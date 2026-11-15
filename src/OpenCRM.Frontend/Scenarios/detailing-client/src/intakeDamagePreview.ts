import { createApp, h, ref, watch } from 'vue'
import '@opencrm/ui/styles.css'
import '@opencrm/workspace/styles.css'
import './styles.css'
import './intakeDamagePreview.css'
import IntakeDamageMap from '../../../packages/workspace/src/components/IntakeDamageMap.vue'
import { intakeDiagramRegistry } from '../../../packages/workspace/src/components/intakeDiagramRegistry'
import type { IntakeDamage, IntakeDamagePayload } from '../../../packages/workspace/src/api/intakeDamages'
import type { WorkspaceMaintenancePhoto } from '../../../packages/workspace/src/api/maintenance'

const version = ref(1)
const previewParameters = new URLSearchParams(location.search)
let simulateConflict = previewParameters.has('conflict')
const failedImageView = previewParameters.get('imageError')
if (failedImageView && Object.hasOwn(intakeDiagramRegistry['sedan-v1'].views, failedImageView)) {
  const view = failedImageView as keyof typeof intakeDiagramRegistry['sedan-v1']['views']
  Object.assign(intakeDiagramRegistry['sedan-v1'].views[view], { imageUrl: '/__intake-test-missing-image__.png' })
}
const diagramVersion = 'sedan-v1'
const readOnly = ref(previewParameters.has('readonly'))
const damages = ref<IntakeDamage[]>([
  { id: 'demo-1', number: 1, viewCode: 'front', damageType: 'scratch', x: 0.3, y: 0.65, description: 'Царапина на бампере', photoIds: ['photo-1'] },
  { id: 'demo-2', number: 3, viewCode: 'left', damageType: 'dent', x: 0.55, y: 0.52, description: 'Небольшая вмятина у задней двери', photoIds: [] }
])
const photos = ref<WorkspaceMaintenancePhoto[]>([
  { photoId: 'photo-1', stage: 1, createdAt: new Date().toISOString(), url: photoPreview('#d7a280', 'До работы · 1') },
  { photoId: 'photo-2', stage: 1, createdAt: new Date().toISOString(), url: photoPreview('#899bad', 'До работы · 2') },
  { photoId: 'photo-after', stage: 2, createdAt: new Date().toISOString(), url: photoPreview('#78977f', 'После работы') }
])
const isDark = ref(false)
watch(isDark, value => { document.documentElement.dataset.crmTheme = value ? 'dark' : 'light' }, { immediate: true })

function photoPreview(color: string, title: string) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="320" height="220"><rect width="320" height="220" fill="${color}"/><path d="M28 155 62 104h188l40 51v28H28Z" fill="#e8edf0" stroke="#37424b" stroke-width="5"/><circle cx="90" cy="184" r="22" fill="#303840"/><circle cx="235" cy="184" r="22" fill="#303840"/><text x="20" y="35" font-family="Arial" font-size="17" fill="#fff">${title}</text></svg>`
  return `data:image/svg+xml,${encodeURIComponent(svg)}`
}

const nativeFetch = window.fetch.bind(window)
window.fetch = async (input, init) => {
  const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, window.location.origin)
  if (url.pathname.endsWith('/intake/damages')) {
    if (init?.method === 'GET' || !init?.method) {
      return Response.json({ version: version.value, diagramVersion, damages: damages.value })
    }
  }
  if (url.pathname.includes('/intake/damages/') && init?.method === 'PUT') {
    const damageId = url.pathname.split('/').at(-1) ?? ''
    const { version: expectedVersion, diagramVersion: expectedDiagram, ...payload } = JSON.parse(String(init.body)) as { version: number; diagramVersion: string } & IntakeDamagePayload
    if (simulateConflict) {
      simulateConflict = false
      damages.value[0].description = 'Уточнено в другом окне предпросмотра'
      version.value++
    }
    if (expectedDiagram !== diagramVersion) return new Response('Версия схемы не совпадает.', { status: 409 })
    if (expectedVersion !== version.value) return new Response('Устаревшая версия данных.', { status: 409 })
    const existing = damages.value.find(item => item.id === damageId)
    if (existing) Object.assign(existing, payload)
    else damages.value.push({ id: damageId, number: Math.max(0, ...damages.value.map(item => item.number)) + 1, ...payload })
    version.value++
    return new Response(null, { status: 204 })
  }
  if (url.pathname.includes('/intake/damages/') && init?.method === 'DELETE') {
    const damageId = url.pathname.split('/').at(-1)
    if (Number(url.searchParams.get('version')) !== version.value) return new Response('Устаревшая версия данных.', { status: 409 })
    damages.value = damages.value.filter(item => item.id !== damageId)
    version.value++
    return new Response(null, { status: 204 })
  }
  if (url.pathname.endsWith('/photos') && init?.method === 'GET') return Response.json(photos.value)
  if (url.pathname.endsWith('/photos') && init?.method === 'POST') {
    const photoId = `photo-${crypto.randomUUID()}`
    photos.value.push({ photoId, stage: 1, createdAt: new Date().toISOString(), url: photoPreview('#8e9e77', 'Загружено в предпросмотре') })
    return new Response(null, { status: 204 })
  }
  return nativeFetch(input, init)
}

createApp({
  setup() {
    return () => h('main', {
      class: ['preview', { 'preview--dark': isDark.value }]
    }, [
      h('header', [
        h('div', [
          h('strong', 'Приёмка и фото'),
          h('span', 'Предпросмотр схемы повреждений')
        ]),
        h('button', {
          type: 'button',
          onClick: () => { isDark.value = !isDark.value }
        }, isDark.value ? 'Светлая тема' : 'Тёмная тема'),
        h('button', {
          type: 'button',
          onClick: () => { readOnly.value = !readOnly.value }
        }, readOnly.value ? 'Режим редактирования' : 'Только чтение')
      ]),
      h('button', {
        type: 'button',
        onClick: () => {
          damages.value[0].description = 'Уточнено в другом окне предпросмотра'
          version.value++
        }
      }, 'Имитировать параллельное изменение'),
      h(IntakeDamageMap, {
        organizationId: 'demo',
        maintenanceId: 'demo',
        maintenanceVersion: version.value,
        photos: photos.value,
        readOnly: readOnly.value
      }),
      h('p', { class: 'preview__note' }, 'Изменения сохраняются локально в памяти страницы. Для проверки можно переключать ширину экрана и тему.')
    ])
  },
}).mount('#app')
