# Assets

## App icon (logo)
- Tạo file: `Assets/app.ico`
- Khuyến nghị: icon multi-size (16, 32, 48, 256).

Sau khi có `Assets/app.ico`:
- Build sẽ tự dùng icon này làm icon cho `.exe` (không cần sửa code).
- Installer Inno Setup cũng tự dùng icon này cho file setup.

### Gợi ý tạo .ico
- Nếu bạn có logo `.png`, hãy convert sang `.ico` bằng:
  - một tool online (convert PNG → ICO)
  - hoặc phần mềm như IrfanView / GIMP / ImageMagick.
