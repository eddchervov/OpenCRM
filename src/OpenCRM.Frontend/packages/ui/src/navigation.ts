export type HeaderLink = {
  label: string
  labelKey?: string
  href: string
}

export type ProfileLink = {
  label: string
  labelKey?: string
  href: string
  kind?: 'link' | 'divider' | 'logout'
}

export type FooterSection = {
  title: string
  titleKey?: string
  links: HeaderLink[]
}

export const defaultHeaderLinks: HeaderLink[] = []

export const defaultProfileLinks: ProfileLink[] = [
  { label: '', labelKey: 'common.workspace', href: '/organizations' },
  { label: '', labelKey: 'common.profile', href: '/personal' },
  { label: '', labelKey: 'common.support', href: '/personal/support' },
  { label: '', labelKey: 'common.vehicles', href: '/vehicles' },
  { label: '', href: '#', kind: 'divider' },
  { label: '', labelKey: 'common.logout', href: '/auth/logout?returnUrl=/', kind: 'logout' }
]

const projectGitHubUrl = typeof document === 'undefined'
  ? ''
  : document.querySelector<HTMLMetaElement>('meta[name="project-github"]')?.content.trim() ?? ''

const projectFooterLinks: HeaderLink[] = [{ label: '', labelKey: 'common.home', href: '/' }]
if (projectGitHubUrl) {
  projectFooterLinks.push({ label: '', labelKey: 'common.github', href: projectGitHubUrl })
}

export const defaultFooterSections: FooterSection[] = [
  {
    title: '',
    titleKey: 'common.project',
    links: projectFooterLinks
  }
]
