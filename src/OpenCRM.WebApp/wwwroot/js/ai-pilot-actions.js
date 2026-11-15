(() => {
    'use strict';

    const running = new Set();
    const memory = new Map();
    const legacy = new Set(['visit', 'book', 'arrive']);
    const workflow = new Set(['start', 'complete', 'reschedule', 'cancel', 'reopen', 'return_to_planned',
        'return_to_work', 'mark_ready', 'handover', 'undo_handover', 'simple_handover']);
    const detailFields = ['detailId', 'name', 'description', 'price', 'vatRateCode', 'employeeId', 'employeeName',
        'catalogItemId', 'quantity', 'materialCatalogItemId', 'materialName', 'materialQuantity', 'materialUnit',
        'materialUnitCost', 'materialTotalCost'];
    const maintenanceFields = ['version', 'status', 'type', 'plannedDate', 'plannedStartTime', 'plannedEndDate',
        'plannedEndTime', 'completedDate', 'location', 'employeeId', 'performer', 'mileage', 'comment',
        'discountAmount', 'laborCostAmount', 'buyerClientId'];
    const vehicleFields = ['kind', 'mileageUnit', 'modelId', 'yearIssue', 'manualBrandName', 'manualModelName',
        'name', 'vin', 'registrationPlate', 'bodyNumber', 'engineNumber', 'color', 'registrationCertificate',
        'commissioningDate', 'description', 'organizationClientId'];
    const clientFields = ['name', 'lastName', 'firstName', 'middleName', 'companyTypeCode', 'phone', 'partyTypeCode',
        'inn', 'kpp', 'legalAddress', 'comment', 'statusCode', 'responsibleUserId', 'tagNamesText'];
    const normalize = value => String(value ?? '').trim().toLocaleLowerCase('ru-RU').replace(/ё/g, 'е').replace(/\s+/g, ' ');
    const clone = value => JSON.parse(JSON.stringify(value));
    const isMaterial = item => Boolean(item.materialCatalogItemId || item.materialName || item.materialQuantity != null);
    const pick = (item, fields) => Object.fromEntries(fields.filter(key => item[key] !== undefined).map(key => [key, item[key]]));
    const day = value => value ? String(value).slice(0, 10) : '';
    const money = value => value == null ? 'цена не указана' : new Intl.NumberFormat('ru-RU').format(value) + ' ₽';
    const labels = { name: 'Название', comment: 'Комментарий', mileage: 'Пробег', completedDate: 'Дата выполнения',
        plannedDate: 'Дата записи', plannedStartTime: 'Начало', plannedEndDate: 'Дата окончания', plannedEndTime: 'Окончание',
        discountAmount: 'Скидка', laborCostAmount: 'Себестоимость труда', performer: 'Исполнитель', location: 'Пост',
        price: 'Цена', quantity: 'Количество', description: 'Описание', vatRateCode: 'НДС', employeeId: 'Сотрудник',
        phone: 'Телефон', firstName: 'Имя', lastName: 'Фамилия', middleName: 'Отчество', companyTypeCode: 'Тип компании',
        partyTypeCode: 'Тип клиента', inn: 'ИНН', kpp: 'КПП', legalAddress: 'Адрес', statusCode: 'Статус',
        responsibleUserId: 'Ответственный', tagNamesText: 'Теги', kind: 'Тип техники', mileageUnit: 'Единица пробега',
        modelId: 'Модель каталога', yearIssue: 'Год выпуска', manualBrandName: 'Марка', manualModelName: 'Модель',
        vin: 'VIN', registrationPlate: 'Госномер', bodyNumber: 'Номер кузова', engineNumber: 'Номер двигателя',
        color: 'Цвет', registrationCertificate: 'СТС', commissioningDate: 'Ввод в эксплуатацию', organizationClientId: 'Клиент',
        completeness: 'Комплектность', visibleDamage: 'Повреждения', notes: 'Замечания' };

    function one(items, name, label = 'объект') {
        const matches = name == null || name === '' ? items : items.filter(item => normalize(item.name) === normalize(name));
        if (matches.length !== 1) {
            throw new Error(matches.length ? `Найдено несколько: ${label}. Уточните название или номер строки.` : `Не найден ${label}: ${name ?? ''}.`);
        }

        return matches[0];
    }

    function line(details, parameters) {
        if (parameters.lineNumber != null) {
            const index = Number(parameters.lineNumber) - 1;
            if (!Number.isInteger(index) || index < 0 || index >= details.length) {
                throw new Error('Номер строки работы не найден. Нумерация начинается с 1.');
            }

            const selected = details[index];
            if (parameters.serviceName && normalize(selected.name) !== normalize(parameters.serviceName)) {
                throw new Error('Номер строки и название работы не совпадают.');
            }

            return selected;
        }

        return one(details, parameters.serviceName, 'работа');
    }

    function load(key) {
        try { return JSON.parse(sessionStorage.getItem(key) || 'null') || memory.get(key) || {}; }
        catch { return memory.get(key) || {}; }
    }

    function save(key, value) {
        memory.set(key, value);
        try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* Защита продолжает действовать в памяти вкладки. */ }
    }

    function createRuntime(command, context, request) {
        const p = Object.fromEntries(Object.entries(command.parameters || {}).filter(([, value]) => value != null));
        const org = `api/v1/organizations/${encodeURIComponent(context.organizationId)}`;
        const query = `organizationId=${encodeURIComponent(context.organizationId)}`;
        const watches = [];
        let selectedVehicle;
        let selectedMaintenance;
        const read = path => request(path);
        const watch = (path, snapshot, project = value => value) => {
            const expected = JSON.stringify(project(snapshot));
            const check = async () => {
                if (JSON.stringify(project(await read(path))) !== expected) {
                    throw new Error('Данные изменились после подготовки. Подготовьте команду заново и проверьте изменения.');
                }
            };
            watches.push(check);

            return check;
        };

        async function vehicle() {
            if (selectedVehicle) return selectedVehicle;
            if (!command.registrationPlate && (p.maintenanceId || context.selectedMaintenanceId)) {
                const id = p.maintenanceId || context.selectedMaintenanceId;
                if (!/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(id)) throw new Error('Некорректный идентификатор заказа.');
                selectedMaintenance = await read(`api/v1/maintenance/${encodeURIComponent(id)}?${query}`);
                selectedVehicle = await read(`api/v1/vehicles/${encodeURIComponent(selectedMaintenance.vehicleId)}?${query}`);
                watch(`api/v1/vehicles/${encodeURIComponent(selectedVehicle.id)}?${query}`, selectedVehicle,
                    item => pick(item, ['id', 'organizationId', 'registrationPlate', 'organizationClientId']));

                return selectedVehicle;
            }
            const plate = globalThis.OpenCRMAiPilotExecutor.normalizePlate(command.registrationPlate);
            if (!/^[ABEKMHOPCTYX]\d{3}[ABEKMHOPCTYX]{2}\d{2,3}$/.test(plate)) {
                throw new Error('Укажите полный госномер с регионом, например А123ВС154.');
            }

            const items = [];
            const archived = command.action === 'vehicle_restore';
            for (let page = 1; page <= 10; page++) {
                const response = await read(`api/v1/vehicles?${query}&isArchived=${archived}&page=${page}&pageSize=100`);
                if (response.totalCount > 1000) throw new Error('Поиск пилота ограничен 1000 автомобилями.');
                items.push(...response.items.filter(item => globalThis.OpenCRMAiPilotExecutor.normalizePlate(item.registrationPlate) === plate));
                if (page >= (response.totalPages || 1)) break;
            }

            const match = one(items, null, 'автомобиль с таким номером');
            selectedVehicle = await read(`api/v1/vehicles/${encodeURIComponent(match.id)}?${query}`);
            watch(`api/v1/vehicles/${encodeURIComponent(selectedVehicle.id)}?${query}`, selectedVehicle,
                item => pick(item, ['id', 'organizationId', 'registrationPlate', 'organizationClientId']));

            return selectedVehicle;
        }

        async function maintenance() {
            if (selectedMaintenance) return selectedMaintenance;
            let id = p.maintenanceId || context.selectedMaintenanceId;
            if (id && !/^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(id)) {
                throw new Error('Некорректный идентификатор записи.');
            }

            if (!id) {
                const car = await vehicle();
                const response = await read(`api/v1/vehicles/${car.id}/maintenance/1/100?${query}&sort=date_desc`);
                if (response.hasMore) throw new Error('Слишком много записей: укажите идентификатор нужного заказа.');
                let candidates = response.items;
                if (p.recordDate) {
                    candidates = candidates.filter(item => [item.date, item.plannedDate, item.completedDate].some(value => day(value) === p.recordDate));
                } else if (!p.latest) {
                    const active = candidates.filter(item => [1, 2].includes(Number(item.status)));
                    if (active.length) candidates = active;
                }

                if (p.latest && candidates.length) candidates = [candidates[0]];
                if (candidates.length !== 1) {
                    const error = new Error(candidates.length ? 'У автомобиля несколько записей. Выберите нужную ниже.' : 'Подходящая запись не найдена.');
                    error.choices = candidates.map(item => ({ value: item.id, label: `${day(item.plannedDate || item.completedDate || item.date)} · ${item.serviceNames?.join(', ') || item.id.slice(0, 8)}` }));
                    throw error;
                }

                id = candidates[0].id;
            }

            selectedMaintenance = await read(`api/v1/maintenance/${encodeURIComponent(id)}?${query}`);
            if (command.registrationPlate) {
                const car = await vehicle();
                if (car.id !== selectedMaintenance.vehicleId) throw new Error('Выбранный заказ относится к другому автомобилю.');
            }

            return selectedMaintenance;
        }

        function plan(config) {
            const requests = clone(config.requests || []);
            const steps = [...requests.map(operation => mutate => mutate(operation.path, operation.method, operation.body)), ...(config.steps || [])];
            const result = { message: steps.length ? 'Изменения сохранены через API CRM.' : 'Данные получены.', ...config.result };
            const links = pick(result, ['url', 'maintenanceUrl', 'vehicleUrl', 'clientUrl']);
            const checks = watches.slice();
            return {
                target: config.target,
                changes: config.changes || [],
                destructive: Boolean(config.destructive),
                readOnly: steps.length === 0,
                data: config.data,
                async execute(options) {
                    if (!options.draftToken) throw new Error('Подготовьте команду заново.');
                    if (config.destructive && options.confirmed !== true) throw new Error('Подтвердите удаление или отмену в списке изменений.');
                    const key = `opencrm.ai-actions.${context.userId}.${context.organizationId}.${options.draftToken}`;
                    const state = load(key);
                    if (state.result) return state.result;
                    if (running.has(key)) throw new Error('Команда уже выполняется.');
                    if (state.pending) throw Object.assign(new Error('Ответ на предыдущий запрос потерян. Проверьте CRM перед повтором.'), links);
                    if (state.failed) throw Object.assign(new Error('Команда выполнена частично. Проверьте результат и подготовьте оставшиеся действия заново.'), links);
                    running.add(key);
                    try {
                        if (!state.done) for (const check of checks) await check();
                        async function mutate(path, method, body) {
                            state.pending = true;
                            save(key, state);
                            try {
                                const response = await request(path, { method, body });
                                state.lastResponse = response;
                                state.pending = false;
                                save(key, state);

                                return response;
                            } catch (error) {
                                if (error.status >= 400 && error.status < 500) state.pending = false;
                                save(key, state);
                                throw error;
                            }
                        }
                        for (let index = state.done || 0; index < steps.length; index++) {
                            await steps[index](mutate);
                            state.done = index + 1;
                            save(key, state);
                        }

                        if (config.created) {
                            const response = state.lastResponse;
                            const id = typeof response === 'string' ? response : response?.client?.id || response?.id;
                            if (id) result[`${config.created}Url`] = `${context.workspaceUrl}#${config.created === 'vehicle' ? 'vehicles' : 'clients'}/${encodeURIComponent(id)}`;
                        }

                        state.result = result;
                        save(key, state);

                        return result;
                    } catch (error) {
                        if (state.done || state.pending) {
                            state.failed = true;
                            save(key, state);
                            Object.assign(error, links);
                        }
                        throw error;
                    } finally { running.delete(key); }
                }
            };
        }

        return { p, context, command, org, query, read, watch, watchServices: check => watches.push(check), plan, vehicle, maintenance, one, pick, line: details => line(details.filter(item => !isMaterial(item)), p) };
    }

    function maintenanceBody(current) {
        return { ...pick(current, maintenanceFields), details: current.maintenanceDetails.map(item => pick(item, detailFields)),
            linkedDefectIds: (current.linkedDefects || []).map(item => item.id), changeBuyer: false,
        };
    }

    function changes(before, after, fields) {
        return fields.filter(key => JSON.stringify(before[key] ?? null) !== JSON.stringify(after[key] ?? null))
            .map(key => `${labels[key] || key}: ${before[key] ?? 'не указано'} → ${after[key] ?? 'очистить'}`);
    }

    async function prepareMaintenance(command, r) {
        const current = await r.maintenance();
        const p = r.p;
        const path = `api/v1/maintenance/${current.id}?${r.query}`;
        const base = `api/v1/maintenance/${current.id}`;
        const result = { maintenanceUrl: `${r.context.workspaceUrl}#maintenance/${current.id}`, vehicleUrl: `${r.context.workspaceUrl}#vehicles/${current.vehicleId}` };
        const target = `Заказ ${day(current.plannedDate || current.completedDate || current.date)} · ${command.registrationPlate || current.id.slice(0, 8)}`;
        r.watch(path, current);
        const make = config => r.plan({ target, result, ...config });
        if (command.action === 'maintenance_get') return make({ data: {
            Статус: current.status, План: `${day(current.plannedDate)} ${current.plannedStartTime || ''}`, Стоимость: money(current.price),
            Получено: money(current.paidAmount), Долг: money(current.debtAmount), Комментарий: current.comment,
            Работы: current.maintenanceDetails.map((item, index) => ({ Строка: index + 1, Работа: item.name, Цена: money(item.price), Количество: item.quantity }))
        } });
        if (command.action === 'maintenance_delete') return make({ destructive: true,
            changes: [`Удалить заказ вместе с его строками: ${current.maintenanceDetails.map(item => item.name).join(', ')}.`, `Стоимость ${money(current.price)}, учтено оплат ${money(current.paidAmount)}.`],
            requests: [{ path, method: 'DELETE' }] });

        if (['service_add', 'service_update', 'service_remove', 'services_replace'].includes(command.action)) {
            if (current.canEditServices === false) throw new Error('Нет права изменять услуги этого заказа.');
            let details = current.maintenanceDetails.filter(item => !isMaterial(item)).map(item => pick(item, detailFields));
            const messages = [];
            if (command.action === 'services_replace') {
                if (!(command.services || []).length) throw new Error('Продиктуйте новый список работ.');
                messages.push(`Удалить прежний список: ${details.map(item => item.name).join(', ')}.`);
                if (command.services.length > 1 && command.totalAmount != null && command.services.some(item => item.price == null)) {
                    throw new Error('Назовите цену каждой отдельной работы. Для одной комплексной услуги явно назовите её одной строкой.');
                } else {
                    details = command.services.map(item => ({ name: item.name, price: item.price ?? (command.services.length === 1 ? command.totalAmount : null), quantity: 1 }));
                    const resolution = await globalThis.OpenCRMAiPilotServiceCatalog.prepareRows(details, r.context, { request: r.read });
                    details = resolution.rows;
                    r.watchServices(resolution.verify);
                    messages.push(...resolution.notes);
                }
                messages.push(`Новый список: ${details.map(item => `${item.name} — ${money(item.price)}`).join('; ')}.`);
            } else if (command.action === 'service_remove') {
                const selected = r.line(details);
                details = details.filter(item => item !== selected);
                messages.push(`Удалить только работу «${selected.name}» — ${money(selected.price)}.`);
            } else {
                const selected = command.action === 'service_add' ? { name: p.serviceName || command.services?.[0]?.name, price: p.price ?? command.totalAmount ?? null, quantity: 1 } : r.line(details);
                const previous = clone(selected);
                Object.assign(selected, pick(p, ['price', 'quantity', 'description', 'vatRateCode']));
                if (p.newName != null) {
                    selected.name = p.newName;
                    if (selected.catalogItemId && selected.name !== previous.name) {
                        throw new Error('У этой строки сохраняется историческое название услуги из справочника. Его нельзя изменить без изменения связи; создайте отдельную работу с нужным названием.');
                    }
                }
                if (p.employeeName != null) {
                    const employees = await r.read(`${r.org}/employees`);
                    selected.employeeId = p.employeeName ? r.one(employees.items.filter(item => item.isActive), p.employeeName, 'сотрудник').id : null;
                }
                if (!selected.name) throw new Error('Укажите название работы.');
                if (selected.price != null && (!Number.isFinite(selected.price) || selected.price < 0)) throw new Error('Цена работы должна быть неотрицательной.');
                if (!Number.isInteger(selected.quantity) || selected.quantity <= 0) throw new Error('Количество работ должно быть целым положительным числом.');
                if (command.action === 'service_add') {
                    const resolution = await globalThis.OpenCRMAiPilotServiceCatalog.prepareRows([selected], r.context, { request: r.read });
                    Object.assign(selected, resolution.rows[0]);
                    r.watchServices(resolution.verify);
                    messages.push(...resolution.notes);
                    details.push(selected);
                }
                messages.push(command.action === 'service_add' ? `Добавить «${selected.name}», ${selected.quantity} × ${money(selected.price)}.` : `Изменить «${previous.name}»: ${changes(previous, selected, ['name', 'price', 'quantity', 'description', 'vatRateCode', 'employeeId']).join('; ')}`);
            }

            if (!details.length) throw new Error('Нельзя оставить пустой список работ. Сначала добавьте заменяющую работу.');
            if (details.some(item => item.price == null) && ![1, 2].includes(Number(current.status))) throw new Error('Для завершённого заказа укажите цену каждой работы.');
            return make({ changes: messages, destructive: ['service_remove', 'services_replace'].includes(command.action), requests: [{
                path: `${base}/services?${r.query}`, method: 'PUT', body: { version: current.version, details,
                    allowUnspecifiedPrices: [1, 2].includes(Number(current.status)) }
            }] });
        }

        if (command.action === 'payment_add') {
            const amount = command.paidAmount ?? p.amount ?? command.totalAmount;
            if (!(amount > 0)) throw new Error('Назовите сумму принятой оплаты.');
            if (current.canManagePayments === false) throw new Error('Нет права принимать оплату.');
            const body = { id: crypto.randomUUID(), amount, methodCode: command.paymentMethod || 'other', expectedPaidAmount: current.paidAmount, comment: command.comment || null };
            if (p.payerName) {
                const client = await resolveClient(command, r, p.payerName);
                body.payerClientId = client.client.id;
            }
            return make({ changes: [`Учесть ${money(amount)}; ранее получено ${money(current.paidAmount)}. Способ: ${{ cash: 'наличные', card: 'карта', bank: 'перевод', other: 'другой' }[body.methodCode] || body.methodCode}.`], requests: [{ path: `${base}/payments?${r.query}`, method: 'POST', body }] });
        }
        if (command.action === 'payment_cancel') {
            const payments = await r.read(`${base}/payments?${r.query}`);
            const active = payments.filter(item => !item.cancelledAt);
            let selected;
            if (p.paymentNumber != null) {
                if (!Number.isInteger(p.paymentNumber) || p.paymentNumber < 1 || p.paymentNumber > active.length) throw new Error('Номер оплаты не найден.');
                selected = active[p.paymentNumber - 1];
            } else {
                selected = r.one(active.filter(item => p.amount == null || item.amount === p.amount), null, 'платёж; укажите сумму или номер');
            }
            if (!p.reason) throw new Error('Назовите причину отмены оплаты.');
            r.watch(`${base}/payments?${r.query}`, payments);
            return make({ destructive: true, changes: [`Отменить оплату ${money(selected.amount)} от ${day(selected.createdAt)}. Причина: ${p.reason}.`], requests: [{ path: `${base}/payments/${selected.id}/cancel?${r.query}`, method: 'POST', body: { reason: p.reason } }] });
        }
        if (command.action === 'assign_employee') {
            if (p.employeeName === undefined) throw new Error('Назовите сотрудника или явно попросите снять назначение.');
            const employees = await r.read(`${r.org}/employees`);
            const selected = p.employeeName ? r.one(employees.items.filter(item => item.isActive), p.employeeName, 'сотрудник') : null;
            return make({ changes: [`Исполнитель: ${current.performer || 'не назначен'} → ${selected?.name || 'снять назначение'}.`], requests: [{ path: `${base}/employee?${r.query}`, method: 'PUT', body: { employeeId: selected?.id || null, version: current.version } }] });
        }
        if (command.action === 'intake_update') {
            const body = { mileage: current.intakeMileage, completeness: current.intakeCompleteness || '', visibleDamage: current.intakeVisibleDamage || '', notes: current.intakeNotes || '', ...pick(p, ['mileage', 'completeness', 'visibleDamage', 'notes']) };
            return make({ changes: Object.entries(body).map(([key, value]) => `${labels[key] || key}: ${value ?? 'не указано'}`), requests: [{ path: `${base}/intake?${r.query}`, method: 'PUT', body }] });
        }
        if (workflow.has(command.action)) {
            const body = { action: command.action, version: current.version, reason: p.reason || null };
            if (command.action === 'reschedule' || command.action === 'reopen') {
                if (!command.date) throw new Error('Укажите новую дату записи.');
                Object.assign(body, { plannedDate: command.date, plannedStartTime: command.startTime ? command.startTime + ':00' : current.plannedStartTime,
                    plannedEndTime: command.endTime ? command.endTime + ':00' : current.plannedEndTime, location: current.location });
                if (p.plannedEndDate != null) body.plannedEndDate = p.plannedEndDate;
            }
            if (command.action === 'complete') {
                if (![1, 2].includes(Number(current.status))) throw new Error('Завершить можно запланированный заказ или заказ в работе. Для изменения завершённого заказа используйте отдельную команду редактирования.');
                if ((command.services || []).length || command.totalAmount != null || command.paidAmount != null) {
                    const previous = current.maintenanceDetails.filter(item => !isMaterial(item));
                    const services = previous.map(item => pick(item, detailFields));
                    for (const incoming of command.services || []) {
                        const matches = services.filter(item => normalize(item.name) === normalize(incoming.name));
                        if (matches.length > 1) throw new Error('Есть одинаковые работы: сначала уточните цену нужной строки отдельной командой.');
                        if (matches.length) {
                            if (incoming.price != null) matches[0].price = incoming.price;
                        } else services.push({ name: incoming.name, price: incoming.price, quantity: 1 });
                    }
                    const resolution = await globalThis.OpenCRMAiPilotServiceCatalog.prepareRows(services, r.context, { request: r.read });
                    services.splice(0, services.length, ...resolution.rows);
                    r.watchServices(resolution.verify);
                    if (command.totalAmount != null) {
                        const actual = services.reduce((sum, item) => sum + Number(item.price || 0) * Number(item.quantity || 1), 0) - Number(current.discountAmount || 0);
                        if (services.length === 1 && !current.discountAmount) {
                            const quantity = services[0].quantity || 1;
                            const unitCents = Math.round(command.totalAmount * 100 / quantity);
                            if (unitCents * quantity !== Math.round(command.totalAmount * 100)) throw new Error('Общая сумма не делится на количество с точностью до копейки. Уточните цену строки.');
                            services[0].price = unitCents / 100;
                        }
                        else if (services.some(item => item.price == null) || Math.round(actual * 100) !== Math.round(command.totalAmount * 100)) {
                            throw new Error('Общий итог не совпадает с ценами всех строк. Укажите цены работ отдельно перед завершением.');
                        }
                    }
                    if (!services.length || services.some(item => item.price == null)) throw new Error('Назовите выполненные работы и цену каждой работы.');
                    const modified = JSON.stringify(previous.map(item => pick(item, detailFields))) !== JSON.stringify(services);
                    const expected = clone(current);
                    let ownCompletion = false;
                    expected.maintenanceDetails = [...services, ...current.maintenanceDetails.filter(isMaterial)];
                    const verify = fresh => {
                        const fields = [...maintenanceFields.filter(key => key !== 'version'), 'paidAmount', 'readyForHandoverAt', 'handedOverAt'];
                        for (const key of fields) {
                            const left = key === 'readyForHandoverAt' && ownCompletion ? Boolean(fresh[key]) : key.toLowerCase().includes('date') ? day(fresh[key]) : fresh[key] ?? null;
                            const right = key === 'readyForHandoverAt' && ownCompletion ? true : key.toLowerCase().includes('date') ? day(expected[key]) : expected[key] ?? null;
                            if (JSON.stringify(left) !== JSON.stringify(right)) throw new Error('Заказ изменён между шагами. Проверьте результат и подготовьте оставшиеся действия заново.');
                        }
                        const actual = fresh.maintenanceDetails || [];
                        if (actual.length !== expected.maintenanceDetails.length) throw new Error('Состав работ изменился между шагами.');
                        for (const row of expected.maintenanceDetails) {
                            const matches = row.detailId ? actual.filter(item => item.detailId === row.detailId) : actual.filter(item => item.name === row.name && !isMaterial(item));
                            if (matches.length !== 1) throw new Error('Строка заказа изменилась между шагами.');
                            for (const key of detailFields.filter(item => item !== 'detailId' && item !== 'employeeName')) {
                                const defaultValue = key === 'quantity' ? 1 : ['materialUnitCost', 'materialTotalCost'].includes(key) ? 0 : null;
                                if (JSON.stringify(matches[0][key] ?? defaultValue) !== JSON.stringify(row[key] ?? defaultValue)) throw new Error('Цены или поля работы изменились между шагами.');
                            }
                        }
                    };
                    const steps = [];
                    if (modified) steps.push(mutate => mutate(`${base}/services?${r.query}`, 'PUT', { version: current.version, details: services }));
                    steps.push(async mutate => {
                        const fresh = modified ? await r.read(path) : current;
                        verify(fresh);
                        await mutate(`${base}/workflow?${r.query}`, 'POST', { action: 'complete', version: fresh.version, completedDate: command.date || today() });
                        expected.status = 3;
                        expected.completedDate = command.date || today();
                        ownCompletion = true;
                    });
                    if (command.paidAmount > 0) {
                        if (current.canManagePayments === false) throw new Error('Нет права принимать оплату.');
                        const paymentId = crypto.randomUUID();
                        steps.push(async mutate => {
                            const fresh = await r.read(path);
                            verify(fresh);
                            await mutate(`${base}/payments?${r.query}`, 'POST', { id: paymentId, amount: command.paidAmount,
                                methodCode: command.paymentMethod || 'other', expectedPaidAmount: fresh.paidAmount, comment: command.comment || null });
                        });
                    }

                    return make({ changes: ['Завершить заказ, сохранив остальные строки и материалы.', ...resolution.notes,
                        ...services.map(item => `${item.name} — ${item.quantity || 1} × ${money(item.price)}`),
                        ...(command.paidAmount > 0 ? [`Учесть оплату ${money(command.paidAmount)} отдельным действием.`] : [])], steps });
                }
                const services = current.maintenanceDetails.filter(item => !isMaterial(item));
                if (!services.length || services.some(item => item.price == null)) throw new Error('Перед завершением укажите выполненные работы и цены.');
                body.completedDate = command.date || today();
            }
            return make({ destructive: ['cancel', 'reopen', 'return_to_planned', 'undo_handover'].includes(command.action),
                changes: [`Действие: ${r.context.actions?.find(item => item.action === command.action)?.title || command.action}.`,
                    ...(['reschedule', 'reopen'].includes(command.action) ? [`${day(current.plannedDate)} ${current.plannedStartTime || ''} → ${body.plannedDate} ${body.plannedStartTime || ''} — ${body.plannedEndTime || ''}`] : []), ...(p.reason ? [`Причина: ${p.reason}`] : [])],
                requests: [{ path: `${base}/workflow?${r.query}`, method: 'POST', body }] });
        }

        const body = maintenanceBody(current);
        const editable = ['comment', 'mileage', 'completedDate', 'plannedEndDate', 'discountAmount', 'laborCostAmount'];
        Object.assign(body, pick(p, editable));
        for (const field of ['mileage', 'discountAmount', 'laborCostAmount']) {
            if (p[field] != null && (p[field] < 0 || field === 'mileage' && !Number.isInteger(p[field]))) throw new Error(`Проверьте поле «${labels[field]}»: нужно неотрицательное значение.`);
        }
        if (command.date != null) {
            body.plannedDate = command.date;
            if (p.plannedEndDate == null && current.plannedEndDate && current.plannedDate) {
                const duration = new Date(day(current.plannedEndDate) + 'T00:00:00Z') - new Date(day(current.plannedDate) + 'T00:00:00Z');
                body.plannedEndDate = new Date(new Date(command.date + 'T00:00:00Z').getTime() + duration).toISOString().slice(0, 10);
            }
        }
        if (command.startTime != null) body.plannedStartTime = command.startTime + ':00';
        if (command.endTime != null) body.plannedEndTime = command.endTime + ':00';
        if (p.clearTime === true) {
            body.plannedStartTime = null;
            body.plannedEndTime = null;
        }
        if (p.clearMileage === true) body.mileage = null;
        if (command.comment != null) body.comment = command.comment;
        if (p.performer !== undefined) {
            const employees = await r.read(`${r.org}/employees`);
            body.employeeId = p.performer ? r.one(employees.items.filter(item => item.isActive), p.performer, 'сотрудник').id : null;
            body.performer = p.performer || null;
        }
        if (command.action === 'assign_location') {
            if (p.locationName === undefined) throw new Error('Назовите пост или явно попросите снять назначение.');
            const catalog = await r.read(`${r.org}/scenario-catalog`);
            body.location = p.locationName ? r.one(catalog.workPosts.filter(item => item.isActive), p.locationName, 'пост').name : null;
        }
        const messages = changes(current, body, [...editable, 'plannedDate', 'plannedStartTime', 'plannedEndTime', 'location', 'performer', 'employeeId']);
        if (!messages.length) throw new Error('Не названы изменения заказа.');

        return make({ changes: messages, requests: [{ path, method: 'PUT', body }] });
    }

    async function resolveClient(command, r, name) {
        const phone = r.p.phone;
        if (command.action.startsWith('client_') && command.registrationPlate) {
            const car = await r.vehicle();
            if (!car.organizationClientId) throw new Error('У автомобиля не указан клиент. Сначала свяжите автомобиль с клиентом.');
            const details = await r.read(`${r.org}/clients/${car.organizationClientId}`);
            if ((name ?? r.p.clientName) && normalize(details.client.name) !== normalize(name ?? r.p.clientName)) throw new Error('Имя клиента не совпадает с владельцем автомобиля.');
            if (phone && normalizePhone(details.client.phone) !== normalizePhone(phone)) throw new Error('Телефон не совпадает с клиентом автомобиля.');

            return details;
        }
        if (!(name ?? r.p.clientName) && !phone) throw new Error('Назовите имя или телефон клиента либо госномер его автомобиля.');
        const items = [];
        for (let page = 1; page <= 10; page++) {
            const response = await r.read(`${r.org}/clients/${page}/100`);
            if (response.totalCount > 1000) throw new Error('Поиск пилота ограничен 1000 клиентами.');
            items.push(...response.items);
            if (page * 100 >= response.totalCount) break;
        }
        const clientName = name ?? r.p.clientName;
        let matches = clientName ? items.filter(item => normalize(item.name) === normalize(clientName)) : items;
        if (phone) matches = matches.filter(item => normalizePhone(item.phone) === normalizePhone(phone));
        const match = r.one(matches, null, 'клиент');

        return r.read(`${r.org}/clients/${match.id}`);
    }

    async function prepareClient(command, r) {
        const p = r.p;
        if (command.action === 'client_create') {
            const body = { partyTypeCode: 'person', ...pick(p, clientFields), name: p.name || p.clientName || p.newName || p.firstName };
            if (!body.name) throw new Error('Укажите имя клиента.');
            return r.plan({ target: `Новый клиент «${body.name}»`, changes: Object.entries(body).map(([key, value]) => `${labels[key] || key}: ${value}`),
                requests: [{ path: `${r.org}/clients`, method: 'POST', body }], created: 'client' });
        }
        const details = await resolveClient(command, r);
        const current = details.client;
        const path = `${r.org}/clients/${current.id}`;
        const result = { clientUrl: `${r.context.workspaceUrl}#clients/${current.id}` };
        const target = `Клиент «${current.name}» · ${current.phone || 'телефон не указан'}`;
        if (command.action === 'client_get') return r.plan({ target, result, data: { Имя: current.name, Телефон: current.phone, Комментарий: current.comment, Автомобили: current.vehicles || [], Долг: money(details.totalDebtAmount) } });
        if (details.canEdit === false) throw new Error('Нет права изменять клиента.');
        r.watch(path, details);
        if (command.action === 'client_comment') {
            if (p.comment === undefined && command.comment == null) throw new Error('Назовите текст примечания или явно попросите его очистить.');
            return r.plan({ target, result, changes: [`Комментарий: ${current.comment || 'пусто'} → ${p.comment ?? command.comment ?? ''}`],
                requests: [{ path: `${path}/comment`, method: 'PUT', body: { comment: p.comment ?? command.comment ?? '' } }] });
        }
        const body = { ...pick(current, clientFields), tagNamesText: (current.tags || []).map(item => item.name).join(', '), ...pick(p, clientFields) };
        if (p.newName && (current.firstName || current.lastName || current.middleName)
            && !['firstName', 'lastName', 'middleName'].some(key => p[key] !== undefined)) {
            throw new Error('У клиента имя и фамилия записаны отдельно. Назовите изменяемую часть имени или фамилию, чтобы сохранить остальные данные.');
        }
        if (p.newName != null) body.name = p.newName;
        if (p.newPhone !== undefined) body.phone = p.newPhone;
        const messages = changes(current, body, clientFields.filter(key => key !== 'tagNamesText' || p.tagNamesText !== undefined));
        if (!messages.length) throw new Error('Не названы изменения клиента.');

        return r.plan({ target, result, changes: messages, requests: [{ path, method: 'PUT', body }] });
    }

    async function prepareVehicle(command, r) {
        const p = r.p;
        if (command.action === 'vehicle_create') {
            const plate = globalThis.OpenCRMAiPilotExecutor.normalizePlate(command.registrationPlate);
            if (!/^[ABEKMHOPCTYX]\d{3}[ABEKMHOPCTYX]{2}\d{2,3}$/.test(plate)) throw new Error('Укажите полный госномер с регионом.');
            const body = { kind: 1, mileageUnit: 1, registrationPlate: plate, organizationId: r.context.organizationId, ...pick(p, vehicleFields) };
            if (p.clientName || p.phone) body.organizationClientId = (await resolveClient(command, r)).client.id;
            return r.plan({ target: `Новый автомобиль ${plate}`, changes: Object.entries(p).map(([key, value]) => `${labels[key] || key}: ${value}`),
                requests: [{ path: 'api/v1/vehicles', method: 'POST', body }], created: 'vehicle' });
        }
        const current = await r.vehicle();
        const path = `api/v1/vehicles/${current.id}?${r.query}`;
        const result = { vehicleUrl: `${r.context.workspaceUrl}#vehicles/${current.id}` };
        const target = `Автомобиль ${current.registrationPlate}`;
        if (command.action === 'vehicle_get') return r.plan({ target, result, data: { Номер: current.registrationPlate, Марка: current.brandName || current.manualBrandName, Модель: current.modelName || current.manualModelName, VIN: current.vin, Пробег: current.currentMileage, Клиент: current.organizationClientName } });
        r.watch(path, current);
        if (command.action === 'vehicle_archive' || command.action === 'vehicle_restore') return r.plan({ target, result,
            changes: [command.action === 'vehicle_archive' ? 'Переместить автомобиль в архив.' : 'Вернуть автомобиль из архива.'], destructive: command.action === 'vehicle_archive',
            requests: [{ path: command.action === 'vehicle_archive' ? path : `api/v1/vehicles/${current.id}/restore?${r.query}`, method: command.action === 'vehicle_archive' ? 'DELETE' : 'POST' }] });
        const body = { ...pick(current, vehicleFields), ...pick(p, vehicleFields) };
        if (p.newRegistrationPlate != null) body.registrationPlate = globalThis.OpenCRMAiPilotExecutor.normalizePlate(p.newRegistrationPlate);
        if (p.manualBrandName !== undefined || p.manualModelName !== undefined) body.modelId = null;
        if (p.clientName || p.phone) body.organizationClientId = (await resolveClient(command, r)).client.id;
        if (p.clientName === '') body.organizationClientId = null;
        const messages = changes(current, body, vehicleFields.filter(key => key !== 'organizationClientId'));
        if (current.organizationClientId !== body.organizationClientId) messages.push(`Клиент: ${current.organizationClientName || 'не указан'} → ${body.organizationClientId ? p.clientName || 'найден по телефону' : 'снять связь'}`);
        if (!messages.length) throw new Error('Не названы изменения автомобиля.');

        return r.plan({ target, result, changes: messages, requests: [{ path, method: 'PUT', body }] });
    }

    async function prepare(command, context, options) {
        if (!context?.organizationId || !context.userId || !options?.request) throw new Error('Не определено рабочее пространство.');
        const definition = context.actions?.find(item => item.action === command.action);
        if (context.actions && !definition) throw new Error('Действие не входит в разрешённый каталог.');
        const r = createRuntime(command, context, options.request);
        if (legacy.has(command.action)) {
            const details = globalThis.OpenCRMAiPilotExecutor.getDetails(command);
            if (!details.length) throw new Error('Какие работы нужно записать? Назовите хотя бы одну работу.');
            const resolution = await globalThis.OpenCRMAiPilotServiceCatalog.prepareRows(details, context, { request: options.request });
            return {
                target: `${definition?.title || command.action} · ${command.registrationPlate}`,
                changes: [
                    ...resolution.notes,
                    ...(command.services || []).map(item => `${item.name}: ${money(item.price)}`),
                    ...(command.date ? [`Дата: ${command.date} ${command.startTime || ''} — ${command.endTime || ''}`] : []),
                    ...(command.totalAmount != null ? [`Общая стоимость: ${money(command.totalAmount)}`] : []),
                    ...(command.paidAmount != null ? [`Принято: ${money(command.paidAmount)}`] : [])
                ],
                destructive: false,
                execute: settings => globalThis.OpenCRMAiPilotExecutor.execute(command, context, {
                    ...settings, serviceResolution: resolution, request: options.request
                })
            };
        }
        if (command.action.startsWith('client_')) return prepareClient(command, r);
        if (command.action.startsWith('vehicle_')) return prepareVehicle(command, r);
        if (command.action === 'schedule' || command.action === 'report') {
            const from = r.p.dateFrom || command.date || today();
            const to = r.p.dateTo || from;
            if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to) || from > to) throw new Error('Укажите корректный период отчёта.');
            if (command.action === 'schedule') {
                const items = await r.read(`${r.org}/workspace/maintenance-calendar?dateFrom=${encodeURIComponent(from)}&dateTo=${encodeURIComponent(to)}&includeAllStatuses=true`);
                return r.plan({ target: `Расписание ${from} — ${to}`, data: items.map(item => ({ Дата: day(item.date), Начало: item.plannedStartTime, Окончание: item.plannedEndTime,
                    Автомобиль: item.registrationPlate || item.vehicleDisplayName, Работы: item.serviceNames?.join(', '), Пост: item.location, Статус: item.statusName, Стоимость: money(item.price) })), result: { url: `${context.workspaceUrl}#maintenance` } });
            }
            const finance = await r.read(`${r.org}/finance?dateFrom=${encodeURIComponent(from)}&dateTo=${encodeURIComponent(to)}`);
            const values = finance.kpis;
            return r.plan({ target: `Финансы ${from} — ${to}`, data: { Заказы: values.ordersCount, Начислено: money(values.netAmount), Получено: money(values.paidAmount),
                Долг: money(values.debtAmount), Расходы: money(values.costAmount), Прибыль: money(values.profitAmount), Скидки: money(values.discountAmount) }, result: { url: `${context.workspaceUrl}#finance` } });
        }
        if (globalThis.OpenCRMAiPilotBusinessActions?.actions.includes(command.action)) return globalThis.OpenCRMAiPilotBusinessActions.prepare(command, r);
        if (workflow.has(command.action) || ['maintenance_get', 'maintenance_update', 'maintenance_delete', 'service_add', 'service_update', 'service_remove', 'services_replace', 'payment_add', 'payment_cancel', 'assign_employee', 'assign_location', 'intake_update'].includes(command.action)) return prepareMaintenance(command, r);
        throw new Error('Для этой команды пока нет исполнителя.');
    }

    globalThis.OpenCRMAiPilotActions = { prepare, isMaterial };

    function today() {
        return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Novosibirsk', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
    }

    function normalizePhone(value) {
        const digits = String(value || '').replace(/\D/g, '');

        return digits.length === 11 && digits.startsWith('8') ? '7' + digits.slice(1) : digits;
    }
})();
