using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class init_account_Scenarios_data : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "AccountFeatureDefinitions"
                    ("Code", "Name", "Description", "IsAvailableNow", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES
                    ('personal-vehicles', 'Своя техника', 'Любые типы техники: легковые авто, грузовики, мото, прицепы и спецтехника.', TRUE, TRUE, 10, NOW(), NOW()),
                    ('customer-vehicles', 'Техника клиентов', 'Авто и другая техника, которые проходят через сервис или студию.', TRUE, TRUE, 20, NOW(), NOW()),
                    ('company-fleet', 'Техника компании', 'Служебная техника, автопарк, учебные авто, грузовики и спецтехника.', TRUE, TRUE, 30, NOW(), NOW()),
                    ('clients', 'Клиенты', 'База клиентов, контакты и связь с техникой или проектами.', FALSE, TRUE, 40, NOW(), NOW()),
                    ('service-history', 'История обслуживания', 'Ремонты, ТО, пробег, работы и итоговая стоимость.', TRUE, TRUE, 50, NOW(), NOW()),
                    ('expenses', 'Расходы', 'Затраты по технике, работам, материалам и владению.', TRUE, TRUE, 60, NOW(), NOW()),
                    ('photos-documents', 'Фото и документы', 'Файлы, акты, документы и фотографии по технике.', TRUE, TRUE, 70, NOW(), NOW()),
                    ('before-after-photos', 'Фото до/после', 'Отдельная визуальная история работ для детейлинга, оклейки и ремонта.', FALSE, TRUE, 80, NOW(), NOW()),
                    ('reports', 'Отчеты', 'Понятная выгрузка истории техники и обслуживания.', TRUE, TRUE, 90, NOW(), NOW()),
                    ('maintenance-reminders', 'Напоминания', 'Контроль ТО, масла, расходников и повторных визитов.', FALSE, TRUE, 100, NOW(), NOW()),
                    ('scheduler', 'Планировщик', 'Записи, задачи, очередь работ и будущие обслуживания.', FALSE, TRUE, 110, NOW(), NOW()),
                    ('team', 'Команда', 'Пользователи внутри одного аккаунта и роли доступа.', FALSE, TRUE, 120, NOW(), NOW()),
                    ('regulations', 'Регламенты', 'Периодичность ТО, чек-листы и правила обслуживания.', FALSE, TRUE, 130, NOW(), NOW()),
                    ('moto-hours', 'Моточасы', 'Учет техники, где важен ресурс по часам, а не только пробег.', TRUE, TRUE, 140, NOW(), NOW()),
                    ('materials-parts', 'Материалы и запчасти', 'Расходники, позиции работ и использованные материалы.', FALSE, TRUE, 150, NOW(), NOW()),
                    ('branded-reports', 'Фирменные отчеты', 'Отчеты сервиса или компании для клиента и продажи техники.', FALSE, TRUE, 160, NOW(), NOW()),
                    ('public-reports', 'Публичная история', 'Открытая страница истории техники для клиента или покупателя.', FALSE, TRUE, 170, NOW(), NOW()),
                    ('analytics', 'Аналитика', 'Стоимость владения, частота ремонтов и слабые места техники.', TRUE, TRUE, 180, NOW(), NOW()),
                    ('training-vehicles', 'Учебная техника', 'Учет учебных автомобилей и другой техники автошколы.', TRUE, TRUE, 190, NOW(), NOW()),
                    ('students', 'Ученики', 'Будущая связка учеников, инструкторов, занятий и учебной техники.', FALSE, TRUE, 200, NOW(), NOW())
                ON CONFLICT ("Code") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Description" = EXCLUDED."Description",
                    "IsAvailableNow" = EXCLUDED."IsAvailableNow",
                    "IsActive" = EXCLUDED."IsActive",
                    "SortOrder" = EXCLUDED."SortOrder",
                    "UpdatedAt" = NOW();
                """);

            migrationBuilder.Sql("""
                INSERT INTO "AccountScenarioDefinitions"
                    ("Code", "Name", "Description", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt")
                VALUES
                    ('personal', 'Личное использование', 'Своя техника, расходы, обслуживание, документы и история для продажи.', TRUE, 10, NOW(), NOW()),
                    ('service-station', 'СТО / автосервис', 'Много входящей техники, быстрый прием, клиенты, работы и повторные визиты.', TRUE, 20, NOW(), NOW()),
                    ('detailing', 'Детейлинг', 'Клиенты, проекты, фото до/после, портфолио и возврат на повторные услуги.', TRUE, 30, NOW(), NOW()),
                    ('wrapping', 'Оклейка пленкой', 'Проектная работа, визуальная история, материалы и аккуратная выдача результата клиенту.', TRUE, 40, NOW(), NOW()),
                    ('fleet', 'Автопарк', 'Техника компании, расходы, регламенты, планирование и контроль владения.', TRUE, 50, NOW(), NOW()),
                    ('driving-school', 'Автошкола', 'Учебная техника, обслуживание, документы, планирование занятий и контроль расходов.', TRUE, 60, NOW(), NOW())
                ON CONFLICT ("Code") DO UPDATE SET
                    "Name" = EXCLUDED."Name",
                    "Description" = EXCLUDED."Description",
                    "IsActive" = EXCLUDED."IsActive",
                    "SortOrder" = EXCLUDED."SortOrder",
                    "UpdatedAt" = NOW();
                """);

            migrationBuilder.Sql("""
                INSERT INTO "AccountScenarioFeatures"
                    ("AccountScenarioDefinitionId", "AccountFeatureDefinitionId", "SortOrder")
                SELECT
                    scenario."Id",
                    feature."Id",
                    scenario_feature."SortOrder"
                FROM (VALUES
                    ('personal', 'personal-vehicles', 10),
                    ('personal', 'service-history', 20),
                    ('personal', 'expenses', 30),
                    ('personal', 'photos-documents', 40),
                    ('personal', 'maintenance-reminders', 50),
                    ('personal', 'reports', 60),
                    ('service-station', 'customer-vehicles', 10),
                    ('service-station', 'clients', 20),
                    ('service-station', 'service-history', 30),
                    ('service-station', 'materials-parts', 40),
                    ('service-station', 'maintenance-reminders', 50),
                    ('service-station', 'scheduler', 60),
                    ('service-station', 'reports', 70),
                    ('detailing', 'customer-vehicles', 10),
                    ('detailing', 'clients', 20),
                    ('detailing', 'before-after-photos', 30),
                    ('detailing', 'photos-documents', 40),
                    ('detailing', 'maintenance-reminders', 50),
                    ('detailing', 'branded-reports', 60),
                    ('wrapping', 'customer-vehicles', 10),
                    ('wrapping', 'clients', 20),
                    ('wrapping', 'before-after-photos', 30),
                    ('wrapping', 'materials-parts', 40),
                    ('wrapping', 'branded-reports', 50),
                    ('fleet', 'company-fleet', 10),
                    ('fleet', 'service-history', 20),
                    ('fleet', 'expenses', 30),
                    ('fleet', 'regulations', 40),
                    ('fleet', 'scheduler', 50),
                    ('fleet', 'team', 60),
                    ('fleet', 'analytics', 70),
                    ('driving-school', 'training-vehicles', 10),
                    ('driving-school', 'company-fleet', 20),
                    ('driving-school', 'service-history', 30),
                    ('driving-school', 'expenses', 40),
                    ('driving-school', 'photos-documents', 50),
                    ('driving-school', 'scheduler', 60),
                    ('driving-school', 'students', 70)
                ) AS scenario_feature("ScenarioCode", "FeatureCode", "SortOrder")
                JOIN "AccountScenarioDefinitions" scenario ON scenario."Code" = scenario_feature."ScenarioCode"
                JOIN "AccountFeatureDefinitions" feature ON feature."Code" = scenario_feature."FeatureCode"
                ON CONFLICT ("AccountScenarioDefinitionId", "AccountFeatureDefinitionId") DO UPDATE SET
                    "SortOrder" = EXCLUDED."SortOrder";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "AccountScenarioFeatures"
                WHERE "AccountScenarioDefinitionId" IN (
                    SELECT "Id"
                    FROM "AccountScenarioDefinitions"
                    WHERE "Code" IN ('personal', 'service-station', 'detailing', 'wrapping', 'fleet', 'driving-school')
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM "AccountScenarioDefinitions"
                WHERE "Code" IN ('personal', 'service-station', 'detailing', 'wrapping', 'fleet', 'driving-school');
                """);

            migrationBuilder.Sql("""
                DELETE FROM "AccountFeatureDefinitions"
                WHERE "Code" IN (
                    'personal-vehicles',
                    'customer-vehicles',
                    'company-fleet',
                    'clients',
                    'service-history',
                    'expenses',
                    'photos-documents',
                    'before-after-photos',
                    'reports',
                    'maintenance-reminders',
                    'scheduler',
                    'team',
                    'regulations',
                    'moto-hours',
                    'materials-parts',
                    'branded-reports',
                    'public-reports',
                    'analytics',
                    'training-vehicles',
                    'students'
                );
                """);
        }
    }
}
