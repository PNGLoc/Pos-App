using Microsoft.EntityFrameworkCore;
using PosSystem.Main.Models;
using System;
using System.IO;
using PosSystem.Main.Helpers;

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
        public DbSet<ActivityLogEntry> ActivityLogs { get; set; }
        public DbSet<Expense> Expenses { get; set; } // [NEW] Expense Table
        public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            AppPaths.EnsureInitialized();
            optionsBuilder.UseSqlite($"Data Source={AppPaths.DbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Seed dữ liệu mẫu cho máy in
            modelBuilder.Entity<Printer>().HasData(
                new Printer { PrinterID = 1, PrinterName = "Máy Thu Ngân", ConnectionType = "USB", ConnectionString = "BILL", IsBillPrinter = true, BeepOnPrint = false, BeepCount = 0 },
                new Printer { PrinterID = 2, PrinterName = "Máy Pha Chế", ConnectionType = "USB", ConnectionString = "PHACHE", IsBillPrinter = false, BeepOnPrint = false, BeepCount = 0 },
                new Printer { PrinterID = 3, PrinterName = "Máy Bếp Mì", ConnectionType = "LAN", ConnectionString = "192.168.1.202", IsBillPrinter = false, BeepOnPrint = false, BeepCount = 0 },
                new Printer { PrinterID = 4, PrinterName = "Máy Bếp Bánh", ConnectionType = "LAN", ConnectionString = "192.168.1.203", IsBillPrinter = false, BeepOnPrint = false, BeepCount = 0 }
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
   }
);
            modelBuilder.Entity<Table>().HasData(
     // Dine-in (CategoryID = 1)
     new Table { TableID = 1, TableName = "Bàn 1", CategoryID = 1 },
     new Table { TableID = 2, TableName = "Bàn 2", CategoryID = 1 },
     new Table { TableID = 3, TableName = "Bàn 3", CategoryID = 1 },
     new Table { TableID = 4, TableName = "Bàn 4", CategoryID = 1 },
     new Table { TableID = 5, TableName = "Bàn 5", CategoryID = 1 },
     new Table { TableID = 6, TableName = "Bàn 6", CategoryID = 1 },
     new Table { TableID = 7, TableName = "Bàn 7", CategoryID = 1 },
     new Table { TableID = 8, TableName = "Bàn 8", CategoryID = 1 },
     new Table { TableID = 9, TableName = "Bàn 9", CategoryID = 1 },
     new Table { TableID = 10, TableName = "Bàn 10", CategoryID = 1 },
     new Table { TableID = 11, TableName = "Bàn 11", CategoryID = 1 },
     new Table { TableID = 12, TableName = "Bàn 12", CategoryID = 1 },
     new Table { TableID = 13, TableName = "Bàn 13", CategoryID = 1 },
     new Table { TableID = 14, TableName = "Bàn 14", CategoryID = 1 },
     new Table { TableID = 15, TableName = "Bàn 15", CategoryID = 1 },
     new Table { TableID = 16, TableName = "Bàn 16", CategoryID = 1 },
     new Table { TableID = 17, TableName = "Bàn 17", CategoryID = 1 },
     new Table { TableID = 18, TableName = "Bàn 18", CategoryID = 1 },
     new Table { TableID = 19, TableName = "Bàn 19", CategoryID = 1 },
     new Table { TableID = 20, TableName = "Bàn 20", CategoryID = 1 },
     new Table { TableID = 21, TableName = "Bàn 21", CategoryID = 1 },
     new Table { TableID = 22, TableName = "Bàn 22", CategoryID = 1 },
     new Table { TableID = 23, TableName = "Bàn 23", CategoryID = 1 },
     new Table { TableID = 24, TableName = "Bàn 24", CategoryID = 1 },
     new Table { TableID = 25, TableName = "Bàn 25", CategoryID = 1 },
     new Table { TableID = 26, TableName = "Bàn 26", CategoryID = 1 },
     new Table { TableID = 27, TableName = "Bàn 27", CategoryID = 1 },
     new Table { TableID = 28, TableName = "Bàn 28", CategoryID = 1 },
     new Table { TableID = 29, TableName = "Bàn 29", CategoryID = 1 },
     new Table { TableID = 30, TableName = "Bàn 30", CategoryID = 1 },
     new Table { TableID = 31, TableName = "Bàn 31", CategoryID = 1 },
     new Table { TableID = 32, TableName = "Bàn 32", CategoryID = 1 },
     new Table { TableID = 33, TableName = "Bàn 33", CategoryID = 1 },
     new Table { TableID = 34, TableName = "Bàn 34", CategoryID = 1 },
     new Table { TableID = 35, TableName = "Bàn 35", CategoryID = 1 },
     // Mang về (CategoryID = 2)
     new Table { TableID = 36, TableName = "Mang về 1", CategoryID = 2 },
     new Table { TableID = 37, TableName = "Mang về 2", CategoryID = 2 },
     new Table { TableID = 38, TableName = "Mang về 3", CategoryID = 2 },
     new Table { TableID = 39, TableName = "Mang về 4", CategoryID = 2 },
     new Table { TableID = 40, TableName = "Mang về 5", CategoryID = 2 },
     new Table { TableID = 41, TableName = "Mang về 6", CategoryID = 2 },
     new Table { TableID = 42, TableName = "Mang về Trụn 1", CategoryID = 2 },
     new Table { TableID = 43, TableName = "Mang về Trụn 2", CategoryID = 2 },
     new Table { TableID = 44, TableName = "Mang về Trụn 3", CategoryID = 2 },
     new Table { TableID = 45, TableName = "Mang về Trụn 4", CategoryID = 2 },
     new Table { TableID = 46, TableName = "Mang về Trụn 5", CategoryID = 2 },
     new Table { TableID = 47, TableName = "Mang về Trụn 6", CategoryID = 2 },
     // Khách Lấy (CategoryID = 3)
     new Table { TableID = 48, TableName = "Khách Lấy 1", CategoryID = 3 },
     new Table { TableID = 49, TableName = "Khách Lấy 2", CategoryID = 3 },
     new Table { TableID = 50, TableName = "Khách Lấy 3", CategoryID = 3 },
     new Table { TableID = 51, TableName = "Khách Lấy 4", CategoryID = 3 },
     new Table { TableID = 52, TableName = "Khách Lấy 5", CategoryID = 3 },
     new Table { TableID = 53, TableName = "Khách Lấy 6", CategoryID = 3 },
     new Table { TableID = 54, TableName = "Khách Lấy Trụn 1", CategoryID = 3 },
     new Table { TableID = 55, TableName = "Khách Lấy Trụn 2", CategoryID = 3 },
     new Table { TableID = 56, TableName = "Khách Lấy Trụn 3", CategoryID = 3 },
     new Table { TableID = 57, TableName = "Khách Lấy Trụn 4", CategoryID = 3 },
     new Table { TableID = 58, TableName = "Khách Lấy Trụn 5", CategoryID = 3 },
     new Table { TableID = 59, TableName = "Khách Lấy Trụn 6", CategoryID = 3 },
     // Ship (CategoryID = 4)
     new Table { TableID = 60, TableName = "Ship 1", CategoryID = 4 },
     new Table { TableID = 61, TableName = "Ship 2", CategoryID = 4 },
     new Table { TableID = 62, TableName = "Ship 3", CategoryID = 4 },
     new Table { TableID = 63, TableName = "Ship 4", CategoryID = 4 },
     new Table { TableID = 64, TableName = "Ship 5", CategoryID = 4 },
     new Table { TableID = 65, TableName = "Ship 6", CategoryID = 4 },
     new Table { TableID = 66, TableName = "Ship Trụn 1", CategoryID = 4 },
     new Table { TableID = 67, TableName = "Ship Trụn 2", CategoryID = 4 },
     new Table { TableID = 68, TableName = "Ship Trụn 3", CategoryID = 4 },
     new Table { TableID = 69, TableName = "Ship Trụn 4", CategoryID = 4 },
     new Table { TableID = 70, TableName = "Ship Trụn 5", CategoryID = 4 },
     new Table { TableID = 71, TableName = "Ship Trụn 6", CategoryID = 4 }

 );

            // Seed Table Categories
            modelBuilder.Entity<TableCategory>().HasData(
                new TableCategory { CategoryID = 1, DisplayOrder = 1, CategoryName = "Bàn Thường", Description = "Bàn tiêu chuẩn", BorderColorHex = "#6598C7", IconClass = "fas fa-chair" },
                new TableCategory { CategoryID = 2, DisplayOrder = 2, CategoryName = "Mang Về", Description = "Khách mang về", BorderColorHex = "#69cf6e", IconClass = "fas fa-shopping-bag" },
                new TableCategory { CategoryID = 3, DisplayOrder = 3, CategoryName = "Khách Lấy", Description = "Khách đến mang đi", BorderColorHex = "#cfc569", IconClass = "fas fa-walking" },
                new TableCategory { CategoryID = 4, DisplayOrder = 4, CategoryName = "Ship", Description = "Giao hàng tận nơi", BorderColorHex = "#a169cf", IconClass = "fas fa-motorcycle" }
            );
            // Seed Template Mặc định
            string defaultJson = "[{\"ElementType\":\"Text\",\"Content\":\"ITADA LONG M\\u1EF8\",\"Align\":\"Center\",\"FontSize\":50,\"IsBold\":true,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"\\u0110C: CMT8, TT.Long M\\u1EF9, Long M\\u1EF9, H\\u1EADu Giang\",\"Align\":\"Left\",\"FontSize\":24,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"SeparatorDashed\",\"Content\":\"- - - - - - - -\",\"Align\":\"Left\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"M\\u00E3 \\u0111\\u01A1n: #{OrderId}\",\"Align\":\"Left\",\"FontSize\":22,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"B\\u00E0n: {Table}\",\"Align\":\"Left\",\"FontSize\":24,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Gi\\u1EDD \\u0111\\u1EBFn: {CheckInTime} | Gi\\u1EDD in: {PrintTime}\",\"Align\":\"Left\",\"FontSize\":24,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Separator\",\"Content\":\"\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"OrderDetails\",\"Content\":\"HeaderSize=28;ItemSize=28;ShowNote=False;NoteSize=26;ColumnSpacing=10;ItemSep=True;SepStyle=Dashed\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"Total\",\"Content\":\"ShowSub=True;ShowDisc=True;TotalHeaderSize=30;SubSize=28\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":true,\"IsVisible\":true,\"QRTextTop\":\"\",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":12,\"QRTextBottomFontSize\":12,\"QRTextTopBold\":true,\"QRTextBottomBold\":true,\"ImageHeight\":300},{\"ElementType\":\"QRCode\",\"Content\":\"QRCode\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"QRTextTop\":\"Qu\\u00E9t m\\u00E3 QR b\\u00EAn d\\u01B0\\u1EDBi \\u0111\\u1EC3 chuy\\u1EC3n kho\\u1EA3n \",\"QRTextBottom\":\"\",\"QRTextTopFontSize\":26,\"QRTextBottomFontSize\":26,\"QRTextTopBold\":false,\"QRTextBottomBold\":false,\"ImageHeight\":200}]";
            string defaultKitchenJson = "[{\"ElementType\":\"Text\",\"Content\":\"{Table}\",\"Align\":\"Center\",\"FontSize\":55,\"IsBold\":true,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"\\u0110\\u1EE2T {Batch}\",\"Align\":\"Left\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Ng\\u01B0\\u1EDDi g\\u1EEDi: {Sender}\",\"Align\":\"Left\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Separator\",\"Content\":\"\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"KitchenOrderDetails\",\"Content\":\"HeaderSize=30;ItemSize=30;NoteSize=28;ColumnSpacing=10;ItemSep=True;SepStyle=Dashed\",\"Align\":\"Center\",\"FontSize\":14,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300},{\"ElementType\":\"Text\",\"Content\":\"Gi\\u1EDD in: {PrintTime}\",\"Align\":\"Center\",\"FontSize\":26,\"IsBold\":false,\"IsVisible\":true,\"ImageHeight\":300}]";
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
                    TemplateContentJson = defaultKitchenJson,
                    IsActive = true
                }
            );

            modelBuilder.Entity<IdempotencyRecord>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.HasIndex(e => e.CreatedAt);
            });

        }

    }
}