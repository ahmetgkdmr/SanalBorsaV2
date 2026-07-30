using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SanalBorsa.Infrastructure.Data;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260729220000_AddUserUsername")]
public class AddUserUsername : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Username",
            table: "Users",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        // Mevcut kullanıcılar: DisplayName / Email'den geçici username üret
        migrationBuilder.Sql("""
            ;WITH src AS (
                SELECT
                    Id,
                    LOWER(
                        LEFT(
                            REPLACE(REPLACE(REPLACE(REPLACE(
                                COALESCE(
                                    NULLIF(LEFT(Email, CHARINDEX('@', Email + '@') - 1), ''),
                                    NULLIF(DisplayName, ''),
                                    'user'
                                ),
                                ' ', ''), '.', ''), '-', ''), '_', ''),
                            24)
                    ) AS BaseName
                FROM Users
            ),
            numbered AS (
                SELECT
                    Id,
                    CASE
                        WHEN BaseName = '' THEN 'user'
                        ELSE BaseName
                    END AS BaseName,
                    ROW_NUMBER() OVER (
                        PARTITION BY
                            CASE WHEN BaseName = '' THEN 'user' ELSE BaseName END
                        ORDER BY Id
                    ) AS rn
                FROM src
            )
            UPDATE u
            SET Username = CASE
                WHEN n.rn = 1 THEN LEFT(n.BaseName, 32)
                ELSE LEFT(n.BaseName, 28) + CAST(n.rn AS nvarchar(4))
            END
            FROM Users u
            INNER JOIN numbered n ON n.Id = u.Id;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Users_Username",
            table: "Users",
            column: "Username",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_Username",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "Username",
            table: "Users");
    }
}
