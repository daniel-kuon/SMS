using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SMS.Migrations
{
    public partial class m2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlbumId",
                table: "Waypoint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Waypoint_AlbumId",
                table: "Waypoint",
                column: "AlbumId");

            migrationBuilder.AddColumn<decimal>(
                name: "LogEnd",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LogStart",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MotorHoursEnd",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MotorHoursStart",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecialOccurences",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WindDirection",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WindSpeed",
                table: "TackBase",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Waypoint_Album_AlbumId",
                table: "Waypoint",
                column: "AlbumId",
                principalTable: "Album",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Waypoint_Album_AlbumId",
                table: "Waypoint");

            migrationBuilder.DropIndex(
                name: "IX_Waypoint_AlbumId",
                table: "Waypoint");

            migrationBuilder.DropColumn(
                name: "AlbumId",
                table: "Waypoint");

            migrationBuilder.DropColumn(
                name: "LogEnd",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "LogStart",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "MotorHoursEnd",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "MotorHoursStart",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "SpecialOccurences",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "WindDirection",
                table: "TackBase");

            migrationBuilder.DropColumn(
                name: "WindSpeed",
                table: "TackBase");
        }
    }
}
