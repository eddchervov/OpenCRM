import { readFileSync } from 'node:fs'

export function assistantUiMessages(locale = 'ru') {
  const suffix = { ru: '', en: '.en-US', 'zh-Hans': '.zh-Hans' }[locale]
  const source = readFileSync(new URL(`../../OpenCRM.WebApp/Resources/Infrastructure/Localization/LocalizationStrings${suffix}.resx`, import.meta.url), 'utf8')
  return Object.fromEntries([...source.matchAll(/<data name="assistant\.ui\.([^"]+)"[^>]*>\s*<value>([\s\S]*?)<\/value>\s*<\/data>/g)].map(match => [
    match[1], match[2].replaceAll('&quot;', '"').replaceAll('&apos;', "'").replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&amp;', '&')
  ]))
}
