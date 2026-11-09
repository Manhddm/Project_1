# SmartMedBox backup/restore runbook

Phạm vi runbook này là CSDL SQL Server và vùng private storage của prescription images. Không có bước nào tự xóa file mồ côi, xóa metadata, hoặc thay đổi retention. `D-04`, `D-16`, `D-17` và `D-19` vẫn là TBD theo tài liệu dự án.

## Nguyên tắc bắt buộc

- Backup CSDL phải bật checksum và mã hóa AES-256 bằng certificate của SQL Server. Certificate và private key phải được backup riêng vào secret store/vùng mã hóa khác; không commit hoặc ghi password vào repository.
- Private storage phải nằm trên encrypted volume hoặc object storage có server-side encryption/KMS. Khi truyền backup ra ngoài máy, dùng HTTPS/TLS.
- Backup phải bao gồm cả CSDL và toàn bộ `FileStorage:RootPath`. Không coi SQL backup là đủ vì CSDL chỉ giữ metadata và storage key.
- Chạy `reconcile-files` sau restore để phát hiện metadata thiếu file, file mồ côi, tên file không hợp lệ và storage key trùng. Lệnh chỉ đọc.

## Backup

Đặt các giá trị nhạy cảm qua secret manager hoặc biến môi trường trong phiên vận hành, không ghi vào shell history. Certificate `SmartMedBoxBackupCertificate` phải tồn tại trên SQL Server trước khi chạy lệnh:

```sql
BACKUP DATABASE [SmartMedBox]
TO DISK = N'<encrypted-backup-volume>\SmartMedBox-<UTC timestamp>.bak'
WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT,
     ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = [SmartMedBoxBackupCertificate]);
GO
RESTORE VERIFYONLY
FROM DISK = N'<encrypted-backup-volume>\SmartMedBox-<UTC timestamp>.bak'
WITH CHECKSUM;
GO
```

Backup private storage bằng công cụ đã được phê duyệt có client-side encryption, ví dụ `restic`:

```powershell
$env:RESTIC_PASSWORD_FILE = '<secret-file-outside-repository>'
restic -r '<encrypted-restic-repository>' backup '<FileStorage:RootPath>' --tag smartmedbox-prescription-files
restic -r '<encrypted-restic-repository>' check
```

Repository remote phải dùng HTTPS/TLS. Nếu dùng encrypted volume/object storage thay cho `restic`, vẫn phải kiểm tra encryption-at-rest và transport encryption trong cấu hình nhà cung cấp trước khi coi backup hợp lệ.

## Restore vào môi trường sạch

1. Tạo database đích rỗng và restore `.bak` sau khi đã khôi phục certificate/private key dùng cho backup encryption.
2. Chạy `RESTORE VERIFYONLY ... WITH CHECKSUM` trước khi mở ứng dụng.
3. Khôi phục private storage vào thư mục riêng, giữ nguyên tên storage key; không đổi tên hoặc flatten thư mục ngoài layout mà `FileStorage` hỗ trợ.
4. Đặt `ConnectionStrings__Default` trỏ tới database đích và `FileStorage__RootPath` trỏ tới thư mục đã khôi phục.
5. Chạy reconciliation:

```powershell
$env:ConnectionStrings__Default = '<restored-sql-server-connection-string>'
$env:FileStorage__RootPath = '<restored-private-storage-root>'
dotnet run --project tools/SmartMedBox.Provisioning -- reconcile-files
```

6. Chỉ mở traffic sau khi lệnh trả exit code `0`, không có finding, và kiểm tra thủ công một ảnh đại diện qua API owner-only. Exit code `1` nghĩa là có finding cần điều tra; không tự xóa hoặc tự sửa để làm sạch.
7. Ghi kết quả vào `docs/backup-restore-test-report.md` hoặc hệ thống vận hành tương đương, gồm thời gian UTC, người thực hiện, backup identifier, database target, storage target, output reconciliation và lỗi còn lại. Không ghi secret, token, email hoặc nội dung ảnh.

## Điều tra finding

- `Missing files`: metadata còn tồn tại nhưng file không có trong private storage. Giữ nguyên metadata; khôi phục file từ backup hoặc đánh dấu sự cố vận hành sau khi có quyết định.
- `Orphan files`: file tồn tại nhưng không có metadata. Không xóa vì retention/xóa vật lý là `TBD-D16`; cô lập và điều tra nguồn tạo file.
- `Invalid files`: tên file không khớp storage-key format. Không mở qua API và không đổi tên tự động.
- `Duplicate keys`: nhiều metadata row trỏ cùng một file. Không tự gộp hoặc xóa vì có thể phá lịch sử.

## Biên bản test restore

Biểu mẫu và trạng thái hiện tại nằm ở `docs/backup-restore-test-report.md`. Checkout này chỉ cung cấp runbook, reconciliation command và test tự động; chưa khẳng định một restore production thành công nếu chưa có SQL Server backup và private-storage backup thực tế.