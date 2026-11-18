import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as scheduler from '../packages/workspace/src/utils/schedulerDay.ts'
import * as localization from '../packages/ui/src/i18n.ts'
import '../packages/workspace/src/schedulerMessages.ts'
import '../packages/workspace/src/schedulerControlMessages.ts'
import '../packages/workspace/src/schedulerCalendarMessages.ts'

const date = '2026-10-30'
const booking = { id:'record', vehicleId:'car', vehicleDisplayName:'Исторический автомобиль', status:1, date, location:'Закрытый пост', plannedStartTime:'10:00', plannedEndTime:'11:00' }
function component(name) {
  const {descriptor} = parse(readFileSync(new URL(`../packages/workspace/src/components/${name}.vue`,import.meta.url),'utf8'))
  const code = ts.transpileModule(compileScript(descriptor,{id:'closed-post-test'}).content,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText
  const props = vue.reactive({items:[booking],date,posts:['Закрытый пост','Открытый пост'],closedPosts:[' закрытый ПОСТ '],start:'09:00',end:'18:00',intervals:[{startTime:'09:00',endTime:'18:00'}],canCreate:true,postsLoaded:true})
  const events = []
  const module = {exports:{}}
  new Function('require','module','exports',code)(id=>{
    if(id==='vue') return {...vue,onMounted(){},onBeforeUnmount(){},useModel:(_,name)=>vue.ref(name==='duration'?60:'')}
    if(id==='@opencrm/ui/i18n') return localization
    if(id==='../utils/schedulerDay') return scheduler
    if(id==='../utils/vehicleBrandLogo') return {getVehicleBrandLogo:()=>null}
    if(id==='../utils/workdayCard') return {shouldOpenRecord:()=>true}
    return {}
  },module,module.exports)
  const state = module.exports.default.setup(props,{expose(){},emit:(...args)=>events.push(args)})
  return {state,props,events}
}

test('Закрытый пост определяется с учётом регистра, а активный тёзка остаётся доступен',()=>{
  assert.deepEqual(scheduler.getClosedSchedulerPosts([
    {name:'Мойка',isActive:false},{name:' мойка ',isActive:true},{name:'Полировка',isActive:false}
  ]),['Полировка'])
})

test('Календарь сохраняет запись закрытого поста, но не создаёт на нём новую',()=>{
  const {state,events}=component('SchedulerDayCalendar')
  assert.equal(state.layoutForPost('Закрытый пост').length,1)
  assert.equal(state.canCreateAt('Закрытый пост',12*60),false)
  assert.equal(state.canCreateAt('Открытый пост',12*60),true)
  state.createAt('Закрытый пост',12*60)
  assert.equal(events.length,0)
  state.createAt('Открытый пост',12*60)
  assert.equal(events[0][0],'create')
  assert.equal(events[0][1].location,'Открытый пост')
})

test('Свободные слоты закрытого поста не предлагаются, занятые записи остаются видимыми',()=>{
  const {state,events}=component('SchedulerSlots')
  const closed=state.columns.value.find(column=>column.location==='Закрытый пост')
  assert.deepEqual(closed.starts,[])
  assert.equal(closed.busy[0].item.id,booking.id)
  assert.equal(closed.isClosed,true)
  state.create('Закрытый пост',12*60)
  assert.equal(events.length,0)
  state.create('Открытый пост',12*60)
  assert.equal(events[0][1].location,'Открытый пост')
})

test('Placeholder поста сравнивается целиком и локализуется, реальные пользовательские названия сохраняются', () => {
  localization.i18n.global.locale.value = 'en'
  const { state, props } = component('SchedulerDayCalendar')
  props.posts = ['Без подъёмника']
  assert.equal(state.placeholderPost.value, 'No bay')
  assert.deepEqual(state.postOptions.value, ['Без подъёмника'])
  props.posts = ['Без поста']
  assert.equal(state.placeholderPost.value, 'Без поста')
  localization.i18n.global.locale.value = 'ru'
})

test('Свободный слот по локализованному placeholder работает с пустой локацией у записи', () => {
  localization.i18n.global.locale.value = 'en'
  const { state, props } = component('SchedulerSlots')
  props.posts = ['No bay']
  props.items = [{ ...booking, location: undefined, plannedStartTime: '10:00', plannedEndTime: '11:00' }]
  assert.equal(state.columns.value[0].busy.length, 1)
  assert.equal(state.columns.value[0].starts.includes(10 * 60), false)
  assert.equal(state.columns.value[0].starts.includes(11 * 60), true)
  localization.i18n.global.locale.value = 'ru'
})

test('Статус календаря переводится по коду, неизвестная подпись сервера сохраняется', () => {
  localization.i18n.global.locale.value = 'en'
  const { state } = component('SchedulerDayCalendar')
  assert.equal(state.statusText({ status: 2, statusName: 'Серверная подпись' }), 'In progress')
  assert.equal(state.statusText({ status: 99, statusName: 'Custom status' }), 'Custom status')
  localization.i18n.global.locale.value = 'ru'
})

test('Длительность свободных слотов переводится независимо для каждого варианта', () => {
  localization.i18n.global.locale.value = 'en'
  const { state } = component('SchedulerSlots')
  assert.deepEqual(state.durationOptions.value.map(option => option.label), [
    '30 minutes', '1 hour', '1 hour 30 minutes', '2 hours', '3 hours', '4 hours'
  ])
  localization.i18n.global.locale.value = 'ru'
})
