using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Database;
using PosSystem.Main.Server.Dtos;
using System.Threading.Tasks;

namespace PosSystem.Main.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Tìm nhân viên khớp username và password
            // Lưu ý: Project nội bộ có thể lưu pass thô, nhưng tốt nhất sau này nên mã hóa MD5/SHA
            var user = await _context.Accounts
                .FirstOrDefaultAsync(u => u.Username == request.Username && u.AccPass == request.Password);

            if (user == null)
            {
                return Unauthorized(new { message = "Sai tên đăng nhập hoặc mật khẩu!" });
            }

            return Ok(new
            {
                user.AccID,
                user.AccName,
                user.AccRole,
                CanMoveTable = user.CanMoveTable,
                CanPayment = user.CanPayment,
                CanCancelItem = user.CanCancelItem
            });
        }
    }
}