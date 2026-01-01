using PosSystem.Main.Database;
using PosSystem.Main.Models;
using System;
using System.Linq;

namespace PosSystem.Main.Services
{
    public class PriceService
    {
        /// <summary>
        /// Lấy giá hiện tại của một sản phẩm dựa vào rule đang hoạt động
        /// </summary>
        public static decimal GetCurrentPrice(int dishId)
        {
            using (var db = new AppDbContext())
            {
                var dish = db.Dishes.Find(dishId);
                if (dish == null) return 0;

                // Lấy rule đang được kích hoạt
                var activeSetting = db.GlobalSettings
                    .FirstOrDefault(g => g.Key == "activePriceRule");

                if (activeSetting == null || string.IsNullOrEmpty(activeSetting.Value))
                {
                    // Nếu không có setting hoặc value rỗng -> dùng giá gốc
                    return dish.Price;
                }

                // Tìm rule phù hợp
                var priceRule = db.DishPriceRules
                    .FirstOrDefault(p => p.DishID == dishId
                        && p.RuleType == activeSetting.Value
                        && p.IsActive);

                if (priceRule != null)
                {
                    // Kiểm tra thời gian có hợp lệ không
                    DateTime now = DateTime.Now;
                    if ((priceRule.StartDate == null || priceRule.StartDate <= now) &&
                        (priceRule.EndDate == null || priceRule.EndDate >= now))
                    {
                        return priceRule.Price;
                    }
                }

                // Nếu rule hết hạn -> dùng giá gốc
                return dish.Price;
            }
        }

        /// <summary>
        /// Thiết lập rule giá đang hoạt động
        /// </summary>
        public static void SetActivePriceRule(string ruleType)
        {
            using (var db = new AppDbContext())
            {
                var setting = db.GlobalSettings
                    .FirstOrDefault(g => g.Key == "activePriceRule");

                if (setting == null)
                {
                    // Tạo mới nếu chưa tồn tại
                    setting = new GlobalSetting
                    {
                        Key = "activePriceRule",
                        Value = ruleType,
                        Description = $"Giá đang áp dụng: {ruleType}",
                        ModifiedDate = DateTime.Now
                    };
                    db.GlobalSettings.Add(setting);
                }
                else
                {
                    // Cập nhật value
                    setting.Value = ruleType;
                    setting.ModifiedDate = DateTime.Now;
                }

                db.SaveChanges();
            }
        }

        /// <summary>
        /// Lấy rule đang hoạt động
        /// </summary>
        public static string GetActivePriceRule()
        {
            using (var db = new AppDbContext())
            {
                var setting = db.GlobalSettings
                    .FirstOrDefault(g => g.Key == "activePriceRule");

                return setting?.Value ?? "";
            }
        }

        /// <summary>
        /// Lấy tất cả rule types khả dụng
        /// </summary>
        public static List<string> GetAvailableRuleTypes()
        {
            using (var db = new AppDbContext())
            {
                return db.DishPriceRules
                    .Where(p => p.IsActive)
                    .Select(p => p.RuleType)
                    .Distinct()
                    .ToList();
            }
        }
    }
}
