namespace PosSystem.Main
{
    public static class UserSession
    {
        public static int AccID { get; set; }
        public static string AccName { get; set; } = "";
        public static string AccRole { get; set; } = ""; // Admin, Staff

        // Hàm kiểm tra xem đã login chưa
        public static bool IsLoggedIn => AccID > 0;

        // Hàm đăng xuất
        public static void Clear()
        {
            AccID = 0;
            AccName = "";
            AccRole = "";
        }
    }
}