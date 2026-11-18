<script setup lang="ts">
import { useLocalization } from '@opencrm/ui/i18n'
import '../proPreviewMessages'
import beforeAfterImage from '../assets/pro/detailing-before-after.png'

const { t } = useLocalization()

defineProps<{ kind: 'albums' | 'messages' | 'assistant' }>()
</script>

<template>
  <div class="pro-feature-preview" :class="`pro-feature-preview--${kind}`">
    <template v-if="kind === 'albums'">
      <header class="pro-feature-preview__header"><strong>{{ t('proPreview.photosAlbums') }}</strong><span>{{ t('proPreview.twoPhotos') }}</span></header>
      <div class="pro-feature-preview__album-heading"><span class="pro-feature-preview__album-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><rect x="4" y="5" width="16" height="14" rx="2" /><path d="m5 15 4-4 4 4 3-3 4 4" /><circle cx="15.5" cy="9" r="1" /></svg></span><div><strong>{{ t('proPreview.polishing') }}</strong><span>{{ t('proPreview.albumExample') }}</span></div></div>
      <div class="pro-feature-preview__photos">
        <figure><div class="pro-feature-preview__photo pro-feature-preview__photo--before"><img :src="beforeAfterImage" :alt="t('proPreview.beforeAlt')" loading="lazy" /></div><figcaption><span>{{ t('proPreview.beforeWork') }}</span><small>{{ t('proPreview.intakeCondition') }}</small></figcaption></figure>
        <figure><div class="pro-feature-preview__photo pro-feature-preview__photo--after"><img :src="beforeAfterImage" :alt="t('proPreview.afterAlt')" loading="lazy" /></div><figcaption><span>{{ t('proPreview.afterWork') }}</span><small>{{ t('proPreview.result') }}</small></figcaption></figure>
      </div>
      <footer class="pro-feature-preview__album-footer"><span>{{ t('proPreview.intake') }}</span><span>{{ t('proPreview.workResult') }}</span><strong>{{ t('proPreview.compare') }}</strong></footer>
    </template>

    <template v-else-if="kind === 'messages'">
      <div class="pro-feature-preview__inbox">
        <aside class="pro-feature-preview__threads" :aria-label="t('proPreview.chatList')">
          <div class="pro-feature-preview__threads-heading"><strong>{{ t('proPreview.chats') }}</strong><span>3</span></div>
          <div class="pro-feature-preview__thread-search">{{ t('proPreview.search') }}</div>
          <div class="pro-feature-preview__thread pro-feature-preview__thread--selected">
            <span class="pro-feature-preview__thread-avatar">{{ t('proPreview.annaInitials') }}</span>
            <div><strong>{{ t('proPreview.anna') }}</strong><small>Toyota Camry · Telegram</small><p>{{ t('proPreview.thankPickup') }}</p></div>
            <time>17:42</time>
          </div>
          <div class="pro-feature-preview__thread">
            <span class="pro-feature-preview__thread-avatar">{{ t('proPreview.mikhailInitials') }}</span>
            <div><strong>{{ t('proPreview.mikhail') }}</strong><small>Kia Sportage · MAX</small><p>{{ t('proPreview.tomorrowWash') }}</p></div>
            <span class="pro-feature-preview__thread-meta"><time>17:28</time><b>2</b></span>
          </div>
          <div class="pro-feature-preview__thread">
            <span class="pro-feature-preview__thread-avatar">{{ t('proPreview.olgaInitials') }}</span>
            <div><strong>{{ t('proPreview.olga') }}</strong><small>Hyundai Solaris · Telegram</small><p>{{ t('proPreview.thanks') }}</p></div>
            <time>16:10</time>
          </div>
        </aside>
        <div class="pro-feature-preview__conversation" :aria-label="t('proPreview.openConversation')">
          <header class="pro-feature-preview__header"><div class="pro-feature-preview__contact"><span class="pro-feature-preview__avatar">{{ t('proPreview.annaInitials') }}</span><div><strong>{{ t('proPreview.anna') }}</strong><small>Toyota Camry</small></div></div><span class="pro-feature-preview__channel">Telegram</span></header>
          <div class="pro-feature-preview__chat">
            <div class="pro-feature-preview__automation"><span aria-hidden="true">◷</span><div><strong>{{ t('proPreview.reminder') }}</strong><small>{{ t('proPreview.tomorrowChannel') }}</small></div><span>{{ t('proPreview.automatic') }}</span></div>
            <div class="pro-feature-preview__bubble pro-feature-preview__bubble--outgoing"><p>{{ t('proPreview.readyMessage') }}</p><div class="pro-feature-preview__photo pro-feature-preview__photo--after pro-feature-preview__chat-photo"><img :src="beforeAfterImage" :alt="t('proPreview.messagePhotoAlt')" loading="lazy" /></div><small>{{ t('proPreview.sentTime') }}</small></div>
            <div class="pro-feature-preview__bubble pro-feature-preview__bubble--incoming"><p>{{ t('proPreview.thankPickup') }}</p><small>17:42</small></div>
          </div>
          <footer class="pro-feature-preview__composer"><div><span>{{ t('proPreview.vehicleReady') }}</span><span>{{ t('proPreview.confirmBooking') }}</span></div><p>{{ t('proPreview.messagePlaceholder') }} <span aria-hidden="true">➤</span></p></footer>
        </div>
      </div>
    </template>

    <template v-else>
      <header class="pro-feature-preview__header"><strong><span class="pro-feature-preview__spark" aria-hidden="true">✦</span> {{ t('proPreview.assistant') }} <span class="pro-feature-preview__beta">{{ t('proPreview.beta') }}</span></strong><span>{{ t('proPreview.voiceText') }}</span></header>
      <div class="pro-feature-preview__channels"><span>{{ t('proPreview.website') }}</span><span>{{ t('proPreview.staffTelegram') }}</span></div>
      <div class="pro-feature-preview__chat">
        <div class="pro-feature-preview__bubble pro-feature-preview__bubble--outgoing"><p>{{ t('proPreview.voiceExample') }}</p><small>{{ t('proPreview.voiceDuration') }}</small></div>
        <div class="pro-feature-preview__bubble pro-feature-preview__bubble--incoming"><p>{{ t('proPreview.draftReply') }}</p><small>{{ t('proPreview.assistant') }}</small><span class="pro-feature-preview__playback"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="m4 9 4 0 5-4v14l-5-4H4ZM17 8a6 6 0 0 1 0 8M20 5a10 10 0 0 1 0 14" /></svg>{{ t('proPreview.playReply') }}</span></div>
        <div class="pro-feature-preview__draft"><header><strong>{{ t('proPreview.polishingBooking') }}</strong><span>{{ t('proPreview.draft') }}</span></header><dl><div><dt>{{ t('proPreview.client') }}</dt><dd>{{ t('proPreview.anna') }}</dd></div><div><dt>{{ t('proPreview.vehicle') }}</dt><dd>Toyota Camry</dd></div><div><dt>{{ t('proPreview.when') }}</dt><dd>{{ t('proPreview.tomorrowTime') }}</dd></div><div><dt>{{ t('proPreview.service') }}</dt><dd>{{ t('proPreview.polishing') }}</dd></div></dl><div class="pro-feature-preview__confirm">{{ t('proPreview.reviewConfirm') }}</div></div>
      </div>
      <footer class="pro-feature-preview__voice"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="3" width="6" height="12" rx="3" /><path d="M5 11a7 7 0 0 0 14 0M12 18v3M8 21h8" /></svg><span>{{ t('proPreview.dictate') }}</span></footer>
    </template>
  </div>
