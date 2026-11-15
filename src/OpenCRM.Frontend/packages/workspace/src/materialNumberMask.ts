import type { MaskInputOptions } from 'maska'

export function createMaterialNumberMask(decimals: number): MaskInputOptions {
  let previous = ''

  return {
    preProcess(value) {
      const raw = value.replace(/\s/g, '').replace(',', '.')
      if (!/^\d*(?:\.\d*)?$/.test(raw)) return previous

      const [whole, fraction] = raw.split('.')
      let normalized = whole
      if (fraction != null) normalized += `.${fraction}`
      previous = normalized

      return normalized
    },
    mask(value) {
      const [whole, fraction] = value.split('.')
      return '#'.repeat(Math.max(1, whole.length))
        + (fraction != null ? `.${'#'.repeat(Math.max(decimals, fraction.length))}` : '')
    }
  }
}
