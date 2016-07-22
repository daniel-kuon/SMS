using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Tacks")]
    public class TacksController : ApiController<Tack>
    {
        public TacksController(SmsDbContext context) : base(context)
        {
        }
    }
}