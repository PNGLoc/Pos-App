using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PosSystem.Main.Server;

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
            return await _context.TableCategories
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.CategoryID)
                .ToListAsync();
        }

        // POST: api/TableCategory
        [HttpPost]
        public async Task<ActionResult<TableCategory>> PostTableCategory(TableCategory category)
        {
            try
            {
                _context.TableCategories.Add(category);
                await _context.SaveChangesAsync();

                return CreatedAtAction("GetTableCategories", new { id = category.CategoryID }, category);
            }
            catch
            {
                return ApiError.Result(500, "TABLE_CATEGORY_CREATE_FAILED", "Lỗi tạo loại bàn");
            }
        }

        // PUT: api/TableCategory/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTableCategory(int id, TableCategory category)
        {
            if (id != category.CategoryID)
            {
                return ApiError.Result(400, "TABLE_CATEGORY_ID_MISMATCH", "Id không khớp");
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
                    return ApiError.Result(404, "TABLE_CATEGORY_NOT_FOUND", "Không tìm thấy loại bàn");
                }
                else
                {
                    return ApiError.Result(409, "TABLE_CATEGORY_CONFLICT", "Dữ liệu đã thay đổi, vui lòng tải lại");
                }
            }
            catch
            {
                return ApiError.Result(500, "TABLE_CATEGORY_UPDATE_FAILED", "Lỗi cập nhật loại bàn");
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
                return ApiError.Result(404, "TABLE_CATEGORY_NOT_FOUND", "Không tìm thấy loại bàn");
            }

            try
            {
                _context.TableCategories.Remove(category);
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch
            {
                return ApiError.Result(500, "TABLE_CATEGORY_DELETE_FAILED", "Lỗi xoá loại bàn");
            }
        }

        private bool TableCategoryExists(int id)
        {
            return _context.TableCategories.Any(e => e.CategoryID == id);
        }
    }
}
