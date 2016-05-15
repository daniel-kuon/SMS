using Microsoft.AspNet.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/LogBookEntries")]
    public class LogBookEntriesController : ApiController<LogBookEntry>
    {
        public LogBookEntriesController(SmsDbContext context) : base(context)
        {
        }
    }
}