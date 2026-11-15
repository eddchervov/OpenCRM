<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import type * as ThreeTypes from 'three'
import type { OrbitControls } from 'three/addons/controls/OrbitControls.js'
import type { WorkPostSceneItem, WorkPostSceneState } from './types'
import { layoutPostMarkers, type PostMarker } from './postMarkers'

const props = defineProps<{
  posts: WorkPostSceneItem[]
  selectedPostId?: string
}>()
const emit = defineEmits<{
  select: [id: string]
  unavailable: []
}>()

type ScenePost = {
  group: ThreeTypes.Group
  car: ThreeTypes.Group
  floor: ThreeTypes.Mesh
  outline: ThreeTypes.LineLoop
}

const host = ref<HTMLDivElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const renderer = shallowRef<ThreeTypes.WebGLRenderer | null>(null)
const overlays = ref<Record<string, PostMarker>>({})
const isUnavailable = ref(false)
const stateLabels: Record<WorkPostSceneState, string> = {
  free: 'Свободен',
  inProgress: 'В работе',
  overdue: 'Ожидает начала',
  paused: 'Есть пауза',
  conflict: 'Несколько заказов'
}

const postsById = computed(() => new Map(props.posts.map(post => [post.id, post])))
let THREE = {} as typeof import('three')
let scene: ThreeTypes.Scene
let camera: ThreeTypes.OrthographicCamera
let cameraTarget: ThreeTypes.Vector3
let controls: OrbitControls | null = null
const postObjects = new Map<string, ScenePost>()
let world: ThreeTypes.Group | null = null
let floorMaterial: ThreeTypes.MeshStandardMaterial | null = null
let wallMaterial: ThreeTypes.MeshStandardMaterial | null = null
let resizeObserver: ResizeObserver | null = null
let themeObserver: MutationObserver | null = null
let frameId = 0
let isMounted = false
let sceneWidth = 0
let sceneDepth = 0

const postColors: Record<WorkPostSceneState, number> = {
  free: 0x62a994,
  inProgress: 0xf28b35,
  overdue: 0xe4584f,
  paused: 0xe5aa43,
  conflict: 0x9c6ade
}

function roundedBox(width: number, height: number, depth: number, radius: number) {
  const shape = new THREE.Shape()
  const halfWidth = width / 2
  const halfDepth = depth / 2
  shape.moveTo(-halfWidth + radius, -halfDepth)
  shape.lineTo(halfWidth - radius, -halfDepth)
  shape.quadraticCurveTo(halfWidth, -halfDepth, halfWidth, -halfDepth + radius)
  shape.lineTo(halfWidth, halfDepth - radius)
  shape.quadraticCurveTo(halfWidth, halfDepth, halfWidth - radius, halfDepth)
  shape.lineTo(-halfWidth + radius, halfDepth)
  shape.quadraticCurveTo(-halfWidth, halfDepth, -halfWidth, halfDepth - radius)
  shape.lineTo(-halfWidth, -halfDepth + radius)
  shape.quadraticCurveTo(-halfWidth, -halfDepth, -halfWidth + radius, -halfDepth)
  const geometry = new THREE.ExtrudeGeometry(shape, {
    depth: height,
    bevelEnabled: true,
    bevelSegments: 2,
    bevelSize: Math.min(radius / 3, height / 4),
    bevelThickness: Math.min(radius / 3, height / 4),
    curveSegments: 4
  })
  geometry.rotateX(-Math.PI / 2)
  geometry.translate(0, -height / 2, 0)
  return geometry
}

function makeMaterial(color: number, roughness = 0.72) {
  return new THREE.MeshStandardMaterial({ color, roughness })
}

