using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeAtlas.Api.Migrations
{
    /// <inheritdoc />
    public partial class StringIngredientQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF SQLite decimals already use invariant TEXT. Never cast through REAL
            // or printf, which would lose precision. Nulls become editable empty values.
            migrationBuilder.Sql("UPDATE Ingredient SET Quantity = CAST(Quantity AS TEXT) WHERE Quantity IS NOT NULL;");
            migrationBuilder.Sql("UPDATE Ingredient SET Quantity = '' WHERE Quantity IS NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "Quantity",
                table: "Ingredient",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A decimal cannot hold a range or an exact fraction. Avoid a lossy rollback.
            throw new NotSupportedException("String quantities cannot be downgraded without loss. Restore the pre-upgrade backup.");
        }
    }
}
