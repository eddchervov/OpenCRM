<script setup lang="ts">
import { computed } from 'vue'
import { VueAwesomePaginate } from 'vue-awesome-paginate'

const model = defineModel<number>({ default: 1 })

const props = withDefaults(defineProps<{
  totalItems: number
  itemsPerPage?: number
  maxPagesShown?: number
  disabled?: boolean
  label?: string
}>(), {
  itemsPerPage: 10,
  maxPagesShown: 5,
  disabled: false,
  label: ''
})

const pageCount = computed(() => Math.max(1, Math.ceil(props.totalItems / Math.max(1, props.itemsPerPage))))
const shouldShow = computed(() => props.totalItems > props.itemsPerPage)
</script>

<template>
  <nav v-if="shouldShow" class="ui-pagination" :aria-label="label || 'Pagination'">
    <VueAwesomePaginate
      v-model="model"
      :total-items="totalItems"
      :items-per-page="itemsPerPage"
      :max-pages-shown="Math.min(maxPagesShown, pageCount)"
      :disable-pagination="disabled"
      :show-ending-buttons="true"
      :hide-prev-next-when-ends="true"
      :show-breakpoint-buttons="false"
      first-page-content="«"
      last-page-content="»"
      prev-button-content="‹"
      next-button-content="›"
      pagination-container-class="pagination ui-pagination__list"
      paginate-buttons-class="page-link ui-pagination__button"
      active-page-class="active ui-pagination__button--active"
      disabled-paginate-buttons-class="disabled ui-pagination__button--disabled"
    />
  </nav>
</template>
