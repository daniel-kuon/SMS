using System;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Metadata;
using Microsoft.Data.Entity.Migrations;
using SMS.Models;

namespace SMS.Migrations.SmsDb
{
    [DbContext(typeof(SmsDbContext))]
    [Migration("20160506103822_LBE")]
    partial class LBE
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.0-rc1-16348")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("SMS.Models.Address", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("Comment");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Street");

                    b.Property<string>("Town");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<string>("Zip");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Album", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<bool>("StandAlone");

                    b.Property<string>("Title");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.AlbumImage", b =>
                {
                    b.Property<int?>("ImageId");

                    b.Property<int?>("AlbumId");

                    b.Property<int?>("Index");

                    b.HasKey("ImageId", "AlbumId");
                });

            modelBuilder.Entity("SMS.Models.Comment", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AddressId");

                    b.Property<int?>("AlbumId");

                    b.Property<int?>("CommentId");

                    b.Property<string>("Content");

                    b.Property<int?>("ImageId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<int?>("JobId");

                    b.Property<int?>("LocationId");

                    b.Property<int?>("ParentId")
                        .IsRequired();

                    b.Property<int?>("PersonId");

                    b.Property<double>("Rating");

                    b.Property<int?>("TackBaseId");

                    b.Property<string>("Title");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<int?>("WaypointId");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Crew", b =>
                {
                    b.Property<int?>("PersonId");

                    b.Property<int?>("TackId");

                    b.HasKey("PersonId", "TackId");
                });

            modelBuilder.Entity("SMS.Models.Entity", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Image", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<int>("Height");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Path");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<int>("Width");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Job", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<int?>("AssignedToId");

                    b.Property<string>("Content");

                    b.Property<bool>("Done");

                    b.Property<DateTime>("DueTo");

                    b.Property<DateTime>("InsertDate");

                    b.Property<int?>("SuperJobId");

                    b.Property<int?>("SuperJobId1");

                    b.Property<string>("Title")
                        .IsRequired();

                    b.Property<int?>("TripId");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Location", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AddressId");

                    b.Property<int?>("AlbumId");

                    b.Property<int?>("HarbourId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Name");

                    b.Property<double>("Rating");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<string>("Website");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.Person", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("FirstName")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("LastName")
                        .IsRequired();

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");
                });

            modelBuilder.Entity("SMS.Models.TackBase", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("Discriminator")
                        .IsRequired();

                    b.Property<double?>("Distance")
                        .IsRequired();

                    b.Property<DateTime>("EndDate");

                    b.Property<int?>("EndId")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<DateTime>("StartDate");

                    b.Property<int?>("StartId")
                        .IsRequired();

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.HasAnnotation("Relational:DiscriminatorProperty", "Discriminator");

                    b.HasAnnotation("Relational:DiscriminatorValue", "TackBase");
                });

            modelBuilder.Entity("SMS.Models.Waypoint", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("Description");

                    b.Property<DateTime>("InsertDate");

                    b.Property<double>("Latitude");

                    b.Property<double>("Longitude");

                    b.Property<string>("Name");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<int?>("WaypointNumber");

                    b.Property<string>("__Discriminator")
                        .IsRequired();

                    b.HasKey("Id");

                    b.HasAnnotation("Relational:DiscriminatorProperty", "__Discriminator");

                    b.HasAnnotation("Relational:DiscriminatorValue", "Waypoint");
                });

            modelBuilder.Entity("SMS.Models.WaypointConnection", b =>
                {
                    b.Property<int>("Waypoint1Id");

                    b.Property<int>("Waypoint2Id");

                    b.HasKey("Waypoint1Id", "Waypoint2Id");
                });

            modelBuilder.Entity("SMS.Models.LogBookEntry", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<decimal>("LogEnd");

                    b.Property<decimal>("LogStart");

                    b.Property<decimal>("MotoHoursStart");

                    b.Property<decimal>("MotorHoursEnd");

                    b.Property<string>("SpecialOccurences");

                    b.HasAnnotation("Relational:DiscriminatorValue", "LogBookEntry");
                });

            modelBuilder.Entity("SMS.Models.Tack", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<int?>("TripId");

                    b.HasAnnotation("Relational:DiscriminatorValue", "Tack");
                });

            modelBuilder.Entity("SMS.Models.Trip", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<string>("Content");

                    b.Property<bool>("IsDummy");

                    b.Property<string>("Name");

                    b.HasAnnotation("Relational:DiscriminatorValue", "Trip");
                });

            modelBuilder.Entity("SMS.Models.Harbour", b =>
                {
                    b.HasBaseType("SMS.Models.Waypoint");

                    b.Property<string>("Content");

                    b.Property<double>("Rating");

                    b.Property<string>("Website");

                    b.HasAnnotation("Relational:DiscriminatorValue", "Harbour");
                });

            modelBuilder.Entity("SMS.Models.Address", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");
                });

            modelBuilder.Entity("SMS.Models.Album", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");
                });

            modelBuilder.Entity("SMS.Models.AlbumImage", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Image")
                        .WithMany()
                        .HasForeignKey("ImageId");
                });

            modelBuilder.Entity("SMS.Models.Comment", b =>
                {
                    b.HasOne("SMS.Models.Address")
                        .WithMany()
                        .HasForeignKey("AddressId");

                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Comment")
                        .WithMany()
                        .HasForeignKey("CommentId");

                    b.HasOne("SMS.Models.Image")
                        .WithMany()
                        .HasForeignKey("ImageId");

                    b.HasOne("SMS.Models.Job")
                        .WithMany()
                        .HasForeignKey("JobId");

                    b.HasOne("SMS.Models.Location")
                        .WithMany()
                        .HasForeignKey("LocationId");

                    b.HasOne("SMS.Models.Entity")
                        .WithMany()
                        .HasForeignKey("ParentId");

                    b.HasOne("SMS.Models.Person")
                        .WithMany()
                        .HasForeignKey("PersonId");

                    b.HasOne("SMS.Models.TackBase")
                        .WithMany()
                        .HasForeignKey("TackBaseId");

                    b.HasOne("SMS.Models.Waypoint")
                        .WithMany()
                        .HasForeignKey("WaypointId");
                });

            modelBuilder.Entity("SMS.Models.Crew", b =>
                {
                    b.HasOne("SMS.Models.Person")
                        .WithMany()
                        .HasForeignKey("PersonId");

                    b.HasOne("SMS.Models.TackBase")
                        .WithMany()
                        .HasForeignKey("TackId");
                });

            modelBuilder.Entity("SMS.Models.Entity", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithOne()
                        .HasForeignKey("SMS.Models.Entity", "AlbumId");
                });

            modelBuilder.Entity("SMS.Models.Image", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");
                });

            modelBuilder.Entity("SMS.Models.Job", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Person")
                        .WithMany()
                        .HasForeignKey("AssignedToId");

                    b.HasOne("SMS.Models.Job")
                        .WithMany()
                        .HasForeignKey("SuperJobId");

                    b.HasOne("SMS.Models.Job")
                        .WithMany()
                        .HasForeignKey("SuperJobId1");

                    b.HasOne("SMS.Models.Trip")
                        .WithMany()
                        .HasForeignKey("TripId");
                });

            modelBuilder.Entity("SMS.Models.Location", b =>
                {
                    b.HasOne("SMS.Models.Address")
                        .WithMany()
                        .HasForeignKey("AddressId");

                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("HarbourId");
                });

            modelBuilder.Entity("SMS.Models.Person", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");
                });

            modelBuilder.Entity("SMS.Models.TackBase", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("EndId");

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("StartId");
                });

            modelBuilder.Entity("SMS.Models.Waypoint", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");
                });

            modelBuilder.Entity("SMS.Models.WaypointConnection", b =>
                {
                    b.HasOne("SMS.Models.Waypoint")
                        .WithMany()
                        .HasForeignKey("Waypoint1Id");

                    b.HasOne("SMS.Models.Waypoint")
                        .WithMany()
                        .HasForeignKey("Waypoint2Id");
                });

            modelBuilder.Entity("SMS.Models.LogBookEntry", b =>
                {
                });

            modelBuilder.Entity("SMS.Models.Tack", b =>
                {
                    b.HasOne("SMS.Models.Trip")
                        .WithMany()
                        .HasForeignKey("TripId");
                });

            modelBuilder.Entity("SMS.Models.Trip", b =>
                {
                });

            modelBuilder.Entity("SMS.Models.Harbour", b =>
                {
                });
        }
    }
}
