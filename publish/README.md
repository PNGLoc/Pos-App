# POS System - Installation Guide

## Cách cài đặt và chạy

### 1. Sao chép toàn bộ thư mục `publish` vào máy tính đích
- Copy toàn bộ thư mục `publish` từ `c:\Users\PNGLoc\Desktop\App-Pos\PosSystem\publish`
- Paste vào vị trí mong muốn trên máy tính đích (ví dụ: `C:\Program Files\POS System\`)

### 2. Chạy ứng dụng
- Double-click vào file `Run_POS_System.bat` để khởi động ứng dụng
- Hoặc chạy trực tiếp `PosSystem.Main.exe`

## Yêu cầu hệ thống
- Windows 10/11 (64-bit)
- .NET Runtime 10.0 (đã được bao gồm trong publish)

## Tính năng chính
- Quản lý bàn ăn
- Đặt món và gửi bếp
- Thanh toán hóa đơn
- In hóa đơn và bếp
- Chuyển bàn và tách bàn
- Hỗ trợ máy in nhiệt ESC-POS

## Lưu ý
- Đảm bảo máy in nhiệt được kết nối và cấu hình đúng
- Database SQLite sẽ được tạo tự động khi chạy lần đầu
- Sử dụng tài khoản Admin để truy cập các tính năng quản trị

## Hỗ trợ
Nếu gặp vấn đề, kiểm tra:
1. Máy in có được kết nối không
2. Port máy in có đúng không (mặc định: TCP 9100)
3. Firewall không chặn kết nối