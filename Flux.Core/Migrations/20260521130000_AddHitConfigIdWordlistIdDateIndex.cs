using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flux.Core.Migrations
{
    /// <summary>
    /// Adds the index that serves the per-job recent-hits query, which filters on
    /// ConfigId + WordlistId and orders by Date. No existing index covered ConfigId.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260521130000_AddHitConfigIdWordlistIdDateIndex")]
    public partial class AddHitConfigIdWordlistIdDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Hits_ConfigId_WordlistId_Date",
                table: "Hits",
                columns: new[] { "ConfigId", "WordlistId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hits_ConfigId_WordlistId_Date",
                table: "Hits");
        }
    }
}
