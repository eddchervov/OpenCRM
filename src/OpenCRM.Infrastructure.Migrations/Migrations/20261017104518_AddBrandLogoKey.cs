using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <summary> Привязывает локальные логотипы к справочнику марок. </summary>
    public partial class AddBrandLogoKey : Migration
    {
        /// <summary> Добавляет ключ и заполняет его для точных названий известных марок. </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoKey",
                table: "Brands",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Brands" AS brand
                SET "LogoKey" = logo.key
                FROM (VALUES
                    ('audi', 'audi'),
                    ('ауди', 'audi'),
                    ('bmw', 'bmw'),
                    ('бмв', 'bmw'),
                    ('chevrolet', 'chevrolet'),
                    ('шевроле', 'chevrolet'),
                    ('ford', 'ford'),
                    ('форд', 'ford'),
                    ('honda', 'honda'),
                    ('хонда', 'honda'),
                    ('hyundai', 'hyundai'),
                    ('хендай', 'hyundai'),
                    ('хёндай', 'hyundai'),
                    ('kia', 'kia'),
                    ('киа', 'kia'),
                    ('mazda', 'mazda'),
                    ('мазда', 'mazda'),
                    ('mercedes-benz', 'mercedes'),
                    ('mercedes benz', 'mercedes'),
                    ('mercedes', 'mercedes'),
                    ('мерседес-бенц', 'mercedes'),
                    ('мерседес', 'mercedes'),
                    ('mini', 'mini'),
                    ('мини', 'mini'),
                    ('mitsubishi', 'mitsubishi'),
                    ('мицубиси', 'mitsubishi'),
                    ('nissan', 'nissan'),
                    ('ниссан', 'nissan'),
                    ('porsche', 'porsche'),
                    ('порше', 'porsche'),
                    ('renault', 'renault'),
                    ('рено', 'renault'),
                    ('skoda', 'skoda'),
                    ('škoda', 'skoda'),
                    ('шкода', 'skoda'),
                    ('subaru', 'subaru'),
                    ('субару', 'subaru'),
                    ('tesla', 'tesla'),
                    ('тесла', 'tesla'),
                    ('toyota', 'toyota'),
                    ('тойота', 'toyota'),
                    ('volkswagen', 'volkswagen'),
                    ('vw', 'volkswagen'),
                    ('фольксваген', 'volkswagen'),
                    ('volvo', 'volvo'),
                    ('вольво', 'volvo')
                ) AS logo(name, key)
                WHERE lower(btrim(brand."Name"::text)) = logo.name
                  AND brand."LogoKey" IS NULL;
                """);
        }

        /// <summary> Удаляет ключи логотипов, сохраняя марки и связи автомобилей. </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoKey",
                table: "Brands");
        }
    }
}
