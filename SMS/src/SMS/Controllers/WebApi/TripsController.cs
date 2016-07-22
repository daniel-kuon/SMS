using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Trips")]
    public class TripsController : ApiController<Trip>
    {
        public TripsController(SmsDbContext context) : base(context)
        {
        }
    }
}