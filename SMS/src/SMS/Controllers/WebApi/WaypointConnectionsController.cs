using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
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
            return _context.WaypointConnections;
        }

        [HttpPost("{id1}/{id2}")]
        public IActionResult Connect([FromRoute] int id1, [FromRoute] int id2)
        {
            if (!Exists(id1, id2))
            {
                _context.WaypointConnections.Add(
                    new WaypointConnection() { Waypoint1Id = id1, Waypoint2Id = id2 });

                _context.SaveChanges();
            }

            return new HttpStatusCodeResult(StatusCodes.Status204NoContent);
        }

        [HttpDelete("{id1}/{id2}")]
        public async Task<IActionResult> Disconnect([FromRoute] int id1, [FromRoute] int id2)
        {
            _context.Remove(
                await
                    _context.WaypointConnections.FirstAsync(
                        wC =>
                            wC.Waypoint1Id == id1 && wC.Waypoint2Id == id2 ||
                            wC.Waypoint1Id == id2 && wC.Waypoint2Id == id1));

            await _context.SaveChangesAsync();

            return new HttpStatusCodeResult(StatusCodes.Status204NoContent);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Disconnect([FromRoute] int id)
        {
            _context.RemoveRange(
                _context.WaypointConnections.Where(
                    wC =>
                        wC.Waypoint1Id == id || wC.Waypoint2Id == id));

            await _context.SaveChangesAsync();

            return new HttpStatusCodeResult(StatusCodes.Status204NoContent);
        }

        public bool Exists(int id1, int id2)
        {
            return _context.WaypointConnections.Count(wC => wC.Waypoint1Id == id1 && wC.Waypoint2Id == id2 ||
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