using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
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