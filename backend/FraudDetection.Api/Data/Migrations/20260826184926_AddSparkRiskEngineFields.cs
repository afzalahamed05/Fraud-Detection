using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FraudDetection.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSparkRiskEngineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingSource",
                table: "transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RiskScore",
                table: "transactions",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "fraud_alerts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TriggeredRules",
                table: "fraud_alerts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_risk_profiles",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionCount = table.Column<int>(type: "integer", nullable: false),
                    AvgAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    StdDevAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DistinctMerchantCategories = table.Column<int>(type: "integer", nullable: false),
                    DistinctCountries = table.Column<int>(type: "integer", nullable: false),
                    AvgTransactionsPerDay = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    LastTransactionAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_risk_profiles", x => x.AccountId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_risk_profiles");

            migrationBuilder.DropColumn(
                name: "ProcessingSource",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "fraud_alerts");

            migrationBuilder.DropColumn(
                name: "TriggeredRules",
                table: "fraud_alerts");
        }
    }
}
