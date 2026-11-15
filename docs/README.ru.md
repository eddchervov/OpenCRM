# Техническая документация OpenCRM

[English](README.md) | **Русский**

Здесь собраны инструкции по запуску и развитию ядра CRM, а также описания архитектуры и контрактов компонентов.

## С чего начать

- [Обзор ядра OpenCRM](../README.ru.md).
- [Локальный запуск: зависимости, БД, миграции и первый вход](local-run.ru.md).
- [Конфигурация своей установки](configuration.ru.md).
- [Микросервисы уведомлений и платежей](microservices.ru.md).
- [Первый администратор, веб- и настольная админки](administration.ru.md).
- [Надстройки, точки расширения и обновление из upstream](extending-opencrm.ru.md).
- [Разработка, тесты и вклад в ядро](../CONTRIBUTING.ru.md).
- [Правила проекта](../AGENTS.md).

## Архитектура и инфраструктура

- [Архитектура приложения](architecture-current.ru.md)
- [Состояние здоровья сервисов](service-health-monitoring.ru.md)
- [Уведомления](notifications.ru.md)
- [Интеграция сервиса платежей](payments-service-integration.ru.md)

## Рабочие области

- [Записи и доступность расписания](booking-availability.ru.md)
- [Перенос записей](calendar-rescheduling.ru.md)
- [Периоды обслуживания](maintenance-periods.ru.md)
- [Услуги и платежи заказов](maintenance-services-payments.ru.md)
- [Целостность каталога](service-catalog-integrity.ru.md)
- [Длительность услуг](service-duration.ru.md)
- [Журнал обслуживания](service-journal.ru.md)
- [Данные организаций](organization-setup.ru.md), [участники](organization-employees.ru.md), [экспорт](organization-data-export.ru.md)
- [Импорт CRM-данных](universal-crm-import.ru.md)
- [Идентификация автомобилей](vehicle-identity.ru.md)

## Диаграммы

Папка `uml` содержит диаграммы модели данных, компонентов и взаимодействия сервисов.

[Указатель UML-диаграмм](uml/README.ru.md).

## Смежные материалы

Назначение иллюстраций интерфейса и ссылки на профильные разделы — в [plan.md](plan.ru.md).

После изменения настроек перезапустите использующие их процессы.

## Полный справочник модулей

- [Модуль аккаунтов: технический дизайн](accounts-module/design.ru.md).
- [Удаление сценариев и возможностей аккаунта](accounts-module/profile-removal.ru.md).
- [Модуль аккаунтов: спецификация переноса](accounts-module/spec.ru.md).
- [Административный дашборд CRM](admin-dashboard-analytics.ru.md).
- [Оплаты подписок и чеки НПД](admin-finance.ru.md).
- [Организации в desktop-админке opencrm.example.invalid](admin-organizations.ru.md).
- [Управление подписками в админках OpenCRM](admin-subscriptions.ru.md).
- [Серверный ИИ-помощник OpenCRM](ai-assistant.ru.md).
- [Карточка клиента](client-card.ru.md).
- [Переписка с клиентами](communications.ru.md).
- [Общая оболочка CRM](crm-workspace-shell.ru.md).
- [Приёмка и фотографии до / после](detailing-intake.ru.md).
- [Рабочий день организации](detailing-workday.ru.md).
- [Обязательное подтверждение email](email-confirmation.ru.md).
- [Смены и учёт сотрудников в OpenCRM](employee-shifts-design.ru.md).
- [Реализация рабочего места исполнителя и контроля администратора](employee-workplace-implementation.ru.md).
- [Рабочее место исполнителя](employee-workplace.ru.md).
- [Шапки форм и внутренних блоков](form-headers.ru.md).
- [Ручные обращения до записи: проект второго этапа](inquiries-design.ru.md).
- [Ручные обращения до записи: реализация](inquiries-implementation.ru.md).
- [Навигация CRM: первый этап,](navigation.ru.md).
- [Границы платежного сервиса](payments-service-design.ru.md).
- [Обзор OpenCRM](project-overview.ru.md).
- [Общий простой режим детейлинга и СТО](simple-maintenance-workflow.ru.md).
- [Развитие СТО и автопарка](sto-fleet-development.ru.md).
- [Команда и доступ в организацию](team-and-access-design.ru.md).
- [Команды голосового пилота opencrm.example.invalid](voice-ai-actions.ru.md).
