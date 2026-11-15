# Voice assistant commands

**English** | [Русский](voice-ai-actions.ru.md)

The complete catalog of 75 executable commands opens through “Доступные команды” (Available commands) in the CRM workspace's `#assistant` section. Use one command per phrase. The assistant first reads the object through the ordinary API and shows the proposed changes; writes occur after confirmation. Deletion and cancellation require an additional checkbox. Select a specific work order when several match; specify the line number when work items share a name.

| Area | Available actions |
| --- | --- |
| Work order | Create a completed visit or preliminary appointment; accept, start, reschedule, change fields/dates, discount/cost, cancel/delete, return to planned/in-progress, complete, mark ready, hand over, and undo handover according to the organization's current workflow. |
| Work items | Add; change name/price/quantity/description/VAT/assignee; delete a line; replace the list. Other lines, materials, and fields are preserved. |
| Payments | Record an individual payment and payer; cancel a specific payment with a reason. No real money is charged. |
| Intake | Mileage, supplied items, damage, notes, employee, and work bay. |
| Vehicles and customers | Create, find, update, and link a vehicle to a customer; archive/restore vehicles; customer details, phone, tags, and notes. |
| Catalogs | View, create, update, and delete entries and work bays; archive through the active flag; explicit service bundle composition. |
| People and shifts | Employee records, deactivation, links to an existing member without changing their role; create/update/cancel shifts, publish and copy a week. |
| Inventory | Stock levels, receipts, write-offs, quantity/cost adjustments, liter/milliliter and kilogram/gram conversion. |
| Defects and readings | Create, update, close, and delete defects; add or delete a vehicle reading. |
| Execution | Start, pause, resume, and complete an operation; adjust minutes with a reason; text report, proposal decision; material consumption and full return for an operation. |
| Read operations | Schedule and financial report for a period. |

## Test phrases

The following Russian phrases are retained as executable examples; English explanations describe their meaning, not a separately verified English voice interface.

- «Добавь в А123ВС154 работу замена фильтра за пятьсот рублей» — add a filter replacement for 500 rubles to А123ВС154.
- «Измени цену замены фильтра в А123ВС154 на семьсот рублей» — change the filter replacement price to 700 rubles.
- «Удали работу замена фильтра из А123ВС154» — remove that filter replacement work item.
- «Перенеси А123ВС154 на послезавтра с двенадцати до часа дня» — reschedule for the day after tomorrow, noon to 1 p.m.
- «Для А123ВС154 измени комментарий на ожидаем запчасти» — change the comment to awaiting parts.
- «Для А123ВС154 установи скидку сто рублей» — set a 100-ruble discount.
- «Прими оплату семьсот рублей наличными за А123ВС154» — record a 700-ruble cash payment.
- «Отмени оплату семьсот рублей за А123ВС154, причина — ошибочная запись» — cancel the 700-ruble payment because it was entered in error.
- «Создай клиента Иван Тестовый с телефоном 79001234567» — create customer Иван Тестовый with the example phone number.
- «Привяжи А123ВС154 к клиенту Иван Тестовый» — link the vehicle to that customer.
- «Измени телефон клиента Иван Тестовый на 79001234568» — update that customer's example phone number.
- «Добавь сотрудника Сергей» — add employee Сергей.
- «Назначь Сергея на заказ А123ВС154» — assign Сергей to the work order.
- «Создай пост Бокс номер два» — create a work bay named Бокс номер два.
- «Назначь пост Бокс номер два для А123ВС154» — assign that bay to the work order.
- «Создай смену Сергею завтра с девяти до восемнадцати» — create a shift for Сергей tomorrow, 09:00–18:00.
- «Прими на склад пять литров масла по двести рублей за литр» — receive five liters of oil at 200 rubles per liter.
- «Поставь замену масла А123ВС154 на паузу, причина — ждём фильтр» — pause the oil change while waiting for a filter.
- «Продолжи замену масла А123ВС154» — resume the oil change.
- «Добавь пятнадцать минут к замене масла А123ВС154, причина — забыл включить таймер» — add 15 minutes because the timer was not started.
- «Покажи финансы с первого по двадцать шестое октября» — show finances for October 1–26.

“Сделали” (work was done) creates a separate completed visit; “закончили” (finished) completes the existing work order found. Before creating an appointment, the assistant asks for at least one work item. New work items are matched against the catalog: an exact match links the service, no match creates a custom line, and similar names require a choice. For planned appointments, an unspecified price comes from the selected active catalog service. If no price exists, the assistant asks for each such item's price; “пока неизвестно” (not known yet) leaves it empty for later clarification. An employee-specified price, including zero, takes precedence. One item's amount can be supplied as a total cost; the assistant does not allocate an overall amount across multiple items. Service selection, date, time, and bay are preserved during price answers; a changed catalog price requires draft revalidation. The last work item cannot be deleted; add a replacement first. Completion requires prices and completed operations under ordinary CRM rules. Unmentioned fields are not cleared.

The model cannot control code, SQL, API URLs, plans, or roles. Files/photos and external campaigns are not connected to the pilot. Absolute past intervals cannot yet be edited; the current timer and minute adjustments are available. See the [assistant description](ai-assistant.md) for isolation, budgets, and validation.
