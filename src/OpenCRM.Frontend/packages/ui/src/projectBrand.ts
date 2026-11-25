export function getProjectBrand(): string {
  if (typeof document === 'undefined') {
    return 'OpenCRM'
  }

  return document.querySelector<HTMLMetaElement>("meta[name='project-brand']")?.content?.trim() || 'OpenCRM'
}

export function shouldShowProjectAttribution(): boolean {
  return typeof document === 'undefined' ||
    document.querySelector<HTMLMetaElement>('meta[name="project-show-attribution"]')?.content !== 'false'
}
