import sedanV1Manifest from '../assets/intake/sedan-v1/diagram.json'
import sedanV1FrontUrl from '../assets/intake/sedan-v1/front.png'
import sedanV1RearUrl from '../assets/intake/sedan-v1/rear.png'
import sedanV1LeftUrl from '../assets/intake/sedan-v1/left.png'
import sedanV1RightUrl from '../assets/intake/sedan-v1/right.png'

export const intakeDiagramRegistry = {
  'sedan-v1': {
    version: sedanV1Manifest.version,
    width: sedanV1Manifest.width,
    height: sedanV1Manifest.height,
    aspectRatio: sedanV1Manifest.width + ' / ' + sedanV1Manifest.height,
    views: {
      front: { imageUrl: sedanV1FrontUrl, file: sedanV1Manifest.views.front.file, sha256: sedanV1Manifest.views.front.sha256 },
      rear: { imageUrl: sedanV1RearUrl, file: sedanV1Manifest.views.rear.file, sha256: sedanV1Manifest.views.rear.sha256 },
      left: { imageUrl: sedanV1LeftUrl, file: sedanV1Manifest.views.left.file, sha256: sedanV1Manifest.views.left.sha256 },
      right: { imageUrl: sedanV1RightUrl, file: sedanV1Manifest.views.right.file, sha256: sedanV1Manifest.views.right.sha256 }
    }
  }
} as const

export type IntakeDiagramVersion = keyof typeof intakeDiagramRegistry

export function getIntakeDiagram(version: string | null | undefined) {
  if (!version || !Object.hasOwn(intakeDiagramRegistry, version)) return null

  return intakeDiagramRegistry[version as IntakeDiagramVersion] ?? null
}
