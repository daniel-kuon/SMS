using Microsoft.AspNetCore.Mvc;
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