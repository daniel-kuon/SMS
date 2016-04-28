using Microsoft.AspNet.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Jobs")]
    public class JobsController : ApiController<Job>
    {
        public JobsController(SmsDbContext context) : base(context)
        {
        }
    }
}