using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Addresses")]
    public class AddressController : ApiController<Address>
    {
        public AddressController(SmsDbContext context) : base(context)
        {
        }
    }
}