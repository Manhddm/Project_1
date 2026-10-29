# TASKS.md — Checklist theo dõi tiến độ MVP Hộp thuốc thông minh

> Phiên bản: 1.4 · Ngày: 2026-10-29 · Trạng thái: **P0-01, P0-02 DONE; QA baseline: 1 PASS / 0 FAIL / 8 BLOCKED / 73 NOT_RUN — MVP NOT ACCEPTED**
>
> Bản 1.2 ghi nhận P0-01 hoàn thành và chốt D-01/D-02. Bản 1.3 cập nhật kết quả QA thực tế. Bản 1.4 ghi nhận P0-02 (schema + migration `InitialSchema`) hoàn thành.
>
> Nguồn kế hoạch: [PLAN.md](PLAN.md) · Hợp đồng API: [API_CONTRACT.md](API_CONTRACT.md) · Quy tắc làm việc: [AGENTS.md](AGENTS.md) · Kế hoạch kiểm thử: [TEST_PLAN.md](TEST_PLAN.md) · Báo cáo kiểm thử: [TEST_REPORT.md](TEST_REPORT.md)

---

## 1. Cách dùng

### 1.1. Trạng thái hợp lệ

| Trạng thái | Nghĩa | Bắt buộc kèm |
|---|---|---|
| `TODO` | Chưa bắt đầu | — |
| `IN_PROGRESS` | Đang làm, đã có thay đổi cục bộ/commit nháp | Ngày bắt đầu, người/agent |
| `BLOCKED` | Không thể tiếp tục | Lý do, đã thử gì, quyết định cần ai (AGENTS §11) |
| `DONE` | Hoàn thành | **Bằng chứng**: test pass, commit hash, đường dẫn log/ảnh |

Quy tắc:

