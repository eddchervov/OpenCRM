import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

const frontendRoot = fileURLToPath(new URL('..', import.meta.url))
const webRoot = path.resolve(frontendRoot, '../OpenCRM.WebApp')

function sourceFiles(directory, extension) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (['node_modules', 'dist', 'bin', 'obj'].includes(entry.name)) return []
    const target = path.join(directory, entry.name)
    return entry.isDirectory() ? sourceFiles(target, extension) : target.endsWith(extension) ? [target] : []
  })
}

test('Все поля редактирования госномера используют общую маску, включая личные Razor-формы', () => {
  const files = [...sourceFiles(frontendRoot, '.vue'), ...sourceFiles(path.join(webRoot, 'Views'), '.cshtml')]
  const fields = []
  for (const file of files) {
    const source = fs.readFileSync(file, 'utf8')
    const tags = source.matchAll(/<([\w.-]+)\b[^>]*?(?:v-model(?:\.[\w-]+)*|asp-for|name)\s*=\s*["'][^"']*registrationPlate[^"']*["'][^>]*>/gsi)
    for (const tag of tags) {
      const relative = path.relative(frontendRoot, file)
      assert.ok(tag[1] === 'VehicleRegistrationPlateInput' || /\bv-registration-plate\s*=/.test(tag[0]),
        `Поле номера без общей маски: ${relative}\n${tag[0]}`)
      fields.push(relative)
    }
  }
  assert.ok(fields.includes(path.join('packages', 'workspace', 'src', 'components', 'VehicleFormSection.vue')))
  for (const scenario of ['autoservice', 'detailing']) {
    assert.ok(fields.includes(path.join('Scenarios', `${scenario}-client`, 'src', 'components', 'QuickBookingSection.vue')))
  }
  for (const view of ['_VehicleEditForm.cshtml', '_VehicleModals.cshtml', 'VehiclePersonal.cshtml']) {
    assert.ok(fields.includes(path.join('..', 'OpenCRM.WebApp', 'Views', 'Vehicle', view)))
  }
})

test('Каждая личная страница с формой номера загружает общий bundle до своей формы', () => {
  for (const view of ['VehicleCreatePersonal.cshtml', 'VehicleEditPersonal.cshtml', 'VehiclePersonal.cshtml', 'List.cshtml']) {
    const source = fs.readFileSync(path.join(webRoot, 'Views', 'Vehicle', view), 'utf8')
    const scripts = [...source.matchAll(/<script\s+src="~\/app\/([^"]+)"/g)].map(match => match[1])
    const bundle = scripts.indexOf('vehicle-registration-plate.bundle.js')
    const form = scripts.findIndex(script => ['vehicle-edit.js', 'vehicles.js', 'vehicle-card.personal.js'].includes(script))
    assert.ok(bundle >= 0 && form > bundle, `${view}: не загружена общая маска перед формой`)
  }
})
