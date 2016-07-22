using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/Waypoints")]
    public class WaypointsController : ApiController<Waypoint>
    {

        public WaypointsController(SmsDbContext context) : base(context)
        {
        }

    }
}