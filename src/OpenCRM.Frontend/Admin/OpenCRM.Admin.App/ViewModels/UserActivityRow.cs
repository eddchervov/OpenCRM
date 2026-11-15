using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Подготовленная для отображения запись журнала активности. </summary>
public sealed record UserActivityRow(AdminUserActivityItem Item)
{
    private static readonly IReadOnlyDictionary<string, string> s_actionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["view"] = "Просмотр",
        ["save"] = "Сохранение",
        ["create"] = "Создание",
        ["create_order"] = "Создание заказа",
        ["create_booking"] = "Создание записи",
        ["create_client"] = "Создание клиента",
        ["create_vehicle"] = "Создание техники",
        ["add_service"] = "Добавление услуги",
        ["add_material"] = "Добавление материала",
        ["add_employee"] = "Добавление сотрудника",
        ["add"] = "Добавление",
        ["delete"] = "Удаление",
        ["edit"] = "Редактирование",
        ["cancel"] = "Отмена",
        ["close"] = "Закрытие",
        ["back"] = "Назад",
        ["continue"] = "Продолжение",
        ["next"] = "Далее",
        ["done"] = "Готово",
        ["confirm"] = "Подтверждение",
        ["open"] = "Открытие",
        ["open_detailing"] = "Открытие детейлинга",
        ["open_autoservice"] = "Открытие автосервиса",
        ["open_fleet"] = "Открытие автопарка",
        ["open_order"] = "Открытие заказа",
        ["start_work"] = "Начало работы",
        ["complete"] = "Завершение",
        ["complete_work"] = "Завершение работы",
        ["handover"] = "Передача автомобиля",
        ["record_payment"] = "Учёт оплаты",
        ["pay"] = "Оплата",
        ["activate"] = "Активация",
        ["start_demo"] = "Запуск демо-доступа",
        ["refresh"] = "Обновление",
        ["apply"] = "Применение",
        ["reset"] = "Сброс",
        ["search"] = "Поиск",
        ["download"] = "Скачивание",
        ["export"] = "Экспорт",
        ["import"] = "Импорт",
        ["print"] = "Печать",
        ["send"] = "Отправка",
        ["invite"] = "Приглашение",
        ["archive"] = "Архивация",
        ["restore"] = "Восстановление",
        ["view_list"] = "Просмотр списка",
        ["view_calendar"] = "Просмотр расписания",
        ["view_posts"] = "Просмотр по постам",
        ["view_slots"] = "Просмотр временных слотов",
        ["view_month"] = "Просмотр месяца",
        ["view_week"] = "Просмотр недели",
        ["view_day"] = "Просмотр дня",
        ["today"] = "Переход к сегодня",
        ["previous"] = "Предыдущий период",
        ["load_more"] = "Загрузка следующего списка",
        ["open_scheduler"] = "Открытие расписания",
        ["open_clients"] = "Открытие клиентов",
        ["open_finance"] = "Открытие финансов",
        ["open_settings"] = "Открытие настроек",
        ["open_services"] = "Открытие услуг",
        ["open_employees"] = "Открытие сотрудников",
        ["link"] = "Переход по ссылке",
        ["submit"] = "Отправка формы",
        ["button"] = "Кнопка",
        ["client_created"] = "Создан клиент",
        ["work_created"] = "Создан заказ",
        ["vehicle_created"] = "Создана техника",
        ["demo_activated"] = "Активирован демо-доступ",
        ["checkout_started"] = "Начато оформление оплаты",
        ["plan_activated"] = "Активирован тариф"
    };

    private static readonly IReadOnlyDictionary<string, string> s_scenarioLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["detailing"] = "Детейлинг",
        ["autoservice"] = "Автосервис",
        ["personal"] = "Личный кабинет",
        ["account"] = "Аккаунт",
        ["fleet"] = "Автопарк"
    };

    private static readonly IReadOnlyDictionary<string, string> s_sectionLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["scheduler"] = "Расписание",
        ["detailing"] = "Детейлинг",
        ["autoservice"] = "Автосервис",
        ["fleet"] = "Автопарк",
        ["personal"] = "Личный кабинет",
        ["clients"] = "Клиенты",
        ["finance"] = "Финансы",
        ["settings"] = "Настройки",
        ["services"] = "Услуги",
        ["employees"] = "Сотрудники",
        ["orders"] = "Заказы",
        ["vehicles"] = "Техника",
        ["vehicle"] = "Техника",
        ["organizations"] = "Организации",
        ["posts"] = "Посты",
        ["slots"] = "Слоты",
        ["profile"] = "Профиль",
        ["account"] = "Аккаунт",
        ["workspace"] = "Рабочее пространство",
        ["catalog"] = "Каталог",
        ["auth"] = "Авторизация",
        ["payments"] = "Платежи",
        ["export"] = "Экспорт",
        ["import"] = "Импорт",
        ["maintenance"] = "Обслуживание",
        ["organization"] = "Организация",
        ["subscription"] = "Подписка",
        ["payment"] = "Оплата",
        ["support"] = "Поддержка",
        ["visualization"] = "Визуализация",
        ["materials"] = "Материалы",
        ["work-journal"] = "Журнал работ",
        ["statistics"] = "Статистика",
        ["shifts"] = "Смены",
        ["members"] = "Участники",
        ["billing"] = "Биллинг",
        ["wiki"] = "База знаний",
        ["defects"] = "Дефекты",
        ["counters"] = "Счётчики",
        ["bundles"] = "Наборы",
        ["index"] = "Главная",
        ["list"] = "Список",
        ["create"] = "Создание",
        ["edit"] = "Редактирование",
        ["new"] = "Создание",
        ["details"] = "Подробности",
        ["history"] = "История",
        ["journal"] = "Журнал",
        ["albums"] = "Альбомы",
        ["album"] = "Альбом",
        ["photos"] = "Фотографии",
        ["documents"] = "Документы",
        ["quick-booking"] = "Быстрая запись",
        ["quickbooking"] = "Быстрая запись",
        ["overview"] = "Обзор",
        ["rates"] = "Тарифы",
        ["invitations"] = "Приглашения",
        ["notifications"] = "Уведомления",
        ["login"] = "Вход",
        ["logout"] = "Выход",
        ["register"] = "Регистрация",
        ["confirmemail"] = "Подтверждение почты",
        ["changepassword"] = "Смена пароля",
        ["setpassword"] = "Установка пароля",
        ["deleteaccount"] = "Удаление аккаунта",
        ["vehiclepersonal"] = "Личная техника",
        ["vehicleeditpersonal"] = "Редактирование техники",
        ["vehiclecreatepersonal"] = "Создание техники",
        ["vehiclejournal"] = "Журнал техники",
        ["vehiclealbums"] = "Альбомы техники",
        ["vehiclealbum"] = "Альбом техники",
        ["vehiclephotos"] = "Фотографии техники",
        ["maintenancepersonal"] = "Личное обслуживание",
        ["maintenanceorganization"] = "Обслуживание организации",
        ["createmaintenance"] = "Создание обслуживания",
        ["createmaintenancepersonal"] = "Создание личного обслуживания",
        ["createmaintenanceorganization"] = "Создание обслуживания организации",
        ["maintenancehistory"] = "История обслуживания",
        ["accountprofile"] = "Профиль аккаунта",
        ["createorganization"] = "Создание организации",
        ["setup"] = "Настройка",
        ["resources"] = "Ресурсы",
        ["schedule"] = "Расписание",
        ["search"] = "Поиск",
        ["help"] = "Помощь"
    };

    public string OccurredAt => UserDisplayFormatting.DateTime(this.Item.OccurredAt);

    public string User => string.IsNullOrWhiteSpace(this.Item.UserLabel) || this.Item.UserLabel == this.Item.UserId
        ? "Пользователь без email"
        : this.Item.UserLabel;

    public string Organization => this.Item.OrganizationName
        ?? (this.Item.OrganizationId.HasValue ? "Организация недоступна" : "Без организации");

    public string Event => this.Item.Kind switch
    {
        "page" => "Открытие страницы",
        "operation" => this.GetOperationLabel(),
        "click" when this.Item.Action == "button" => "Нажатие кнопки",
        "click" => s_actionLabels.GetValueOrDefault(this.Item.Action, "Нажатие кнопки"),
        _ => s_actionLabels.GetValueOrDefault(this.Item.Action, "Действие")
    };

    public string Location
    {
        get
        {
            var scenario = s_scenarioLabels.GetValueOrDefault(this.Item.Scenario, string.Empty);
            var parts = this.Item.Page.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var skip = parts.Length > 1 && parts[0] == "clients" && s_scenarioLabels.ContainsKey(parts[1]) ? 2 : 0;
            var pageLabels = parts.Skip(skip)
                .Select(part => s_sectionLabels.GetValueOrDefault(part, string.Empty))
                .Where(label => !string.IsNullOrEmpty(label) && label != scenario)
                .Distinct()
                .ToArray();
            var page = pageLabels.Length > 0
                ? string.Join(" · ", pageLabels)
                : s_sectionLabels.GetValueOrDefault(this.Item.Section, string.Empty);

            return string.IsNullOrEmpty(page) || page == scenario ? scenario
                : string.IsNullOrEmpty(scenario) ? page
                : $"{scenario} · {page}";
        }
    }

    private string GetOperationLabel()
    {
        var parts = this.Item.Action.Split(':');
        var action = parts[0] switch
        {
            "DELETE" => "Удаление",
            "PUT" or "PATCH" => "Сохранение изменений",
            _ => "Отправка данных"
        };

        return parts.Length > 1 && parts[1] == "error" ? $"{action} — ошибка" : action;
    }
}
