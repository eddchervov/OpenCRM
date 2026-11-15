(() => {
    'use strict';

    const actions = new Set(['visit', 'book', 'arrive', 'start', 'complete', 'reschedule']);
    const activeExecutions = new Set();
    const memory = new Map();

    function normalizePlate(value) {
        const source = 'АВЕКМНОРСТУХ';
        const target = 'ABEKMHOPCTYX';

        return String(value || '').toUpperCase().replace(/[^A-ZА-Я0-9]/g, '')
            .split('').map(character => source.includes(character) ? target[source.indexOf(character)] : character).join('');
    }

    function loadState(key) {
        try {
            return JSON.parse(sessionStorage.getItem(key) || 'null') || memory.get(key) || {};
        } catch {
            return memory.get(key) || {};
        }
    }

    function storeState(key, state) {
        memory.set(key, state);
        try {
            sessionStorage.setItem(key, JSON.stringify(state));
        } catch {
            // При запрете хранилища защита от повторения сохраняется в текущем окне.
        }
    }

    function getStatus(item) {
        const statuses = { planned: 1, inprogress: 2, completed: 3, cancelled: 4 };

        return typeof item.status === 'number' ? item.status : statuses[String(item.status).toLowerCase()];
    }

    function getDetails(command) {
        const services = command.services || [];
        if (!services.length) {
            return [];
        }

        if (command.totalAmount != null && services.every(service => service.price != null)) {
            const sum = services.reduce((total, service) => total + Math.round(Number(service.price) * 100), 0);
            if (sum !== Math.round(Number(command.totalAmount) * 100)) {
                throw new Error('Сумма цен работ отличается от общего итога. Исправьте команду перед сохранением.');
            }
        }

        if (services.length > 1 && command.totalAmount != null && services.some(service => service.price == null)) {
            throw new Error('Назовите цену каждой работы отдельно, чтобы сохранить услуги отдельными строками.');
        }

        return services.map(service => ({
            name: service.name,
            price: services.length === 1 && service.price == null ? command.totalAmount : service.price,
            ...(service.catalogItemId ? { catalogItemId: service.catalogItemId } : {})
        }));
    }

    function localDate() {
        return new Intl.DateTimeFormat('en-CA', {
            timeZone: 'Asia/Novosibirsk', year: 'numeric', month: '2-digit', day: '2-digit'
        }).format(new Date());
    }

    async function execute(command, context, options) {
        if (!command || !actions.has(command.action)) {
            throw new Error('Это действие не разрешено в пилоте.');
        }

        const plate = normalizePlate(command.registrationPlate);
        if (!/^[ABEKMHOPCTYX]\d{3}[ABEKMHOPCTYX]{2}\d{2,3}$/.test(plate)) {
            throw new Error('Для демо укажите полный российский госномер с регионом, например А123ВС154.');
        }

        if (!context.organizationId || !context.userId || !options.draftToken) {
            throw new Error('Не определён контекст тестовой организации. Обновите страницу.');
        }

        let details = getDetails(command);
        if (['visit', 'book', 'arrive'].includes(command.action) && !details.length) {
            throw new Error('Какие работы нужно записать? Назовите хотя бы одну работу.');
        }

        if (command.action === 'visit' && details.some(item => item.price == null)) {
            throw new Error('Для выполненного визита укажите стоимость работ. Для бесплатной работы скажите «ноль рублей».');
        }

        const key = `opencrm.ai-pilot.${context.userId}.${context.organizationId}.${options.draftToken}`;
        if (activeExecutions.has(key)) {
            throw new Error('Эта команда уже выполняется.');
        }

        const state = loadState(key);
        if (state.result) {
            return state.result;
        }

        if (state.pending) {
            throw Object.assign(new Error('Результат предыдущего запроса не подтверждён. Проверьте записи в CRM перед повтором.'), links(state, context));
        }

        activeExecutions.add(key);
        const request = options.request;
        const query = `organizationId=${encodeURIComponent(context.organizationId)}`;
        const persist = () => storeState(key, state);
        let serviceResolution;
        let servicesVerified = false;
        async function mutate(path, method, body) {
            if (!servicesVerified && serviceResolution) {
                await serviceResolution.verify();
                servicesVerified = true;
            }
            state.pending = true;
            persist();
            try {
                const result = await request(path, { method, body });
                state.pending = false;
                persist();

                return result;
            } catch (error) {
                if (error.status >= 400 && error.status < 500) {
                    state.pending = false;
                    persist();
                }

                throw error;
            }
        }

        async function detail() {
            return request(`api/v1/maintenance/${state.maintenanceId}?${query}`);
        }

        async function workflow(action, fields = {}) {
            const current = await detail();
            await mutate(`api/v1/maintenance/${state.maintenanceId}/workflow?${query}`, 'POST', {
                action, version: current.version, ...fields
            });
        }

        try {
            if (details.length) {
                if (!globalThis.OpenCRMAiPilotServiceCatalog) throw new Error('Подбор услуг не загрузился. Обновите страницу.');
                serviceResolution = options.serviceResolution || await globalThis.OpenCRMAiPilotServiceCatalog.prepareRows(details, context, { request });
                details = serviceResolution.rows;
            }
            if (!state.vehicleId) {
                const matches = [];
                let page = 1;
                let total = 0;
                do {
                    const vehicles = await request(`api/v1/vehicles?${query}&page=${page}&pageSize=100`);
                    total = Number(vehicles.totalCount || 0);
                    if (total > 1000) {
                        throw new Error('Демо поддерживает до 1000 автомобилей в тестовой организации.');
                    }

                    matches.push(...vehicles.items.filter(vehicle => normalizePlate(vehicle.registrationPlate) === plate));
                    if (!vehicles.items.length || page >= Number(vehicles.totalPages || Math.ceil(total / Number(vehicles.pageSize || 100)))) {
                        break;
                    }

                    page += 1;
                } while (page <= 1000);

                if (matches.length > 1) {
                    throw new Error('Найдено несколько автомобилей с таким номером. Устраните неоднозначность в списке автомобилей.');
                }

                if (matches.length) {
                    state.vehicleId = matches[0].id;
                } else {
                    if (['start', 'complete', 'reschedule'].includes(command.action)) {
                        throw new Error('Автомобиль с таким номером не найден. Для нового визита используйте «приехал» или «сделали».');
                    }

                    state.vehicleId = await mutate('api/v1/vehicles', 'POST', {
                        organizationId: context.organizationId, registrationPlate: plate, name: plate, kind: 1
                    });
                }

                persist();
            }

            if (!state.maintenanceId && command.action !== 'book' && command.action !== 'visit') {
                if (context.selectedMaintenanceId) {
                    const selected = await request(`api/v1/maintenance/${encodeURIComponent(context.selectedMaintenanceId)}?${query}`);
                    if (selected.vehicleId !== state.vehicleId) throw new Error('Выбранный заказ относится к другому автомобилю.');
                    state.maintenanceId = selected.id;
                    persist();
                }
            }

            if (!state.maintenanceId && command.action !== 'book' && command.action !== 'visit') {
                const candidates = [];
                for (const status of [1, 2]) {
                    const page = await request(`api/v1/vehicles/${state.vehicleId}/maintenance/1/100?${query}&status=${status}`);
                    if (page.hasMore) {
                        throw new Error('Слишком много незавершённых записей для автоматического выбора.');
                    }

                    candidates.push(...page.items);
                }

                const unique = Array.from(new Map(candidates.map(item => [item.id, item])).values());
                if (unique.length > 1) {
                    throw new Error('У автомобиля несколько незавершённых записей. Откройте нужную запись вручную.');
                }

                if (unique.length) {
                    state.maintenanceId = unique[0].id;
                    persist();
                } else if (['start', 'complete', 'reschedule'].includes(command.action)) {
                    throw new Error('Подходящая незавершённая запись не найдена. Изменения не выполнялись.');
                }
            }

            if (!state.maintenanceId) {
                if (command.action === 'visit' && !details.length) {
                    throw new Error('Для выполненного визита продиктуйте хотя бы одну работу.');
                }

                const status = command.action === 'book' ? 1 : command.action === 'arrive' ? 2 : 3;
                const comment = [command.comment, command.action === 'arrive' && command.startTime ? `Со слов сотрудника начало: ${command.startTime}.` : null]
                    .filter(Boolean).join('\n') || null;
                state.maintenanceId = await mutate(`api/v1/vehicles/${state.vehicleId}/maintenance?${query}`, 'POST', {
                    status, type: 1, plannedDate: command.date || localDate(),
                    plannedStartTime: status === 1 && command.startTime ? command.startTime + ':00' : null,
                    plannedEndTime: status === 1 && command.endTime ? command.endTime + ':00' : null,
                    completedDate: status === 3 ? command.date || localDate() : null,
                    location: null, performer: null, mileage: null, comment,
                    details
                });
                state.created = true;
                persist();
            } else if (!state.workflowDone) {
                let current = await detail();
                if (command.action === 'reschedule') {
                    if (!command.date) {
                        throw new Error('Для переноса укажите новую дату.');
                    }

                    await workflow('reschedule', {
                        plannedDate: command.date,
                        plannedStartTime: command.startTime ? command.startTime + ':00' : current.plannedStartTime,
                        plannedEndTime: command.endTime ? command.endTime + ':00' : current.plannedEndTime,
                        location: current.location
                    });
                } else if (command.action === 'start' || command.action === 'arrive') {
                    if (details.length && !state.servicesDone) {
                        if ((current.maintenanceDetails || []).length) {
                            throw new Error('У записи уже есть работы. Примите автомобиль без нового списка, затем измените работы в карточке заказа.');
                        }

                        await mutate(`api/v1/maintenance/${state.maintenanceId}/services?${query}`, 'PUT', {
                            version: current.version, details, allowUnspecifiedPrices: true
                        });
                        state.servicesDone = true;
                        persist();
                    }

                    if (getStatus(current) === 1) {
                        await workflow('start');
                    }
                } else if (command.action === 'complete' || command.action === 'visit') {
                    if ((current.maintenanceDetails || []).some(item => item.materialCatalogItemId || item.materialName || item.materialQuantity != null)) {
                        throw new Error('В заказе есть материалы. Завершите его в карточке: голосовой пилот пока учитывает только услуги.');
                    }

                    const completionDetails = details.length || command.totalAmount == null ? details : getDetails({
                        ...command,
                        services: (current.maintenanceDetails || []).map(item => ({ name: item.name, price: null }))
                    });
                    const finalDetails = completionDetails.length ? completionDetails : current.maintenanceDetails || [];
                    if (!finalDetails.length) {
                        throw new Error('Перед завершением продиктуйте выполненные работы.');
                    }

                    if (finalDetails.some(item => item.price == null)) {
                        throw new Error('Перед завершением укажите стоимость работ. Для бесплатной работы скажите «ноль рублей».');
                    }

                    if (completionDetails.length && !state.servicesDone) {
                        await mutate(`api/v1/maintenance/${state.maintenanceId}/services?${query}`, 'PUT', {
                            version: current.version, details: completionDetails
                        });
                        state.servicesDone = true;
                        persist();
                        current = await detail();
                    }

                    if (getStatus(current) !== 3) {
                        await workflow('complete', { completedDate: command.date || localDate() });
                    }
                }

                state.workflowDone = true;
                persist();
            }

            if (command.paidAmount != null && command.paidAmount > 0 && !state.paymentDone) {
                const current = await detail();
                if (!current.canManagePayments) {
                    throw new Error('Заказ сохранён, но у пользователя нет права принимать оплату.');
                }

                state.paymentId ||= crypto.randomUUID();
                persist();
                await mutate(`api/v1/maintenance/${state.maintenanceId}/payments?${query}`, 'POST', {
                    id: state.paymentId, amount: command.paidAmount,
                    methodCode: ['cash', 'card', 'bank'].includes(command.paymentMethod) ? command.paymentMethod : 'other',
                    expectedPaidAmount: current.paidAmount, comment: 'Учтено через локальный голосовой пилот.'
                });
                state.paymentDone = true;
                persist();
            }

            state.result = { message: 'Команда выполнена через API локальной CRM.', maintenanceId: state.maintenanceId, vehicleId: state.vehicleId, ...links(state, context) };
            persist();

            return state.result;
        } catch (error) {
            Object.assign(error, links(state, context));
            if (state.maintenanceId) {
                error.message += ' Запись уже существует; проверьте её по ссылке перед повтором.';
            }

            throw error;
        } finally {
            activeExecutions.delete(key);
        }
    }

    function links(state, context) {
        return {
            maintenanceUrl: state.maintenanceId ? `${context.workspaceUrl}#maintenance/${state.maintenanceId}` : null,
            vehicleUrl: state.vehicleId ? `${context.workspaceUrl}#vehicles/${state.vehicleId}` : null
        };
    }

    globalThis.OpenCRMAiPilotExecutor = { execute, normalizePlate, getDetails };
})();
