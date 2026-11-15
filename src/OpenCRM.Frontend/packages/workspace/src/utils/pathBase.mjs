export function getPathBase() {
  return document.querySelector('meta[name="opencrm-path-base"]')?.content.replace(/\/$/, '') ?? ''
}

export function withPathBase(path, pathBase = getPathBase()) {
  if (!pathBase || !path.startsWith('/') || path.startsWith('//') || path === pathBase || path.startsWith(`${pathBase}/`)) {
    return path
  }

  return `${pathBase}${path}`
}

export function pushHashState(hash) {
  if (window.location.hash === hash) return

  window.history.pushState(null, '', `${window.location.pathname}${window.location.search}${hash}`)
}
