using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Harbours")]
    public class HarboursController : ApiController<Harbour>
    {
        public HarboursController(SmsDbContext context) : base(context)
        {
        }
    }
}