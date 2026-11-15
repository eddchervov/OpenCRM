interface ActiveModal {
  element: HTMLElement
  close: () => void
  opener: HTMLElement | null
}

const activeModals: ActiveModal[] = []
let listenersAttached = false

function getOwnedPortals(modal: ActiveModal) {
  const ids = new Set<string>()
  for (const trigger of modal.element.querySelectorAll<HTMLElement>('[aria-controls], [aria-owns]')) {
    for (const id of `${trigger.getAttribute('aria-controls') ?? ''} ${trigger.getAttribute('aria-owns') ?? ''}`.split(/\s+/)) {
      if (id) {
        ids.add(id)
      }
    }
  }

  return [...ids]
    .map(id => document.getElementById(id))
    .filter((element): element is HTMLElement => element instanceof HTMLElement && !modal.element.contains(element))
}

function getModalFocusRegions(modal: ActiveModal) {
  return [modal.element, ...getOwnedPortals(modal)]
}

function getFocusableElements(modal: ActiveModal) {
  return getModalFocusRegions(modal)
    .flatMap(element => Array.from(element.querySelectorAll<HTMLElement>(
      'a[href], button:not(:disabled):not(.ui-modal__backdrop), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])'
    )))
    .filter(item => item.getAttribute('aria-hidden') !== 'true' && item.getClientRects().length > 0)
}

function focusModal(modal: ActiveModal) {
  const focusableElements = getFocusableElements(modal)
  ;(focusableElements[0] ?? modal.element).focus()
}

function handleModalKeydown(event: KeyboardEvent) {
  const modal = activeModals.at(-1)
  if (!modal) {
    return
  }

  if (event.key === 'Escape') {
    event.preventDefault()
    event.stopPropagation()
    modal.close()
    return
  }

  if (event.key !== 'Tab') {
    return
  }

  const focusableElements = getFocusableElements(modal)
  if (focusableElements.length === 0) {
    event.preventDefault()
    modal.element.focus()
    return
  }

  const firstElement = focusableElements[0]
  const lastElement = focusableElements[focusableElements.length - 1]
  const focusRegions = getModalFocusRegions(modal)
  const isFocusInside = focusRegions.some(element => element.contains(document.activeElement))
  if (event.shiftKey && (document.activeElement === firstElement || !isFocusInside)) {
    event.preventDefault()
    lastElement.focus()
  } else if (!event.shiftKey && (document.activeElement === lastElement || !isFocusInside)) {
    event.preventDefault()
    firstElement.focus()
  }
}

function handleModalFocus(event: FocusEvent) {
  const modal = activeModals.at(-1)
  if (!modal) {
    return
  }

  if (!getModalFocusRegions(modal).some(element => element.contains(event.target as Node))) {
    focusModal(modal)
  }
}

function attachModalListeners() {
  if (listenersAttached) {
    return
  }

  document.addEventListener('keydown', handleModalKeydown)
  document.addEventListener('focusin', handleModalFocus)
  listenersAttached = true
}

function detachModalListeners() {
  if (!listenersAttached || activeModals.length > 0) {
    return
  }

  document.removeEventListener('keydown', handleModalKeydown)
  document.removeEventListener('focusin', handleModalFocus)
  listenersAttached = false
}

export function activateModalFocus(element: HTMLElement, close: () => void) {
  const modal: ActiveModal = {
    element,
    close,
    opener: document.activeElement instanceof HTMLElement ? document.activeElement : null
  }
  activeModals.push(modal)
  attachModalListeners()
  focusModal(modal)

  return () => {
    const wasTopModal = activeModals.at(-1) === modal
    const modalIndex = activeModals.indexOf(modal)
    if (modalIndex >= 0) {
      activeModals.splice(modalIndex, 1)
    }

    detachModalListeners()
    if (!wasTopModal) {
      return
    }

    const parentModal = activeModals.at(-1)
    if (modal.opener?.isConnected && (!parentModal || getModalFocusRegions(parentModal).some(region => region.contains(modal.opener)))) {
      modal.opener.focus()
    } else if (parentModal) {
      focusModal(parentModal)
    }
  }
}
