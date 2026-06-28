using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atendefy.API.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                schema: "public",
                table: "tenant_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: usuários já existentes (aprovados antes desta feature) são tratados como
            // verificados, para não bloquear logins atuais (ex.: o dono do tenant em produção).
            migrationBuilder.Sql(@"UPDATE ""public"".""tenant_users"" SET ""EmailVerified"" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerified",
                schema: "public",
                table: "tenant_users");
        }
    }
}
