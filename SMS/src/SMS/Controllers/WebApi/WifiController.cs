using Microsoft.AspNet.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Wifis")]
    public class WifiController : ApiController<Wifi>
    {

        public WifiController(SmsDbContext context) : base(context)
        {
        }

    }
}