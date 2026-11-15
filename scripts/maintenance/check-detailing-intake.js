/* Проверка приёмки в открытой сессии владельца: выполнить как Snippet в DevTools на странице детейлинга. */
(async () => {
  'use strict';
  if (!['opencrm.example.invalid', 'localhost', '127.0.0.1'].includes(location.hostname)) {
    throw new Error('Откройте OpenCRM перед запуском проверки.');
  }
  const organizationId = location.pathname.match(/^\/detailing\/([a-f\d-]{36})(?:\/|$)/i)?.[1];
  if (!organizationId) throw new Error('Откройте рабочее пространство организации детейлинга.');
  const token = document.querySelector('meta[name="request-verification-token"]')?.content;
  if (!token) throw new Error('Не найден токен формы. Обновите страницу авторизованного аккаунта.');
  if (!navigator.locks) throw new Error('Для защиты от двойного запуска требуется HTTPS или localhost.');

  const request = async (path, method = 'GET', body, expectedStatus) => {
    const headers = { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' };
    if (method !== 'GET') headers.RequestVerificationToken = token;
    if (body && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, {
      method, credentials: 'same-origin', headers,
      body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined
    });
    if (expectedStatus !== undefined) {
      if (response.status !== expectedStatus) {
        const details = (await response.text()).slice(0, 250);
        throw new Error(`Проверка отклонения запроса: ожидался HTTP ${expectedStatus}, получен ${response.status}. Ответ: ${details}`);
      }
      return null;
    }
    if (!response.ok) {
      const error = await response.json().catch(() => null);
      throw new Error(`${method} ${path}: HTTP ${response.status}. ${error?.message ?? error?.title ?? ''}`);
    }
    return response.status === 204 || response.headers.get('content-length') === '0'
      ? null : response.headers.get('content-type')?.includes('json') ? response.json() : response;
  };
  const context = await request(`/api/v1/detailing/organizations/${organizationId}/workspace/context`);
  if (!context.user?.canOpenAdminPanel || context.workspaceAccess?.roleCode !== 'owner' || context.workspaceAccess.isReadOnly) {
    throw new Error('Нужна организация, владельцем которой является открытый основной аккаунт администратора, с доступом на запись.');
  }
  const storageKey = `opencrm:qa:intake:v1:${context.user.id}:${organizationId}`;
  await navigator.locks.request(storageKey, { ifAvailable: true }, async lock => {
    if (!lock) throw new Error('Проверка уже выполняется в другой вкладке.');
    const previous = localStorage.getItem(storageKey);
    if (previous) {
      console.table(JSON.parse(previous).records ?? []);
      throw new Error('Этот набор уже запускался. Состояние сохранено в localStorage; повторное создание отключено.');
    }
    const runId = crypto.randomUUID().slice(0, 8);
    const prefix = `ТЕСТ ПРИЁМКИ ${runId}`;
    const state = { runId, organizationId, accountId: context.user.id, startedAt: new Date().toISOString(), records: [], checks: [], pending: null, completed: false };
    const persist = () => localStorage.setItem(storageKey, JSON.stringify(state));
    persist();
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const passed = message => { state.checks.push(message); persist(); console.info('✓', message); };
    const mutate = async (label, path, method, body, expectedStatus) => {
      state.pending = { label, path, method };
      persist();
      const result = await request(path, method, body, expectedStatus);
      state.pending = null;
      persist();
      return result;
    };
    const query = `?organizationId=${organizationId}`;
    const maintenancePath = id => `/api/v1/maintenance/${id}${query}`;
    const photoPath = id => `/api/v1/media/maintenance/${id}/photos${query}`;
    const workflow = (id, action, extra = {}) => mutate(action, `/api/v1/maintenance/${id}/workflow${query}`, 'POST', {
      action, reason: `${prefix}: проверка перехода статуса`, ...extra
    });
    const date = offset => {
      const value = new Date(); value.setDate(value.getDate() + offset);
      return `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`;
    };
    const imageFile = async stage => {
      const canvas = document.createElement('canvas'); canvas.width = 960; canvas.height = 640;
      const drawing = canvas.getContext('2d');
      drawing.fillStyle = stage === 1 ? '#40566c' : '#23746c'; drawing.fillRect(0, 0, 960, 640);
      drawing.fillStyle = '#fff'; drawing.font = 'bold 36px sans-serif';
      drawing.fillText(stage === 1 ? 'ДО РАБОТЫ' : 'ПОСЛЕ РАБОТЫ', 50, 85);
      drawing.font = '24px sans-serif'; drawing.fillText(prefix, 50, 135);
      drawing.fillText('Синтетическое изображение для проверки', 50, 570);
      drawing.fillStyle = '#cdd9e5'; drawing.fillRect(150, 280, 650, 160); drawing.fillRect(280, 200, 330, 100);
      drawing.fillStyle = '#18202b';
      for (const x of [280, 680]) { drawing.beginPath(); drawing.arc(x, 440, 60, 0, Math.PI * 2); drawing.fill(); }
      const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
      assert(blob, 'Не удалось создать тестовое изображение.');
      return new File([blob], `${runId}-${stage === 1 ? 'before' : 'after'}.png`, { type: 'image/png' });
    };
    try {
      console.info('Аккаунт:', context.user.email, 'Организация:', context.organization.name, 'Набор:', prefix);
      const catalogPath = `/api/v1/organizations/${organizationId}/scenario-catalog`;
      const service = await mutate('Тестовая услуга', catalogPath, 'POST', { kindCode: 'service', name: `${prefix}: полировка`, sortOrder: 999 });
      state.serviceId = service.id; persist();
      const post = await mutate('Тестовый пост', catalogPath, 'POST', { kindCode: 'work_post', name: `${prefix}: пост`, sortOrder: 999 });
      state.postId = post.id; persist();
      const scenarios = [
        { name: 'Запланировано', day: 0, endDay: 0, start: '18:00:00', end: '19:00:00' },
        { name: 'В работе', day: 0, endDay: 0, start: '08:00:00', end: '17:00:00' },
        { name: 'Выполнено, два дня', day: -1, endDay: 0, start: '09:00:00', end: '07:00:00' }
      ];
      for (const [index, scenario] of scenarios.entries()) {
        const vehicleId = await mutate('Тестовый автомобиль', '/api/v1/vehicles', 'POST', {
          organizationId, kind: 1, mileageUnit: 1, name: `${prefix}: авто ${index + 1}`,
          description: `${prefix}. Синтетические данные, реального клиента нет.`,
          registrationPlate: null, modelId: null, manualBrandName: null, manualModelName: null
        });
        const record = { vehicleId, name: scenario.name, maintenanceId: null }; state.records.push(record); persist();
        const payload = {
          status: 1, type: 1, plannedDate: date(scenario.day), plannedEndDate: date(scenario.endDay),
          plannedStartTime: scenario.start, plannedEndTime: scenario.end, completedDate: null,
          location: post.name, performer: null, mileage: null, comment: `${prefix}: ${scenario.name}`,
          details: [{ catalogItemId: service.id, name: service.name, price: 0 }]
        };
        record.maintenanceId = await mutate('Тестовая запись', `/api/v1/vehicles/${vehicleId}/maintenance${query}`, 'POST', payload);
        record.url = `${location.origin}/detailing/${organizationId}#maintenance/${record.maintenanceId}`;
        persist();
        const id = record.maintenanceId;
        const intakePath = `/api/v1/maintenance/${id}/intake${query}`;
        await mutate('Отказ пустой приёмки', intakePath, 'PUT', { notes: ' ' }, 400);
        await mutate('Приёмка', intakePath, 'PUT', { notes: `${prefix}: царапина на бампере, ключи приняты.` });
        const first = await request(maintenancePath(id));
        assert(first.status === 1 && first.intakeRecordedAt, 'Приёмка изменила статус или не сохранила время.');
        await mutate('Уточнение приёмки', intakePath, 'PUT', { notes: `${prefix}: царапина на бампере слева, ключи приняты.` });
        const updated = await request(maintenancePath(id));
        assert(updated.intakeRecordedAt === first.intakeRecordedAt && updated.intakeNotes.includes('слева'), 'Нарушено сохранение приёмки.');
        for (const stage of [1, 2]) {
          const form = new FormData(); form.append('formFiles', await imageFile(stage));
          await mutate('Фото ' + stage, `${photoPath(id)}&stage=${stage}`, 'POST', form);
        }
        const photos = await request(photoPath(id));
        assert(photos.length === 2 && photos.some(p => p.stage === 1) && photos.some(p => p.stage === 2), 'Не сохранились категории фото.');
        const before = photos.find(photo => photo.stage === 1);
        const stagePath = `/api/v1/media/maintenance/${id}/photos/${before.photoId}/stage${query}`;
        await mutate('Перенос фото', `${stagePath}&stage=2`, 'PUT');
        assert((await request(photoPath(id))).find(photo => photo.photoId === before.photoId)?.stage === 2, 'Этап фото не изменился.');
        await mutate('Возврат фото в «до»', `${stagePath}&stage=1`, 'PUT');
        if (index === 0) {
          await workflow(id, 'start'); await workflow(id, 'return_to_planned');
          await workflow(id, 'cancel');
          await workflow(id, 'reopen', { plannedDate: payload.plannedDate, plannedEndDate: payload.plannedEndDate, plannedStartTime: payload.plannedStartTime, plannedEndTime: payload.plannedEndTime });
          assert((await request(maintenancePath(id))).status === 1, 'Не удалось вернуть запись в запланированные.');
          await mutate('Запрет пересечения поста', `/api/v1/vehicles/${vehicleId}/maintenance${query}`, 'POST', payload, 400);
          passed('Пересечение поста отклонено; отмена, восстановление и возврат в план работают');
        } else if (index === 1) {
          await workflow(id, 'start');
          assert((await request(maintenancePath(id))).status === 2, 'Запись не перешла в работу.');
        } else {
          await workflow(id, 'start'); await workflow(id, 'complete');
          assert((await request(maintenancePath(id))).completedDate, 'Не зафиксирована дата выполнения.');
          await workflow(id, 'return_to_work');
          assert(!(await request(maintenancePath(id))).completedDate, 'При возврате в работу осталась дата выполнения.');
          await workflow(id, 'complete');
          const pdf = await request(`/api/v1/documents/pdf/maintenance/${id}${query}&includePhotos=true`);
          const bytes = await pdf.arrayBuffer();
          assert(new TextDecoder().decode(bytes.slice(0, 4)) === '%PDF', 'Вместо PDF получен другой ответ.');
          record.pdfUrl = `/api/v1/documents/pdf/maintenance/${id}${query}&includePhotos=true`;
          passed('Многодневная запись, выполнение, возврат в работу и PDF с фото');
        }
        const history = await request(`/api/v1/maintenance/${id}/history${query}`);
        assert(history.some(event => event.eventType === 'intake_saved'), 'Приёмка отсутствует в истории.');
        assert(history.some(event => event.eventType === 'photo_stage_changed'), 'Изменение категории отсутствует в истории.');
        passed(`${scenario.name}: приёмка, уточнение, фото до/после и история`);
      }
      state.completed = true; state.finishedAt = new Date().toISOString(); persist();
      console.table(state.records);
      console.info('Проверка завершена. Состояние и ссылки:', storageKey);
      window.opencrmIntakeCheckResult = state;
    } catch (error) {
      state.error = error.message; persist();
      console.table(state.records);
      console.error('Проверка остановлена; созданные данные сохранены. Повторно автоматически не запускайте.', state);
      throw error;
    }
  });
})();
