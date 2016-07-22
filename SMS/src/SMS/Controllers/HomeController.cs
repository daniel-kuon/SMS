using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers
{
    public class HomeController : Controller
    {
        private readonly SmsDbContext _db;

        public HomeController(SmsDbContext context)
        {
            _db = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View();
        }

        public IActionResult Jobs()
        {
            return View();
        }


        public IActionResult Seed([FromRoute] bool recreate = false)
        {
            //if (Startup.IsDevelopment)
            //{
            //    _db.Database.EnsureDeleted();
            //    _db.Database.EnsureCreated();
            //    var w1 = new Waypoint { Description = "Desc", Latitude = 50, Longitude = 11, Name = "W" };
            //    var h1 = new Harbour
            //    {
            //        Description = "Desc",
            //        Latitude = 50,
            //        Longitude = 10,
            //        Name = "H1",
            //        Locations =
            //        {
            //            new Location()
            //            {
            //                Name = "N",
            //                Type = LocationType.Restaurant,
            //                Address = new Address
            //                {
            //                    Comment = "C",
            //                    Street = "S",
            //                    Town = "T",
            //                    Zip = "Z"
            //                }
            //            }
            //        }
            //    };
            //    var h2 = new Harbour
            //    {
            //        Description = "Desc",
            //        Latitude = 50,
            //        Longitude = 12,
            //        Name = "H2",
            //        Album = new Album()
            //    };
            //    _db.AddHarbour(h1);
            //    _db.AddHarbour(h2);
            //    _db.AddWaypoint(w1);
            //    _db.WaypointConnections.Add(new WaypointConnection { Waypoint1 = h1, Waypoint2 = w1 });
            //    _db.WaypointConnections.Add(new WaypointConnection { Waypoint1 = h2, Waypoint2 = w1 });
            //    _db.SaveChanges();
            //}
            return View("Index");
        }
    }
}