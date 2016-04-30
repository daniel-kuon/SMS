using System.Collections.Generic;
using Microsoft.AspNet.Mvc;
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

    [Produces("application/json")]
    [Route("api/AlbumImages")]
    public class AlbumImageController : Controller
    {
        private SmsDbContext _context;

        public AlbumImageController(SmsDbContext context)
        {
            this._context = context;
        }

        [HttpGet]
        public IEnumerable<AlbumImage> Get()
        {
            return _context.Set<AlbumImage>();
        }
    }
}