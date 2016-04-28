using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Cors;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc;
using Microsoft.Data.Entity;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    [Route("api/Waypoints")]
    public class WaypointsController : Controller
    {
        private readonly SmsDbContext _context;

        public WaypointsController(SmsDbContext context)
        {
            _context = context;
        }

        // GET: api/Waypoints
        [HttpGet]
        public IEnumerable<Waypoint> GetWaypoints()
        {
            return _context.Waypoints;
        }

        // GET: api/Waypoints/5
        [HttpGet("{id}", Name = "GetWaypoint")]
        public async Task<IActionResult> GetWaypoint([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return HttpBadRequest(ModelState);
            }

            var waypoint = await _context.Waypoints.SingleAsync(m => m.Id == id);

            if (waypoint == null)
            {
                return HttpNotFound();
            }

            return Ok(waypoint);
        }

        // PUT: api/Waypoints/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutWaypoint([FromRoute] int id, [FromBody] Waypoint waypoint)
        {
            if (!ModelState.IsValid)
            {
                return HttpBadRequest(ModelState);
            }

            if (id != waypoint.Id)
            {
                return HttpBadRequest();
            }

            _context.Entry(waypoint).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WaypointExists(id))
                {
                    return HttpNotFound();
                }
                else
                {
                    throw;
                }
            }

            return new HttpStatusCodeResult(StatusCodes.Status204NoContent);
        }

        // POST: api/Waypoints
        [HttpPost]
        public async Task<IActionResult> PostWaypoint([FromBody] Waypoint waypoint)
        {
            if (!ModelState.IsValid)
            {
                return HttpBadRequest(ModelState);
            }

            _context.Waypoints.Add(waypoint);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (WaypointExists(waypoint.Id))
                {
                    return new HttpStatusCodeResult(StatusCodes.Status409Conflict);
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("GetWaypoint", new {id = waypoint.Id}, waypoint);
        }

        // DELETE: api/Waypoints/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteWaypoint([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return HttpBadRequest(ModelState);
            }

            var waypoint = await _context.Waypoints.SingleAsync(m => m.Id == id);
            if (waypoint == null)
            {
                return HttpNotFound();
            }

            _context.Waypoints.Remove(waypoint);
            await _context.SaveChangesAsync();

            return Ok(waypoint);
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _context.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool WaypointExists(int? id)
        {
            return _context.Waypoints.Count(e => e.Id == id) > 0;
        }
    }
}