function makeCar(post: WorkPostSceneItem) {
  const car = new THREE.Group()
  const color = postColors[post.state]
  const body = new THREE.Mesh(roundedBox(2.15, 0.6, 4.35, 0.5), makeMaterial(color, 0.42))
  body.castShadow = true
  body.position.y = 0.68
  car.add(body)

  const cabin = new THREE.Mesh(roundedBox(1.68, 0.82, 2.2, 0.38), makeMaterial(0x253548, 0.35))
  cabin.position.set(0, 1.13, -0.06)
  cabin.castShadow = true
  car.add(cabin)

  const roof = new THREE.Mesh(roundedBox(1.52, 0.12, 1.48, 0.32), makeMaterial(color, 0.38))
  roof.position.set(0, 1.58, -0.13)
  car.add(roof)

  const tireMaterial = makeMaterial(0x20252b, 0.9)
  for (const side of [-1, 1]) {
    for (const z of [-1.35, 1.32]) {
      const tire = new THREE.Mesh(new THREE.CylinderGeometry(0.43, 0.43, 0.24, 16), tireMaterial)
      tire.rotation.z = Math.PI / 2
      tire.position.set(side * 1.16, 0.39, z)
      tire.castShadow = true
      car.add(tire)
    }
  }

  const lampMaterial = makeMaterial(0xffedc1, 0.35)
  const rearLampMaterial = makeMaterial(0x982431, 0.35)
  for (const side of [-1, 1]) {
    const lamp = new THREE.Mesh(new THREE.BoxGeometry(0.47, 0.12, 0.12), lampMaterial)
    lamp.position.set(side * 0.61, 0.76, -2.18)
    car.add(lamp)
    const rearLamp = new THREE.Mesh(new THREE.BoxGeometry(0.47, 0.14, 0.1), rearLampMaterial)
    rearLamp.position.set(side * 0.61, 0.76, 2.18)
    car.add(rearLamp)
  }

  car.visible = post.state !== 'free' && post.count > 0
  return car
}

function createPost(post: WorkPostSceneItem, index: number, columns: number, rows: number) {
  const group = new THREE.Group()
  const column = index % columns
  const row = Math.floor(index / columns)
  group.position.set((column - (columns - 1) / 2) * 5.7, 0, (row - (rows - 1) / 2) * 7.6)

  const floor = new THREE.Mesh(new THREE.PlaneGeometry(4.9, 6.8), floorMaterial!)
  floor.rotation.x = -Math.PI / 2
  floor.position.y = 0.015
  group.add(floor)

  const corners = [
    new THREE.Vector3(-2.45, 0.04, -3.4),
    new THREE.Vector3(2.45, 0.04, -3.4),
    new THREE.Vector3(2.45, 0.04, 3.4),
    new THREE.Vector3(-2.45, 0.04, 3.4)
  ]
  const outline = new THREE.LineLoop(
    new THREE.BufferGeometry().setFromPoints(corners),
    new THREE.LineBasicMaterial({ color: postColors[post.state] })
  )
  group.add(outline)

  const car = makeCar(post)
  group.add(car)
  scene.add(group)
  postObjects.set(post.id, { group, car, floor, outline })
}

function readThemeColor(property: string, fallback: string) {
  if (!host.value) return new THREE.Color(fallback)
  const value = getComputedStyle(host.value).getPropertyValue(property).trim()
  return new THREE.Color(value || fallback)
}

function updateTheme() {
  if (!floorMaterial || !wallMaterial || !renderer.value) return
  floorMaterial.color.copy(readThemeColor('--crm-surface-soft', '#e9edf1'))
  wallMaterial.color.copy(readThemeColor('--crm-surface', '#f7f8fa'))
  renderer.value.setClearColor(readThemeColor('--crm-surface', '#f7f8fa'))
  draw()
}

function updateSelectedPost() {
  if (!renderer.value) return
  for (const [id, postObject] of postObjects) {
    const selected = id === props.selectedPostId
    const post = postsById.value.get(id)
    if (postObject.outline.material instanceof THREE.LineBasicMaterial) {
      postObject.outline.material.color.set(selected ? 0x2c7eea : postColors[post?.state ?? 'free'])
      postObject.outline.material.linewidth = selected ? 3 : 1
    }
  }
  draw()
}

