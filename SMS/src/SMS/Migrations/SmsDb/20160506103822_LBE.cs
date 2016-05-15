using System;
using System.Collections.Generic;
using Microsoft.Data.Entity.Migrations;
using Microsoft.Data.Entity.Metadata;

namespace SMS.Migrations.SmsDb
{
    public partial class LBE : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Album",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    StandAlone = table.Column<bool>(nullable: false),
                    Title = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Album", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Album_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Address",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
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
                    table.ForeignKey(
                        name: "FK_Address_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Entity",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entity", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entity_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateTable(
                name: "Image",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    Height = table.Column<int>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Path = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    Width = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Image_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Person",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    FirstName = table.Column<string>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    LastName = table.Column<string>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Person", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Person_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Waypoint",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    Latitude = table.Column<double>(nullable: false),
                    Longitude = table.Column<double>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    WaypointNumber = table.Column<int>(nullable: true),
                    __Discriminator = table.Column<string>(nullable: false),
                    Content = table.Column<string>(nullable: true),
                    Rating = table.Column<double>(nullable: true),
                    Website = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waypoint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Waypoint_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "AlbumImage",
                columns: table => new
                {
                    ImageId = table.Column<int>(nullable: false),
                    AlbumId = table.Column<int>(nullable: false),
                    Index = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbumImage", x => new { x.ImageId, x.AlbumId });
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
                    AlbumId = table.Column<int>(nullable: true),
                    HarbourId = table.Column<int>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
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
                        name: "FK_Location_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Location_Harbour_HarbourId",
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
                    Distance = table.Column<double>(nullable: false),
                    EndDate = table.Column<DateTime>(nullable: false),
                    EndId = table.Column<int>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    StartDate = table.Column<DateTime>(nullable: false),
                    StartId = table.Column<int>(nullable: false),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    LogEnd = table.Column<decimal>(nullable: true),
                    LogStart = table.Column<decimal>(nullable: true),
                    MotoHoursStart = table.Column<decimal>(nullable: true),
                    MotorHoursEnd = table.Column<decimal>(nullable: true),
                    SpecialOccurences = table.Column<string>(nullable: true),
                    TripId = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    IsDummy = table.Column<bool>(nullable: true),
                    Name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TackBase", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tack_Trip_TripId",
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
                        name: "FK_TackBase_Harbour_EndId",
                        column: x => x.EndId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TackBase_Harbour_StartId",
                        column: x => x.StartId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaypointConnection_Waypoint_Waypoint2Id",
                        column: x => x.Waypoint2Id,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Crew_TackBase_TackId",
                        column: x => x.TackId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Job",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AlbumId = table.Column<int>(nullable: true),
                    AssignedToId = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    Done = table.Column<bool>(nullable: false),
                    DueTo = table.Column<DateTime>(nullable: false),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    SuperJobId = table.Column<int>(nullable: true),
                    SuperJobId1 = table.Column<int>(nullable: true),
                    Title = table.Column<string>(nullable: false),
                    TripId = table.Column<int>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Job", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Job_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "FK_Job_Job_SuperJobId1",
                        column: x => x.SuperJobId1,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Job_Trip_TripId",
                        column: x => x.TripId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn),
                    AddressId = table.Column<int>(nullable: true),
                    AlbumId = table.Column<int>(nullable: true),
                    CommentId = table.Column<int>(nullable: true),
                    Content = table.Column<string>(nullable: true),
                    ImageId = table.Column<int>(nullable: true),
                    InsertDate = table.Column<DateTime>(nullable: false),
                    JobId = table.Column<int>(nullable: true),
                    LocationId = table.Column<int>(nullable: true),
                    ParentId = table.Column<int>(nullable: false),
                    PersonId = table.Column<int>(nullable: true),
                    Rating = table.Column<double>(nullable: false),
                    TackBaseId = table.Column<int>(nullable: true),
                    Title = table.Column<string>(nullable: true),
                    UpdateDate = table.Column<DateTime>(nullable: false),
                    WaypointId = table.Column<int>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Address_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Address",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Album_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Album",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Comment_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Image_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Image",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Job_JobId",
                        column: x => x.JobId,
                        principalTable: "Job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Location_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Entity_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Entity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comment_Person_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Person",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_TackBase_TackBaseId",
                        column: x => x.TackBaseId,
                        principalTable: "TackBase",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comment_Waypoint_WaypointId",
                        column: x => x.WaypointId,
                        principalTable: "Waypoint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("AlbumImage");
            migrationBuilder.DropTable("Comment");
            migrationBuilder.DropTable("Crew");
            migrationBuilder.DropTable("WaypointConnection");
            migrationBuilder.DropTable("Image");
            migrationBuilder.DropTable("Job");
            migrationBuilder.DropTable("Location");
            migrationBuilder.DropTable("Entity");
            migrationBuilder.DropTable("Person");
            migrationBuilder.DropTable("TackBase");
            migrationBuilder.DropTable("Address");
            migrationBuilder.DropTable("Waypoint");
            migrationBuilder.DropTable("Album");
        }
    }
}
