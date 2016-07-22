using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
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