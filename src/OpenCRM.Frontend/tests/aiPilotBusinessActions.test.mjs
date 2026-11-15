import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import test from 'node:test';

const source = await readFile(new URL('../../OpenCRM.WebApp/wwwroot/js/ai-pilot-business-actions.js', import.meta.url), 'utf8');

function createRuntime(command, responses = {}) {
    const plans = [];
    const requests = [];
    const watched = [];
    let uuid = 0;
    const context = {
        OpenCRMAiPilotBusinessActions: undefined,
        URLSearchParams,
        crypto: { randomUUID: () => `command-${++uuid}` }
    };
    vm.runInNewContext(source, context);

    const runtime = {
        p: command.parameters ?? {},
        command,
        context: { organizationId: 'org-1', workspaceUrl: '/autoservice/org-1' },
        org: 'api/v1/organizations/org-1',
        query: 'organizationId=org-1',
        read: async path => {
            if (!(path in responses)) throw new Error(`Unexpected GET: ${path}`);
            return responses[path];
        },
        one: (items, name, label) => {
            const match = items.filter(item => item.name.toLowerCase() === name.toLowerCase());
            if (match.length !== 1) throw new Error(`Ambiguous ${label}`);
            return match[0];
        },
        vehicle: async () => ({ id: 'vehicle-1', registrationPlate: 'А123ВС154' }),
        maintenance: async () => ({ id: 'maintenance-1', registrationPlate: 'А123ВС154', maintenanceDetails: [{ id: 'detail-1', detailId: 'detail-1', name: 'Замена масла' }] }),
        line: items => items[0],
        pick: (object, fields) => Object.fromEntries(fields.map(field => [field, object[field]])),
        watch: (path, snapshot, project) => {
            watched.push({ path, snapshot, project });
            return { path, snapshot };
        },
        plan: async plan => {
            plans.push(plan);
            return { ...plan, execute: async () => {
                requests.push(...(plan.requests ?? []));
                return { ok: true };
            } };
        }
    };

    return { actions: context.OpenCRMAiPilotBusinessActions, runtime, plans, requests, watched };
}

test('inventory issue prepares a fixed payload and does not mutate before execute', async () => {
    const { actions, runtime, plans, requests, watched } = createRuntime({
        action: 'inventory_issue',
        parameters: { name: 'Масло 5W-30', quantity: 2 }
    }, {
        'api/v1/organizations/org-1/materials?stocksPage=1&stocksPageSize=100': {
            stocks: [{ id: 'stock-1', name: 'Масло 5W-30', unit: 'л', availableQuantity: 7 }]
        }
    });

    const plan = await actions.prepare(runtime.command, runtime);

    assert.equal(plans.length, 1);
    assert.equal(watched.length, 1);
    assert.deepEqual(JSON.parse(JSON.stringify(plan.requests[0])), {
        path: 'api/v1/organizations/org-1/materials/issue',
        method: 'POST',
        body: { stockId: 'stock-1', quantity: 2, comment: null }
    });
    assert.equal(requests.length, 0);

    await plan.execute();

    assert.equal(requests.length, 1);
});

test('catalog mutation rejects ambiguous names before creating a plan', async () => {
    const { actions, runtime, plans } = createRuntime({
        action: 'catalog_delete',
        parameters: { catalogKind: 'service', name: 'Диагностика' }
    }, {
        'api/v1/organizations/org-1/scenario-catalog': {
            services: [
                { id: 'service-1', name: 'Диагностика', isSystem: false },
                { id: 'service-2', name: 'Диагностика', isSystem: false }
            ]
        }
    });

    await assert.rejects(actions.prepare(runtime.command, runtime), /несколько совпадений/i);
    assert.equal(plans.length, 0);
});

test('shift deletion preserves the server version and requires a reason', async () => {
    const { actions, runtime } = createRuntime({
        action: 'shift_delete',
        date: '2026-10-26',
        parameters: { employeeName: 'Иван', reason: 'Сотрудник не выйдет' }
    }, {
        'api/v1/organizations/org-1/shifts?weekStart=2026-10-26': {
            canManage: true,
            employees: [{ id: 'employee-1', name: 'Иван', isActive: true }],
            shifts: [{ id: 'shift-1', employeeId: 'employee-1', localStartDate: '2026-10-26', startLocal: '2026-10-26T09:00:00', endLocal: '2026-10-26T18:00:00', version: 17, isCancelled: false }]
        }
    });

    const plan = await actions.prepare(runtime.command, runtime);

    assert.deepEqual(JSON.parse(JSON.stringify(plan.requests[0])), {
        path: 'api/v1/organizations/org-1/shifts/shift-1',
        method: 'DELETE',
        body: { version: 17, reason: 'Сотрудник не выйдет' }
    });
});

test('employee creation uses the API field names', async () => {
    const { actions, runtime } = createRuntime({
        action: 'employee_create',
        parameters: { name: 'Иван', phone: '+7 000 000-00-01' }
    }, {
        'api/v1/organizations/org-1/employees': { canManage: true, items: [] }
    });

    const plan = await actions.prepare(runtime.command, runtime);

    assert.deepEqual(JSON.parse(JSON.stringify(plan.requests[0].body)), {
        name: 'Иван', phone: '+7 000 000-00-01', memberId: null, isActive: true, version: 0
    });
});

