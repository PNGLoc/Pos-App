using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Models;
using System;
using System.IO;

namespace PosSystem.Main.Database
{
    public class AppDbContext : DbContext
    {
        public DbSet<Category> Categories { get; set; } // Mới
        public DbSet<Dish> Dishes { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "pos_data.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Seed Category
            modelBuilder.Entity<Category>().HasData(
                new Category { CategoryID = 1, CategoryName = "Cà phê", OrderIndex = 1 },
                new Category { CategoryID = 2, CategoryName = "Sinh tố & Nước ép", OrderIndex = 2 },
                new Category { CategoryID = 3, CategoryName = "Đồ ăn vặt", OrderIndex = 3 }
            );

            // Seed Dish (Có thêm CategoryID và ImagePath)
            modelBuilder.Entity<Dish>().HasData(
                new Dish { DishID = 1, DishName = "Cà phê đen", Price = 20000, Unit = "Ly", CategoryID = 1, ImagePath = "cfden.png" },
                new Dish { DishID = 2, DishName = "Cà phê sữa", Price = 25000, Unit = "Ly", CategoryID = 1, ImagePath = "cfsua.png" },
                new Dish { DishID = 3, DishName = "Sinh tố bơ", Price = 40000, Unit = "Ly", CategoryID = 2, DishType = "Drink", ImagePath = "stbo.png" },
                new Dish { DishID = 4, DishName = "Khoai tây chiên", Price = 30000, Unit = "Dĩa", CategoryID = 3, DishType = "Food", ImagePath = "khoaitay.png" }
            );

            // Seed Table & Account giữ nguyên như cũ...
            modelBuilder.Entity<Account>().HasData(
               new Account { AccID = 1, AccName = "Admin", Username = "admin", AccPass = "123", AccRole = "Admin" },
                 new Account { AccID = 2, AccName = "Nhân viên 1", Username = "nv1", AccPass = "123", AccRole = "Staff" }
           );
            modelBuilder.Entity<Table>().HasData(
                new Table { TableID = 1, TableName = "Bàn 1", TableType = "DineIn" },
                new Table { TableID = 2, TableName = "Bàn 2", TableType = "DineIn" }
            );

            // Seed dữ liệu mẫu cho Template Hóa Đơn
            // Đây là cấu hình mặc định (JSON)
            string defaultBillLayout = @"
        {
            ""Header"": {
                ""ShowLogo"": true,
                ""ShopNameSize"": 14,
                ""AddressSize"": 10
            },
            ""Body"": {
                ""ShowNo"": true,
                ""ShowPrice"": true
            },
            ""Footer"": {
                ""ShowWifi"": true,
                ""ShowQr"": true,
                ""EndMessage"": ""Xin cảm ơn và hẹn gặp lại!""
            }
        }";

            modelBuilder.Entity<PrintTemplate>().HasData(
                new PrintTemplate
                {
                    TemplateID = 1,
                    TemplateName = "Mẫu Hóa Đơn Chuẩn (80mm)",
                    TemplateType = "Bill",
                    PaperSize = 80,
                    IsActive = true,
                    LayoutConfig = defaultBillLayout
                },
                new PrintTemplate
                {
                    TemplateID = 2,
                    TemplateName = "Mẫu Bếp (Rút gọn)",
                    TemplateType = "Kitchen",
                    PaperSize = 80,
                    IsActive = true,
                    LayoutConfig = "{ \"Header\": { \"ShowShopName\": false }, \"Body\": { \"FontSize\": 14 } }"
                }
            );

        }

    }
}