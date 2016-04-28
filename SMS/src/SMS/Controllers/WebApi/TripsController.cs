using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
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