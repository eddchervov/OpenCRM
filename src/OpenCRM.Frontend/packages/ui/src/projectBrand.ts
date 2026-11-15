export function getProjectBrand(): string {
  if (typeof document === 'undefined') {
    return 'OpenCRM'
  }

  return document.querySelector<HTMLMetaElement>("meta[name='project-brand']")?.content?.trim() || 'OpenCRM'
}
