using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flux.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Hits_Date",
                table: "Hits",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Hits_OwnerId_ConfigName_Date",
                table: "Hits",
                columns: new[] { "OwnerId", "ConfigName", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Hits_OwnerId_Type_Date",
                table: "Hits",
                columns: new[] { "OwnerId", "Type", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Hits_Type_Date",
                table: "Hits",
                columns: new[] { "Type", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_GroupId_LastChecked",
                table: "Proxies",
                columns: new[] { "GroupId", "LastChecked" });

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_GroupId_Status",
                table: "Proxies",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_Status_Ping",
                table: "Proxies",
                columns: new[] { "Status", "Ping" });

            migrationBuilder.CreateIndex(
                name: "IX_Records_ConfigId_WordlistId",
                table: "Records",
                columns: new[] { "ConfigId", "WordlistId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hits_Date",
                table: "Hits");

            migrationBuilder.DropIndex(
                name: "IX_Hits_OwnerId_ConfigName_Date",
                table: "Hits");

            migrationBuilder.DropIndex(
                name: "IX_Hits_OwnerId_Type_Date",
                table: "Hits");

            migrationBuilder.DropIndex(
                name: "IX_Hits_Type_Date",
                table: "Hits");

            migrationBuilder.DropIndex(
                name: "IX_Proxies_GroupId_LastChecked",
                table: "Proxies");

            migrationBuilder.DropIndex(
                name: "IX_Proxies_GroupId_Status",
                table: "Proxies");

            migrationBuilder.DropIndex(
                name: "IX_Proxies_Status_Ping",
                table: "Proxies");

            migrationBuilder.DropIndex(
                name: "IX_Records_ConfigId_WordlistId",
                table: "Records");
        }
    }
}
