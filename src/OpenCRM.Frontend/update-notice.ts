import './update-notice.css'

export function initializeUpdateNotice(clientPath: string) {
  let isNoticeVisible = false
  const entry = document.querySelector<HTMLScriptElement>(`script[type="module"][src^="${clientPath}assets/"]`)

  function showNotice(text = 'Вышла новая версия. Обновите страницу после сохранения изменений.') {
    if (isNoticeVisible) return
    isNoticeVisible = true

    const notice = document.createElement('div')
    notice.className = 'opencrm-update-notice'
    notice.setAttribute('role', 'alert')
    const message = document.createElement('span')
    message.textContent = text
    const button = document.createElement('button')
    button.type = 'button'
    button.textContent = 'Обновить страницу'
    button.addEventListener('click', () => window.location.reload())
    notice.append(message, button)
    document.body.append(notice)
  }

  async function checkVersion() {
    if (!entry || isNoticeVisible || document.visibilityState === 'hidden') return

    try {
      const response = await fetch(`${clientPath}manifest.json`, { cache: 'no-store' })
      if (!response.ok) return

      const manifest = await response.json()
      const file = manifest['index.html']?.file
      if (typeof file === 'string' && new URL(clientPath + file, location.origin).pathname !== new URL(entry.src).pathname) {
        showNotice()
      }
    } catch {
      // Временная ошибка сети не означает, что приложение обновилось.
    }
  }

  window.addEventListener('vite:preloadError', event => {
    event.preventDefault()
    showNotice('Не удалось загрузить часть приложения. Сохраните изменения и обновите страницу.')
  })
  document.addEventListener('visibilitychange', () => void checkVersion())
  window.setInterval(() => void checkVersion(), 60_000)
  void checkVersion()
}
