using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Cors;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
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