function disposeObject(root: ThreeTypes.Object3D) {
  const geometries = new Set<ThreeTypes.BufferGeometry>()
  const materials = new Set<ThreeTypes.Material>()
  root.traverse(object => {
    if (object instanceof THREE.Mesh || object instanceof THREE.LineLoop) {
      geometries.add(object.geometry)
      const objectMaterials = Array.isArray(object.material) ? object.material : [object.material]
      objectMaterials.forEach(material => materials.add(material))
    }
    if (object instanceof THREE.DirectionalLight) object.shadow.dispose()
  })
  geometries.forEach(geometry => geometry.dispose())
  materials.forEach(material => {
    if (material !== floorMaterial && material !== wallMaterial) material.dispose()
  })
}

function updatePosts() {
  if (!isMounted || !renderer.value) return
  const previousWidth = sceneWidth
  const previousDepth = sceneDepth
  for (const postObject of postObjects.values()) {
    scene.remove(postObject.group)
    disposeObject(postObject.group)
  }
  postObjects.clear()

  const columns = props.posts.length <= 4 ? Math.max(1, props.posts.length) : Math.ceil(Math.sqrt(props.posts.length * 1.4))
  const rows = Math.max(1, Math.ceil(props.posts.length / columns))
  sceneWidth = Math.max(6.8, columns * 5.7)
  sceneDepth = Math.max(6.8, rows * 7.6)

  if (world) {
    scene.remove(world)
    disposeObject(world)
  }
  world = new THREE.Group()
  const platform = new THREE.Mesh(new THREE.BoxGeometry(sceneWidth + 1.6, 0.28, sceneDepth + 1.6), wallMaterial!)
  platform.position.y = -0.18
  platform.receiveShadow = true
  world.add(platform)
  const wall = new THREE.Mesh(new THREE.BoxGeometry(sceneWidth + 1.6, 1.2, 0.2), wallMaterial!)
  wall.position.set(0, 0.45, -sceneDepth / 2 - 0.7)
  world.add(wall)
  scene.add(world)
  props.posts.forEach((post, index) => createPost(post, index, columns, rows))
  updateSelectedPost()
  if (previousWidth !== sceneWidth || previousDepth !== sceneDepth) fitCamera()
}

function fitCamera() {
  if (!host.value || !renderer.value) return
  const { width, height } = host.value.getBoundingClientRect()
  if (!width || !height) return
  const aspect = width / height
  const extent = sceneWidth + sceneDepth
  const direction = camera.position.clone().sub(cameraTarget).normalize()
  camera.position.copy(cameraTarget).addScaledVector(direction, extent * 2)
  camera.far = extent * 5
  camera.lookAt(cameraTarget)
  camera.updateMatrixWorld()
  let horizontalExtent = 0
  let verticalExtent = 0
  for (const x of [-sceneWidth / 2 - 0.8, sceneWidth / 2 + 0.8]) {
    for (const z of [-sceneDepth / 2 - 0.8, sceneDepth / 2 + 0.8]) {
      for (const y of [0, 2.5]) {
        const point = new THREE.Vector3(x, y, z).applyMatrix4(camera.matrixWorldInverse)
        horizontalExtent = Math.max(horizontalExtent, Math.abs(point.x))
        verticalExtent = Math.max(verticalExtent, Math.abs(point.y))
      }
    }
  }
  const verticalSize = Math.max(verticalExtent * 2.35, horizontalExtent * 2.35 / aspect)
  camera.left = -verticalSize * aspect / 2
  camera.right = verticalSize * aspect / 2
  camera.top = verticalSize / 2
  camera.bottom = -verticalSize / 2
  camera.updateProjectionMatrix()
  controls?.update()
  draw()
}

function updateOverlays() {
  if (!host.value) return
  const bounds = host.value.getBoundingClientRect()
  const projectedPosts = []
  for (const [id, postObject] of postObjects) {
    const anchor = postObject.group.localToWorld(new THREE.Vector3(0, 0.08, 3.4))
    const projected = anchor.project(camera)
    projectedPosts.push({
      id,
      left: (projected.x + 1) / 2 * bounds.width,
      top: (-projected.y + 1) / 2 * bounds.height,
      depth: projected.z,
      occupied: (postsById.value.get(id)?.count ?? 0) > 0
    })
  }

  // Подписи не перекрывают друг друга; полный список сохраняет доступ к любому посту при плотной схеме.
  const labelWidth = window.matchMedia('(max-width: 600px)').matches ? 76 : 136
  overlays.value = layoutPostMarkers(projectedPosts, bounds.width, bounds.height, props.selectedPostId, labelWidth)
}

