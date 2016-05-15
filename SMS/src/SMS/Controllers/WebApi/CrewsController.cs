using System.Collections.Generic;
using Microsoft.AspNet.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/Crews")]
    public class CrewsController : Controller
    {
        private SmsDbContext _context;

        public CrewsController(SmsDbContext context)
        {
            this._context = context;
        }

        [HttpGet]
        public IEnumerable<Crew> Get()
        {
            return _context.Set<Crew>();
        }
    }
}