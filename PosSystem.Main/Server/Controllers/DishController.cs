using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using System.Linq;
using System.Threading.Tasks;

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DishController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DishController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/dish
        // Lấy tất cả các món ăn (kèm thông tin category)
        [HttpGet]
        public async Task<IActionResult> GetAllDishes()
        {
            var dishes = await _context.Dishes
                .Include(d => d.Category)
                .Where(d => d.DishStatus == "Active")
                .OrderBy(d => d.CategoryID)
                .ThenBy(d => d.DishName)
                .ToListAsync();

            var result = dishes.Select(d => new
            {
                d.DishID,
                d.DishName,
                d.Price,
                Category = d.Category != null ? new
                {
                    d.Category.CategoryID,
                    d.Category.CategoryName
                } : null
            });

            return Ok(result);
        }

        // GET: api/dish/{id}
        // Lấy thông tin chi tiết của một món ăn
        [HttpGet("{id}")]
        public async Task<IActionResult> GetDishById(int id)
        {
            var dish = await _context.Dishes
                .Include(d => d.Category)
                .FirstOrDefaultAsync(d => d.DishID == id && d.DishStatus == "Active");

            if (dish == null)
                return NotFound("Không tìm thấy món ăn");

            return Ok(new
            {
                dish.DishID,
                dish.DishName,
                dish.Price,
                Category = dish.Category != null ? new
                {
                    dish.Category.CategoryID,
                    dish.Category.CategoryName
                } : null
            });
        }
    }
}
