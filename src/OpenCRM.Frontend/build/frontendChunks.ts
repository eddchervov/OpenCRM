import { basename } from 'node:path'

export function resolveSharedChunk(id: string): string | undefined {
  const path = id.replaceAll('\\', '/')
  if (/\/node_modules\/(?:@vue\/|@intlify\/|vue\/|vue-i18n\/|vue-router\/|pinia\/)/.test(path)) {
    return 'vue-vendor'
  }

  if (path.includes('/node_modules/three/build/three.core.js')) {
    return 'three-core'
  }

  const vendor = path.match(/\/node_modules\/(intl-tel-input|chart\.js|v-calendar|@vueform\/multiselect)\//)?.[1]
  if (vendor) {
    return vendor.replaceAll(/[\/@.]/g, '-')
  }

  if (/\/[^/]+Messages\.ts$/.test(path)) {
    return `messages-${basename(path, '.ts').replace(/Messages$/, '').toLowerCase()}`
  }

  return undefined
}
