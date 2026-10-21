using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recipoke.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimisticAggregateVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Recipes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Cookbooks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            // A different unpredictable usable token for each existing aggregate; no content is rewritten.
            migrationBuilder.Sql("UPDATE Recipes SET Version = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)));");
            migrationBuilder.Sql("UPDATE Cookbooks SET Version = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("Restore a complete pre-upgrade backup with matching application versions; dropping versions would permit unsafe stale writes.");
        }
    }
}