function draw() {
  if (!renderer.value || !host.value) return
  const { width, height } = host.value.getBoundingClientRect()
  if (!width || !height) return
  renderer.value.setSize(width, height, false)
  camera.updateProjectionMatrix()
  renderer.value.render(scene, camera)
  updateOverlays()
}

function scheduleDraw() {
  if (frameId) cancelAnimationFrame(frameId)
  frameId = requestAnimationFrame(() => {
    frameId = 0
    fitCamera()
  })
}

function changeZoom(amount: number) {
  if (!renderer.value) return
  camera.zoom = Math.min(4, Math.max(0.5, camera.zoom + amount))
  camera.updateProjectionMatrix()
  controls?.update()
  draw()
}

function resetView() {
  if (!renderer.value) return
  camera.zoom = 1
  fitCamera()
}

function rotateView(angle: number) {
  if (!controls) return
  controls.rotateLeft(angle)
  controls.update()
}

function stateLabel(post: WorkPostSceneItem) {
  return post.state === 'free' ? stateLabels.free : post.state === 'overdue' && post.count > 0 ? 'Ожидает начала' : stateLabels[post.state]
}

function vehicleLabel(post: WorkPostSceneItem) {
  const plate = post.registrationPlate && !post.vehicleLabel?.includes(post.registrationPlate) ? post.registrationPlate : ''

  return [post.vehicleLabel, plate].filter(Boolean).join(' · ')
}

function markerTitle(post: WorkPostSceneItem) {
  return `${post.name} · ${stateLabel(post)}${vehicleLabel(post) ? ` · ${vehicleLabel(post)}` : ''}`.slice(0, 140)
}

function handleContextLost(event: Event) {
  event.preventDefault()
  isUnavailable.value = true
  emit('unavailable')
}

function initializeScene() {
  if (!canvas.value || !host.value) return
  try {
    renderer.value = new THREE.WebGLRenderer({ canvas: canvas.value, antialias: true, alpha: true })
    renderer.value.setPixelRatio(Math.min(window.devicePixelRatio, 2))
    renderer.value.shadowMap.enabled = true
    renderer.value.shadowMap.type = THREE.PCFShadowMap

    floorMaterial = makeMaterial(0xe9edf1)
    wallMaterial = makeMaterial(0xf7f8fa)
    scene.background = null

    const ambientLight = new THREE.HemisphereLight(0xffffff, 0x8a97a5, 2.1)
    scene.add(ambientLight)
    const keyLight = new THREE.DirectionalLight(0xffffff, 2.5)
    keyLight.position.set(-8, 15, 8)
    keyLight.castShadow = true
    keyLight.shadow.mapSize.set(1024, 1024)
    scene.add(keyLight)

    updateTheme()
    updatePosts()
    resizeObserver = new ResizeObserver(scheduleDraw)
    resizeObserver.observe(host.value)
    themeObserver = new MutationObserver(updateTheme)
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style', 'data-crm-theme'] })
  } catch {
    renderer.value?.dispose()
    renderer.value = null
    isUnavailable.value = true
    emit('unavailable')
  }
}

watch(() => props.posts, updatePosts, { deep: true })
watch(() => props.selectedPostId, updateSelectedPost)

onMounted(async () => {
  isMounted = true
  try {
    THREE = await import('three')
    const { OrbitControls } = await import('three/addons/controls/OrbitControls.js')
    if (!isMounted) return
    scene = new THREE.Scene()
    camera = new THREE.OrthographicCamera(-10, 10, 10, -10, 0.1, 100)
    cameraTarget = new THREE.Vector3(0, 0.5, 0)
    camera.position.copy(cameraTarget).add(new THREE.Vector3(0.28, 0.9, 1.4))
    controls = new OrbitControls(camera, canvas.value!)
    controls.target.copy(cameraTarget)
    controls.enablePan = false
    controls.minPolarAngle = Math.PI / 8
    controls.maxPolarAngle = Math.PI / 2.6
    controls.minZoom = 0.5
    controls.maxZoom = 4
    controls.addEventListener('change', draw)
    controls.update()
    initializeScene()
  } catch {
    if (!isMounted) return
    isUnavailable.value = true
    emit('unavailable')
  }
})

