<script setup lang="ts">
import { translate as t } from '../i18n'
import { computed, inject } from 'vue'
import { pageBreadcrumbsKey, type PageBreadcrumb } from '../pageBreadcrumbs'

const parentBreadcrumbs = inject(pageBreadcrumbsKey, undefined)

const props = defineProps<{
  page?: boolean
  title?: string
  subtitle?: string
  breadcrumbs?: PageBreadcrumb[]
}>()

const breadcrumbs = computed(() => props.breadcrumbs ?? parentBreadcrumbs?.value)

function followBreadcrumb(event: MouseEvent, item: PageBreadcrumb) {
  if (!item.onClick) return

  event.preventDefault()
  item.onClick()
}
</script>

<template>
  <section class="ui-card card" :class="{ 'ui-card--page': page }">
    <header v-if="title || subtitle || $slots.header || $slots.actions" class="ui-card__header card-header" :class="page ? 'ui-form-header' : 'ui-section-header'">
      <slot name="header">
        <div class="ui-card__title">
          <nav v-if="page && breadcrumbs && title" class="ui-card__breadcrumbs" :aria-label="t('common.breadcrumbs')">
            <ol>
              <li v-for="(item, index) in breadcrumbs" :key="index"><a v-if="item.href" :href="item.href" @click="followBreadcrumb($event, item)">{{ item.title }}</a><span v-else>{{ item.title }}</span></li>
              <li><h2 aria-current="page">{{ title }}</h2></li>
            </ol>
          </nav>
          <h2 v-else-if="title">{{ title }}</h2>
          <p v-if="subtitle">{{ subtitle }}</p>
        </div>
      </slot>

      <div v-if="$slots.actions" class="ui-card__actions">
        <slot name="actions" />
      </div>
    </header>

    <div class="ui-card__body card-body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.ui-card__breadcrumbs ol {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 6px 10px;
  margin: 0;
  padding: 0;
  list-style: none;
}
.ui-card__breadcrumbs li {
  display: flex;
  align-items: baseline;
  gap: 10px;
  min-width: 0;
  overflow-wrap: anywhere;
}
.ui-card__breadcrumbs li + li::before {
  content: '/';
  color: var(--crm-text-muted, #a7aeb8);
  font-size: 15px;
  font-weight: 400;
}
.ui-card__breadcrumbs a {
  color: var(--crm-text-muted, #707987);
  font-size: 14px;
  font-weight: 500;
  text-decoration: none;
}
.ui-card__breadcrumbs a:hover {
  color: #a94b16;
  text-decoration: underline;
}
</style>
