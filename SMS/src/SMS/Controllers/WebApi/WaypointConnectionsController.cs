using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/WaypointConnections")]
    public class WaypointConnectionsController : Controller
    {
        private readonly SmsDbContext _context;

        public WaypointConnectionsController(SmsDbContext context)
        {
            _context = context;
        }

        // GET: api/WaypointConnections
        [HttpGet]
        public IQueryable<WaypointConnection> GetWaypointConnections()
        {
            return _context.Set<WaypointConnection>();
        }

        [Authorize]
        [HttpPost("{id1}/{id2}")]
        public IActionResult Connect([FromRoute] int id1, [FromRoute] int id2)
        {
            if (!Exists(id1, id2))
            {
                _context.Set<WaypointConnection>().Add(
                    new WaypointConnection() { Waypoint1Id = id1, Waypoint2Id = id2 });

                _context.SaveChanges();
            }

            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        [Authorize]
        [HttpDelete("{id1}/{id2}")]
        public async Task<IActionResult> Disconnect([FromRoute] int id1, [FromRoute] int id2)
        {
            _context.Remove(
                await
                    _context.Set<WaypointConnection>().FirstAsync(
                        wC =>
                            wC.Waypoint1Id == id1 && wC.Waypoint2Id == id2 ||
                            wC.Waypoint1Id == id2 && wC.Waypoint2Id == id1));

            await _context.SaveChangesAsync();

            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Disconnect([FromRoute] int id)
        {
            _context.RemoveRange(
                _context.Set<WaypointConnection>().Where(
                    wC =>
                        wC.Waypoint1Id == id || wC.Waypoint2Id == id));

            await _context.SaveChangesAsync();

            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        public bool Exists(int id1, int id2)
        {
            return _context.Set<WaypointConnection>().Count(wC => wC.Waypoint1Id == id1 && wC.Waypoint2Id == id2 ||
                                                            wC.Waypoint1Id == id2 && wC.Waypoint2Id == id1) > 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}