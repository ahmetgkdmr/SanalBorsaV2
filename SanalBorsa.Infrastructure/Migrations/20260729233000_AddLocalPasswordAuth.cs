using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SanalBorsa.Infrastructure.Data;

#nullable disable

namespace SanalBorsa.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260729233000_AddLocalPasswordAuth")]
public class AddLocalPasswordAuth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "Users",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.DropIndex(
            name: "IX_Users_FirebaseUid",
            table: "Users");

        migrationBuilder.AlterColumn<string>(
            name: "FirebaseUid",
            table: "Users",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128);

        migrationBuilder.CreateIndex(
            name: "IX_Users_FirebaseUid",
            table: "Users",
            column: "FirebaseUid",
            unique: true,
            filter: "[FirebaseUid] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_FirebaseUid",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "PasswordHash",
            table: "Users");

        migrationBuilder.Sql("""
            UPDATE Users SET FirebaseUid = CONCAT('legacy-', CAST(Id AS nvarchar(36)))
            WHERE FirebaseUid IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "FirebaseUid",
            table: "Users",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_FirebaseUid",
            table: "Users",
            column: "FirebaseUid",
            unique: true);
    }
}
