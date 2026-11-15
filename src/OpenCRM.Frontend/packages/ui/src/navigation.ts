export type HeaderLink = {
  label: string
  href: string
}

export type ProfileLink = {
  label: string
  href: string
  kind?: 'link' | 'divider' | 'logout'
}

export type FooterSection = {
  title: string
  links: HeaderLink[]
}

export const defaultHeaderLinks: HeaderLink[] = []

export const defaultProfileLinks: ProfileLink[] = [
  { label: 'Рабочее пространство', href: '/organizations' },
  { label: 'Профиль', href: '/personal' },
  { label: 'Обращения', href: '/personal/support' },
  { label: 'Личная техника', href: '/vehicles' },
  { label: '', href: '#', kind: 'divider' },
  { label: 'Выйти', href: '/auth/logout?returnUrl=/', kind: 'logout' }
]

const projectGitHubUrl = typeof document === 'undefined'
  ? ''
  : document.querySelector<HTMLMetaElement>('meta[name="project-github"]')?.content.trim() ?? ''

const projectFooterLinks: HeaderLink[] = [{ label: 'Главная', href: '/' }]
if (projectGitHubUrl) {
  projectFooterLinks.push({ label: 'GitHub', href: projectGitHubUrl })
}

export const defaultFooterSections: FooterSection[] = [
  {
    title: 'Проект',
    links: projectFooterLinks
  }
]