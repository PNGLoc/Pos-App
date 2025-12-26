using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using System.Linq;
using System.Threading.Tasks;

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MenuController : ControllerBase
    {
        private readonly AppDbContext _context;

        public MenuController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetMenu()
        {
            // Lấy danh mục
            var categories = await _context.Categories.OrderBy(c => c.OrderIndex).ToListAsync();

            // Lấy món ăn đang Active
            var dishes = await _context.Dishes.Where(d => d.DishStatus == "Active").ToListAsync();

            // Nhóm lại để Mobile dễ hiển thị dạng Tabs
            var result = categories.Select(cat => new
            {
                cat.CategoryID,
                cat.CategoryName,
                Dishes = dishes.Where(d => d.CategoryID == cat.CategoryID).ToList()
            });

            return Ok(result);
        }
    }
}