onBeforeUnmount(() => {
  isMounted = false
  if (frameId) cancelAnimationFrame(frameId)
  resizeObserver?.disconnect()
  themeObserver?.disconnect()
  controls?.dispose()
  if (scene) disposeObject(scene)
  floorMaterial?.dispose()
  wallMaterial?.dispose()
  postObjects.clear()
  renderer.value?.dispose()
  renderer.value = null
})
</script>

<template>
  <section class="work-posts-scene" aria-label="Трёхмерная схема рабочих постов">
    <div class="work-posts-scene__toolbar" aria-label="Управление видом схемы">
      <button type="button" aria-label="Повернуть влево" title="Повернуть влево" @click="rotateView(Math.PI / 8)">↶</button>
      <button type="button" aria-label="Повернуть вправо" title="Повернуть вправо" @click="rotateView(-Math.PI / 8)">↷</button>
      <button type="button" aria-label="Приблизить схему" @click="changeZoom(0.18)">+</button>
      <button type="button" aria-label="Отдалить схему" @click="changeZoom(-0.18)">−</button>
      <button type="button" @click="resetView">100%</button>
    </div>
    <div ref="host" class="work-posts-scene__viewport">
      <canvas ref="canvas" class="work-posts-scene__canvas" aria-hidden="true" @webglcontextlost="handleContextLost" />
      <div
        v-for="(post, index) in posts"
        v-show="overlays[post.id]?.visible"
        :key="post.id"
        class="work-posts-scene__marker"
        :class="[`is-${post.state}`, { 'is-selected': selectedPostId === post.id, 'is-compact': overlays[post.id]?.compact }]"
        :style="{ left: `${overlays[post.id]?.left ?? 0}px`, top: `${overlays[post.id]?.top ?? 0}px`, '--marker-width': `${overlays[post.id]?.width ?? 116}px` }"
      >
        <button
          type="button"
          class="work-posts-scene__marker-button"
          :aria-pressed="selectedPostId === post.id"
          :title="markerTitle(post)"
          :aria-label="`${post.name}: ${post.vehicleLabel || post.registrationPlate || stateLabel(post)}. ${stateLabel(post)}${post.count > 0 ? `, работ: ${post.count}` : ''}`"
          @click="emit('select', post.id)"
        >
          <span class="work-posts-scene__marker-title">{{ overlays[post.id]?.compact ? index + 1 : post.name }}</span>
          <span v-if="!overlays[post.id]?.compact && vehicleLabel(post)" class="work-posts-scene__marker-vehicle">{{ vehicleLabel(post) }}</span>
          <span v-if="!overlays[post.id]?.compact" class="work-posts-scene__marker-state">{{ stateLabel(post) }}</span>
        </button>
      </div>
      <div v-if="isUnavailable" class="work-posts-scene__fallback">
        <p>Трёхмерная сцена недоступна в этом браузере.</p>
        <button v-for="post in posts" :key="post.id" type="button" @click="emit('select', post.id)">
          <span>{{ post.name }}</span>
          <span>{{ stateLabel(post) }}<template v-if="post.count > 0"> · {{ post.count }}</template></span>
          <span v-if="post.registrationPlate || post.vehicleLabel">{{ post.registrationPlate || post.vehicleLabel }}</span>
        </button>
      </div>
      <p v-else-if="posts.length === 0" class="work-posts-scene__empty">Нет рабочих постов</p>
    </div>
  </section>
</template>

