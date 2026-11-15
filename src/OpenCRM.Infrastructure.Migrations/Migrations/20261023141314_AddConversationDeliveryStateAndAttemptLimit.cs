using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCRM.Infrastructure.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationDeliveryStateAndAttemptLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChannelStateChangedAt",
                table: "OrganizationConversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsChatActive",
                table: "OrganizationConversations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryAttemptCount",
                table: "OrganizationConversationMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelStateChangedAt",
                table: "OrganizationConversations");

            migrationBuilder.DropColumn(
                name: "IsChatActive",
                table: "OrganizationConversations");

            migrationBuilder.DropColumn(
                name: "DeliveryAttemptCount",
                table: "OrganizationConversationMessages");
        }
    }
}
