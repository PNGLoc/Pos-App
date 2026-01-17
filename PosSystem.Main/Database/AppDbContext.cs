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
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            AppPaths.EnsureInitialized();
            optionsBuilder.UseSqlite($"Data Source={AppPaths.DbPath}");
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
                new Table { TableID = 1, TableName = "Bàn 1", TableType = "DineIn", CategoryID = 1 },
                new Table { TableID = 2, TableName = "Bàn 2", TableType = "DineIn", CategoryID = 1 }
            );

            // Seed Table Categories
            modelBuilder.Entity<TableCategory>().HasData(
                new TableCategory { CategoryID = 1, DisplayOrder = 1, CategoryName = "Bàn Thường", Description = "Bàn tiêu chuẩn" },
                new TableCategory { CategoryID = 2, DisplayOrder = 2, CategoryName = "Mang Về", Description = "Khách mang về" },
                new TableCategory { CategoryID = 3, DisplayOrder = 3, CategoryName = "Khách Lấy", Description = "Khách đến mang đi" },
                new TableCategory { CategoryID = 4, DisplayOrder = 4, CategoryName = "Ship", Description = "Giao hàng tận nơi" }
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

        }

    }
}