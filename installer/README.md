# PosSystem Installer (Inno Setup)

Mục tiêu:
- Cho khách chọn thư mục cài (default: `C:\\PosSystem`).
- Database + hình ảnh lưu trong `data` nằm ngay dưới thư mục cài đặt.

## 1) Publish trước khi đóng gói
Chạy trong thư mục solution:

```powershell
cd "C:\Users\PNGLoc\Desktop\App-Pos\PosSystem"

dotnet publish .\PosSystem.Main\PosSystem.Main.csproj -c Release -r win-x64 --self-contained true -o .\PosSystem.Main\bin\Release\net10.0-windows\publish
```

Ghi chú:
- Nếu bạn muốn nhẹ hơn, có thể bỏ `--self-contained true` nhưng máy khách phải có .NET runtime phù hợp.

## 2) Build installer
1. Cài Inno Setup 6+.
2. Mở file: `installer\\PosSystem.iss`
3. Bấm **Compile**.

File output mặc định: `installer\\PosSystem-Setup.exe`

## 3) Cấu trúc dữ liệu runtime
Ứng dụng sẽ tự tạo và sử dụng:
- `data\\pos_data.db`
- `data\\image\\...`

nằm ngay dưới thư mục `{app}` (thư mục cài đặt).
