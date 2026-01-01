using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Models;
using System;
using System.IO;

namespace PosSystem.Main.Database
{
    public class AppDbContext : DbContext
    {
        public DbSet<Category> Categories { get; set; }
        public DbSet<Dish> Dishes { get; set; }
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<Printer> Printers { get; set; }
        public DbSet<PrintTemplate> PrintTemplates { get; set; }
        public DbSet<DishPriceRule> DishPriceRules { get; set; }
        public DbSet<GlobalSetting> GlobalSettings { get; set; }
        public DbSet<PriceRuleType> PriceRuleTypes { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath = Path.Combine(AppContext.BaseDirectory, "pos_data.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Seed dữ liệu mẫu cho máy in
            modelBuilder.Entity<Printer>().HasData(
                new Printer { PrinterID = 1, PrinterName = "Máy Thu Ngân", ConnectionType = "USB", ConnectionString = "XP-80C", IsBillPrinter = true },
                new Printer { PrinterID = 2, PrinterName = "Máy Bar", ConnectionType = "LAN", ConnectionString = "192.168.1.201", IsBillPrinter = false },
                new Printer { PrinterID = 3, PrinterName = "Máy Bếp", ConnectionType = "LAN", ConnectionString = "192.168.1.202", IsBillPrinter = false }
            );
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
                new Dish { DishID = 3, DishName = "Sinh tố bơ", Price = 40000, Unit = "Ly", CategoryID = 2, ImagePath = "stbo.png" },
                new Dish { DishID = 4, DishName = "Khoai tây chiên", Price = 30000, Unit = "Dĩa", CategoryID = 3, ImagePath = "khoaitay.png" }
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
            // Seed Template Mặc định
            string defaultJson = "[{\"ElementType\":\"Text\",\"Content\":\"NHÀ HÀNG DEMO\",\"FontSize\":24,\"IsBold\":true,\"Align\":\"Center\",\"IsVisible\":true},{\"ElementType\":\"Separator\",\"Content\":\"\",\"FontSize\":14,\"IsBold\":false,\"Align\":\"Center\",\"IsVisible\":true},{\"ElementType\":\"OrderDetails\",\"Content\":\"\",\"FontSize\":14,\"IsBold\":false,\"Align\":\"Center\",\"IsVisible\":true},{\"ElementType\":\"Separator\",\"Content\":\"\",\"FontSize\":14,\"IsBold\":false,\"Align\":\"Center\",\"IsVisible\":true},{\"ElementType\":\"Total\",\"Content\":\"\",\"FontSize\":14,\"IsBold\":false,\"Align\":\"Center\",\"IsVisible\":true}]";

            modelBuilder.Entity<PrintTemplate>().HasData(
                new PrintTemplate
                {
                    TemplateID = 1,
                    TemplateName = "Mẫu chuẩn",
                    TemplateType = "Bill",
                    TemplateContentJson = defaultJson,
                    IsActive = true
                }
            );

        }

    }
}