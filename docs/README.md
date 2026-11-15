# Техническая документация OpenCRM

Здесь собраны инструкции по запуску и развитию ядра CRM, а также описания архитектуры и контрактов компонентов.

## С чего начать

- [Обзор ядра OpenCRM](../README.md).
- [Локальный запуск: зависимости, БД, миграции и первый вход](local-run.md).
- [Конфигурация своей установки](configuration.md).
- [Микросервисы уведомлений и платежей](microservices.md).
- [Первый администратор, веб- и настольная админки](administration.md).
- [Надстройки, точки расширения и обновление из upstream](extending-opencrm.md).
- [Разработка, тесты и вклад в ядро](../CONTRIBUTING.md).
- [Правила проекта](../AGENTS.md).

## Архитектура и инфраструктура

- [Архитектура приложения](architecture-current.md)
- [Состояние здоровья сервисов](service-health-monitoring.md)
- [Уведомления](notifications.md)
- [Интеграция сервиса платежей](payments-service-integration.md)

## Рабочие области

- [Записи и доступность расписания](booking-availability.md)
- [Перенос записей](calendar-rescheduling.md)
- [Периоды обслуживания](maintenance-periods.md)
- [Услуги и платежи заказов](maintenance-services-payments.md)
- [Целостность каталога](service-catalog-integrity.md)
- [Длительность услуг](service-duration.md)
- [Журнал обслуживания](service-journal.md)
- [Данные организаций](organization-setup.md), [участники](organization-employees.md), [экспорт](organization-data-export.md)
- [Импорт CRM-данных](universal-crm-import.md)
- [Идентификация автомобилей](vehicle-identity.md)

## Диаграммы

Папка `uml` содержит диаграммы модели данных, компонентов и взаимодействия сервисов.

## Смежные материалы

Назначение иллюстраций интерфейса и ссылки на профильные разделы — в [plan.md](plan.md).

После изменения настроек перезапустите использующие их процессы.
