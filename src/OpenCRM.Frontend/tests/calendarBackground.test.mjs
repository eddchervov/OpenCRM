import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../../OpenCRM.WebApp/wwwroot/lib/fullcalendar/index.global.min.js', import.meta.url), 'utf8')
const parsed = ts.createSourceFile('fullcalendar.js', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.JS)
const declarations = new Map()
function collect(node) {
  if ((ts.isFunctionDeclaration(node) || ts.isMethodDeclaration(node)) && node.name) {
    declarations.set(node.name.getText(parsed), node.getText(parsed))
  }
  ts.forEachChild(node, collect)
}
collect(parsed)
const renderFillSegs = new Function('p', 'y', 'go', 'mi', 'gi', 'ho',
  declarations.get('Xl') + ';' + declarations.get('tc') + ';return ({' + declarations.get('renderFillSegs') + '}).renderFillSegs'
)(
  (type, props, ...children) => ({ type, props, children }),
  'fragment', type => type, () => 'segment', () => ({}), 'background'
)

for (const kind of ['non-business', 'bg-event', 'highlight']) {
  test(kind + ': фон короткого интервала не растягивается до высоты карточки', () => {
    const segments = [{ start: 16 * 60, end: 16 * 60 + 15 }, { start: 12 * 60, end: 13 * 60 }]
    const context = {
      props: { date: 0, slatCoords: { computeDateTop: value => value * 48 / 60 } },
      context: { options: { eventMinHeight: 60 } }
    }
    const rendered = renderFillSegs.call(context, segments, kind)
    const harnesses = rendered.children[0]
    assert.equal(harnesses.length, 2)
    assert.equal(-harnesses[0].props.style.bottom - harnesses[0].props.style.top, 12)
    assert.equal(-harnesses[1].props.style.bottom - harnesses[1].props.style.top, 48)
  })
}
