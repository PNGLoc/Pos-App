using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PosSystem.Main.Migrations
{
    /// <inheritdoc />
    public partial class FinalInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccName = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    AccPass = table.Column<string>(type: "TEXT", nullable: false),
                    AccRole = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccID);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CategoryName = table.Column<string>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryID);
                });

            migrationBuilder.CreateTable(
                name: "PrintTemplate",
                columns: table => new
                {
                    TemplateID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TemplateName = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateType = table.Column<string>(type: "TEXT", nullable: false),
                    PaperSize = table.Column<int>(type: "INTEGER", nullable: false),
                    LayoutConfig = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintTemplate", x => x.TemplateID);
                });

            migrationBuilder.CreateTable(
                name: "Tables",
                columns: table => new
                {
                    TableID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TableName = table.Column<string>(type: "TEXT", nullable: false),
                    TableType = table.Column<string>(type: "TEXT", nullable: false),
                    TableStatus = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tables", x => x.TableID);
                });

            migrationBuilder.CreateTable(
                name: "Dishes",
                columns: table => new
                {
                    DishID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DishName = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    DishType = table.Column<string>(type: "TEXT", nullable: false),
                    DishStatus = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dishes", x => x.DishID);
                    table.ForeignKey(
                        name: "FK_Dishes_Categories_CategoryID",
                        column: x => x.CategoryID,
                        principalTable: "Categories",
                        principalColumn: "CategoryID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    OrderID = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CheckoutTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OrderStatus = table.Column<string>(type: "TEXT", nullable: false),
                    SubTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    FinalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    TableID = table.Column<int>(type: "INTEGER", nullable: true),
                    AccID = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.OrderID);
                    table.ForeignKey(
                        name: "FK_Orders_Accounts_AccID",
                        column: x => x.AccID,
                        principalTable: "Accounts",
                        principalColumn: "AccID");
                    table.ForeignKey(
                        name: "FK_Orders_Tables_TableID",
                        column: x => x.TableID,
                        principalTable: "Tables",
                        principalColumn: "TableID");
                });

            migrationBuilder.CreateTable(
                name: "OrderDetails",
                columns: table => new
                {
                    OrderDetailID = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    ItemStatus = table.Column<string>(type: "TEXT", nullable: false),
                    OrderID = table.Column<long>(type: "INTEGER", nullable: false),
                    DishID = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDetails", x => x.OrderDetailID);
                    table.ForeignKey(
                        name: "FK_OrderDetails_Dishes_DishID",
                        column: x => x.DishID,
                        principalTable: "Dishes",
                        principalColumn: "DishID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderDetails_Orders_OrderID",
                        column: x => x.OrderID,
                        principalTable: "Orders",
                        principalColumn: "OrderID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "AccID", "AccName", "AccPass", "AccRole", "Username" },
                values: new object[,]
                {
                    { 1, "Admin", "123", "Admin", "admin" },
                    { 2, "Nhân viên 1", "123", "Staff", "nv1" }
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "CategoryID", "CategoryName", "OrderIndex" },
                values: new object[,]
                {
                    { 1, "Cà phê", 1 },
                    { 2, "Sinh tố & Nước ép", 2 },
                    { 3, "Đồ ăn vặt", 3 }
                });

            migrationBuilder.InsertData(
                table: "PrintTemplate",
                columns: new[] { "TemplateID", "IsActive", "LayoutConfig", "PaperSize", "TemplateName", "TemplateType" },
                values: new object[,]
                {
                    { 1, true, "\r\n        {\r\n            \"Header\": {\r\n                \"ShowLogo\": true,\r\n                \"ShopNameSize\": 14,\r\n                \"AddressSize\": 10\r\n            },\r\n            \"Body\": {\r\n                \"ShowNo\": true,\r\n                \"ShowPrice\": true\r\n            },\r\n            \"Footer\": {\r\n                \"ShowWifi\": true,\r\n                \"ShowQr\": true,\r\n                \"EndMessage\": \"Xin cảm ơn và hẹn gặp lại!\"\r\n            }\r\n        }", 80, "Mẫu Hóa Đơn Chuẩn (80mm)", "Bill" },
                    { 2, true, "{ \"Header\": { \"ShowShopName\": false }, \"Body\": { \"FontSize\": 14 } }", 80, "Mẫu Bếp (Rút gọn)", "Kitchen" }
                });

            migrationBuilder.InsertData(
                table: "Tables",
                columns: new[] { "TableID", "TableName", "TableStatus", "TableType" },
                values: new object[,]
                {
                    { 1, "Bàn 1", "Empty", "DineIn" },
                    { 2, "Bàn 2", "Empty", "DineIn" }
                });

            migrationBuilder.InsertData(
                table: "Dishes",
                columns: new[] { "DishID", "CategoryID", "DishName", "DishStatus", "DishType", "ImagePath", "Price", "Unit" },
                values: new object[,]
                {
                    { 1, 1, "Cà phê đen", "Active", "Food", "cfden.png", 20000m, "Ly" },
                    { 2, 1, "Cà phê sữa", "Active", "Food", "cfsua.png", 25000m, "Ly" },
                    { 3, 2, "Sinh tố bơ", "Active", "Drink", "stbo.png", 40000m, "Ly" },
                    { 4, 3, "Khoai tây chiên", "Active", "Food", "khoaitay.png", 30000m, "Dĩa" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dishes_CategoryID",
                table: "Dishes",
                column: "CategoryID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_DishID",
                table: "OrderDetails",
                column: "DishID");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_OrderID",
                table: "OrderDetails",
                column: "OrderID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AccID",
                table: "Orders",
                column: "AccID");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TableID",
                table: "Orders",
                column: "TableID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderDetails");

            migrationBuilder.DropTable(
                name: "PrintTemplate");

            migrationBuilder.DropTable(
                name: "Dishes");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Tables");
        }
    }
}
