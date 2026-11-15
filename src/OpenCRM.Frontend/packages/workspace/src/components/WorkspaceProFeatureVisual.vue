<script setup lang="ts">
import { computed } from 'vue'
import type { ProFeatureKey } from '../proFeatureDetails'
import { proFeatureDetails } from '../proFeatureDetails'
import teamImage from '../assets/pro/team.png'
import intakeImage from '../assets/pro/intake.png'
import statisticsImage from '../assets/pro/statistics.png'
import WorkspaceProFeaturePreview from './WorkspaceProFeaturePreview.vue'
import WorkspaceProWorkflowPreview from './WorkspaceProWorkflowPreview.vue'

const props = defineProps<{ feature: ProFeatureKey }>()
const visual = computed(() => proFeatureDetails[props.feature].visual)
const images = { team: teamImage, intake: intakeImage, statistics: statisticsImage }
const screenshotAspect = computed(() => {
  if (visual.value.type !== 'screenshot') return undefined
  const [, , width, height] = visual.value.viewBox.split(' ').map(Number)
  return `${width} / ${height}`
})
</script>

<template>
  <WorkspaceProFeaturePreview v-if="visual.type === 'preview'" :kind="visual.kind" />
  <svg v-else-if="visual.type === 'screenshot'" :viewBox="visual.viewBox" preserveAspectRatio="xMidYMin slice" role="img" :aria-label="visual.alt" class="pro-feature-visual" :style="{ aspectRatio: screenshotAspect }">
    <image :href="images[visual.image]" :width="visual.imageWidth" :height="visual.imageHeight" />
  </svg>
  <WorkspaceProWorkflowPreview v-else :feature="feature" :visual="visual" />
</template>

<style scoped>
.pro-feature-visual { display: block; width: 100%; height: auto; overflow: hidden; border-radius: 10px; background: var(--crm-surface, #fff); }
</style>
