using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UserLoginService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_login_records",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    login_timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ip_numeric_high = table.Column<long>(type: "bigint", nullable: false),
                    ip_numeric_low = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_login_records", x => new { x.user_id, x.ip_address });
                });

            migrationBuilder.CreateIndex(
                name: "idx_user_login_records_ip_address",
                table: "user_login_records",
                column: "ip_address");

            migrationBuilder.CreateIndex(
                name: "idx_user_login_records_ip_numeric_range",
                table: "user_login_records",
                columns: new[] { "ip_numeric_high", "ip_numeric_low" });

            migrationBuilder.CreateIndex(
                name: "idx_user_login_records_user_id_ip_address",
                table: "user_login_records",
                columns: new[] { "user_id", "ip_address" });

            migrationBuilder.CreateIndex(
                name: "idx_user_login_records_user_id_ip_numeric",
                table: "user_login_records",
                columns: new[] { "user_id", "ip_numeric_high", "ip_numeric_low" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginRecord_IpNumeric",
                table: "user_login_records",
                columns: new[] { "ip_numeric_high", "ip_numeric_low" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_login_records");
        }
    }
}
