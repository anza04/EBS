using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ebm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CountryEuEea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEuEea",
                table: "Countries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Existing countries: EU 27, EEA (Iceland, Liechtenstein, Norway) and Switzerland.
            migrationBuilder.Sql(
                "UPDATE \"Countries\" SET \"IsEuEea\" = 1 WHERE \"Code\" IN ("
                + "'AUT','BEL','BGR','HRV','CYP','CZE','DNK','EST','FIN','FRA','DEU','GRC','HUN','IRL','ITA','LVA','LTU','LUX','MLT','NLD',"
                + "'POL','PRT','ROU','SVK','SVN','ESP','SWE','ISL','LIE','NOR','CHE');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsEuEea",
                table: "Countries");
        }
    }
}
