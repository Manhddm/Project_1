# Biên bản kiểm thử backup/restore

## Trạng thái

`NOT RUN` trong checkout phát triển: chưa có backup production, certificate/private key vận hành, hoặc encrypted private-storage repository để thực hiện restore thật. Không dùng trạng thái này làm bằng chứng production đã khôi phục được.

## Checklist ghi nhận khi chạy thật

- Thời điểm UTC:
- Người thực hiện:
- Backup database identifier:
- Storage backup identifier:
- Database restore target:
- Private storage restore target:
- `RESTORE VERIFYONLY ... WITH CHECKSUM`: `PASS / FAIL`
- `reconcile-files` exit code: `0 / 1 / 2`
- Missing files:
- Orphan files:
- Invalid files:
- Duplicate keys:
- Owner-only API smoke test: `PASS / FAIL`
- Lỗi/finding còn lại:
- Quyết định xử lý và người phê duyệt:

Không ghi password, token, DeviceApiKey, email, đường dẫn chứa secret hoặc nội dung ảnh vào biên bản.