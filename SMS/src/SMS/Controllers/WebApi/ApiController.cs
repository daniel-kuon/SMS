using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMS.Models;

namespace SMS.Controllers.WebApi
{
    [Produces("application/json")]
    public abstract  class ApiController<T> : Controller where T : class, IEntity
    {
        protected SmsDbContext Context;

        protected ApiController(SmsDbContext context)
        {
            Context = context;
        }

        [HttpGet]
        public IEnumerable<T> Get()
        {
            return Context.Set<T>();
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            T Entity = await Context.Set<T>().SingleAsync(m => m.Id == id);

            if (Entity == null)
            {
                return NotFound();
            }

            return Ok(Entity);
        }

        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> Put([FromRoute] int id, [FromBody] T entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != entity.Id)
            {
                return BadRequest();
            }


            entity.AddOrUpdate(Context);
            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!Exists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return Ok(entity);
            //return new HttpStatusCodeResult(StatusCodes.Status204NoContent);
        }



        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Post([FromBody] T entity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            entity.AddOrUpdate(Context);
            try
            {
                await Context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (Exists(entity.Id))
                {
                    return new StatusCodeResult(StatusCodes.Status409Conflict);
                }
                else
                {
                    throw;
                }
            }
            return Created(HttpContext.Request.GetEncodedUrl() + entity.Id, entity);
        }

        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete([FromRoute] int id)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            IEntity entity = await Context.Set<T>().SingleAsync(m => m.Id == id);
            if (entity == null)
            {
                return NotFound();
            }

            entity.RemoveFromContext(Context);
            await Context.SaveChangesAsync();

            return Ok(entity);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Context.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool Exists(int? id)
        {
            return id!=null && Context.Set<T>().Count(e => e.Id == id) > 0;
        }
    }
}