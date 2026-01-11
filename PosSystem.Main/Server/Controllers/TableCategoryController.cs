using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TableCategoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public TableCategoryController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/TableCategory
        [HttpGet]
        public async Task<ActionResult<IEnumerable<TableCategory>>> GetTableCategories()
        {
            return await _context.TableCategories.ToListAsync();
        }

        // POST: api/TableCategory
        [HttpPost]
        public async Task<ActionResult<TableCategory>> PostTableCategory(TableCategory category)
        {
            _context.TableCategories.Add(category);
            await _context.SaveChangesAsync();

            return CreatedAtAction("GetTableCategories", new { id = category.CategoryID }, category);
        }

        // PUT: api/TableCategory/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTableCategory(int id, TableCategory category)
        {
            if (id != category.CategoryID)
            {
                return BadRequest();
            }

            _context.Entry(category).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!TableCategoryExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/TableCategory/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTableCategory(int id)
        {
            var category = await _context.TableCategories.FindAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            _context.TableCategories.Remove(category);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool TableCategoryExists(int id)
        {
            return _context.TableCategories.Any(e => e.CategoryID == id);
        }
    }
}