test('work finish creates a versioned, idempotent transition plan', async () => {
    const { actions, runtime } = createRuntime({
        action: 'work_finish',
        parameters: { maintenanceId: 'maintenance-1', serviceName: 'Замена масла' }
    }, {
        'api/v1/organizations/org-1/work/maintenance-1/progress': [
            { detailId: 'detail-1', name: 'Замена масла', status: 2, version: 9 }
        ]
    });

    const plan = await actions.prepare(runtime.command, runtime);

    assert.deepEqual(JSON.parse(JSON.stringify(plan.requests[0])), {
        path: 'api/v1/organizations/org-1/work/maintenance-1/operations/detail-1/transition',
        method: 'POST',
        body: { commandId: 'command-1', version: 9, action: 'complete', reason: null }
    });
});

test('date validation rejects impossible calendar dates', async () => {
    const { actions, runtime, plans } = createRuntime({
        action: 'shift_list',
        date: '2026-02-30'
    });

    await assert.rejects(actions.prepare(runtime.command, runtime), /формате ГГГГ-ММ-ДД/);
    assert.equal(plans.length, 0);
});

test('a paused operation uses resume instead of start', async () => {
    const { actions, runtime } = createRuntime({ action: 'work_start', parameters: { serviceName: 'Замена масла' } }, {
        'api/v1/organizations/org-1/work/maintenance-1/progress': [
            { detailId: 'detail-1', name: 'Замена масла', status: 3, version: 10 }
        ]
    });
    const plan = await actions.prepare(runtime.command, runtime);
    assert.equal(plan.requests[0].body.action, 'resume');
    assert.equal(plan.requests[0].body.version, 10);
});

for (const status of [1, 3, 4]) {
    test(`an operation in state ${status} cannot be completed without starting or resuming`, async () => {
        const { actions, runtime } = createRuntime({ action: 'work_finish', parameters: {} }, {
            'api/v1/organizations/org-1/work/maintenance-1/progress': [
                { detailId: 'detail-1', name: 'Замена масла', status, version: 10 }
            ]
        });
        await assert.rejects(actions.prepare(runtime.command, runtime), /Завершить можно выполняющуюся/);
    });
}

test('a running timer does not invalidate a prepared operation but a version change does', async () => {
    const progress = [{ detailId: 'detail-1', name: 'Масло', status: 2, version: 7, activeTime: '00:01:00' }];
    const { actions, runtime, watched } = createRuntime({ action: 'work_pause', parameters: { reason: 'Перерыв' } }, {
        'api/v1/organizations/org-1/work/maintenance-1/progress': progress
    });
    await actions.prepare(runtime.command, runtime);
    const check = watched[0].project;
    assert.equal(JSON.stringify(check(progress)), JSON.stringify(check([{ ...progress[0], activeTime: '00:02:00' }])));
    assert.notEqual(JSON.stringify(check(progress)), JSON.stringify(check([{ ...progress[0], version: 8 }])));
});

test('spoken liters are converted to the stock milliliters before issuing', async () => {
    const { actions, runtime } = createRuntime({ action: 'inventory_issue', parameters: { name: 'Масло', quantity: 2, unit: 'л' } }, {
        'api/v1/organizations/org-1/materials?stocksPage=1&stocksPageSize=100': {
            stocks: [{ id: 'stock', name: 'Масло', unit: 'мл', availableQuantity: 5000 }]
        }
    });
    const plan = await actions.prepare(runtime.command, runtime);
    assert.equal(plan.requests[0].body.quantity, 2000);
});

test('bundle confirmation names the old and new services', async () => {
    const { actions, runtime } = createRuntime({ action: 'catalog_update', parameters: { catalogKind: 'service_bundle', name: 'Комплекс', serviceNames: 'Мойка; Полировка' } }, {
        'api/v1/organizations/org-1/scenario-catalog': {
            services: [{ id: 'wash', name: 'Мойка' }, { id: 'polish', name: 'Полировка' }],
            serviceBundles: [{ id: 'bundle', name: 'Комплекс', serviceIds: ['wash'], sortOrder: 1, isActive: true }]
        }
    });
    const plan = await actions.prepare(runtime.command, runtime);
    assert.ok(plan.changes.some(text => text.includes('Услуги набора: Мойка → Мойка; Полировка')));
});

test('bundle order changes are shown in the same order the API will save', async () => {
    const { actions, runtime } = createRuntime({ action: 'catalog_update', parameters: { catalogKind: 'service_bundle', name: 'Комплекс', serviceNames: 'Полировка; Мойка' } }, {
        'api/v1/organizations/org-1/scenario-catalog': {
            services: [{ id: 'wash', name: 'Мойка' }, { id: 'polish', name: 'Полировка' }],
            serviceBundles: [{ id: 'bundle', name: 'Комплекс', serviceIds: ['wash', 'polish'], sortOrder: 1, isActive: true }]
        }
    });
    const plan = await actions.prepare(runtime.command, runtime);
    assert.ok(plan.changes.some(text => text.includes('Мойка; Полировка → Полировка; Мойка')));
    assert.equal(plan.requests[0].body.serviceIds[0], 'polish');
});
