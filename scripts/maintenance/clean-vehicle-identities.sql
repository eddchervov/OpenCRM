-- Очистка только точных подстановок старой быстрой записи. Запускать через psql.
-- По умолчанию выполняется проверка без изменения постоянных таблиц.
-- Применение: -v apply=true -v backup_file=/путь/к/новому/vehicle-identity-backup.json
-- Применять после публикации версии, допускающей пустую марку и модель.
\set ON_ERROR_STOP on
\if :{?apply}
\else
    \set apply false
\endif
\if :apply
    \if :{?backup_file}
    \else
        \echo 'Для применения требуется backup_file.'
        \quit 1
    \endif
\endif

BEGIN;
SET LOCAL lock_timeout = '5s';
\if :apply
    -- Исключаем изменение кандидатов между резервным копированием и очисткой.
    LOCK TABLE "Vehicles", "VehicleCatalogSuggestions" IN SHARE ROW EXCLUSIVE MODE;
\endif

CREATE TEMP TABLE vehicle_identity_candidates ON COMMIT DROP AS
SELECT v.*,
       CASE v."Kind"
           WHEN 1 THEN 'Автомобиль' WHEN 2 THEN 'Грузовая техника'
           WHEN 3 THEN 'Мотоцикл' WHEN 4 THEN 'Квадроцикл' WHEN 5 THEN 'Багги / UTV'
           WHEN 9 THEN 'Спецтехника' WHEN 10 THEN 'Прицеп' ELSE 'Техника'
       END AS generated_brand
FROM "Vehicles" v
WHERE v."ModelId" IS NULL AND v."BrandId" IS NULL
  AND NULLIF(btrim(v."RegistrationPlate"), '') IS NOT NULL
  AND btrim(v."ManualModelName") = btrim(v."RegistrationPlate")
  AND v."Name" = concat(btrim(v."ManualBrandName"), ' ', btrim(v."ManualModelName"), ' ', btrim(v."RegistrationPlate"));

CREATE TEMP TABLE vehicle_identity_suggestions ON COMMIT DROP AS
SELECT s.* FROM "VehicleCatalogSuggestions" s
WHERE s."Status" = 1
  AND EXISTS (
      SELECT 1 FROM vehicle_identity_candidates v
      WHERE v."Kind" = s."Kind" AND v."UserId" = s."SuggestedByUserId"
        AND lower(btrim(v."ManualBrandName")) = lower(btrim(s."BrandName"))
        AND lower(btrim(v."ManualModelName")) = lower(btrim(s."ModelName")))
  AND NOT EXISTS (
      SELECT 1 FROM "Vehicles" v
      WHERE v."Kind" = s."Kind"
        AND lower(btrim(v."ManualBrandName")) = lower(btrim(s."BrandName"))
        AND lower(btrim(v."ManualModelName")) = lower(btrim(s."ModelName"))
        AND NOT EXISTS (SELECT 1 FROM vehicle_identity_candidates c WHERE c."Id" = v."Id"));

SELECT count(*) AS vehicles_to_clean,
       count(*) FILTER (WHERE btrim("ManualBrandName") = generated_brand) AS generated_brands_to_clear,
       (SELECT count(*) FROM vehicle_identity_suggestions) AS pending_suggestions_to_remove
FROM vehicle_identity_candidates;

\if :apply
    -- Сначала сохраняем исходные строки. Ошибка записи файла прерывает выполнение.
    \pset format unaligned
    \pset tuples_only on
    SELECT jsonb_build_object(
        'database', current_database(), 'createdAt', now(),
        'vehicles', COALESCE((SELECT jsonb_agg(to_jsonb(v) - 'generated_brand') FROM vehicle_identity_candidates v), '[]'::jsonb),
        'suggestions', COALESCE((SELECT jsonb_agg(to_jsonb(s)) FROM vehicle_identity_suggestions s), '[]'::jsonb))
    \g :backup_file
    \pset tuples_only off
    \pset format aligned

    UPDATE "Vehicles" v
    SET "ManualBrandName" = CASE WHEN btrim(c."ManualBrandName") = c.generated_brand THEN NULL ELSE v."ManualBrandName" END,
        "ManualModelName" = NULL,
        "Name" = NULL
    FROM vehicle_identity_candidates c WHERE v."Id" = c."Id";

    DELETE FROM "VehicleCatalogSuggestions" s
    USING vehicle_identity_suggestions c WHERE s."Id" = c."Id";
    COMMIT;
\else
    ROLLBACK;
\endif
