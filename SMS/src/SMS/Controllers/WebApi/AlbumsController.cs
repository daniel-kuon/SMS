using Microsoft.AspNetCore.Mvc;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Route("api/Albums")]
    public class AlbumsController : ApiController<Album>
    {
        public AlbumsController(SmsDbContext context) : base(context)
        {
        }
    }
}