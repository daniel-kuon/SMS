using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using SMS.Models;


namespace SMS.Controllers.WebApi
{
    [Route("api/People")]
    public class PeopleController : ApiController<Person>
    {
        public PeopleController(SmsDbContext context) : base(context)
        {
        }
    }
}