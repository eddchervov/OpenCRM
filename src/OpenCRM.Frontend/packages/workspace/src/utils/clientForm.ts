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

export function validateClientIdentity(form: ClientIdentityForm, submitted = false, hasLegacyName = false) {
  const errors: Record<string, string> = {}
  const isCompany = form.partyTypeCode === 'company'
  if (!isCompany) {
    for (const [key, label] of [['lastName', 'Фамилия'], ['firstName', 'Имя'], ['middleName', 'Отчество']] as const) {
      if (form[key].trim().length > 60) errors[key] = `${label}: не более 60 символов.`
    }
    const hasParts = Boolean(form.lastName.trim() || form.firstName.trim() || form.middleName.trim())
    if (submitted && !form.firstName.trim() && (hasParts || !hasLegacyName)) errors.firstName = 'Укажите имя клиента.'
    if (clientDisplayName(form).length > 160) errors.fullName = 'ФИО не должно превышать 160 символов.'
  } else {
    if (submitted && !form.name.trim()) errors.name = 'Укажите название организации или ИП.'
    if (form.name.trim().length > 160) errors.name = 'Название не должно превышать 160 символов.'
    const inn = normalizeTaxCode(form.inn)
    const innLength = form.companyTypeCode === 'entrepreneur' ? 12 : 10
    if (!form.companyTypeCode && form.inn.trim().length > 20) errors.inn = 'ИНН: не более 20 символов.'
    if (!form.companyTypeCode && form.kpp.trim().length > 20) errors.kpp = 'КПП: не более 20 символов.'
    if (form.companyTypeCode && inn && !new RegExp(`^[0-9]{${innLength}}$`).test(inn)) errors.inn = `ИНН: нужно ${innLength} цифр.`
    const kpp = normalizeTaxCode(form.kpp)
    if (form.companyTypeCode && form.companyTypeCode !== 'entrepreneur' && kpp && !/^[0-9]{4}[0-9A-Z]{2}[0-9]{3}$/.test(kpp)) {
      errors.kpp = kpp.length === 9 ? 'Проверьте формат КПП.' : 'КПП: нужно 9 знаков.'
    }
    if (form.legalAddress.trim().length > 500) errors.legalAddress = 'Юридический адрес: не более 500 символов.'
  }
  if (form.phone.trim().length > 40) errors.phone = 'Телефон: не более 40 символов.'
  return errors
}
