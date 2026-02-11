using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Services; // Import PriceService
using System.Linq;
using System.Threading.Tasks;
using PosSystem.Main.Server;

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
            try
            {
                // Lấy danh mục
                var categories = await _context.Categories.OrderBy(c => c.OrderIndex).ToListAsync();

                // Lấy món ăn đang Active
                var dishes = await _context.Dishes.Where(d => d.DishStatus == "Active").ToListAsync();

                // Nhóm lại để Mobile dễ hiển thị dạng Tabs
                // [Fix] Sử dụng viewModel/DTO tại chỗ để update giá theo Rule
                var result = categories.Select(cat => new
                {
                    cat.CategoryID,
                    cat.CategoryName,
                    Dishes = dishes.Where(d => d.CategoryID == cat.CategoryID)
                                   .Select(d => new
                                   {
                                       d.DishID,
                                       d.DishName,
                                       d.Unit,
                                       Image = d.ImagePath, // Map ImagePath -> Image for frontend compatibility if needed, or just use d.ImagePath
                                       // [Important] Tính lại giá theo Rule
                                       Price = PriceService.GetCurrentPrice(d.DishID, _context),
                                       OriginalPrice = d.Price, // Giá gốc để tham khảo nếu cần
                                       d.DishStatus,
                                       d.CategoryID
                                   }).ToList()
                });

                return Ok(result);
            }
            catch
            {
                return ApiError.Result(500, "MENU_LOAD_FAILED", "Lỗi tải thực đơn");
            }
        }
    }
}