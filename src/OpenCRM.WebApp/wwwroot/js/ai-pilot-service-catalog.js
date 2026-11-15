(() => {
    'use strict';

    const normalize = value => String(value || '').toLocaleLowerCase('ru-RU').replace(/ё/g, 'е')
        .replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
    const keyFor = (row, index) => `${index}:${normalize(row.name)}`;
    const snapshot = items => JSON.stringify(items.map(item => ({ id: item.id, name: item.name, isActive: item.isActive }))
        .sort((left, right) => String(left.id).localeCompare(String(right.id))));

    function words(value) {
        const ignored = new Set(['замен', 'ремонт', 'установ', 'снят', 'мойк', 'работ', 'услуг']);
        const tokens = normalize(value).split(' ').filter(word => word.length > 2)
            .map(word => word.replace(/(иями|ами|ого|ему|ыми|ая|ой|ое|ые|ий|ый|ам|ям|ах|ях|ов|ев|ом|ем|ы|и|а|я|у|ю|е|о)$/, ''));
        return tokens.length > 1 ? tokens.filter(word => !ignored.has(word)) : tokens;
    }

    function similar(name, item) {
        const requestedName = normalize(name);
        const candidateName = normalize(item.name);
        if (candidateName.startsWith(requestedName + ' ') || requestedName.startsWith(candidateName + ' ')) return true;
        const requested = words(name);
        const candidate = words(item.name);
        return requested.some(word => candidate.includes(word));
    }

    async function prepareRows(rows, context, options) {
        if (!rows.length || rows.every(row => row.detailId || row.catalogItemId)) {
            return { rows, notes: [], verify: async () => {} };
        }
        const path = `api/v1/organizations/${encodeURIComponent(context.organizationId)}/scenario-catalog`;
        const read = async () => {
            const catalog = await options.request(path);
            if (!Array.isArray(catalog.services)) throw new Error('Не удалось прочитать справочник услуг. Изменения не выполнялись.');
            return catalog.services;
        };
        const all = await read();
        const active = all.filter(item => item.isActive);
        const notes = [];
        const resolved = [];
        for (let index = 0; index < rows.length; index++) {
            const row = rows[index];
            if (row.detailId || row.catalogItemId) {
                resolved.push({ ...row });
                continue;
            }
            const key = keyFor(row, index);
            const exact = active.filter(item => normalize(item.name) === normalize(row.name));
            const candidates = exact.length ? exact : active.filter(item => similar(row.name, item));
            const selectedId = context.serviceSelections?.[key];
            let selected;
            if (selectedId && selectedId !== 'custom') {
                selected = candidates.find(item => item.id === selectedId);
                if (!selected) throw new Error('Выбранная услуга изменилась. Подготовьте действие заново.');
            } else if (selectedId !== 'custom' && exact.length === 1) {
                selected = exact[0];
            } else if (selectedId !== 'custom' && candidates.length) {
                if (candidates.length > 20) throw new Error('Найдено больше 20 похожих услуг. Назовите работу точнее.');
                const error = new Error(`Уточните работу «${row.name}»: выберите услугу из справочника или произвольную строку.`);
                error.serviceChoice = { key, name: row.name, options: candidates.map(item => ({
                    value: item.id, label: `${item.name}${item.unitPrice == null ? '' : ` · цена справочника ${item.unitPrice} ₽`}${item.description ? ` · ${item.description.slice(0, 80)}` : ''}`
                })) };
                throw error;
            }
            if (selected) {
                resolved.push({ ...row, name: selected.name, catalogItemId: selected.id });
                notes.push(`«${selected.name}» — услуга из справочника. ${row.price == null ? 'Цена пока не указана.' : 'Названная цена сохранена.'}`);
            } else {
                resolved.push({ ...row, catalogItemId: null });
                notes.push(`«${row.name}» — произвольная работа.`);
            }
        }
        const expected = snapshot(all);
        return { rows: resolved, notes, verify: async () => {
            if (snapshot(await read()) !== expected) throw new Error('Справочник услуг изменился. Подготовьте действие заново.');
        } };
    }

    globalThis.OpenCRMAiPilotServiceCatalog = { prepareRows };
})();
