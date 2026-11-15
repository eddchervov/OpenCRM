-- Выполнять только для базы notifications вместе с обновлённой настройкой MigrationsHistoryTable.
-- При любой ошибке транзакция откатывается; посторонние объекты не удаляются каскадно.
BEGIN;
SET LOCAL lock_timeout = '10s';

DO $$
BEGIN
    IF current_database() <> 'notifications' THEN
        RAISE EXCEPTION 'Скрипт предназначен только для базы notifications.';
    END IF;

    IF to_regclass('notifications."__EFMigrationsHistory"') IS NULL THEN
        RAISE EXCEPTION 'Не найдена таблица истории в схеме notifications.';
    END IF;

    IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
        IF EXISTS (
            SELECT 1
            FROM public."__EFMigrationsHistory" old_history
            JOIN notifications."__EFMigrationsHistory" new_history USING ("MigrationId")
            WHERE old_history."ProductVersion" <> new_history."ProductVersion"
        ) THEN
            RAISE EXCEPTION 'Версии в двух таблицах истории миграций различаются.';
        END IF;

        INSERT INTO notifications."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        SELECT "MigrationId", "ProductVersion"
        FROM public."__EFMigrationsHistory"
        ON CONFLICT ("MigrationId") DO NOTHING;

        DROP TABLE public."__EFMigrationsHistory";
    END IF;
END;
$$;

-- RESTRICT не даст удалить схему, если в ней обнаружатся другие объекты.
DROP SCHEMA IF EXISTS public RESTRICT;
ALTER DATABASE notifications SET search_path = notifications;
COMMIT;
