export type ClientIdentityForm = {
  name: string
  lastName: string
  firstName: string
  middleName: string
  partyTypeCode: string
  companyTypeCode: string
  phone: string
  inn: string
  kpp: string
  legalAddress: string
}

export function normalizeTaxCode(value: string) {
  return value.replace(/\s/g, '').toUpperCase()
}

export function clientInnMask(companyTypeCode: string) {
  if (!companyTypeCode) return undefined
  return companyTypeCode === 'entrepreneur' ? '############' : '##########'
}

export const clientKppMask = {
  mask: '####AA###',
  tokens: { A: { pattern: /[0-9A-Z]/, transform: (value: string) => value.toUpperCase() } }
}

export function clientDisplayName(form: ClientIdentityForm, legacyName?: string) {
  if (form.partyTypeCode === 'company') return form.name.trim()
  const parts = [form.lastName, form.firstName, form.middleName].map(value => value.trim()).filter(Boolean)
  return parts.length ? parts.join(' ') : (legacyName ?? form.name).trim()
}

export function validateClientIdentity(
  form: ClientIdentityForm,
  submitted = false,
  hasLegacyName = false,
  translate: (key: string, params?: Record<string, string | number>) => string = (_key, params) => String(params?.message ?? '')
) {
  const errors: Record<string, string> = {}
  const isCompany = form.partyTypeCode === 'company'
  if (!isCompany) {
    for (const [key, labelKey] of [['lastName', 'lastName'], ['firstName', 'firstName'], ['middleName', 'middleName']] as const) {
      if (form[key].trim().length > 60) errors[key] = translate('clients.validation.nameLength', { label: translate(`clients.${labelKey}`), limit: 60 })
    }
    const hasParts = Boolean(form.lastName.trim() || form.firstName.trim() || form.middleName.trim())
    if (submitted && !form.firstName.trim() && (hasParts || !hasLegacyName)) errors.firstName = translate('clients.validation.firstNameRequired')
    if (clientDisplayName(form).length > 160) errors.fullName = translate('clients.validation.fullNameLength', { limit: 160 })
  } else {
    if (submitted && !form.name.trim()) errors.name = translate('clients.validation.companyNameRequired')
    if (form.name.trim().length > 160) errors.name = translate('clients.validation.companyNameLength', { limit: 160 })
    const inn = normalizeTaxCode(form.inn)
    const innLength = form.companyTypeCode === 'entrepreneur' ? 12 : 10
    if (!form.companyTypeCode && form.inn.trim().length > 20) errors.inn = translate('clients.validation.taxCodeLength', { label: translate('clients.inn'), limit: 20 })
    if (!form.companyTypeCode && form.kpp.trim().length > 20) errors.kpp = translate('clients.validation.taxCodeLength', { label: translate('clients.kpp'), limit: 20 })
    if (form.companyTypeCode && inn && !new RegExp(`^[0-9]{${innLength}}$`).test(inn)) errors.inn = translate('clients.validation.innDigits', { count: innLength })
    const kpp = normalizeTaxCode(form.kpp)
    if (form.companyTypeCode && form.companyTypeCode !== 'entrepreneur' && kpp && !/^[0-9]{4}[0-9A-Z]{2}[0-9]{3}$/.test(kpp)) {
      errors.kpp = kpp.length === 9 ? translate('clients.validation.kppFormat') : translate('clients.validation.kppLength', { label: translate('clients.kpp') })
    }
    if (form.legalAddress.trim().length > 500) errors.legalAddress = translate('clients.validation.addressLength', { limit: 500 })
  }
  if (form.phone.trim().length > 40) errors.phone = translate('clients.validation.phoneLength', { limit: 40 })
  return errors
}
