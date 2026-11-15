import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { parse, compileScript } from '@vue/compiler-sfc'
import ts from 'typescript'
import * as vue from 'vue'
import * as scheduler from '../packages/workspace/src/utils/schedulerDay.ts'

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