</template>

<style scoped>
.pro-feature-preview { overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 12px; background: var(--crm-surface, #fff); color: var(--crm-text, #202631); font-size: 12px; line-height: 1.5; }
.pro-feature-preview__header { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 14px 16px; border-bottom: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); }
.pro-feature-preview__header > span { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__header strong { font-size: 13px; }
.pro-feature-preview__beta { display: inline-block; margin-left: 4px; padding: 2px 5px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px; color: var(--crm-action-accent, #bd4d16); font-size: 9px; vertical-align: middle; letter-spacing: .04em; }
.pro-feature-preview__channels { display: flex; flex-wrap: wrap; gap: 6px; padding: 12px 14px 0; color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__channels > span { padding: 3px 7px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px; }
.pro-feature-preview__playback { display: flex; align-items: center; gap: 6px; margin-top: 8px; color: var(--crm-action-accent, #bd4d16); font-size: 10px; }
.pro-feature-preview__playback svg { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 1.5; stroke-linecap: round; stroke-linejoin: round; }
.pro-feature-preview__album-heading { display: flex; align-items: center; gap: 10px; padding: 16px; }
.pro-feature-preview__album-heading > div { display: grid; gap: 2px; }
.pro-feature-preview__album-heading div > span { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__album-icon { display: grid; width: 34px; height: 34px; place-items: center; flex: none; border-radius: 9px; background: var(--crm-accent-soft, #fff1e7); color: var(--crm-accent, #e87532); font-size: 22px; }
.pro-feature-preview__album-icon svg, .pro-feature-preview__voice svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 1.5; stroke-linecap: round; stroke-linejoin: round; }
.pro-feature-preview__photos { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; padding: 0 14px 14px; }
.pro-feature-preview figure { min-width: 0; margin: 0; overflow: hidden; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; }
.pro-feature-preview__photo { position: relative; overflow: hidden; aspect-ratio: 5 / 4; background: var(--crm-surface-soft, #f6f7f9); }
.pro-feature-preview__photo img { display: block; width: 200%; max-width: none; height: 100%; object-fit: cover; }
.pro-feature-preview__photo--after img { transform: translateX(-50%); }
.pro-feature-preview figcaption { display: grid; gap: 2px; padding: 9px 10px; }
.pro-feature-preview figcaption > span { font-weight: 700; }
.pro-feature-preview figcaption small { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__album-footer { display: flex; align-items: center; flex-wrap: wrap; gap: 6px; padding: 0 14px 14px; font-size: 10px; }
.pro-feature-preview__album-footer > span { padding: 4px 7px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px; color: var(--crm-text-muted, #687386); }
.pro-feature-preview__album-footer > strong { margin-left: auto; color: var(--crm-accent, #c8561b); }
.pro-feature-preview__contact { display: flex; align-items: center; gap: 9px; }
.pro-feature-preview__contact > div { display: grid; }
.pro-feature-preview__contact small { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__avatar { display: grid; place-items: center; width: 32px; height: 32px; border-radius: 50%; background: var(--crm-line, #e5e9ef); font-size: 10px; font-weight: 600; }
.pro-feature-preview__channel { padding: 4px 7px; border-radius: 6px; background: var(--crm-surface, #fff); }
.pro-feature-preview__chat { display: grid; gap: 10px; padding: 14px; }
.pro-feature-preview__automation { display: flex; align-items: center; gap: 8px; padding: 9px 10px; border: 1px dashed var(--crm-line, #dce2e9); border-radius: 8px; }
.pro-feature-preview__automation > span:first-child { color: var(--crm-accent, #e87532); font-size: 20px; }
.pro-feature-preview__automation > div { display: grid; }
.pro-feature-preview__automation strong { font-size: 10px; }
.pro-feature-preview__automation small { color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__automation > span:last-child { margin-left: auto; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__bubble { max-width: 90%; padding: 10px 12px; border-radius: 10px; }
.pro-feature-preview__bubble--outgoing { justify-self: end; background: color-mix(in srgb, var(--crm-accent, #e87532) 11%, var(--crm-surface, #fff)); border-bottom-right-radius: 3px; }
.pro-feature-preview__bubble--incoming { justify-self: start; border: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); border-bottom-left-radius: 3px; }
.pro-feature-preview__bubble p { margin: 0; font-size: 11px; }
.pro-feature-preview__bubble > small { display: block; margin-top: 5px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__chat-photo { width: 135px; height: 80px; margin-top: 8px; border-radius: 6px; }
.pro-feature-preview__composer { padding: 0 14px 14px; }
.pro-feature-preview__composer > div { display: flex; flex-wrap: wrap; gap: 6px; }
.pro-feature-preview__composer div > span { padding: 4px 7px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 5px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__composer p { display: flex; justify-content: space-between; gap: 10px; margin: 9px 0 0; padding: 10px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 7px; color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__composer p > span, .pro-feature-preview__spark { color: var(--crm-accent, #e87532); }
.pro-feature-preview__draft { padding: 12px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 9px; }
.pro-feature-preview__draft > header { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.pro-feature-preview__draft header > span { padding: 3px 5px; border-radius: 4px; background: var(--crm-surface-soft, #f6f7f9); color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__draft dl { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin: 12px 0; }
.pro-feature-preview__draft dt { color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__draft dd { margin: 2px 0 0; font-size: 11px; font-weight: 600; }
.pro-feature-preview__confirm { padding: 8px; border-radius: 6px; background: var(--crm-accent-soft, #fff1e7); color: var(--crm-accent, #c8561b); font-size: 10px; font-weight: 700; text-align: center; }
.pro-feature-preview__voice { display: flex; align-items: center; justify-content: center; flex-wrap: wrap; gap: 6px; padding: 10px 14px 14px; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__voice svg { color: var(--crm-accent, #e87532); }
.pro-feature-preview__voice strong { font-weight: 400; }
.pro-feature-preview--messages { container-type: inline-size; }
.pro-feature-preview__inbox { display: grid; grid-template-columns: minmax(155px, .8fr) minmax(0, 1.2fr); }
.pro-feature-preview__threads { min-width: 0; border-right: 1px solid var(--crm-line, #dce2e9); background: var(--crm-surface-soft, #f6f7f9); }
.pro-feature-preview__threads-heading { display: flex; align-items: center; justify-content: space-between; padding: 13px 12px 10px; font-size: 12px; }
.pro-feature-preview__threads-heading > span { color: var(--crm-text-muted, #687386); font-size: 10px; }
.pro-feature-preview__thread-search { padding: 7px 8px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 6px; margin: 0 10px 9px; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__thread { display: flex; position: relative; align-items: flex-start; gap: 7px; padding: 12px 9px; border-top: 1px solid var(--crm-line, #dce2e9); }
.pro-feature-preview__thread--selected { box-shadow: inset 3px 0 var(--crm-action-accent, #b94a20); background: color-mix(in srgb, var(--crm-action-accent, #b94a20) 10%, var(--crm-surface, #fff)); }
.pro-feature-preview__thread-avatar { display: grid; flex: none; place-items: center; width: 24px; height: 24px; border: 1px solid var(--crm-line, #dce2e9); border-radius: 50%; background: var(--crm-surface, #fff); color: var(--crm-text-muted, #687386); font-size: 8px; font-weight: 600; }
.pro-feature-preview__thread > div { display: grid; min-width: 0; flex: 1; gap: 3px; padding-top: 2px; }
.pro-feature-preview__thread strong { padding-right: 23px; font-size: 10px; line-height: 1.35; }
.pro-feature-preview__thread small, .pro-feature-preview__thread p { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pro-feature-preview__thread small { color: var(--crm-text-muted, #687386); font-size: 8px; }
.pro-feature-preview__thread p { margin: 0; color: var(--crm-text-muted, #687386); font-size: 9px; }
.pro-feature-preview__thread time { position: absolute; top: 15px; right: 8px; color: var(--crm-text-muted, #687386); font-size: 7px; }
.pro-feature-preview__thread-meta b { position: absolute; bottom: 10px; right: 8px; display: grid; width: 14px; height: 14px; place-items: center; border-radius: 50%; background: var(--crm-action-accent, #b94a20); color: var(--crm-surface, #fff); font-size: 8px; }
.pro-feature-preview__conversation { min-width: 0; }
.pro-feature-preview__conversation .pro-feature-preview__header { padding: 12px; }
.pro-feature-preview__conversation .pro-feature-preview__header strong { font-size: 11px; }
.pro-feature-preview__conversation .pro-feature-preview__channel { font-size: 8px; }
.pro-feature-preview__conversation .pro-feature-preview__contact { gap: 6px; }
.pro-feature-preview__conversation .pro-feature-preview__chat { padding: 10px; }
.pro-feature-preview__conversation .pro-feature-preview__automation { flex-wrap: wrap; }
.pro-feature-preview__conversation .pro-feature-preview__automation > span:last-child { display: none; }
@container (max-width: 390px) {
  .pro-feature-preview__inbox { grid-template-columns: minmax(0, 1fr); }
  .pro-feature-preview__threads { border-right: 0; border-bottom: 1px solid var(--crm-line, #dce2e9); }
  .pro-feature-preview__thread { padding: 9px 10px; }
  .pro-feature-preview__thread time { top: 12px; }
}

@media (max-width: 400px) { .pro-feature-preview__photos { gap: 6px; padding: 0 10px 10px; } .pro-feature-preview__album-footer > strong { margin-left: 0; } .pro-feature-preview__automation > span:last-child { display: none; } }
</style>
