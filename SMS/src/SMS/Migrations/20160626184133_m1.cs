using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SMS.Migrations
{
    public partial class m1 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Comment = table.Column<string>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Street = table.Column<string>(nullable: true),
                    Town = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    Zip = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Album",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    StandAlone = table.Column<bool>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Album", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Content = table.Column<string>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    ParentId = table.Column<int>(nullable: false),
                    Rating = table.Column<double>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Comment_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentPage",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Content = table.Column<string>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Image",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Height = table.Column<int>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Path = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    Width = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Image", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Person",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    FirstName = table.Column<string>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    LastName = table.Column<string>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Waypoint",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(nullable: true),
                    Discriminator = table.Column<string>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Latitude = table.Column<double>(nullable: false),
                    Longitude = table.Column<double>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    WaypointNumber = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    Rating = table.Column<double>(nullable: true),
                    Website = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waypoint", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlbumImage",
                columns: table => new
                {
                    AlbumId = table.Column<int>(nullable: false),
                    ImageId = table.Column<int>(nullable: false),
                    Index = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumImage", x => new { x.AlbumId, x.ImageId });
                    table.ForeignKey(
                        name: "FK_AlbumImage_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlbumImage_Image_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Image",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Location",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AddressId = table.Column<int>(nullable: true),
                    HarbourId = table.Column<int>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    LocationType = table.Column<int>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Rating = table.Column<double>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    Website = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Location", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Location_Address_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Location_Waypoint_HarbourId",
                        column: x => x.HarbourId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TackBase",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    Discriminator = table.Column<string>(nullable: false),
                    Distance = table.Column<double>(nullable: true),
                    EndDate = table.Column<DateTime>(nullable: false),
                    EndId = table.Column<int>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    StartDate = table.Column<DateTime>(nullable: false),
                    StartId = table.Column<int>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    TripId = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    IsDummy = table.Column<bool>(nullable: true),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TackBase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TackBase_TackBase_TripId",
                        column: x => x.TripId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TackBase_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TackBase_Waypoint_EndId",
                        column: x => x.EndId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TackBase_Waypoint_StartId",
                        column: x => x.StartId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaypointConnection",
                columns: table => new
                {
                    Waypoint1Id = table.Column<int>(nullable: false),
                    Waypoint2Id = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaypointConnection", x => new { x.Waypoint1Id, x.Waypoint2Id });
                    table.ForeignKey(
                        name: "FK_WaypointConnection_Waypoint_Waypoint1Id",
                        column: x => x.Waypoint1Id,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WaypointConnection_Waypoint_Waypoint2Id",
                        column: x => x.Waypoint2Id,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Wifi",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    Free = table.Column<bool>(nullable: false),
                    HarbourId = table.Column<int>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Name = table.Column<string>(nullable: false),
                    Password = table.Column<string>(nullable: true),
                    Speed = table.Column<int>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wifi", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Wifi_Waypoint_HarbourId",
                        column: x => x.HarbourId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Crew",
                columns: table => new
                {
                    PersonId = table.Column<int>(nullable: false),
                    TackId = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crew", x => new { x.PersonId, x.TackId });
                    table.ForeignKey(
                        name: "FK_Crew_Person_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Crew_TackBase_TackId",
                        column: x => x.TackId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Job",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AssignedToId = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    Done = table.Column<bool>(nullable: false),
                    DueTo = table.Column<DateTime>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    SuperJobId = table.Column<int>(nullable: true),
                    Title = table.Column<string>(nullable: false),
                    TripId = table.Column<int>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Job", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Job_Person_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_Job_SuperJobId",
                        column: x => x.SuperJobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_TackBase_TripId",
                        column: x => x.TripId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlbumImage_AlbumId",
                table: "AlbumImage",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumImage_ImageId",
                table: "AlbumImage",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_ParentId",
                table: "Comment",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Crew_PersonId",
                table: "Crew",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Crew_TackId",
                table: "Crew",
                column: "TackId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_AssignedToId",
                table: "Job",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_SuperJobId",
                table: "Job",
                column: "SuperJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Job_TripId",
                table: "Job",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_Location_AddressId",
                table: "Location",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_Location_HarbourId",
                table: "Location",
                column: "HarbourId");

            migrationBuilder.CreateIndex(
                name: "IX_TackBase_AlbumId",
                table: "TackBase",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_TackBase_EndId",
                table: "TackBase",
                column: "EndId");

            migrationBuilder.CreateIndex(
                name: "IX_TackBase_StartId",
                table: "TackBase",
                column: "StartId");

            migrationBuilder.CreateIndex(
                name: "IX_TackBase_TripId",
                table: "TackBase",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_WaypointConnection_Waypoint1Id",
                table: "WaypointConnection",
                column: "Waypoint1Id");

            migrationBuilder.CreateIndex(
                name: "IX_WaypointConnection_Waypoint2Id",
                table: "WaypointConnection",
                column: "Waypoint2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Wifi_HarbourId",
                table: "Wifi",
                column: "HarbourId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlbumImage");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "ContentPage");

            migrationBuilder.DropTable(
                name: "Crew");

            migrationBuilder.DropTable(
                name: "Job");

            migrationBuilder.DropTable(
                name: "Location");

            migrationBuilder.DropTable(
                name: "WaypointConnection");

            migrationBuilder.DropTable(
                name: "Wifi");

            migrationBuilder.DropTable(
                name: "Image");

            migrationBuilder.DropTable(
                name: "Person");

            migrationBuilder.DropTable(
                name: "TackBase");

            migrationBuilder.DropTable(
                name: "Address");

            migrationBuilder.DropTable(
                name: "Album");

            migrationBuilder.DropTable(
                name: "Waypoint");
        }
    }
}
