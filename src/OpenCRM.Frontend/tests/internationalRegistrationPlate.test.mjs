import test from 'node:test'
import assert from 'node:assert/strict'
import { registrationPlateFormatsForCountry, internationalRegistrationPlateError } from '../packages/workspace/src/internationalRegistrationPlate.ts'
import { registrationCountryOptions } from '../packages/workspace/src/utils/registrationCountries.ts'
import { organizationTimeZones } from '../packages/workspace/src/utils/organizationTimeZone.ts'

test('Иностранный номер не получает российскую маску и таблицу замены букв', () => {
  const config = { maxLength: 16, letterPattern: '[АВЕКМНОРСТУХ]', characterMappings: { A: 'А' }, formats: [{ code: 'car', kinds: [1] }] }
  assert.equal(registrationPlateFormatsForCountry(config, null), config)
  assert.equal(registrationPlateFormatsForCountry(config, 'RU'), config)
  const foreign = registrationPlateFormatsForCountry(config, 'CN')
  assert.deepEqual(foreign.characterMappings, {})
  assert.deepEqual(foreign.formats, [])
  assert.equal(foreign.maxLength, 24)
  assert.deepEqual(registrationPlateFormatsForCountry(config, 'ZZ'), foreign)
})

test('Иероглифы и средняя точка допустимы, управляющие символы отклоняются', () => {
  assert.equal(internationalRegistrationPlateError('沪A·12345'), '')
  assert.equal(internationalRegistrationPlateError('AB-123 CD'), '')
  assert.equal(internationalRegistrationPlateError('A\u202e123'), 'characters')
  assert.equal(internationalRegistrationPlateError('A\n123'), 'characters')
  assert.equal(internationalRegistrationPlateError(' - · '), 'short')
  assert.equal(internationalRegistrationPlateError('A'.repeat(25)), 'length')
})

test('Страны переводятся независимо от кодов, мировые часовые пояса доступны', () => {
  for (const locale of ['ru', 'en', 'zh-Hans']) {
    const options = registrationCountryOptions(locale)
    assert.equal(new Set(options.map(option => option.value)).size, options.length)
    assert.ok(options.some(option => option.value === 'CN'))
    assert.ok(options.some(option => option.value === 'RU'))
    assert.ok(options.every(option => option.label.length > 0))
  }
  assert.ok(organizationTimeZones.some(zone => zone.value === 'Asia/Shanghai'))
  assert.ok(organizationTimeZones.some(zone => zone.value === 'America/New_York'))
})
