-- Проверка старых подстановок без записи в базу.
WITH vehicle_identity_candidates AS (
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
  AND v."Name" = concat(btrim(v."ManualBrandName"), ' ', btrim(v."ManualModelName"), ' ', btrim(v."RegistrationPlate"))
), vehicle_identity_suggestions AS (
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
        AND NOT EXISTS (SELECT 1 FROM vehicle_identity_candidates c WHERE c."Id" = v."Id"))
)
SELECT count(*) AS vehicles_to_clean,
       count(*) FILTER (WHERE btrim("ManualBrandName") = generated_brand) AS generated_brands_to_clear,
       (SELECT count(*) FROM vehicle_identity_suggestions) AS pending_suggestions_to_remove
FROM vehicle_identity_candidates;
