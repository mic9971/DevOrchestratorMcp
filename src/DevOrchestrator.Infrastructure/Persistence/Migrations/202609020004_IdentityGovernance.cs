using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
[Migration("202609020004_IdentityGovernance")]
public sealed class IdentityGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "identity_users",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Provider = table.Column<string>(maxLength: 40, nullable: false),
                Subject = table.Column<string>(maxLength: 200, nullable: false),
                Login = table.Column<string>(maxLength: 120, nullable: false),
                DisplayName = table.Column<string>(maxLength: 200, nullable: false),
                Email = table.Column<string>(maxLength: 320, nullable: true),
                IsEnabled = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                LastLoginAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_identity_users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "machine_credentials",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Name = table.Column<string>(maxLength: 160, nullable: false),
                KeyHash = table.Column<string>(maxLength: 64, nullable: false),
                KeyPrefix = table.Column<string>(maxLength: 16, nullable: false),
                Role = table.Column<string>(maxLength: 40, nullable: false),
                IsActive = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(nullable: true),
                LastUsedAtUtc = table.Column<DateTime>(nullable: true),
                RevokedAtUtc = table.Column<DateTime>(nullable: true),
                CreatedBy = table.Column<string>(maxLength: 200, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_machine_credentials", x => x.Id));

        migrationBuilder.CreateTable(
            name: "security_audit_events",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Actor = table.Column<string>(maxLength: 200, nullable: false),
                ActorType = table.Column<string>(maxLength: 40, nullable: false),
                Action = table.Column<string>(maxLength: 120, nullable: false),
                ResourceType = table.Column<string>(maxLength: 80, nullable: false),
                ResourceId = table.Column<string>(maxLength: 300, nullable: false),
                Reason = table.Column<string>(maxLength: 2000, nullable: true),
                BeforeJson = table.Column<string>(nullable: true),
                AfterJson = table.Column<string>(nullable: true),
                IpAddress = table.Column<string>(maxLength: 80, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_security_audit_events", x => x.Id));

        migrationBuilder.CreateTable(
            name: "identity_user_roles",
            columns: table => new
            {
                UserId = table.Column<Guid>(nullable: false),
                Role = table.Column<string>(maxLength: 40, nullable: false),
                GrantedAtUtc = table.Column<DateTime>(nullable: false),
                GrantedBy = table.Column<string>(maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_identity_user_roles", x => new { x.UserId, x.Role });
                table.ForeignKey(
                    name: "FK_identity_user_roles_identity_users_UserId",
                    column: x => x.UserId,
                    principalTable: "identity_users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_identity_users_Login", "identity_users", "Login");
        migrationBuilder.CreateIndex("IX_identity_users_Provider_Subject", "identity_users", new[] { "Provider", "Subject" }, unique: true);
        migrationBuilder.CreateIndex("IX_machine_credentials_KeyHash", "machine_credentials", "KeyHash", unique: true);
        migrationBuilder.CreateIndex("IX_machine_credentials_IsActive_ExpiresAtUtc", "machine_credentials", new[] { "IsActive", "ExpiresAtUtc" });
        migrationBuilder.CreateIndex("IX_security_audit_events_CreatedAtUtc", "security_audit_events", "CreatedAtUtc");
        migrationBuilder.CreateIndex("IX_security_audit_events_ResourceType_ResourceId", "security_audit_events", new[] { "ResourceType", "ResourceId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("identity_user_roles");
        migrationBuilder.DropTable("machine_credentials");
        migrationBuilder.DropTable("security_audit_events");
        migrationBuilder.DropTable("identity_users");
    }
}
