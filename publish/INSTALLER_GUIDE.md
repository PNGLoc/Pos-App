# Tạo File EXE Installer cho POS System

## 📋 Yêu cầu

Để tạo file `.exe` cài đặt chuyên nghiệp, bạn cần cài đặt **Inno Setup Compiler**:

### Bước 1: Tải và cài đặt Inno Setup
1. Truy cập: https://jrsoftware.org/isdl.php
2. Tải bản mới nhất (hiện tại là Inno Setup 6.3.0)
3. Cài đặt bình thường

### Bước 2: Biên dịch installer
1. Mở **Inno Setup Compiler** (ISCmplr.exe hoặc Inno Setup IDE)
2. Mở file: `PosSystemInstaller.iss` (ở thư mục `publish`)
3. Click **Build** → **Compile**
4. Đợi quá trình hoàn tất (khoảng 1-2 phút)

### Kết quả
- File `PosSystem_Setup.exe` sẽ được tạo trong thư mục `publish`
- File này có thể được phân phối cho người dùng cuối

## 🎯 Cách sử dụng installer

### Trên máy người dùng:
1. Double-click `PosSystem_Setup.exe`
2. Chọn ngôn ngữ (English hoặc Vietnamese)
3. Chọn thư mục cài đặt (mặc định: `C:\Program Files\POS System`)
4. Click **Install**
5. App sẽ tự động khởi động sau khi cài đặt

## ✨ Tính năng của installer

✅ Tạo shortcut trên Desktop  
✅ Tạo entry trong Start Menu  
✅ Tạo Uninstall program  
✅ Hỗ trợ ngôn ngữ Tiếng Việt  
✅ Tự động khởi động app sau cài đặt  
✅ Kiểm tra quyền Administrator  
✅ Hỗ trợ Windows 64-bit  

## 📝 Tùy chỉnh installer

Nếu bạn muốn chỉnh sửa thông tin installer, mở file `PosSystemInstaller.iss` và sửa:
- `AppName` - Tên ứng dụng
- `AppVersion` - Phiên bản
- `AppPublisher` - Tên công ty/nhà hàng
- `DefaultDirName` - Thư mục cài đặt mặc định

## ⚙️ Tùy chọn khác

Nếu bạn không muốn cài Inno Setup, bạn có thể sử dụng:

### Option 1: Sử dụng PowerShell installer (đã có sẵn)
```powershell
.\Install_POS_System.ps1
```

### Option 2: Copy thủ công
- Copy toàn bộ thư mục `publish` vào `C:\Program Files\POS System`
- Tạo shortcut bằng tay

## 🆘 Gặp vấn đề?

Nếu installer không tạo được, kiểm tra:
1. ✓ Inno Setup đã được cài đặt đúng không?
2. ✓ File `PosSystemInstaller.iss` có ở thư mục `publish` không?
3. ✓ Các file trong thư mục `publish` có đầy đủ không?