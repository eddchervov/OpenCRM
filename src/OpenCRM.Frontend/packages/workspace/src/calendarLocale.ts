import type { UiLocale } from '@opencrm/ui/i18n'

type FullCalendarLocaleText = {
  buttonText: {
    prev: string
    next: string
    prevYear: string
    nextYear: string
    year: string
    today: string
    month: string
    week: string
    day: string
    list: string
  }
  buttonHints: {
    prev: string
    next: string
    prevYear: string
    nextYear: string
    today: string
    year: string
    month: string
    week: string
    day: string
    list: string
  }
  weekText: string
  weekTextLong: string
  closeHint: string
  timeHint: string
  eventHint: string
  allDayText: string
  moreLinkText: string
  noEventsText: string
  viewHint: string
  navLinkHint: string
  moreLinkHint: (count: number) => string
}

const localeText = {
  ru: {
    buttonText: { prev: 'назад', next: 'вперед', prevYear: 'пред. год', nextYear: 'след. год', year: 'год', today: 'Сегодня', month: 'Месяц', week: 'Неделя', day: 'День', list: 'Список' },
    buttonHints: { prev: 'Предыдущий $0', next: 'Следующий $0', prevYear: 'Предыдущий год', nextYear: 'Следующий год', today: 'Перейти к сегодняшней дате', year: 'Перейти к году', month: 'Перейти к месяцу', week: 'Перейти к неделе', day: 'Перейти ко дню', list: 'Перейти к списку' },
    weekText: 'Нед.', weekTextLong: 'Неделя', closeHint: 'Закрыть', timeHint: 'Время', eventHint: 'Запись', allDayText: 'Без времени', moreLinkText: 'еще', noEventsText: 'Нет событий для отображения', viewHint: 'Представление: $0', navLinkHint: 'Перейти к $0', moreLinkHint: (count: number) => {
      const lastTwoDigits = count % 100
      let word = 'событий'
      if (lastTwoDigits < 11 || lastTwoDigits > 14) {
        if (count % 10 === 1) word = 'событие'
        else if (count % 10 >= 2 && count % 10 <= 4) word = 'события'
      }
      return `Показать еще ${count} ${word}`
    }
  },
  en: {
    buttonText: { prev: 'back', next: 'forward', prevYear: 'prev year', nextYear: 'next year', year: 'year', today: 'Today', month: 'Month', week: 'Week', day: 'Day', list: 'List' },
    buttonHints: { prev: 'Previous $0', next: 'Next $0', prevYear: 'Previous year', nextYear: 'Next year', today: 'Go to today', year: 'Go to the year', month: 'Go to the month', week: 'Go to the week', day: 'Go to the day', list: 'Go to the list' },
    weekText: 'Wk', weekTextLong: 'Week', closeHint: 'Close', timeHint: 'Time', eventHint: 'Appointment', allDayText: 'All day', moreLinkText: 'more', noEventsText: 'No events to display', viewHint: '$0 view', navLinkHint: 'Go to $0', moreLinkHint: (count: number) => `Show ${count} more event${count === 1 ? '' : 's'}`
  },
  'zh-Hans': {
    buttonText: { prev: '上一个', next: '下一个', prevYear: '上一年', nextYear: '下一年', year: '年', today: '今天', month: '月', week: '周', day: '日', list: '列表' },
    buttonHints: { prev: '上一个$0', next: '下一个$0', prevYear: '上一年', nextYear: '下一年', today: '转到今天', year: '转到年份', month: '转到月份', week: '转到周', day: '转到日期', list: '转到列表' },
    weekText: '周', weekTextLong: '周', closeHint: '关闭', timeHint: '时间', eventHint: '预约', allDayText: '全天', moreLinkText: '更多', noEventsText: '没有可显示的事件', viewHint: '$0视图', navLinkHint: '转到$0', moreLinkHint: (count: number) => `显示另外 ${count} 个预约`
  }
} satisfies Record<UiLocale, FullCalendarLocaleText>

export function fullCalendarLocaleCode(locale: UiLocale): string {
  return locale === 'zh-Hans' ? 'zh-cn' : locale
}

export function createFullCalendarLocale(locale: UiLocale) {
  const text = localeText[locale]

  return {
    code: fullCalendarLocaleCode(locale),
    week: { dow: 1, doy: 4 },
    buttonText: text.buttonText,
    buttonHints: text.buttonHints,
    weekText: text.weekText,
    weekTextLong: text.weekTextLong,
    closeHint: text.closeHint,
    timeHint: text.timeHint,
    eventHint: text.eventHint,
    allDayText: text.allDayText,
    moreLinkText: text.moreLinkText,
    noEventsText: text.noEventsText,
    viewHint: text.viewHint,
    navLinkHint: text.navLinkHint,
    moreLinkHint: text.moreLinkHint
  }
}

export function getFullCalendarLocales() {
  return (Object.keys(localeText) as UiLocale[]).map(createFullCalendarLocale)
}
