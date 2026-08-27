using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FraudDetection.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddKafkaProcessingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "transactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedToKafkaUtc",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessedAtUtc",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PublishedToKafkaUtc",
                table: "transactions");
        }
    }
}
