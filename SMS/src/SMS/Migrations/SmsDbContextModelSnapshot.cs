using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using SMS.Models;

namespace SMS.Migrations
{
    [DbContext(typeof(SmsDbContext))]
    partial class SmsDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.0.0-rc2-20901")
                .HasAnnotation("SqlServer:ValueGenerationStrategy", SqlServerValueGenerationStrategy.IdentityColumn);

            modelBuilder.Entity("SMS.Models.Address", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Comment");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Street");

                    b.Property<string>("Town");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<string>("Zip");

                    b.HasKey("Id");

                    b.ToTable("Address");
                });

            modelBuilder.Entity("SMS.Models.Album", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<DateTime>("InsertDate");

                    b.Property<bool>("StandAlone");

                    b.Property<string>("Title");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.ToTable("Album");
                });

            modelBuilder.Entity("SMS.Models.AlbumImage", b =>
                {
                    b.Property<int?>("AlbumId");

                    b.Property<int?>("ImageId");

                    b.Property<int?>("Index");

                    b.HasKey("AlbumId", "ImageId");

                    b.HasIndex("AlbumId");

                    b.HasIndex("ImageId");

                    b.ToTable("AlbumImage");
                });

            modelBuilder.Entity("SMS.Models.Comment", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Content");

                    b.Property<DateTime>("InsertDate");

                    b.Property<int?>("ParentId")
                        .IsRequired();

                    b.Property<double>("Rating");

                    b.Property<string>("Title");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.HasIndex("ParentId");

                    b.ToTable("Comment");
                });

            modelBuilder.Entity("SMS.Models.ContentPage", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("Content")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Title")
                        .IsRequired();

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.ToTable("ContentPage");
                });

            modelBuilder.Entity("SMS.Models.Crew", b =>
                {
                    b.Property<int?>("PersonId");

                    b.Property<int?>("TackId");

                    b.HasKey("PersonId", "TackId");

                    b.HasIndex("PersonId");

                    b.HasIndex("TackId");

                    b.ToTable("Crew");
                });

            modelBuilder.Entity("SMS.Models.Image", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int>("Height");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Path");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<int>("Width");

                    b.HasKey("Id");

                    b.ToTable("Image");
                });

            modelBuilder.Entity("SMS.Models.Job", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AssignedToId");

                    b.Property<string>("Content");

                    b.Property<bool>("Done");

                    b.Property<DateTime>("DueTo");

                    b.Property<DateTime>("InsertDate");

                    b.Property<int?>("SuperJobId");

                    b.Property<string>("Title")
                        .IsRequired();

                    b.Property<int?>("TripId");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.HasIndex("AssignedToId");

                    b.HasIndex("SuperJobId");

                    b.HasIndex("TripId");

                    b.ToTable("Job");
                });

            modelBuilder.Entity("SMS.Models.Location", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AddressId");

                    b.Property<int?>("HarbourId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<int>("LocationType");

                    b.Property<string>("Name");

                    b.Property<double>("Rating");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<string>("Website");

                    b.HasKey("Id");

                    b.HasIndex("AddressId");

                    b.HasIndex("HarbourId");

                    b.ToTable("Location");
                });

            modelBuilder.Entity("SMS.Models.Person", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<string>("FirstName")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("LastName")
                        .IsRequired();

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.ToTable("Person");
                });

            modelBuilder.Entity("SMS.Models.TackBase", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("Discriminator")
                        .IsRequired();

                    b.Property<double?>("Distance");

                    b.Property<DateTime>("EndDate");

                    b.Property<int?>("EndId")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<DateTime>("StartDate");

                    b.Property<int?>("StartId")
                        .IsRequired();

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.HasIndex("AlbumId");

                    b.HasIndex("EndId");

                    b.HasIndex("StartId");

                    b.ToTable("TackBase");

                    b.HasDiscriminator<string>("Discriminator").HasValue("TackBase");
                });

            modelBuilder.Entity("SMS.Models.Waypoint", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<int?>("AlbumId");

                    b.Property<string>("Description");

                    b.Property<string>("Discriminator")
                        .IsRequired();

                    b.Property<DateTime>("InsertDate");

                    b.Property<double>("Latitude");

                    b.Property<double>("Longitude");

                    b.Property<string>("Name");

                    b.Property<DateTime>("UpdateDate");

                    b.Property<int?>("WaypointNumber");

                    b.HasKey("Id");

                    b.HasIndex("AlbumId");

                    b.ToTable("Waypoint");

                    b.HasDiscriminator<string>("Discriminator").HasValue("Waypoint");
                });

            modelBuilder.Entity("SMS.Models.WaypointConnection", b =>
                {
                    b.Property<int>("Waypoint1Id");

                    b.Property<int>("Waypoint2Id");

                    b.HasKey("Waypoint1Id", "Waypoint2Id");

                    b.HasIndex("Waypoint1Id");

                    b.HasIndex("Waypoint2Id");

                    b.ToTable("WaypointConnection");
                });

            modelBuilder.Entity("SMS.Models.Wifi", b =>
                {
                    b.Property<int?>("Id")
                        .ValueGeneratedOnAdd();

                    b.Property<bool>("Free");

                    b.Property<int>("HarbourId");

                    b.Property<DateTime>("InsertDate");

                    b.Property<string>("Name")
                        .IsRequired();

                    b.Property<string>("Password");

                    b.Property<int>("Speed");

                    b.Property<DateTime>("UpdateDate");

                    b.HasKey("Id");

                    b.HasIndex("HarbourId");

                    b.ToTable("Wifi");
                });

            modelBuilder.Entity("SMS.Models.LogBookEntry", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<decimal>("LogEnd");

                    b.Property<decimal>("LogStart");

                    b.Property<decimal>("MotorHoursEnd");

                    b.Property<decimal>("MotorHoursStart");

                    b.Property<string>("SpecialOccurences");

                    b.Property<string>("WindDirection");

                    b.Property<decimal>("WindSpeed");

                    b.ToTable("LogBookEntry");

                    b.HasDiscriminator().HasValue("LogBookEntry");
                });

            modelBuilder.Entity("SMS.Models.Tack", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<int?>("TripId");

                    b.HasIndex("TripId");

                    b.ToTable("Tack");

                    b.HasDiscriminator().HasValue("Tack");
                });

            modelBuilder.Entity("SMS.Models.Trip", b =>
                {
                    b.HasBaseType("SMS.Models.TackBase");

                    b.Property<string>("Content");

                    b.Property<bool>("IsDummy");

                    b.Property<string>("Name");

                    b.ToTable("Trip");

                    b.HasDiscriminator().HasValue("Trip");
                });

            modelBuilder.Entity("SMS.Models.Harbour", b =>
                {
                    b.HasBaseType("SMS.Models.Waypoint");

                    b.Property<string>("Content");

                    b.Property<double>("Rating");

                    b.Property<string>("Website");

                    b.ToTable("Harbour");

                    b.HasDiscriminator().HasValue("Harbour");
                });

            modelBuilder.Entity("SMS.Models.AlbumImage", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("SMS.Models.Image")
                        .WithMany()
                        .HasForeignKey("ImageId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("SMS.Models.Comment", b =>
                {
                    b.HasOne("SMS.Models.Comment")
                        .WithMany()
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("SMS.Models.Crew", b =>
                {
                    b.HasOne("SMS.Models.Person")
                        .WithMany()
                        .HasForeignKey("PersonId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("SMS.Models.TackBase")
                        .WithMany()
                        .HasForeignKey("TackId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("SMS.Models.Job", b =>
                {
                    b.HasOne("SMS.Models.Person")
                        .WithMany()
                        .HasForeignKey("AssignedToId");

                    b.HasOne("SMS.Models.Job")
                        .WithMany()
                        .HasForeignKey("SuperJobId");

                    b.HasOne("SMS.Models.Trip")
                        .WithMany()
                        .HasForeignKey("TripId");
                });

            modelBuilder.Entity("SMS.Models.Location", b =>
                {
                    b.HasOne("SMS.Models.Address")
                        .WithMany()
                        .HasForeignKey("AddressId");

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("HarbourId");
                });

            modelBuilder.Entity("SMS.Models.TackBase", b =>
                {
                    b.HasOne("SMS.Models.Album")
                        .WithMany()
                        .HasForeignKey("AlbumId");

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("EndId")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("StartId")
                        .OnDelete(DeleteBehavior.Cascade);
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
                        .HasForeignKey("Waypoint1Id")
                        .OnDelete(DeleteBehavior.Cascade);

                    b.HasOne("SMS.Models.Waypoint")
                        .WithMany()
                        .HasForeignKey("Waypoint2Id")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("SMS.Models.Wifi", b =>
                {
                    b.HasOne("SMS.Models.Harbour")
                        .WithMany()
                        .HasForeignKey("HarbourId")
                        .OnDelete(DeleteBehavior.Cascade);
                });

            modelBuilder.Entity("SMS.Models.Tack", b =>
                {
                    b.HasOne("SMS.Models.Trip")
                        .WithMany()
                        .HasForeignKey("TripId");
                });
        }
    }
}
