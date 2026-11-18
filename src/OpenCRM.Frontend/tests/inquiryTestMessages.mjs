import { readFileSync } from 'node:fs'
import ts from 'typescript'
import * as localization from '@opencrm/ui/i18n'

const source = readFileSync(new URL('../packages/workspace/src/inquiryMessages.ts', import.meta.url), 'utf8')
const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS } })
new Function('require', 'exports', outputText)(() => localization, {})
