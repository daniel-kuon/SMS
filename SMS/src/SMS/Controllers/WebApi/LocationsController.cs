using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Locations")]
    public class LocationsController : ApiController<Location>
    {
        public LocationsController(SmsDbContext context) : base(context)
        {
        }
    }
}