- **Không** đánh `DONE` khi chưa chạy test hoặc chưa có bằng chứng kiểm chứng.
- Không đổi trạng thái sang `DONE` nếu còn dependency ở trạng thái `TODO`/`BLOCKED`.
- Mỗi lần cập nhật: sửa đúng dòng, ghi bằng chứng ngắn trong cột cuối, thêm dòng vào §10 nếu là cột mốc lớn.
- Nếu phát hiện TBD mới: thêm vào [PLAN.md §3](PLAN.md#decision-log) và ghi lại trong báo cáo; **không tự chốt**.

### 1.2. Mẫu bằng chứng

```
test: <TênTest> (x/y pass) · cmd: dotnet test --filter <...>
commit: <hash>
artifacts: <đường dẫn test report / ảnh chụp màn hình / log>
ghi chú: <phần chưa kiểm chứng nếu có>
```

---

## 2. Cổng mốc (Milestone gates)

> Mỗi cổng chỉ được tick khi **mọi task thuộc mốc DONE** và có bằng chứng.

- [ ] **M1 — Nền móng**: `P0-01`…`P0-05`, `HW-01` DONE; build xanh; migration đầu chạy được; API contract chốt TBD nhóm P0.
- [ ] **M2 — Tài khoản & ghép hộp**: `P1-*`, `P2-*`, `UI-01`, `UI-02` DONE; AC-08, AC-12 (phần API) pass.
- [ ] **M3 — Lịch & nhắc tại chỗ**: `P3-*`, `HW-02`…`HW-04`, `UI-03` DONE; AC-01, AC-04, AC-10 pass (hoặc ghi rõ phần chờ thiết bị).
- [ ] **M4 — Đồng bộ & cảnh báo (MVP lõi)**: `P4-*`, `P5-*`, `UI-04`, `UI-05` DONE; AC-02, AC-03, AC-05, AC-06, AC-09 pass.
- [ ] **M5 — Hoàn thiện demo**: `P6-*`, `P7-*`, `UI-06`, `UI-07`, `HW-05` DONE; AC-07, AC-11 pass.
- [ ] **MVP SIGN-OFF**: M1–M5 hoàn tất + **12/12 tiêu chí nghiệm thu** + kịch bản demo chạy đầu-cuối (xem §13). _QA 2026-10-29: chưa đạt — 0/12 AC, xem §13.2._

**Cổng quyết định (chặn DONE, không chặn code thăm dò):**

- [ ] **D-22 + D-23** chốt trước khi `P3-02`/`P3-03`/`P3-04` được đánh `DONE`.
- [ ] **D-24** chốt trước khi `P3-05`/`P4-03`/`P5-02`/`P5-05` được đánh `DONE`.
- [ ] **D-25 + D-26** chốt trước khi `HW-04`/`P4-05` được đánh `DONE`.
- [ ] **D-11 + D-12** chốt trước khi `P3-03`/`P4-04`/`P5-02` được đánh `DONE`.

---

## 3. Phase 0 — Nền móng & hợp đồng

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P0-01](PLAN.md#p0-01) | Chốt cấu trúc repo & skeleton | `DONE` | — | `dotnet build`: 0 warning/0 error · `HomePageTests.Get_Root_ReturnsHtmlPage` 1/1 pass · Kestrel: `GET /` 200 `text/html`, `GET /css/site.css` 200 | D-01 = Razor trong Api; D-02 = cookie + CSRF; scaffold MVC cũ đã xóa; xem §14 |
| [P0-02](PLAN.md#p0-02) | Mô hình dữ liệu & migration đầu | `DONE` | P0-01 | `20261029070908_InitialSchema` apply trên LocalDB trống · `SchemaConstraintTests` 21/21 pass · full suite 22/22 · build 0W/0E | 17 bảng (bỏ `RefreshTokens` do D-02) + 4 index nóng + check enum; ERD [docs/erd.md](docs/erd.md), SQL [docs/schema.sql](docs/schema.sql); D-09 vẫn tạm theo API_CONTRACT §10.2 |
| [P0-03](PLAN.md#p0-03) | Chốt hợp đồng API & enum | `TODO` | P0-02 | | D-07/08/09 |
| [P0-04](PLAN.md#p0-04) | Hạ tầng cắt ngang (ProblemDetails, clock, email, storage) | `TODO` | P0-01 | | D-03/D-04 |
| [P0-05](PLAN.md#p0-05) | Secret & vệ sinh repo | `TODO` | P0-01 | | |

---

## 4. Phase 1 — Tài khoản

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P1-01](PLAN.md#p1-01) | Entity người dùng & hash mật khẩu | `TODO` | P0-02 | | |
| [P1-02](PLAN.md#p1-02) | Đăng ký & xác minh email | `TODO` | P1-01, P0-04 | | D-14 |
| [P1-03](PLAN.md#p1-03) | Đăng nhập/đăng xuất/hồ sơ | `TODO` | P1-01, P0-03 | | D-02 đã chốt: cookie + CSRF, không có refresh/JWT |
| [P1-04](PLAN.md#p1-04) | Quên & đặt lại mật khẩu | `TODO` | P1-01, P0-04 | | D-14 |
| [P1-05](PLAN.md#p1-05) | Phân quyền theo hộp (owner/relative) | `TODO` | P1-03 | | Hoàn thiện sau P2-08 |
| [P1-06](PLAN.md#p1-06) | Rate limiting & chống lạm dụng | `TODO` | P1-03 | | D-14 |

---

## 5. Phase 2 — Thiết bị, ghép QR, người thân

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P2-01](PLAN.md#p2-01) | Entity Devices/PairingCodes/Compartments | `TODO` | P0-02 | | |
| [P2-02](PLAN.md#p2-02) | Provision & sinh khóa thiết bị | `TODO` | P2-01, P0-05 | | D-05, D-08 |
| [P2-03](PLAN.md#p2-03) | Payload QR & in nhãn | `TODO` | P2-02 | | D-08 |
| [P2-04](PLAN.md#p2-04) | API ghép hộp atomic, một lần | `TODO` | P2-01, P2-02, P1-02, P1-05 | | AC-08, rate limit pair |
| [P2-05](PLAN.md#p2-05) | API trạng thái hộp của tôi | `TODO` | P2-04 | | |
| [P2-06](PLAN.md#p2-06) | Xác thực thiết bị | `TODO` | P2-02 | | D-07 |
| [P2-07](PLAN.md#p2-07) | Heartbeat Unpaired/Paired | `TODO` | P2-06 | | |
| [P2-08](PLAN.md#p2-08) | Entity CareRelationships/RelativeInvitations | `TODO` | P2-01, P1-01 | | |
| [P2-09](PLAN.md#p2-09) | API lời mời người thân | `TODO` | P2-08, P1-06, P0-04 | | D-14; flow người chưa có tài khoản |
| [P2-10](PLAN.md#p2-10) | API quan hệ & ngừng theo dõi | `TODO` | P2-09, P1-05 | | AC-07 |

---

## 6. Phase 3 — Lịch, lần uống, snapshot

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P3-01](PLAN.md#p3-01) | Entity lịch & thuốc | `TODO` | P2-01 | | D-10 |
| [P3-02](PLAN.md#p3-02) | CRUD lịch & ScheduleVersion | `TODO` | P3-01, P1-05 | | AC-07, AC-10, D-22, D-23 |
| [P3-03](PLAN.md#p3-03) | Sinh DoseInstances + snapshot | `TODO` | P3-01, P0-04 | | AC-10, D-11, D-22, D-23 |
| [P3-04](PLAN.md#p3-04) | Đồng bộ lịch cho thiết bị | `TODO` | P3-01, P3-02, P2-07 | | timezone từng lịch, D-22 |
| [P3-05](PLAN.md#p3-05) | Máy trạng thái lần uống | `TODO` | P3-03 | | D-24 |
| [P3-06](PLAN.md#p3-06) | API dashboard & lịch sử | `TODO` | P3-05, P1-05 | | AC-06 |

---

## 7. Phase 4 — Sự kiện & đồng bộ offline

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P4-01](PLAN.md#p4-01) | Entity DeviceEvents | `TODO` | P2-01 | | AC-05 |
| [P4-02](PLAN.md#p4-02) | API batch sự kiện idempotent | `TODO` | P4-01, P2-06 | | AC-05 |
| [P4-03](PLAN.md#p4-03) | Ghép sự kiện với lần uống | `TODO` | P4-02, P3-05 | | AC-02, AC-03, D-24 |
| [P4-04](PLAN.md#p4-04) | Kết nối & trạng thái Unknown | `TODO` | P3-05, P2-07, P4-03 | | AC-06, D-12; đánh giá tại DeadlineAt |
| [P4-05](PLAN.md#p4-05) | Đặc tả hợp đồng firmware | `TODO` | P0-03, P3-04, P4-02 | | Bàn giao HW track, D-25, D-26 |

---

## 8. Phase 5 — Cảnh báo & email

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P5-01](PLAN.md#p5-01) | Entity Alerts/AlertRecipients/EmailDeliveries | `TODO` | P3-01, P2-08 | | D-09 |
| [P5-02](PLAN.md#p5-02) | Job đánh giá quá hạn & tạo cảnh báo | `TODO` | P5-01, P3-05, P4-04, P2-08 | | AC-09, D-24; transaction alert+recipients |
| [P5-03](PLAN.md#p5-03) | Worker gửi email & retry độc lập | `TODO` | P5-01, P0-04 | | D-03, D-13; claim/lease, 2 worker |
| [P5-04](PLAN.md#p5-04) | Mẫu email & nội dung bắt buộc | `TODO` | P5-03 | | AC-06 |
| [P5-05](PLAN.md#p5-05) | API cảnh báo & đánh dấu đã đọc | `TODO` | P5-01, P5-02, P1-05 | | D-24 `resolvedAt` |

---

## 9. Phase 6 — Ảnh đơn thuốc

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P6-01](PLAN.md#p6-01) | Entity ảnh & storage private | `TODO` | P0-04, P1-05 | | D-04 |
| [P6-02](PLAN.md#p6-02) | Upload validation (5 MB, magic bytes, EXIF) | `TODO` | P6-01 | | AC-11, D-16 |
| [P6-03](PLAN.md#p6-03) | Xem/tải ảnh có kiểm tra quyền | `TODO` | P6-02 | | AC-11 |
| [P6-04](PLAN.md#p6-04) | Xóa mềm & audit ảnh | `TODO` | P6-03 | | D-16 |
| [P6-05](PLAN.md#p6-05) | Đối chiếu file & runbook backup | `TODO` | P6-01 | | |

---

## 10. Phase 7 — Hoàn thiện & nghiệm thu

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P7-01](PLAN.md#p7-01) | Phủ audit thao tác quan trọng | `TODO` | P2-04, P2-09, P3-02, P5-02, P6-04 | | |
| [P7-02](PLAN.md#p7-02) | Bộ test nghiệm thu AC-01…AC-12 | `TODO` | P1–P7-01, HW-02…HW-04, UI-01…UI-07 | QA baseline 2026-10-29: [TEST_PLAN.md](TEST_PLAN.md) 82 case · [TEST_REPORT.md](TEST_REPORT.md) 1 PASS / 0 FAIL / 8 BLOCKED / 73 NOT_RUN | Theo dõi tại §13; harness E2E chưa có (DEF-13) |
| [P7-03](PLAN.md#p7-03) | Rà soát bảo mật & hardening | `TODO` | P1–P6 | | |
| [P7-04](PLAN.md#p7-04) | Observability & health | `TODO` | P0-04 | | |
| [P7-05](PLAN.md#p7-05) | Bộ demo & tài liệu bàn giao | `TODO` | P7-02 | | |
| [P7-06](PLAN.md#p7-06) | Runbook vận hành & chính sách dữ liệu | `TODO` | P6-05 | | D-17 |

---

## 11. Web UI track

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [UI-01](PLAN.md#ui-01) | Trang xác thực tài khoản | `TODO` | P1-02…P1-04, P0-01 | | AC-12, D-01 = Razor trong Api |
| [UI-02](PLAN.md#ui-02) | Ghép hộp & trạng thái thiết bị | `TODO` | P2-04, P2-05, P2-07, UI-01 | | |
| [UI-03](PLAN.md#ui-03) | Quản lý lịch uống | `TODO` | P3-02, UI-01 | | |
| [UI-04](PLAN.md#ui-04) | Dashboard & lịch sử | `TODO` | P3-06, P4-04, UI-02 | | AC-06 |
| [UI-05](PLAN.md#ui-05) | Người thân & cảnh báo | `TODO` | P2-09, P2-10, P5-05, UI-01 | | |
| [UI-06](PLAN.md#ui-06) | Ảnh đơn thuốc | `TODO` | P6-02…P6-04, UI-01 | | AC-11 |
| [UI-07](PLAN.md#ui-07) | Rà soát responsive & thuật ngữ | `TODO` | UI-01…UI-06 | | Không còn "đã uống" |

---

## 12. Hardware / firmware track

> [TBD-D06] Code firmware có thể nằm ngoài repo này; checklist vẫn theo dõi để đảm bảo tích hợp.

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [HW-01](PLAN.md#hw-01) | Sơ đồ khối, BOM, pin map, nguồn | `TODO` | — | | D-25 onboarding |
| [HW-02](PLAN.md#hw-02) | Firmware nhắc theo RTC, đèn/còi đúng ngăn | `TODO` | HW-01 | | AC-01, AC-04, D-26 |
| [HW-03](PLAN.md#hw-03) | Cạnh nắp, nắp mở sẵn, hàng đợi sự kiện | `TODO` | HW-02 | | AC-02, AC-03, AC-04; trạng thái nắp khi reboot |
| [HW-04](PLAN.md#hw-04) | Wi‑Fi, heartbeat, sync lịch, gửi batch | `TODO` | HW-03, P2-07, P3-04, P4-02 | | AC-05, D-25, D-26 |
| [HW-05](PLAN.md#hw-05) | In QR & quy trình provision nhãn | `TODO` | P2-02, P2-03 | | AC-08 |

---

## 13. Tiêu chí nghiệm thu (theo dõi riêng)

<a id="acceptance"></a>

> Chỉ tick khi có test/bằng chứng tương ứng. Xem [PLAN.md §16](PLAN.md#traceability).
>
> Cột **Trạng thái** dùng quy ước QA của [TEST_PLAN.md §2](TEST_PLAN.md#2-quy-ước-trạng-thái): `PASS` / `FAIL` / `BLOCKED` / `NOT_RUN`. Không AC nào được tính đạt khi chưa có bằng chứng chạy thực tế. Kết quả QA ngày 2026-10-29: [TEST_REPORT.md](TEST_REPORT.md).

| AC | Tiêu chí | Trạng thái | Test / Bằng chứng |
|---|---|---|---|
| AC-01 | Nhắc đúng ngăn, đúng giờ; hai ngăn kia không sáng | `BLOCKED` | TC-SYNC-001, TC-DOSE-003 — thiếu thiết bị/simulator (D-06) và API lần uống |
| AC-02 | Xác nhận đúng cửa sổ; sớm/sai ngăn không xác nhận | `NOT_RUN` | TC-DOSE-003…005, TC-DOSE-009 — chưa có P3-05/P4-03 |
| AC-03 | Nắp mở sẵn không tự xác nhận; đóng rồi mở lại hợp lệ | `BLOCKED` | TC-DOSE-006 — cần hành vi firmware/cạnh nắp (HW-03) |
| AC-04 | Offline vẫn nhắc theo RTC và giữ sự kiện | `BLOCKED` | TC-SYNC-001, TC-SYNC-002 — thiếu thiết bị/simulator (D-06) |
| AC-05 | Gửi lại batch không tạo kết quả trùng | `NOT_RUN` | TC-SYNC-003, TC-SYNC-004, TC-SYNC-012 — chưa có P4-02 |
| AC-06 | Offline: Web/email dùng Unknown/"không thể xác nhận" | `NOT_RUN` | TC-ALERT-002, TC-ALERT-008, TC-UI-002 — chưa có P4-04/P5-02/UI-04 |
| AC-07 | Người thân xem được nhưng bị chặn sửa cấu hình/ảnh | `NOT_RUN` | TC-AUTHZ-002…TC-AUTHZ-006 — chưa có endpoint để phân quyền |
| AC-08 | QR dùng một lần; quét lại bị từ chối; không lộ DeviceApiKey | `NOT_RUN` | TC-PAIR-005, TC-PAIR-007, TC-PAIR-009 — chưa có P2-02…P2-04 |
| AC-09 | Email quá hạn đủ chủ + mọi relative Accepted; retry không nhân đôi | `NOT_RUN` | TC-ALERT-003, TC-ALERT-006 — chưa có P5-02/P5-03, D-03 chưa chốt |
| AC-10 | Snapshot không đổi ngược khi sửa lịch/thuốc | `NOT_RUN` | TC-SCHED-006, TC-DOSE-001, TC-DOSE-008 — chưa có P0-02/P3-03 |
| AC-11 | Ảnh sai loại/quá 5 MB bị từ chối; loại EXIF; file ngoài DB | `NOT_RUN` | TC-IMG-002…TC-IMG-007 — chưa có P6-*, D-04 chưa chốt |
| AC-12 | Đăng ký → xác minh → đăng nhập → quên/đặt lại hoạt động | `NOT_RUN` | TC-AUTH-001, TC-AUTH-004…007 — chưa có P1-02…P1-04 |

### 13.1. Kịch bản kiểm thử tối thiểu (dùng cho P7-02)

| AC | Setup → Hành động → Kỳ vọng (đo được) |
|---|---|
| AC-01 | Lịch 07:00 ngăn 1, grace 15. Tại 07:00: chỉ LED ngăn 1 sáng + còi; LED ngăn 2/3 tắt; log/telemetry ghi đúng slot 1. |
| AC-02 | Dose 12:00–12:15 ngăn 2. (a) mở ngăn 2 lúc 11:59 → không `Confirmed`; (b) mở ngăn 1 lúc 12:05 → không; (c) mở ngăn 2 lúc 12:05 → `Confirmed`, `confirmedAt=12:05`; (d) mở đúng 12:15 → `Confirmed` (biên inclusive). |
| AC-03 | Nắp ngăn 2 mở từ 11:50. Lúc 12:05 vẫn mở → không `Confirmed`. Đóng 12:06, mở lại 12:07 → `Confirmed`; event 11:50 vẫn được lưu. |
| AC-04 | Ngắt Wi‑Fi trước 07:00. 07:00 thiết bị vẫn sáng đèn/còi theo RTC; mở đúng ngăn; event nằm trong queue; reconnect → batch gửi lên, server ghi 1 event. |
| AC-05 | Gửi cùng batch (gồm 1 event mở hợp lệ) 3 lần. DB có đúng 1 `DeviceEvents`/`eventId`, 1 `Confirmed`, không thêm alert/recipient; lần 2–3 trả `Duplicate`. |
| AC-06 | Thiết bị offline tại `DeadlineAt`. Dashboard `Unknown`; email/web dùng "không thể xác nhận do mất kết nối"; không có chuỗi "quên uống"/"bỏ liều". |
| AC-07 | Relative Accepted: GET schedules/dashboard/history/alerts → 200; POST/PUT/DELETE schedule → 403; invitation tạo/revoke → 403; ảnh content → 403. Owner làm cùng thao tác → thành công. |
| AC-08 | Pair mã lần 1 → 200; quét lại → 409 `PAIRING_CODE_USED`; response + QR không chứa `DeviceApiKey`/hash; 2 request đồng thời → đúng 1 thành công. |
| AC-09 | Owner + 2 relative Accepted + 1 relative đã revoke. Dose online quá hạn → alert có đúng 3 recipient; email sink nhận 3; cho 1 địa chỉ lỗi và retry → người khác không nhận thêm bản thứ 2. |
| AC-10 | Sinh dose 07:00/15 phút với thuốc A. Sửa lịch thành 08:00/30 phút, thuốc B. Dose cũ + snapshot vẫn 07:00/15/A; lịch sử/dashboard không đổi; chỉ lịch hiện hành đổi. |
| AC-11 | Upload `.exe` đổi tên `.jpg` → 415; file 5,242,881 bytes → 413; JPEG có EXIF GPS → file lưu không còn EXIF; DB không có binary; file không nằm trong `wwwroot`; relative tải → 403. |
| AC-12 | Đăng ký → nhận email xác minh → xác minh → đăng nhập; quên mật khẩu → nhận token → đặt lại → đăng nhập bằng mật khẩu mới; token cũ dùng lại bị từ chối. |

### 13.2. Kết quả QA baseline — 2026-10-29

> Chi tiết: [TEST_PLAN.md](TEST_PLAN.md) (82 test case) và [TEST_REPORT.md](TEST_REPORT.md) (kết quả + defect log).

| Chỉ số | Giá trị |
|---|---|
| Tổng test case | 82 |
| Đã chạy | 1 (TC-SMOKE-001) |
| `PASS` | 1 |
| `FAIL` | 0 |
| `BLOCKED` | 8 (TC-SCHED-007, TC-SCHED-009, TC-DOSE-011, TC-SYNC-001, TC-SYNC-002, TC-SYNC-006, TC-SYNC-011, TC-ALERT-009) |
| `NOT_RUN` | 73 |
| AC đạt | 0/12 |

Kết luận: **MVP NOT ACCEPTED**. Nguyên nhân gốc: P0-02…P7-06 chưa triển khai (API nghiệp vụ trả `404`), `QA-ENV-1` thiếu SQL Server/email sink/clock/simulator, và D-22/D-23/D-24/D-26 chưa chốt. Không tick cổng M1–M5. Xem danh sách lỗi cần sửa tại [TEST_REPORT.md §7–§9](TEST_REPORT.md#7-defect-log-lỗi-cần-sửa).

---

## 14. Nhật ký bằng chứng (append-only)

> Thêm một dòng mỗi khi một task chuyển `DONE` hoặc một cổng mốc được tick. Không sửa/xóa dòng cũ.

| Ngày | Task/Mốc | Từ → Đến | Bằng chứng (test/commit/artifact) | Người thực hiện |
|---|---|---|---|---|
| 2026-10-29 | Khởi tạo tài liệu | — → hoàn tất kế hoạch | 4 tài liệu PLAN/TASKS/API_CONTRACT/AGENTS | Agent |
| 2026-10-29 | P0-01 | `TODO` → `DONE` | Build 0W/0E; `HomePageTests.Get_Root_ReturnsHtmlPage` 1/1; HTTP 200 HTML + CSS 200; D-01/D-02 chốt trong PLAN §3 | Agent |
| 2026-10-29 | QA baseline | — → 1 PASS / 82 | `dotnet build` 0W/0E; `dotnet test` 1/1 pass; `GET /api/users/me` & `/api/schedules` → 404; [TEST_PLAN.md](TEST_PLAN.md) + [TEST_REPORT.md](TEST_REPORT.md); 0/12 AC đạt | QA Automation |
| 2026-10-29 | P0-02 | `TODO` → `DONE` | Migration `20261029070908_InitialSchema` apply trên DB LocalDB trống; `SchemaConstraintTests` 21/21; full suite 22/22; build 0W/0E; [docs/erd.md](docs/erd.md) + [docs/schema.sql](docs/schema.sql) | Agent |

---

## 15. Việc chưa được phép làm ở giai đoạn này

- Chỉ triển khai task có trong [PLAN.md](PLAN.md), dependency đã `DONE`; không tự thêm task/hạng mục ngoài kế hoạch.
- Không đánh dấu bất kỳ task `DONE`/cổng mốc nào khi chưa có bằng chứng.
- Không tự chốt các TBD trong [PLAN.md §3](PLAN.md#decision-log).

---

## 16. Ghi chú đồng bộ tài liệu

- Khi task hoàn thành mà thay đổi endpoint/schema → cập nhật [API_CONTRACT.md](API_CONTRACT.md) và [PLAN.md](PLAN.md) trước, rồi mới `DONE`.
- Khi phát hiện task thiếu → thêm vào PLAN với ID mới (đúng phase), rồi thêm dòng tương ứng ở đây.
- Khi một task bị tách/gộp → ghi rõ ID thay thế vào cột "Ghi chú", không tái sử dụng ID cũ.

---

## 17. Phụ lục — phân rã task lớn (dùng nội bộ, không thay ID PLAN)

> Các ID con chỉ để chia việc trong một task; trạng thái vẫn ghi ở dòng task cha. Khi tách thành task PLAN thật, cấp ID mới theo quy tắc §16.

### P0-02 · Schema & migration

- [x] P0-02a — 17 entity + quan hệ 1–n/n–n + `DbContext`
- [x] P0-02b — unique/check/filtered index + 4 index đường đọc nóng
- [x] P0-02c — migration `InitialSchema` + test apply trên SQL Server trống
- [x] P0-02d — test ràng buộc (slot 1–3, owner unique, event unique, alert recipient unique)

### P2-09 · Lời mời người thân

- [ ] P2-09a — entity/validation + chặn trùng Pending/relationship
- [ ] P2-09b — tạo/revoke/list invitation + audit
- [ ] P2-09c — email token một lần + flow người chưa có tài khoản
- [ ] P2-09d — accept transaction + sai email/hết hạn/dùng lại + rate limit

### P3-03 · Sinh dose & snapshot

- [ ] P3-03a — tính occurrence theo timezone/ngày/khung hiệu lực
- [ ] P3-03b — insert idempotent + `DoseItemSnapshots` + grace copy
- [ ] P3-03c — chạy lặp/song song + sinh bù sau downtime
- [ ] P3-03d — test AC-10 + D-22 (không đổi dose đã sinh)

### P4-02 · Nhận batch sự kiện

- [ ] P4-02a — auth thiết bị + validation batch/phần tử + giới hạn
- [ ] P4-02b — xử lý theo thứ tự `occurredAt`/`eventId`, idempotent
- [ ] P4-02c — kết quả từng phần tử + phần tử lỗi không rollback phần tử hợp lệ
- [ ] P4-02d — test gửi lặp 3 lần, batch xáo thứ tự, event tương lai

### P5-02 · Job cảnh báo

- [ ] P5-02a — query dose đến hạn + xác định online/offline tại `DeadlineAt`
- [ ] P5-02b — tạo alert + recipients trong transaction, idempotent
- [ ] P5-02c — chọn đúng AlertType + không alert dose Confirmed
- [ ] P5-02d — test AC-09, chạy job 2 instance, revoke trước đánh giá

### P5-03 · Email worker

- [ ] P5-03a — claim delivery bằng lease, chống 2 worker
- [ ] P5-03b — gửi + lưu `SentAt`/`ProviderMessageId`/attempt
- [ ] P5-03c — backoff, max attempts, `Exhausted`, cô lập lỗi
- [ ] P5-03d — test retry một người không ảnh hưởng người khác + crash mô phỏng

### P7-02 · Nghiệm thu

- [ ] P7-02a — harness E2E API + DB + email sink + clock giả
- [ ] P7-02b — simulator thiết bị cho AC-01…AC-05
- [ ] P7-02c — UI browser test cho AC-06/07/11/12
- [ ] P7-02d — báo cáo 12/12 + gap chưa kiểm chứng trên phần cứng thật
