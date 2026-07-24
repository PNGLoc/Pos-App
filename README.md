# POS System

POS System là phần mềm quản lý bán hàng (Point of Sale) kiến trúc Hybrid, được phát triển trên nền tảng .NET WPF kết hợp Embedded Web Server. Hệ thống cho phép thu ngân quản lý tại quầy qua giao diện Desktop, đồng thời hỗ trợ nhân viên phục vụ order trực tiếp tại bàn qua thiết bị di động trong mạng nội bộ (LAN) với dữ liệu đồng bộ thời gian thực qua SignalR.

## Kiến trúc hệ thống

Ứng dụng Desktop đóng vai trò là máy chủ trung tâm (Host Server), tự chứa cơ sở dữ liệu và xử lý các kết nối từ client di động:

[ Mobile/Tablet Client ] <--- (HTTP / SignalR) ---> [ WPF Desktop App / Embedded Web Server ] <---> [ SQLite DB ]
                                                                      |
                                                                      +---> [ ESC/POS Thermal Printer ]

## Tính năng chính

* **Quản lý vận hành (Desktop App):** Quản lý thực đơn, thiết lập sơ đồ bàn, quy tắc giá động (Price Rules), theo dõi thu chi và chấm công nhân viên.
* **Order tại bàn (Mobile Access):** Tích hợp sẵn Web Server nhúng cho phép các thiết bị di động truy cập giao diện order qua trình duyệt mà không cần cài đặt thêm server ngoại vi.
* **Đồng bộ thời gian thực:** Mọi thao tác order từ thiết bị di động hoặc cập nhật từ bếp được đồng bộ tức thì tới máy thu ngân thông qua ASP.NET Core SignalR.
* **Tích hợp phần cứng & Báo cáo:** Giao tiếp trực tiếp với máy in nhiệt qua giao thức ESC/POS để in hóa đơn và phiếu bếp; hỗ trợ xuất báo cáo doanh thu và bảng công ra định dạng Excel (.xlsx).
* **Tối ưu hiệu năng:** Xử lý triệt để tình trạng trùng lặp đơn hàng (Double-posting) khi mạng chập chờn bằng cơ chế Idempotency; tối ưu hiển thị danh sách lớn với VirtualizingWrapPanel.

## Công nghệ sử dụng

* **Core:** .NET C#, Windows Presentation Foundation (WPF), XAML
* **Database & ORM:** SQLite, Entity Framework Core
* **Real-time & Network:** ASP.NET Core SignalR, Embedded HTTP Web Server
* **Hardware & Reports:** ESC/POS Protocol, Raw Printer Helper, Excel Service
* **Frontend (Mobile):** HTML5, CSS3, Vanilla JavaScript

## Hướng dẫn cài đặt & Chạy ứng dụng
* Tải bộ cài đặt **`LP_Pos-Setup.exe`** tại thư mục `/installer` của kho lưu trữ này hoặc tải trực tiếp tại đây:
  **[Tải xuống LP_Pos-Setup.exe (v1.0.0)](https://github.com/PNGLoc/Pos-App/releases/download/v1.0.0/LP_Pos-Setup.exe)**.
* Chạy bộ cài đặt và khởi động phần mềm từ Desktop. Hệ thống sẽ tự động khởi tạo CSDL SQLite và bật Web Server nhúng.
* Tài khoản admin mặc định: user name **`admin`** , password `123`.
* Sau khi cài đặt thành công ứng dụng, vào setting để thiết lập các phần còn lại như máy in, menu, danh sách bàn, tạo tài khoản cho nhân viên...

## Kết nối thiết bị di động
* Đảm bảo thiết bị di động kết nối cùng mạng Wi-Fi/LAN với máy tính chạy phần mềm.
* Mở trình duyệt trên thiết bị di động và truy cập vào địa chỉ IP của máy tính kèm cổng dịch vụ (Ví dụ: `http://192.168.1.100:5000)`, sau đó login với account đã được admin tạo.
  <img width="693" height="350" alt="image" src="https://github.com/user-attachments/assets/687774d9-258a-4dce-b996-e59d0626ff29" />
* Thao tác đặt món trên giao diện mobile và quan sát dữ liệu đồng bộ về màn hình Desktop.
