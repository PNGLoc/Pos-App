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
        public DbSet<TableCategory> TableCategories { get; set; }
        public DbSet<Table> Tables { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<Printer> Printers { get; set; }
        public DbSet<PrintTemplate> PrintTemplates { get; set; }
        public DbSet<CancelledLog> CancelledLogs { get; set; }
        public DbSet<DishPriceRule> DishPriceRules { get; set; }
        public DbSet<GlobalSetting> GlobalSettings { get; set; }
        public DbSet<PriceRuleType> PriceRuleTypes { get; set; }
        public DbSet<Employee> Employees { get; set; }
        public DbSet<TimeLog> TimeLogs { get; set; }
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
                new Printer { PrinterID = 2, PrinterName = "Máy Pha Chế", ConnectionType = "LAN", ConnectionString = "192.168.1.201", IsBillPrinter = false },
                new Printer { PrinterID = 3, PrinterName = "Máy Bếp Mì", ConnectionType = "LAN", ConnectionString = "192.168.1.202", IsBillPrinter = false },
                new Printer { PrinterID = 4, PrinterName = "Máy Bếp Bánh", ConnectionType = "LAN", ConnectionString = "192.168.1.203", IsBillPrinter = false }
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
   new Account
   {
       AccID = 1,
       AccName = "Admin",
       Username = "admin",
       AccPass = "123",
       AccRole = "Admin",
       CanMoveTable = true,
       CanPayment = true,
       CanCancelItem = true, // Admin full quyền
       CanPrintProvisional = true
   },
   new Account
   {
       AccID = 2,
       AccName = "Nhân viên 1",
       Username = "nv1",
       AccPass = "123",
       AccRole = "Staff",
       CanMoveTable = false,
       CanPayment = false,
       CanCancelItem = false // NV hạn chế
   }
);
            modelBuilder.Entity<Table>().HasData(
                new Table { TableID = 1, TableName = "Bàn 1", TableType = "DineIn", CategoryID = 1 },
                new Table { TableID = 2, TableName = "Bàn 2", TableType = "DineIn", CategoryID = 1 }
            );

            // Seed Table Categories
            modelBuilder.Entity<TableCategory>().HasData(
                new TableCategory { CategoryID = 1, CategoryName = "Bàn Thường", Description = "Bàn tiêu chuẩn" },
                new TableCategory { CategoryID = 2, CategoryName = "Bàn VIP", Description = "Phòng lạnh, ghế sofa" },
                new TableCategory { CategoryID = 3, CategoryName = "Mang Về", Description = "Khách mang đi" },
                new TableCategory { CategoryID = 4, CategoryName = "Ship", Description = "Giao hàng tận nơi" }
            );
            // Seed Template Mặc định
            string defaultJson = "[{\"ElementType\":\"Text\",\"Content\":\"ITADA LONG M\\u1EF8\",\"Align\":\"Center\",\"FontSize\":50,\"IsBold\":true,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"\\u0110C: CMT8, TT.Long M\\u1EF9, Long M\\u1EF9, H\\u1EADu Giang\",\"Align\":\"Left\",\"FontSize\":24,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"B\\u00E0n: {Table}\",\"Align\":\"Left\",\"FontSize\":28,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Gi\\u1EDD \\u0111\\u1EBFn: {CheckInTime} | Gi\\u1EDD in: {PrintTime}\",\"Align\":\"Left\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Separator\",\"Content\":\"\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"OrderDetails\",\"Content\":\"HeaderSize=28;ItemSize=28;ShowNote=False;NoteSize=26;ColumnSpacing=10;ItemSep=True;SepStyle=Dashed\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Total\",\"Content\":\"ShowSub=True;ShowDisc=True;TotalHeaderSize=30;SubSize=28\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":true,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Logo\",\"Content\":\"Logo\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":200}]";

            modelBuilder.Entity<PrintTemplate>().HasData(
                new PrintTemplate
                {
                    TemplateID = 1,
                    TemplateName = "Mẫu chuẩn",
                    TemplateType = "Bill",
                    TemplateContentJson = defaultJson,
                    IsActive = true
                },
                new PrintTemplate
                {
                    TemplateID = 2,
                    TemplateName = "Mẫu bếp chuẩn",
                    TemplateType = "Kitchen",
                    TemplateContentJson = "[{\"ElementType\":\"Text\",\"Content\":\"{Table}\",\"Align\":\"Center\",\"FontSize\":55,\"IsBold\":true,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\" \\u0110\\u1EE2T {Batch}\",\"Align\":\"Left\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Ng\\u01B0\\u1EDDi g\\u1EEDi: {Sender}\",\"Align\":\"Left\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Separator\",\"Content\":\"\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"KitchenOrderDetails\",\"Content\":\"HeaderSize=30;ItemSize=30;NoteSize=28;ColumnSpacing=10;ItemSep=True;SepStyle=Dashed\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Gi\\u1EDD in: {PrintTime}\",\"Align\":\"Center\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300}]",
                    IsActive = true
                }
            );

        }

    }
}