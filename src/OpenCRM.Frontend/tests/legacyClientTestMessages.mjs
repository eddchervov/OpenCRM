import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'

export function legacyClientText(locale = 'ru') {
  const suffix = { ru: '', en: '.en-US', 'zh-Hans': '.zh-Hans' }[locale]
  const root = new URL('../../OpenCRM.WebApp/', import.meta.url)
  const source = readFileSync(new URL(`Resources/Infrastructure/Localization/LocalizationStrings${suffix}.resx`, root), 'utf8')
  const messages = Object.fromEntries([...source.matchAll(/<data name="clientUi\.([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>\s*<\/data>/g)].map(match => [
    match[1], match[2].replaceAll('&quot;', '"').replaceAll('&apos;', "'").replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&')
  ]))
  const partial = readFileSync(new URL('Views/Shared/_ClientLocalization.cshtml', root), 'utf8')
  const script = partial.match(/<script>([\s\S]+)<\/script>/)[1]
    .replace('@Html.Raw(System.Text.Json.JsonSerializer.Serialize(messages))', JSON.stringify(messages))
  const window = {}
  runInNewContext(script, { window })
  return window.OpenCRMText
}
