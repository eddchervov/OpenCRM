<script setup lang="ts">
import { ref, watch } from 'vue'
import { UiButton, UiImagePreview, UiInput, UiLoader } from '@opencrm/ui'
import type { WorkspaceWikiArticle, WorkspaceWikiData } from '../api/workspaceAdmin'

const props = defineProps<{
  wiki: WorkspaceWikiData
  selectedArticle: WorkspaceWikiArticle | null
  activeWikiSlug: string
  isArticleLoading: boolean
  articleErrorMessage: string
  previewOnly?: boolean
}>()

const wikiSearch = defineModel<string>('wikiSearch', { default: '' })
const previewScreenshot = ref<{ src: string, title: string } | null>(null)

watch(() => props.selectedArticle?.slug, () => {
  previewScreenshot.value = null
})

defineEmits<{
  search: []
  openArticle: [slug: string]
}>()
</script>

<template>
  <div class="workspace-common-stack workspace-wiki">
    <form class="workspace-common-toolbar" @submit.prevent="$emit('search')">
      <UiInput v-model="wikiSearch" placeholder="Поиск по вики" aria-label="Поиск по вики" :disabled="previewOnly" />
      <UiButton type="submit" variant="secondary" :disabled="previewOnly">Найти</UiButton>
    </form>

    <div v-if="previewOnly" class="workspace-wiki-layout workspace-wiki-preview" aria-hidden="true">
      <nav class="workspace-wiki-nav" aria-label="Статьи вики">
        <div v-for="index in 5" :key="index" class="workspace-wiki-preview-nav-item">
          <span></span><i></i>
        </div>
      </nav>
      <article class="workspace-wiki-article">
        <span class="workspace-wiki-preview-title"></span>
        <p><i></i><i></i><i></i></p>
        <section v-for="index in 3" :key="index">
          <span class="workspace-wiki-preview-subtitle"></span>
          <p><i></i><i></i><i></i></p>
        </section>
      </article>
    </div>
    <div v-if="!previewOnly && !wiki.articles.length" class="workspace-common-empty">
      {{ wiki.searchQuery ? 'По вашему запросу статьи не найдены.' : 'Раздел справки не подключён.' }}
    </div>
    <div v-if="!previewOnly" class="workspace-wiki-layout">
      <nav class="workspace-wiki-nav" aria-label="Статьи вики">
        <button
          v-for="article in wiki.articles"
          :key="article.slug"
          type="button"
          :class="{ active: article.slug === activeWikiSlug }"
          :aria-current="article.slug === activeWikiSlug ? 'page' : undefined"
          :disabled="isArticleLoading && article.slug === activeWikiSlug"
          @click="$emit('openArticle', article.slug)"
        >
          <strong>{{ article.title }}</strong>
          <span>{{ article.summary }}</span>
        </button>
      </nav>

      <article v-if="isArticleLoading" class="workspace-wiki-article workspace-wiki-article--loading">
        <UiLoader text="Загружаем статью..." />
      </article>

      <article v-else-if="articleErrorMessage" class="workspace-wiki-article">
        <div class="entity-note entity-note--error">
          <strong>Ошибка</strong>
          <p>{{ articleErrorMessage }}</p>
        </div>
      </article>

      <article v-else-if="selectedArticle" class="workspace-wiki-article">
        <h3 class="ui-section-header">{{ selectedArticle.title }}</h3>
        <p>{{ selectedArticle.summary }}</p>
        <figure v-if="selectedArticle.screenshotUrl" class="workspace-wiki-screenshot">
          <button
            type="button"
            class="workspace-wiki-screenshot-button"
            aria-label="Увеличить скриншот"
            aria-haspopup="dialog"
            @click="previewScreenshot = { src: selectedArticle.screenshotUrl, title: selectedArticle.screenshotAlt || selectedArticle.title }"
          >
            <img :src="selectedArticle.screenshotUrl" :alt="selectedArticle.screenshotAlt || selectedArticle.title" loading="lazy" />
          </button>
          <figcaption>{{ selectedArticle.screenshotAlt || selectedArticle.title }}. Нажмите, чтобы увеличить.</figcaption>
        </figure>
        <section v-for="item in selectedArticle.sections" :key="item.title">
          <h4 class="ui-section-header">{{ item.title }}</h4>
          <p v-for="paragraph in item.paragraphs" :key="paragraph">{{ paragraph }}</p>
          <ul v-if="item.bullets.length">
            <li v-for="bullet in item.bullets" :key="bullet">{{ bullet }}</li>
          </ul>
          <div v-if="item.note" class="entity-note">{{ item.note }}</div>
        </section>
      </article>
    </div>
    <UiImagePreview
      :src="previewScreenshot?.src"
      :title="previewScreenshot?.title"
      @close="previewScreenshot = null"
    />
  </div>
</template>

<style scoped>
.workspace-wiki-screenshot-button {
  display: block;
  width: 100%;
  padding: 0;
  border: 0;
  border-radius: 8px;
  background: transparent;
  cursor: zoom-in;
}

.workspace-wiki-screenshot-button:focus-visible {
  outline: 2px solid var(--crm-brand, #e87532);
  outline-offset: 3px;
}

.workspace-wiki-preview-nav-item,
.workspace-wiki-preview-title,
.workspace-wiki-preview-subtitle,
.workspace-wiki-preview p i {
  display: block;
  border-radius: 5px;
  background: var(--crm-border, #dce2e9);
  opacity: .65;
}

.workspace-wiki-preview-nav-item { display: grid; gap: 8px; padding: 12px; border-bottom: 1px solid var(--crm-border, #dce2e9); }
.workspace-wiki-preview-nav-item span { width: 72%; height: 12px; }
.workspace-wiki-preview-nav-item i { width: 92%; height: 8px; }
.workspace-wiki-preview-title { width: 48%; height: 22px; margin-bottom: 18px; }
.workspace-wiki-preview-subtitle { width: 36%; height: 16px; margin-top: 20px; }
.workspace-wiki-preview p { display: grid; gap: 9px; }
.workspace-wiki-preview p i { width: 100%; height: 9px; }
.workspace-wiki-preview p i:last-child { width: 68%; }
</style>
