import { isAbsolute, resolve } from 'node:path'

export function resolveFrontendOutputDir(defaultOutputDir: string, hostedOutputPath: string): string {
  const outputRoot = process.env.OPENCRM_FRONTEND_OUTPUT_ROOT?.trim()
  if (!outputRoot) {
    return defaultOutputDir
  }

  if (!isAbsolute(outputRoot)) {
    throw new Error('OPENCRM_FRONTEND_OUTPUT_ROOT must be an absolute path.')
  }

  return resolve(outputRoot, hostedOutputPath)
}
