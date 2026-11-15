import assert from 'node:assert/strict'
import test from 'node:test'
import { Mask } from 'maska'
import {
  createVehicleRegistrationPlateMask,
  getVehicleRegistrationPlateFormat,
  normalizeRegistrationPlate,
  matchesRegistrationPlateSearch,
  prepareRegistrationPlatePaste,
  validateRegistrationPlate
} from '../packages/workspace/src/vehicleRegistrationPlate.ts'

const config = {
  maxLength: 16,
  letterPattern: '[АВЕКМНОРСТУХ]',
  characterMappings: { A: 'А', B: 'В', C: 'С', E: 'Е', H: 'Н', K: 'К', M: 'М', O: 'О', P: 'Р', T: 'Т', X: 'Х', Y: 'У' },
  formats: [
    { code: 'car', kinds: [1, 2], masks: ['L###LL##', 'L###LL###'], pattern: '^[АВЕКМНОРСТУХ][0-9]{3}[АВЕКМНОРСТУХ]{2}[0-9]{2,3}$', example: 'А123ВС154' },
    { code: 'motorcycle', kinds: [3, 4, 5, 6, 9], masks: ['####LL##', '####LL###'], pattern: '^[0-9]{4}[АВЕКМНОРСТУХ]{2}[0-9]{2,3}$', example: '1234ВС154' },
    { code: 'trailer', kinds: [10], masks: ['LL######', 'LL#######'], pattern: '^[АВЕКМНОРСТУХ]{2}[0-9]{4}[0-9]{2,3}$', example: 'АА1234154' }
  ]
}

test('Поиск госномера сопоставляет латиницу, кириллицу и разделители', () => {
  assert.equal(matchesRegistrationPlateSearch('Е103КХ154', 'E103KX154', config), true)
  assert.equal(matchesRegistrationPlateSearch('E103KX154', 'е103 кх-154', config), true)
  assert.equal(matchesRegistrationPlateSearch('Е103КХ154', '103', config), true)
  assert.equal(matchesRegistrationPlateSearch('Е103КХ154', '104', config), false)
  assert.equal(matchesRegistrationPlateSearch('Е103КХ154', ' - ', config), false)
})

test('Легковой номер с регионом из двух или трёх цифр проходит в маске и при проверке', () => {
  for (const value of ['A123BC77', 'a123bc 777']) {
    const normalized = normalizeRegistrationPlate(value, config)
    assert.equal(validateRegistrationPlate(normalized, 1, config), '')
  }
  assert.equal(validateRegistrationPlate('А123ВС154', 2, config), '')
})

test('Маска приводит раскладку и латинские омоглифы к общему виду', () => {
  const options = createVehicleRegistrationPlateMask(1, config)
  const mask = new Mask({ ...options, preProcess: undefined })
  assert.equal(normalizeRegistrationPlate('a123bc 77', config), 'А123ВС77')
  assert.equal(mask.masked(options.preProcess('a123bc 77')), 'А123ВС77')
  assert.equal(mask.masked(options.preProcess('a123bc 777')), 'А123ВС777')
})

test('Прицеп и мото техника используют маски своего типа', () => {
  assert.equal(getVehicleRegistrationPlateFormat(10, config)?.code, 'trailer')
  assert.equal(getVehicleRegistrationPlateFormat(3, config)?.code, 'motorcycle')
  assert.equal(validateRegistrationPlate('АА1234154', 10, config), '')
  assert.equal(validateRegistrationPlate('1234ВС77', 3, config), '')
  assert.match(validateRegistrationPlate('АА1234154', 1, config), /пример/)
})

test('Режим другого формата сохраняет исходное значение без удаления символов', () => {
  const original = 'AB-123 456 X'
  assert.equal(normalizeRegistrationPlate(original, config, true), original)
  assert.equal(validateRegistrationPlate(original, 1, config, true), '')
  assert.match(validateRegistrationPlate('X', 1, config, true), /не менее двух/)
  assert.match(validateRegistrationPlate('AB/123456', 1, config, true), /только буквы, цифры/)
  assert.match(validateRegistrationPlate('01234567890123456', 1, config, true), /длиннее/)
})

test('Неполный или перепутанный номер нельзя признать автомобильным', () => {
  assert.match(validateRegistrationPlate('А776777154', 1, config), /пример/)
  assert.match(validateRegistrationPlate('А123В456С154', 1, config), /пример/)
  assert.equal(validateRegistrationPlate('А123ВС154', 99, config), '')
})

test('Неподходящая вставка остаётся целой и не превращается в похожий номер', () => {
  const pasted = 'A123B456C154'
  const result = prepareRegistrationPlatePaste(pasted, 1, config)

  assert.equal(result.value, pasted)
  assert.equal(result.preventDefault, true)
  assert.match(result.error, /пример/)
})

test('Вставка в выделенный фрагмент использует курсор и сохраняет нормальный ввод по частям', () => {
  const validReplacement = prepareRegistrationPlatePaste('9', 1, config, false, 'А123ВС77', 1, 2)
  assert.equal(validReplacement.value, 'А923ВС77')
  assert.equal(validReplacement.error, '')
  assert.equal(validReplacement.preventDefault, true)

  const partialInsertion = prepareRegistrationPlatePaste('7', 1, config, false, 'А123ВС', 6, 6)
  assert.equal(partialInsertion.value, 'А123ВС7')
  assert.equal(partialInsertion.error, '')
  assert.equal(partialInsertion.preventDefault, false)

  const partialIntoEmpty = prepareRegistrationPlatePaste('A123', 1, config)
  assert.equal(partialIntoEmpty.value, 'A123')
  assert.equal(partialIntoEmpty.error, '')
  assert.equal(partialIntoEmpty.preventDefault, false)

  const invalidInsertion = prepareRegistrationPlatePaste('X', 1, config, false, 'А123ВС', 6, 6)
  assert.equal(invalidInsertion.value, 'А123ВСX')
  assert.equal(invalidInsertion.preventDefault, true)
  assert.match(invalidInsertion.error, /пример/)
})

test('Стандартный номер принимает только разрешённые буквы, не подменяя запрещённые', () => {
  for (const letter of 'АВЕКМНОРСТУХ') {
    assert.equal(validateRegistrationPlate(`${letter}123${letter}${letter}000`, 1, config), '')
  }
  for (const letter of ['Б', 'Г', 'Д', 'Ё', 'Ж', 'З', 'И', 'Й', 'Л', 'П', 'Ф', 'Ц', 'Ч', 'Ш', 'Щ', 'Ъ', 'Ы', 'Ь', 'Э', 'Ю', 'Я', 'D', 'G']) {
    for (const [kind, value] of [[1, `А123${letter}В000`], [10, `${letter}В1234000`], [3, `1234${letter}В000`]]) {
      assert.notEqual(validateRegistrationPlate(value, kind, config), '')
      const pasted = prepareRegistrationPlatePaste(value, kind, config)
      assert.equal(pasted.value, value)
      assert.ok(pasted.error)
      assert.equal(validateRegistrationPlate(value, kind, config, true), '')
    }
  }
})