<style scoped>
.work-posts-scene {
  position: relative;
  min-width: 0;
  overflow: hidden;
  color: var(--crm-text, #252a31);
  background: var(--crm-surface, #f7f8fa);
  border: 1px solid var(--crm-border, #d9dee5);
  border-radius: 12px;
}

.work-posts-scene__viewport {
  position: relative;
  height: clamp(300px, 44vh, 500px);
  min-height: 300px;
  overflow: hidden;
}

.work-posts-scene__canvas {
  display: block;
  width: 100%;
  height: 100%;
  cursor: grab;
  touch-action: none;
}

.work-posts-scene__canvas:active { cursor: grabbing; }

.work-posts-scene__toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 12px 12px 0;
}

.work-posts-scene__toolbar button,
.work-posts-scene__marker-button,
.work-posts-scene__fallback button {
  color: var(--crm-text, #252a31);
  background: var(--crm-surface-overlay, rgba(255, 255, 255, 0.94));
  border: 1px solid var(--crm-border, #d9dee5);
  box-shadow: 0 2px 8px rgb(20 30 40 / 12%);
}

.work-posts-scene__toolbar button {
  min-width: 34px;
  height: 34px;
  padding: 0 9px;
  border-radius: 8px;
  cursor: pointer;
}

.work-posts-scene__toolbar button:first-child,
.work-posts-scene__toolbar button:nth-child(2) {
  font-size: 20px;
  line-height: 1;
}

.work-posts-scene__marker {
  position: absolute;
  z-index: 1;
  transform: translate(-50%, 0);
  pointer-events: none;
}

.work-posts-scene__marker-button {
  display: grid;
  gap: 2px;
  width: var(--marker-width, 116px);
  padding: 5px 7px;
  border-radius: 9px;
  text-align: left;
  cursor: pointer;
  pointer-events: auto;
  line-height: 1.3;
}

.work-posts-scene__marker-button:focus-visible,
.work-posts-scene__toolbar button:focus-visible,
.work-posts-scene__fallback button:focus-visible {
  outline: 3px solid var(--crm-accent, #e9822b);
  outline-offset: 2px;
}

.work-posts-scene__marker.is-compact .work-posts-scene__marker-button {
  width: 28px;
  height: 28px;
  padding: 0;
  place-items: center;
  border-radius: 6px;
}

.work-posts-scene__marker.is-compact .work-posts-scene__marker-title {
  font-size: 11px;
}

.work-posts-scene__marker-title {
  font-size: 12px;
  font-weight: 700;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.work-posts-scene__marker-state { display: block; }

.work-posts-scene__marker-state,
.work-posts-scene__marker-vehicle {
  overflow: hidden;
  color: var(--crm-text-muted, #68717d);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.work-posts-scene__marker.is-inProgress .work-posts-scene__marker-state,
.work-posts-scene__marker.is-overdue .work-posts-scene__marker-state,
.work-posts-scene__marker.is-paused .work-posts-scene__marker-state,
.work-posts-scene__marker.is-conflict .work-posts-scene__marker-state {
  color: var(--crm-text, #252a31);
}

.work-posts-scene__marker.is-selected .work-posts-scene__marker-button {
  border-color: var(--crm-accent, #e9822b);
  box-shadow: 0 0 0 2px var(--crm-accent, #e9822b), 0 4px 12px rgb(20 30 40 / 15%);
}

.work-posts-scene__fallback {
  position: absolute;
  inset: 0;
  z-index: 3;
  display: flex;
  flex-direction: column;
  gap: 8px;
  overflow: auto;
  padding: 52px 16px 16px;
  background: var(--crm-surface, #f7f8fa);
}

.work-posts-scene__fallback p,
.work-posts-scene__empty {
  margin: 0;
  color: var(--crm-text-muted, #68717d);
}

.work-posts-scene__fallback button {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 8px;
  text-align: left;
  cursor: pointer;
}

.work-posts-scene__empty {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
}

@media (max-width: 600px) {
  .work-posts-scene__viewport { height: 280px; min-height: 260px; }

  .work-posts-scene__toolbar {
    top: 8px;
    right: 8px;
  }

  .work-posts-scene__marker-button {
    width: min(var(--marker-width, 76px), 76px);
    padding: 4px 5px;
    place-items: center;
    border-radius: 6px;
  }

  .work-posts-scene__marker-title,
  .work-posts-scene__marker-vehicle { max-width: 100%; font-size: 10px; text-align: center; }
  .work-posts-scene__marker-state { max-width: 100%; font-size: 9px; text-align: center; }

  .work-posts-scene__fallback button {
    flex-direction: column;
    gap: 3px;
  }
}
</style>
