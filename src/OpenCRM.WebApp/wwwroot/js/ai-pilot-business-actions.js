(() => {
    'use strict';

    const catalogGroups = {
        service: 'services',
        service_bundle: 'serviceBundles',
        work_post: 'workPosts',
        work_type: 'workTypes',
        document: 'documents',
        planning_rule: 'planningRules',
        material: 'materials'
    };
    const catalogNames = { service: 'Услуги', service_bundle: 'Наборы услуг', work_post: 'Посты', work_type: 'Виды работ', document: 'Документы', planning_rule: 'Правила планирования', material: 'Материалы' };
    const fieldNames = { name: 'Название', description: 'Описание', unitPrice: 'Цена', durationMinutes: 'Длительность, мин.', isActive: 'Активность', sortOrder: 'Порядок', serviceIds: 'Услуги набора' };

    function required(value, label) {
        if (value === null || value === undefined || value === '') {
            throw new Error(`Укажите ${label}.`);
        }
        return value;
    }

    function text(value, label, maximum = 500) {
        const result = String(required(value, label)).trim();
        if (!result || result.length > maximum) {
            throw new Error(`Проверьте поле «${label}».`);
        }
        return result;
    }

    function amount(value, label, { allowZero = true } = {}) {
        const result = Number(required(value, label));
        if (!Number.isFinite(result) || result < (allowZero ? 0 : Number.EPSILON) || result > 10_000_000) {
            throw new Error(`Проверьте сумму в поле «${label}».`);
        }
        return result;
    }

    function quantity(value, label = 'количество') {
        const result = Number(required(value, label));
        if (!Number.isFinite(result) || result <= 0 || result > 1_000_000) {
            throw new Error(`Проверьте поле «${label}».`);
        }
        return result;
    }

    function wholeQuantity(value, label) {
        const result = quantity(value, label);
        if (!Number.isInteger(result)) throw new Error(`Поле «${label}» должно быть целым числом.`);
        return result;
    }

    function materialUnit(value) {
        const unit = String(value || 'шт').trim().toLocaleLowerCase('ru-RU');
        const aliases = { 'шт.': 'шт', штук: 'шт', штука: 'шт', литр: 'л', литра: 'л', литров: 'л', килограмм: 'кг', килограммов: 'кг', грамм: 'г', граммов: 'г', миллилитр: 'мл', миллилитров: 'мл', упаковка: 'уп', упаковок: 'уп' };
        const normalized = aliases[unit] || unit;
        if (!['шт', 'л', 'мл', 'кг', 'г', 'м', 'м2', 'компл', 'уп'].includes(normalized)) throw new Error('Назовите единицу материала: шт, л, мл, кг, г, м, м2, компл или уп.');

        return normalized;
    }

    function stockQuantity(value, unit, stock) {
        const requested = materialUnit(unit || stock.unit);
        const stored = materialUnit(stock.unit);
        let factor = 1;
        if (requested === 'уп') {
            if (!(stock.packageQuantity > 0)) throw new Error('Для материала не указан размер упаковки.');
            factor = stock.packageQuantity;
        } else if (requested !== stored) {
            if (requested === 'л' && stored === 'мл' || requested === 'кг' && stored === 'г') factor = 1000;
            else if (requested === 'мл' && stored === 'л' || requested === 'г' && stored === 'кг') factor = 0.001;
            else throw new Error(`Единица ${requested} не соответствует складской единице ${stored}.`);
        }
        const converted = value * factor;
        if (Math.abs(converted - Math.round(converted * 1000) / 1000) > 0.0000001) throw new Error('После перевода единиц допустимы три знака после запятой.');

        return Math.round(converted * 1000) / 1000;
    }

    function wholeAmount(value, label) {
        const result = amount(value, label);
        if (!Number.isInteger(result)) throw new Error(`Поле «${label}» должно быть целым числом.`);
        return result;
    }

    function date(value) {
        const result = text(value, 'дату', 10);
        const parsed = new Date(`${result}T00:00:00Z`);
        if (!/^\d{4}-\d{2}-\d{2}$/.test(result) || Number.isNaN(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== result) {
            throw new Error('Укажите дату в формате ГГГГ-ММ-ДД.');
        }
        return result;
    }

    function list(value) {
        if (Array.isArray(value)) return value;
        if (Array.isArray(value?.items)) return value.items;
        return [];
    }

    function progressSnapshot(value) {
        return list(value).map(item => ({ detailId: item.detailId, name: item.name, status: item.status,
            version: item.version, employeeName: item.employeeName, hasAssignedEmployee: item.hasAssignedEmployee }));
    }

    function findUnique(items, name, label) {
        if (!Array.isArray(items) || !items.length) throw new Error(`Не удалось найти ${label}.`);
        const normalized = String(required(name, label)).trim().toLocaleLowerCase('ru-RU');
        const matches = items.filter(item => String(item.name ?? item.title ?? '').trim().toLocaleLowerCase('ru-RU') === normalized);
        if (matches.length !== 1) {
            throw new Error(matches.length ? `Найдено несколько совпадений: ${label}. Уточните название.` : `Не найдено: ${label}.`);
        }
        return matches[0];
    }

    function chooseByNumber(items, number, label) {
        const position = Number(required(number, `номер строки ${label}`));
        if (!Number.isInteger(position) || position < 1 || position > items.length) {
            throw new Error(`Укажите номер строки ${label} от 1 до ${items.length}.`);
        }
        return items[position - 1];
    }

    function chooseOneOrByNumber(items, number, label) {
        if (number == null && items.length === 1) return items[0];
        return chooseByNumber(items, number, label);
    }

    function queryPath(path, key, value) {
        const query = new URLSearchParams();
        query.set(key, String(value));
        return `${path}?${query}`;
    }

    function pathForWork(r, maintenanceId) {
        return `${r.org}/work/${encodeURIComponent(maintenanceId)}`;
    }

    function appendPlan(r, values) {
        return r.plan(values);
    }

    async function prepareCatalog(action, r) {
        const p = r.p;
        if (action === 'catalog_list' || action === 'location_list') {
            const catalog = await r.read(`${r.org}/scenario-catalog`);
            const requestedKind = action === 'location_list' ? 'work_post' : (p.catalogKind || null);
            if (requestedKind && !catalogGroups[requestedKind]) throw new Error('Неизвестный вид справочника.');
            let items = requestedKind ? list(catalog[catalogGroups[requestedKind]]) : Object.values(catalogGroups).flatMap(key => list(catalog[key]));
            if (p.name) {
                const normalizedName = text(p.name, 'название', 150).toLocaleLowerCase('ru-RU');
                items = items.filter(item => String(item.name ?? '').trim().toLocaleLowerCase('ru-RU') === normalizedName);
                if (items.length !== 1) throw new Error(items.length ? 'Найдено несколько совпадений. Уточните вид справочника.' : 'Позиция не найдена.');
            }
            return appendPlan(r, {
                target: requestedKind ? `Справочник: ${catalogNames[requestedKind]}` : 'Справочники СТО',
                changes: items.slice(0, 30).map(item => `${item.name}${item.isActive === false ? ' (неактивно)' : ''}`),
                data: items
            });
        }

        const catalogKind = action.startsWith('location_') ? 'work_post' : text(p.catalogKind, 'вид справочника', 40);
        const group = catalogGroups[catalogKind];
        if (!group) throw new Error('Этот вид справочника недоступен.');
        const catalog = await r.read(`${r.org}/scenario-catalog`);
        const items = list(catalog[group]);
        if (action === 'catalog_create' || action === 'location_create') {
            const name = text(p.name, 'название', 150);
            const payload = {
                kindCode: catalogKind,
                name,
                description: p.description == null ? null : text(p.description, 'описание'),
                unitPrice: (p.unitPrice ?? p.price) == null ? null : amount(p.unitPrice ?? p.price, 'стоимость'),
                durationMinutes: p.durationMinutes == null ? null : wholeQuantity(p.durationMinutes, 'длительность'),
                serviceIds: p.serviceNames ? String(p.serviceNames).split(';').map(name => findUnique(list(catalog.services), name.trim(), 'услугу набора').id) : [],
                sortOrder: p.sortOrder == null ? items.length : wholeAmount(p.sortOrder, 'порядок')
            };
            if (catalogKind === 'service_bundle' && !payload.serviceIds.length) throw new Error('Назовите входящие в набор услуги через точку с запятой.');
            r.watch(`${r.org}/scenario-catalog`, catalog);
            return appendPlan(r, {
                target: `Справочник: ${catalogNames[catalogKind]}`,
                changes: [`Добавить «${name}»`, ...(payload.unitPrice != null ? [`Цена: ${payload.unitPrice} ₽`] : []),
                    ...(payload.durationMinutes != null ? [`Длительность: ${payload.durationMinutes} мин.`] : []), ...(payload.description ? [`Описание: ${payload.description}`] : []),
                    ...(payload.serviceIds.length ? [`Услуги набора: ${payload.serviceIds.map(id => list(catalog.services).find(item => item.id === id)?.name || id).join('; ')}`] : [])],
                requests: [{ path: `${r.org}/scenario-catalog`, method: 'POST', body: payload }],
                result: { url: r.context.workspaceUrl }
            });
        }

        const item = findUnique(items, p.name, 'элемент справочника');
        if (item.isSystem) throw new Error('Системный элемент справочника нельзя изменить через AI.');
        if (action === 'catalog_delete' || action === 'location_delete') {
            if (catalogKind === 'work_post') {
                const affected = await r.read(`${r.org}/scenario-catalog/${encodeURIComponent(item.id)}/affected-maintenance`);
                if (affected.totalCount > 0) throw new Error(`У поста есть назначения (${affected.totalCount}). Сначала перенесите связанные записи в CRM.`);
                r.watch(`${r.org}/scenario-catalog/${encodeURIComponent(item.id)}/affected-maintenance`, affected);
            }
            if (item.isUsed) throw new Error('Справочник используется существующими заказами. Архивация через AI пока не поддерживается.');
            r.watch(`${r.org}/scenario-catalog`, catalog);
            return appendPlan(r, {
                target: item.name,
                changes: [`Удалить из справочника «${item.name}»`],
                destructive: true,
                requests: [{ path: `${r.org}/scenario-catalog/${encodeURIComponent(item.id)}`, method: 'DELETE' }]
            });
        }

        if (action === 'catalog_update' || action === 'location_update') {
            const payload = {
                name: p.newName == null ? item.name : text(p.newName, 'новое название', 150),
                description: p.description == null ? item.description ?? null : p.description === '' ? null : text(p.description, 'описание'),
                unitPrice: (p.unitPrice ?? p.price) == null ? item.unitPrice ?? null : amount(p.unitPrice ?? p.price, 'стоимость'),
                durationMinutes: p.durationMinutes == null ? item.durationMinutes ?? null : wholeQuantity(p.durationMinutes, 'длительность'),
                serviceIds: p.serviceNames ? String(p.serviceNames).split(';').map(name => findUnique(list(catalog.services), name.trim(), 'услугу набора').id) : item.serviceIds ?? [],
                sortOrder: p.sortOrder == null ? item.sortOrder : wholeAmount(p.sortOrder, 'порядок'),
                isActive: p.isActive == null ? item.isActive : Boolean(p.isActive)
            };
            r.watch(`${r.org}/scenario-catalog`, catalog);
            return appendPlan(r, {
                target: item.name,
                changes: Object.entries(payload).filter(([key]) => Object.hasOwn(fieldNames, key))
                    .filter(([key]) => JSON.stringify(payload[key]) !== JSON.stringify(item[key])).map(([key, value]) => key === 'serviceIds'
                        ? `Услуги набора: ${(item.serviceIds || []).map(id => list(catalog.services).find(entry => entry.id === id)?.name || id).join('; ')} → ${value.map(id => list(catalog.services).find(entry => entry.id === id)?.name || id).join('; ')}`
                        : `${fieldNames[key]}: ${item[key] ?? 'не указано'} → ${value ?? 'очистить'}`),
                requests: [{ path: `${r.org}/scenario-catalog/${encodeURIComponent(item.id)}`, method: 'PUT', body: payload }]
            });
        }

        throw new Error('Действие со справочником не поддерживается.');
    }

    async function prepareEmployees(action, r) {
        const p = r.p;
        const data = await r.read(`${r.org}/employees`);
        const employees = list(data);
        if (action === 'employee_list') {
            return appendPlan(r, { target: 'Сотрудники', changes: employees.map(item => `${item.name}${item.isActive ? '' : ' (неактивен)'}`), data: employees });
        }

        if (!data.canManage) throw new Error('У пользователя нет права управлять сотрудниками.');
        if (action === 'employee_create') {
            const name = text(p.name ?? p.newName, 'имя сотрудника', 150);
            if (employees.some(item => String(item.name).trim().toLocaleLowerCase('ru-RU') === name.toLocaleLowerCase('ru-RU'))) {
                throw new Error('Сотрудник с таким именем уже есть. Уточните существующую запись.');
            }
            const employee = { name, phone: p.phone ? text(p.phone, 'телефон', 50) : null, memberId: null, isActive: p.isActive !== false, version: 0 };
            r.watch(`${r.org}/employees`, data);
            return appendPlan(r, {
                target: 'Сотрудники', changes: [`Добавить сотрудника ${name}`, `Телефон: ${employee.phone || 'не указан'}`, `Активен: ${employee.isActive ? 'да' : 'нет'}`],
                requests: [{ path: `${r.org}/employees`, method: 'POST', body: employee }]
            });
        }

        const employee = findUnique(employees, p.employeeName, 'сотрудника');
        if (action === 'employee_link') {
            if (!data.canLinkMembers) throw new Error('Нет права связывать сотрудников с участниками.');
            if (p.memberEmail === undefined) throw new Error('Назовите email участника или явно попросите снять связь.');
            const member = p.memberEmail ? findUnique((data.members || []).map(item => ({ ...item, name: item.email || item.userName })), p.memberEmail, 'участника') : null;
            r.watch(`${r.org}/employees`, data);
            return appendPlan(r, { target: employee.name, destructive: true, changes: [`Связь с участником: ${p.memberEmail || 'снять связь'}. Роль пользователя сохраняется.`],
                requests: [{ path: `${r.org}/employees/${employee.id}/member`, method: 'PUT', body: { memberId: member?.id || null, version: employee.version } }] });
        }
        if (action === 'employee_deactivate') {
            if (!employee.isActive) throw new Error('Этот сотрудник уже неактивен.');
            const body = { id: employee.id, name: employee.name, phone: employee.phone ?? null, memberId: employee.memberId ?? null, isActive: false, version: employee.version };
            r.watch(`${r.org}/employees`, data);
            return appendPlan(r, {
                target: employee.name, changes: ['Сделать сотрудника неактивным'], destructive: true,
                requests: [{ path: `${r.org}/employees/${encodeURIComponent(employee.id)}`, method: 'PUT', body }]
            });
        }

        if (action === 'employee_update') {
            const body = {
                id: employee.id,
                name: p.newName == null ? employee.name : text(p.newName, 'новое имя', 150),
                phone: p.phone == null ? employee.phone ?? null : p.phone === '' ? null : text(p.phone, 'телефон', 50),
                memberId: employee.memberId ?? null,
                isActive: p.isActive == null ? employee.isActive : Boolean(p.isActive),
                version: employee.version
            };
            r.watch(`${r.org}/employees`, data);
            return appendPlan(r, {
                target: employee.name, changes: [`Имя: ${employee.name} → ${body.name}`, `Телефон: ${employee.phone || 'не указан'} → ${body.phone || 'очистить'}`, `Активен: ${body.isActive ? 'да' : 'нет'}`],
                requests: [{ path: `${r.org}/employees/${encodeURIComponent(employee.id)}`, method: 'PUT', body }]
            });
        }

        throw new Error('Действие с сотрудником не поддерживается.');
    }

    function weekStart(value) {
        const dateValue = date(value);
        const parsed = new Date(`${dateValue}T12:00:00Z`);
        const offset = (parsed.getUTCDay() + 6) % 7;
        parsed.setUTCDate(parsed.getUTCDate() - offset);
        return parsed.toISOString().slice(0, 10);
    }

    function localDateTime(day, time) {
        const dayValue = date(day);
        const timeValue = text(time, 'время', 5);
        if (!/^\d{2}:\d{2}$/.test(timeValue) || Number(timeValue.slice(0, 2)) > 23 || Number(timeValue.slice(3)) > 59) {
            throw new Error('Укажите время в формате ЧЧ:ММ.');
        }
        return `${dayValue}T${timeValue}:00`;
    }

    function timeMinutes(value) {
        return Number(value.slice(11, 13)) * 60 + Number(value.slice(14, 16));
    }

    function ensureNoShiftOverlap(shifts, startLocal, endLocal, excludedShiftId = null) {
        const conflict = shifts.find(item => item.id !== excludedShiftId && !item.isCancelled
            && item.startLocal < endLocal && item.endLocal > startLocal);
        if (conflict) throw new Error(`Смена пересекается с существующей: ${conflict.startLocal} — ${conflict.endLocal}.`);
    }

    async function prepareShifts(action, r) {
        const p = r.p;
        const targetDate = date(p.date ?? r.command.date);
        const week = await r.read(`${r.org}/shifts?weekStart=${weekStart(targetDate)}`);
        if (!week.canManage && action !== 'shift_list') throw new Error('У пользователя нет права управлять сменами.');
        if (action === 'shift_list') {
            return appendPlan(r, { target: `Смены с ${weekStart(targetDate)}`, changes: list(week.shifts).map(item => `${item.employeeName}: ${item.startLocal} — ${item.endLocal}`), data: week.shifts });
        }
        if (action === 'shift_publish' || action === 'shift_copy') {
            const weekBody = { weekStart: weekStart(targetDate) };
            r.watch(`${r.org}/shifts?weekStart=${weekBody.weekStart}`, week);
            return appendPlan(r, {
                target: `Неделя с ${weekBody.weekStart}`,
                changes: [action === 'shift_publish' ? 'Опубликовать смены сотрудникам' : 'Скопировать смены предыдущей недели'],
                destructive: true,
                requests: [{ path: `${r.org}/shifts/${action === 'shift_publish' ? 'publish' : 'copy-previous-week'}`, method: 'POST', body: weekBody }]
            });
        }

        const employee = findUnique(list(week.employees).filter(item => item.isActive), p.employeeName, 'активного сотрудника');
        if (action === 'shift_create') {
            const startLocal = localDateTime(targetDate, r.command.startTime ?? p.startTime);
            const endLocal = localDateTime(targetDate, r.command.endTime ?? p.endTime);
            if (endLocal <= startLocal) throw new Error('Время окончания смены должно быть позже начала.');
            const dayShifts = list(week.shifts).filter(item => item.employeeId === employee.id && item.localStartDate === targetDate);
            ensureNoShiftOverlap(dayShifts, startLocal, endLocal);
            const plannedBreakMinutes = p.plannedBreakMinutes == null ? 0 : wholeAmount(p.plannedBreakMinutes, 'перерыв');
            if (plannedBreakMinutes > timeMinutes(endLocal) - timeMinutes(startLocal)) throw new Error('Перерыв не может быть длиннее смены.');
            const body = { employeeId: employee.id, startLocal, endLocal, plannedBreakMinutes, version: null, reason: p.reason ? text(p.reason, 'причину', 500) : null };
            r.watch(`${r.org}/shifts?weekStart=${weekStart(targetDate)}`, week);
            return appendPlan(r, { target: employee.name, changes: [`Создать смену ${startLocal} — ${endLocal}`], requests: [{ path: `${r.org}/shifts`, method: 'POST', body }] });
        }

        const existing = list(week.shifts).filter(item => item.employeeId === employee.id && item.localStartDate === targetDate && !item.isCancelled);
        if (action === 'shift_delete') {
            const shift = chooseOneOrByNumber(existing, p.shiftNumber, 'смены');
            const reason = p.reason ? text(p.reason, 'причину отмены', 500) : 'Изменение графика';
            r.watch(`${r.org}/shifts?weekStart=${weekStart(targetDate)}`, week);
            return appendPlan(r, {
                target: `${employee.name}, ${shift.startLocal} — ${shift.endLocal}`,
                changes: [`Отменить смену: ${reason}`], destructive: true,
                requests: [{ path: `${r.org}/shifts/${encodeURIComponent(shift.id)}`, method: 'DELETE', body: { version: shift.version, reason } }]
            });
        }

        if (action === 'shift_update') {
            const shift = chooseOneOrByNumber(existing, p.shiftNumber, 'смены');
            const startLocal = localDateTime(targetDate, r.command.startTime ?? p.startTime ?? shift.startLocal.slice(11, 16));
            const endLocal = localDateTime(targetDate, r.command.endTime ?? p.endTime ?? shift.endLocal.slice(11, 16));
            if (endLocal <= startLocal) throw new Error('Время окончания смены должно быть позже начала.');
            ensureNoShiftOverlap(existing, startLocal, endLocal, shift.id);
            const plannedBreakMinutes = p.plannedBreakMinutes == null ? shift.plannedBreakMinutes : wholeAmount(p.plannedBreakMinutes, 'перерыв');
            if (plannedBreakMinutes > timeMinutes(endLocal) - timeMinutes(startLocal)) throw new Error('Перерыв не может быть длиннее смены.');
            const body = { employeeId: shift.employeeId, startLocal, endLocal, plannedBreakMinutes, version: shift.version, reason: p.reason ? text(p.reason, 'причину', 500) : null };
            r.watch(`${r.org}/shifts?weekStart=${weekStart(targetDate)}`, week);
            return appendPlan(r, { target: `${employee.name}, ${targetDate}`, changes: [`Изменить смену на ${startLocal} — ${endLocal}`], requests: [{ path: `${r.org}/shifts/${encodeURIComponent(shift.id)}`, method: 'PUT', body }] });
        }

        throw new Error('Действие со сменой не поддерживается.');
    }

    async function prepareInventory(action, r) {
        const p = r.p;
        const inventory = await r.read(`${r.org}/materials?stocksPage=1&stocksPageSize=100`);
        const stocks = list(inventory?.stocks ?? inventory);
        if (action === 'inventory_list') {
            if (!p.name) return appendPlan(r, { target: 'Склад', changes: stocks.map(item => `${item.name}: остаток ${item.quantityOnHand} ${item.unit}`), data: stocks });
            const normalizedName = text(p.name, 'название материала', 150).toLocaleLowerCase('ru-RU');
            const matches = stocks.filter(item => String(item.name ?? '').trim().toLocaleLowerCase('ru-RU') === normalizedName);
            if (matches.length !== 1) throw new Error(matches.length ? 'Найдено несколько материалов с таким названием.' : 'Материал не найден на складе.');
            return appendPlan(r, { target: matches[0].name, changes: [`Остаток: ${matches[0].quantityOnHand} ${matches[0].unit}`], data: matches[0] });
        }
        const name = text(p.name, 'название материала', 150);
        const normalizedStockName = name.toLocaleLowerCase('ru-RU');
        const matchingStocks = stocks.filter(item => String(item.name ?? '').trim().toLocaleLowerCase('ru-RU') === normalizedStockName);
        if (matchingStocks.length > 1) throw new Error('Найдено несколько материалов с таким названием. Уточните склад вручную.');
        const stock = matchingStocks[0] ?? null;
        if (action === 'inventory_receive') {
            const body = {
                name: stock?.name ?? name,
                catalogItemId: stock?.catalogItemId ?? null,
                unit: materialUnit(p.unit || stock?.unit || 'шт'),
                quantity: quantity(p.quantity),
                packageQuantity: p.packageQuantity == null ? stock?.packageQuantity ?? null : wholeQuantity(p.packageQuantity, 'количество в упаковке'),
                unitCost: p.unitCost == null ? null : amount(p.unitCost, 'цена за единицу'),
                comment: p.comment ? text(p.comment, 'комментарий', 500) : null
            };
            r.watch(`${r.org}/materials?stocksPage=1&stocksPageSize=100`, inventory);
            return appendPlan(r, { target: `Склад: ${body.name}`, changes: [`Принять ${body.quantity} ${body.unit}`, body.unitCost == null ? 'Себестоимость не указана' : `Себестоимость: ${body.unitCost}`], requests: [{ path: `${r.org}/materials/receipt`, method: 'POST', body }] });
        }
        if (!stock) throw new Error(`Материал «${name}» не найден на складе.`);
        if (action === 'inventory_issue') {
            const count = stockQuantity(quantity(p.quantity), p.unit, stock);
            if (count > stock.availableQuantity) throw new Error(`Доступно только ${stock.availableQuantity} ${stock.unit}.`);
            const body = { stockId: stock.id, quantity: count, comment: p.comment ? text(p.comment, 'комментарий', 500) : null };
            r.watch(`${r.org}/materials?stocksPage=1&stocksPageSize=100`, inventory);
            return appendPlan(r, { target: stock.name, changes: [`Списать ${count} ${stock.unit}`], destructive: true, requests: [{ path: `${r.org}/materials/issue`, method: 'POST', body }] });
        }
        if (action === 'inventory_adjust') {
            const count = stockQuantity(amount(p.quantityOnHand ?? p.quantity, 'новый остаток'), p.unit, stock);
            const unitCost = p.unitCost == null ? stock.averageUnitCost : amount(p.unitCost, 'себестоимость') / stockQuantity(1, p.unit, stock);
            const body = { quantityOnHand: count, averageUnitCost: unitCost, comment: text(p.comment, 'обоснование корректировки', 500) };
            r.watch(`${r.org}/materials?stocksPage=1&stocksPageSize=100`, inventory);
            return appendPlan(r, { target: stock.name, changes: [`Установить остаток ${count} ${stock.unit}`, `Себестоимость: ${unitCost}`, `Причина: ${body.comment}`], destructive: true, requests: [{ path: `${r.org}/materials/${encodeURIComponent(stock.id)}`, method: 'PUT', body }] });
        }
        throw new Error('Действие со складом не поддерживается.');
    }

    async function prepareDefects(action, r) {
        const p = r.p;
        const vehicle = await r.vehicle();
        const base = `${r.org}/defects`;
        const page = await r.read(queryPath(`${base}/1/100`, 'vehicleId', vehicle.id));
        const defects = list(page);
        if (page.hasMore && action !== 'defect_list') throw new Error('Список неисправностей большой. Найдите нужную запись в карточке автомобиля.');
        if (action === 'defect_list') {
            return appendPlan(r, { target: `Неисправности ${vehicle.registrationPlate ?? vehicle.name}`, changes: defects.map(item => `${item.title}: ${item.statusName}`), data: defects });
        }
        if (action === 'defect_create') {
            const body = { vehicleId: vehicle.id, title: text(p.name, 'название неисправности', 200), description: p.description ? text(p.description, 'описание') : null, severityCode: p.severityCode ?? 'medium', statusCode: p.statusCode ?? 'new' };
            if (!['low', 'medium', 'high', 'critical'].includes(body.severityCode)) throw new Error('Неизвестная важность неисправности.');
            if (!['new', 'in_progress', 'resolved'].includes(body.statusCode)) throw new Error('Неизвестный статус неисправности.');
            r.watch(queryPath(`${base}/1/100`, 'vehicleId', vehicle.id), page);
            return appendPlan(r, { target: vehicle.registrationPlate, changes: [`Добавить неисправность «${body.title}»`], requests: [{ path: base, method: 'POST', body }], result: { vehicleUrl: `${r.context.workspaceUrl}#vehicles/${vehicle.id}` } });
        }
        const defect = findUnique(defects, p.name, 'неисправность');
        if (action === 'defect_delete') {
            if (defect.linkedMaintenanceCount > 0) throw new Error('У неисправности есть связанное обслуживание. Удалите связь в CRM перед удалением.');
            r.watch(queryPath(`${base}/1/100`, 'vehicleId', vehicle.id), page);
            return appendPlan(r, { target: defect.title, changes: [`Удалить неисправность «${defect.title}»`], destructive: true, requests: [{ path: `${base}/${encodeURIComponent(defect.id)}`, method: 'DELETE' }] });
        }
        if (action === 'defect_close') {
            if (defect.isClosed) throw new Error('Эта неисправность уже закрыта.');
            r.watch(queryPath(`${base}/1/100`, 'vehicleId', vehicle.id), page);
            return appendPlan(r, { target: defect.title, changes: [`Закрыть неисправность «${defect.title}»`], requests: [{ path: `${base}/${encodeURIComponent(defect.id)}/status`, method: 'POST', body: { statusCode: 'resolved' } }] });
        }
        if (action === 'defect_update') {
            const body = {
                vehicleId: vehicle.id,
                title: p.newName ? text(p.newName, 'новое название', 200) : defect.title,
                description: p.description == null ? defect.description ?? null : text(p.description, 'описание'),
                severityCode: p.severityCode ?? defect.severityCode,
                statusCode: p.statusCode ?? defect.statusCode
            };
            if (!['low', 'medium', 'high', 'critical'].includes(body.severityCode) || !['new', 'in_progress', 'resolved'].includes(body.statusCode)) throw new Error('Проверьте важность и статус неисправности.');
            r.watch(queryPath(`${base}/1/100`, 'vehicleId', vehicle.id), page);
            return appendPlan(r, { target: defect.title, changes: [`Название: ${defect.title} → ${body.title}`, `Описание: ${body.description || 'очистить'}`, `Важность: ${body.severityCode}`, `Статус: ${body.statusCode}`], requests: [{ path: `${base}/${encodeURIComponent(defect.id)}`, method: 'PUT', body }] });
        }
        throw new Error('Действие с неисправностью не поддерживается.');
    }

    async function prepareCounters(action, r) {
        const vehicle = await r.vehicle();
        const counters = await r.read(`${r.org}/counters/history/1/100?vehicleId=${encodeURIComponent(vehicle.id)}&sort=recordedAt_desc`);
        const entries = list(counters);
        if (action === 'counter_add') {
            const value = amount(r.p.value, 'показание');
            const latest = entries[0];
            if (latest && value < latest.value) throw new Error(`Новое показание меньше последнего (${latest.value}). Уточните историю в карточке авто.`);
            const recordedAt = r.p.recordedAt ?? (r.command.date ? `${date(r.command.date)}T${r.command.startTime ?? '12:00'}:00` : new Date().toISOString());
            if (Number.isNaN(Date.parse(recordedAt))) throw new Error('Проверьте дату и время показания.');
            const body = { vehicleId: vehicle.id, value, recordedAt, comment: r.p.comment ? text(r.p.comment, 'комментарий', 500) : null };
            r.watch(`${r.org}/counters/history/1/100?vehicleId=${encodeURIComponent(vehicle.id)}&sort=recordedAt_desc`, counters);
            return appendPlan(r, { target: vehicle.registrationPlate, changes: [`Записать показание ${value}`], requests: [{ path: `${r.org}/counters`, method: 'POST', body }], result: { vehicleUrl: `${r.context.workspaceUrl}#vehicles/${vehicle.id}` } });
        }
        if (action === 'counter_remove') {
            const entry = chooseByNumber(entries, r.p.entryNumber, 'показания');
            if (!entry.canManage) throw new Error('У пользователя нет права удалять это показание.');
            r.watch(`${r.org}/counters/history/1/100?vehicleId=${encodeURIComponent(vehicle.id)}&sort=recordedAt_desc`, counters);
            return appendPlan(r, { target: `${vehicle.registrationPlate}: ${entry.value}`, changes: ['Удалить запись показания'], destructive: true, requests: [{ path: `${r.org}/counters/${encodeURIComponent(entry.id)}`, method: 'DELETE' }] });
        }
        throw new Error('Действие со счётчиком не поддерживается.');
    }

    async function prepareMaterials(action, r) {
        const maintenance = await r.maintenance();
        const details = list(maintenance.maintenanceDetails);
        const line = r.line(details);
        const workPath = pathForWork(r, maintenance.id);
        const progressPath = `${workPath}/progress`;
        const progress = await r.read(progressPath);
        if (action === 'material_add') {
            const availablePath = `${workPath}/operations/${encodeURIComponent(line.detailId ?? line.id)}/materials/available`;
            const available = list(await r.read(availablePath));
            const stock = findUnique(available, r.p.name, 'доступный материал');
            const count = quantity(r.p.quantity);
            if (stockQuantity(count, r.p.unit, stock) > stock.availableQuantity) throw new Error(`Материала «${stock.name}» доступно только ${stock.availableQuantity} ${stock.unit}.`);
            const commandId = crypto.randomUUID();
            r.watch(progressPath, progress, progressSnapshot);
            r.watch(availablePath, available);
            const unit = materialUnit(r.p.unit || stock.unit);
            return appendPlan(r, { target: `${maintenance.registrationPlate ?? 'Заказ'} · ${line.name}`, changes: [`Списать ${count} ${unit} «${stock.name}»`], requests: [{ path: `${workPath}/operations/${encodeURIComponent(line.detailId ?? line.id)}/materials`, method: 'POST', body: { stockId: stock.id, quantity: count, unit, commandId } }] });
        }
        if (action === 'material_remove') {
            const issues = list(await r.read(`${workPath}/operations/${encodeURIComponent(line.detailId ?? line.id)}/materials`));
            const issue = r.p.issueNumber == null
                ? findUnique(issues.map(item => ({ ...item, name: item.materialName ?? item.name })), r.p.name, 'списанный материал')
                : chooseByNumber(issues, r.p.issueNumber, 'списанного материала');
            const commandId = crypto.randomUUID();
            const reason = text(r.p.reason, 'причину возврата', 500);
            r.watch(progressPath, progress, progressSnapshot);
            r.watch(`${workPath}/operations/${encodeURIComponent(line.detailId ?? line.id)}/materials`, issues);
            return appendPlan(r, { target: `${maintenance.registrationPlate ?? 'Заказ'} · ${line.name}`, changes: [`Вернуть ${issue.quantity} ${issue.unit} «${issue.materialName}»: ${reason}`], requests: [{ path: `${workPath}/operations/${encodeURIComponent(line.detailId ?? line.id)}/materials/${encodeURIComponent(issue.id)}/return`, method: 'POST', body: { commandId, reason } }] });
        }
        throw new Error('Действие с материалами заказа не поддерживается.');
    }

    async function prepareWork(action, r) {
        const maintenance = await r.maintenance();
        const workPath = pathForWork(r, maintenance.id);
        const progressPath = `${workPath}/progress`;
        const progress = list(await r.read(progressPath));
        if (action === 'work_decision') {
            const reports = list(await r.read(`${workPath}/reports`));
            const selected = chooseByNumber(reports, r.p.reportNumber, 'сообщения');
            if (!['approved', 'rejected'].includes(r.p.decision)) throw new Error('Укажите решение: согласовать или отклонить.');
            if (r.p.decision === 'approved' && (r.p.price == null || !r.p.vatRateCode)) throw new Error('Для согласования назовите цену и ставку НДС.');
            if (r.p.decision === 'rejected' && !r.p.reason) throw new Error('Для отказа назовите причину.');
            r.watch(`${workPath}/reports`, reports);
            return appendPlan(r, { target: selected.text, destructive: true, changes: [`${r.p.decision === 'approved' ? 'Согласовать' : 'Отклонить'} сообщение №${r.p.reportNumber}.`, ...(r.p.price != null ? [`Цена: ${r.p.price} ₽, НДС: ${r.p.vatRateCode}`] : []), ...(r.p.reason ? [`Причина: ${r.p.reason}`] : [])],
                requests: [{ path: `${workPath}/reports/${selected.id}/decision`, method: 'POST', body: { decision: r.p.decision, reason: r.p.reason || null, price: r.p.price ?? null, vatRateCode: r.p.vatRateCode || null } }] });
        }
        const line = r.line(progress);
        const path = `${workPath}/operations/${encodeURIComponent(line.detailId)}`;
        const commandId = crypto.randomUUID();
        const reason = r.p.reason ? text(r.p.reason, 'причину', 500) : null;
        if (['work_start', 'work_pause', 'work_finish'].includes(action)
            && (r.p.startTime || r.p.endTime || r.command.startTime || r.command.endTime)) {
            throw new Error('Начало и завершение операции фиксируются текущим временем. Для исправления прошлой работы используйте корректировку времени в минутах.');
        }
        if (action === 'work_start' || action === 'work_pause' || action === 'work_finish') {
            const transition = { work_start: Number(line.status) === 3 ? 'resume' : 'start', work_pause: 'pause', work_finish: 'complete' }[action];
            if (action === 'work_start' && ![1, 3].includes(Number(line.status))) throw new Error('Начать можно назначенную работу, возобновить — работу на паузе.');
            if (transition === 'pause' && line.status !== 2) throw new Error('Приостановить можно только выполняющуюся работу.');
            if (transition === 'pause' && !reason) throw new Error('Назовите причину паузы.');
            if (transition === 'complete' && Number(line.status) !== 2) throw new Error('Завершить можно выполняющуюся работу. Сначала возобновите работу с паузы.');
            r.watch(progressPath, progress, progressSnapshot);
            return appendPlan(r, {
                target: `${maintenance.registrationPlate ?? 'Заказ'} · ${line.name}`,
                changes: [{ start: 'Начать', resume: 'Возобновить', pause: 'Приостановить', complete: 'Завершить' }[transition] + ` работу «${line.name}»`],
                requests: [{ path: `${path}/transition`, method: 'POST', body: { commandId, version: line.version, action: transition, reason } }]
            });
        }
        if (action === 'work_time') {
            const minutes = Number(required(r.p.minutes, 'минуты корректировки'));
            if (!Number.isInteger(minutes) || minutes === 0 || Math.abs(minutes) > 1440) throw new Error('Корректировка времени должна быть целым числом минут от −1440 до 1440.');
            const explanation = text(r.p.reason, 'причину корректировки', 500);
            r.watch(progressPath, progress, progressSnapshot);
            return appendPlan(r, { target: line.name, changes: [`Скорректировать время на ${minutes} мин.`, `Причина: ${explanation}`], requests: [{ path: `${path}/time-adjustments`, method: 'POST', body: { commandId, minutes, reason: explanation } }] });
        }
        if (action === 'work_report') {
            const kind = r.p.kind == null ? 'comment' : text(r.p.kind, 'тип сообщения', 20);
            if (!['comment', 'defect', 'extra', 'time'].includes(kind)) throw new Error('Этот тип сообщения о работе не поддерживается.');
            const reportText = text(r.p.text, 'текст сообщения', 2000);
            r.watch(progressPath, progress, progressSnapshot);
            return appendPlan(r, { target: line.name, changes: [`Добавить сообщение (${kind}): ${reportText}`], requests: [{ path: `${workPath}/operations/${encodeURIComponent(line.detailId)}/reports`, method: 'POST', body: { commandId, kind, text: reportText } }] });
        }
        throw new Error('Действие над исполнением работы не поддерживается.');
    }

    async function prepare(command, r) {
        const action = command.action;
        switch (action) {
            case 'catalog_list': case 'catalog_create': case 'catalog_update': case 'catalog_delete':
            case 'location_list': case 'location_create': case 'location_update': case 'location_delete':
                return prepareCatalog(action, r);
            case 'employee_list': case 'employee_create': case 'employee_update': case 'employee_deactivate': case 'employee_link':
                return prepareEmployees(action, r);
            case 'shift_list': case 'shift_create': case 'shift_update': case 'shift_delete': case 'shift_publish': case 'shift_copy':
                return prepareShifts(action, r);
            case 'inventory_list': case 'inventory_receive': case 'inventory_issue': case 'inventory_adjust':
                return prepareInventory(action, r);
            case 'defect_list': case 'defect_create': case 'defect_update': case 'defect_close': case 'defect_delete':
                return prepareDefects(action, r);
            case 'counter_add': case 'counter_remove':
                return prepareCounters(action, r);
            case 'material_add': case 'material_remove':
                return prepareMaterials(action, r);
            case 'work_start': case 'work_pause': case 'work_finish': case 'work_time': case 'work_report': case 'work_decision':
                return prepareWork(action, r);
            default:
                return null;
        }
    }

    globalThis.OpenCRMAiPilotBusinessActions = {
        actions: [
            'catalog_list', 'catalog_create', 'catalog_update', 'catalog_delete',
            'location_list', 'location_create', 'location_update', 'location_delete',
            'employee_list', 'employee_create', 'employee_update', 'employee_deactivate', 'employee_link',
            'shift_list', 'shift_create', 'shift_update', 'shift_delete', 'shift_publish', 'shift_copy',
            'inventory_list', 'inventory_receive', 'inventory_issue', 'inventory_adjust',
            'defect_list', 'defect_create', 'defect_update', 'defect_close', 'defect_delete',
            'counter_add', 'counter_remove', 'material_add', 'material_remove',
            'work_start', 'work_pause', 'work_finish', 'work_time', 'work_report', 'work_decision'
        ],
        prepare
    };
})();
