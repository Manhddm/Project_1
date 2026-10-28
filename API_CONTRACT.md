# API_CONTRACT.md — Hợp đồng API MVP Hộp thuốc thông minh

> Phiên bản: 1.3 · Ngày: 2026-10-29 · Trạng thái: **Nháp có kiểm soát — chờ chốt các TBD tại §15**
>
> Bản 1.3 rà soát QA: bỏ tàn dư refresh token ở logout/reset-password sau khi chốt D-02.
>
> Tài liệu liên quan: [AGENTS.md](AGENTS.md) · [PLAN.md](PLAN.md) · [TASKS.md](TASKS.md)
>
> HTML cho phép **tên endpoint là gợi ý**, nhưng **phạm vi chức năng, kiểm tra quyền và idempotency không được thiếu**. Mọi thay đổi tên/đường dẫn phải cập nhật cả 4 tài liệu.

---

## 1. Quy ước chung

<a id="conventions"></a>

| Quy ước | Giá trị |
|---|---|
| Base path | `/api` |
| Content type | `application/json; charset=utf-8` (trừ upload multipart và tải ảnh binary) |
| Field naming | `camelCase` |
| ID nội bộ | GUID string |
| `deviceId` trong URL | GUID nội bộ của thiết bị (không phải `Devices.DeviceId` dùng cho thiết bị) |
| Thời gian | ISO 8601 có offset, chuẩn hóa UTC (`2026-10-02T12:00:00Z`) |
| Ngày | `yyyy-MM-dd`; giờ trong ngày | `HH:mm` (24h) |
| Số lượng thuốc | JSON number, tối đa `decimal(10,2)` |
| Ngày trong tuần | `1` = Thứ 2 … `7` = Chủ nhật (ISO-8601) **[ĐỀ XUẤT — D-09]** |
| Phân trang | `page` (≥1, mặc định 1), `pageSize` (1–100, mặc định 20) |
| Lỗi | `ProblemDetails` (RFC 7807) + `code` ổn định + `traceId` |
| Correlation | `X-Correlation-Id` (tùy chọn) **[ĐỀ XUẤT]** |

### 1.1. Định dạng lỗi

```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "code": "PAIRING_CODE_USED",
  "detail": "Mã ghép đã được sử dụng.",
  "traceId": "0HN7...:00000001",
  "errors": {
    "email": ["Email không hợp lệ."]
  }
}
```

- `code` là hợp đồng ổn định — client dùng để xử lý; `detail` chỉ để hiển thị/hiểu.
- `errors` chỉ có khi lỗi validation theo từng trường.
- Không trả stack trace. Không đổi mã lỗi đã phát hành; mã mới phải bổ sung vào §14.

### 1.2. Xác thực

| Loại người gọi | Cơ chế | Ghi chú |
|---|---|---|
| Web (chủ hộp / người thân) | Cookie phiên (HttpOnly/Secure/SameSite) + CSRF token | **D-02 đã chốt (2026-10-29)** — không dùng Bearer/JWT |
| ESP32 | Header `X-Device-Id: <Devices.DeviceId>` + `X-Device-Api-Key: <khóa thô>` | **[ĐỀ XUẤT — D-07]**; server so sánh hash constant-time |
| Anonymous | Chỉ các endpoint auth/register/login/forgot/reset/verify | Rate limited |

- Endpoint người dùng **không** chấp nhận xác thực thiết bị; endpoint thiết bị **không** chấp nhận token người dùng.
- Thiếu/sai thông tin xác thực thiết bị: `401 DEVICE_UNAUTHORIZED` (không tiết lộ thiết bị có tồn tại hay không).
- `POST /api/devices/pair` và `POST /api/relatives/invitations/accept` phải **rate-limit theo người gọi + IP/token** dù đã đăng nhập, trả `429 RATE_LIMITED` kèm `Retry-After`; ngưỡng **[TBD-D14]**. Đây là chống brute-force mã/token ngắn, không thay thế kiểm tra quyền.
- Cookie session (D-02): cookie `HttpOnly`, `Secure`, `SameSite=Lax/Strict`, và mọi request ghi phải có CSRF token (antiforgery). Không dùng Bearer/JWT, không lưu token trong `localStorage`.
- Pairing code, token và `DeviceApiKey` lưu dạng hash có **pepper/HMAC** phía server (không SHA-256 trần) để chống dò offline khi mã ngắn.
- Endpoint thiết bị không tin đồng hồ thiết bị tuyệt đối: server dùng `occurredAt` để ghép nhưng trả `serverTimeUtc` để thiết bị chỉnh RTC; ngưỡng lệch theo **[TBD-D26]**.

### 1.3. Quy ước trạng thái HTTP

| Status | Dùng khi |
|---|---|
| `200 OK` | Đọc/cập nhật thành công |
| `201 Created` | Tạo mới (kèm `Location`) |
| `202 Accepted` | Đã nhận yêu cầu, xử lý không đồng bộ hoặc phản hồi trung tính |
| `204 No Content` | Xóa/không có body |
| `400 Bad Request` | Validation, mã không hợp lệ |
| `401 Unauthorized` | Thiếu/sai xác thực |
| `403 Forbidden` | Đã xác thực nhưng không có quyền |
| `404 Not Found` | Không tồn tại trong phạm vi người gọi được biết |
| `409 Conflict` | Xung đột trạng thái/trùng lặp nghiệp vụ |
| `410 Gone` | Token/mã đã hết hạn hoặc không còn hiệu lực |
| `413 Payload Too Large` | File vượt 5 MB |
| `415 Unsupported Media Type` | File không thuộc JPEG/PNG/WebP |
| `429 Too Many Requests` | Vượt rate limit (kèm `Retry-After`) |
| `500 Internal Server Error` | Lỗi hệ thống (không lộ chi tiết) |

---

## 2. Danh sách endpoint

### 2.1. API cho Web (người dùng)

| # | Method | Endpoint | Quyền | Task |
|---|---|---|---|---|
| 1 | POST | `/api/auth/register` | Anonymous | P1-02 |
| 2 | POST | `/api/auth/verify-email` | Anonymous (có token) | P1-02 |
| 3 | POST | `/api/auth/resend-verification` | Anonymous **[ĐỀ XUẤT]** | P1-02 |
| 4 | POST | `/api/auth/login` | Anonymous | P1-03 |
| 5 | — | *Không áp dụng — D-02 dùng cookie session* | — | — |
| 6 | POST | `/api/auth/logout` | Đã đăng nhập | P1-03 |
| 7 | POST | `/api/auth/forgot-password` | Anonymous | P1-04 |
| 8 | POST | `/api/auth/reset-password` | Anonymous (có token) | P1-04 |
| 9 | GET | `/api/users/me` | Đã đăng nhập | P1-03 |
| 10 | POST | `/api/devices/pair` | Đã đăng nhập + email xác minh | P2-04 |
| 11 | GET | `/api/devices/mine` | Chủ hộp | P2-05 |
| 12 | GET | `/api/schedules` | Owner/Relative | P3-02 |
| 13 | GET | `/api/schedules/{id}` | Owner/Relative | P3-02 |
| 14 | POST | `/api/schedules` | Chủ hộp | P3-02 |
| 15 | PUT | `/api/schedules/{id}` | Chủ hộp | P3-02 |
| 16 | DELETE | `/api/schedules/{id}` | Chủ hộp | P3-02 |
| 17 | POST | `/api/relatives/invitations` | Chủ hộp | P2-09 |
| 18 | GET | `/api/relatives/invitations` | Chủ hộp **[ĐỀ XUẤT]** | P2-09 |
| 19 | POST | `/api/relatives/invitations/{id}/revoke` | Chủ hộp | P2-09 |
| 20 | POST | `/api/relatives/invitations/accept` | Người được mời (đã đăng nhập) | P2-09 |
| 21 | GET | `/api/relatives/relationships` | Owner/Relative (người tham gia) | P2-10 |
| 22 | DELETE | `/api/relatives/relationships/{id}` | Owner/Relative (người tham gia) | P2-10 |
| 23 | GET | `/api/dashboard` | Owner/Relative | P3-06 |
| 24 | GET | `/api/dose-history` | Owner/Relative | P3-06 |
| 25 | GET | `/api/alerts` | Owner/Relative | P5-05 |
| 26 | POST | `/api/alerts/{id}/read` | Owner/Relative **[ĐỀ XUẤT]** | P5-05 |
| 27 | GET | `/api/prescriptions/images` | Chủ hộp **[ĐỀ XUẤT]** | P6-03 |
| 28 | POST | `/api/prescriptions/images` | Chủ hộp | P6-02 |
| 29 | GET | `/api/prescriptions/images/{id}/content` | Chủ hộp | P6-03 |
| 30 | DELETE | `/api/prescriptions/images/{id}` | Chủ hộp | P6-04 |

### 2.2. API cho ESP32

| # | Method | Endpoint | Xác thực | Task |
|---|---|---|---|---|
| 31 | POST | `/api/device/heartbeat` | Device (Paired hoặc Unpaired) | P2-07 |
| 32 | GET | `/api/device/schedule-sync` | Device (Paired) | P3-04 |
| 33 | POST | `/api/device/events/batch` | Device (Paired) | P4-02 |

---

## 3. Auth & Profile (Web)

<a id="auth"></a>

### 3.1. `POST /api/auth/register`

- **Auth:** Anonymous · **Rate limit:** [ĐỀ XUẤT] · **Task:** P1-02

**Request**

```json
{
  "email": "owner@example.com",
  "password": "P@ssw0rd-123"
}
```

**Validation:** email hợp lệ, ≤ 320 ký tự, chuẩn hóa trim/lowercase; mật khẩu theo [TBD-D14] (đề xuất ≥ 8 ký tự, có chữ và số, ≤ 128).

**Responses**

| Status | Body | Khi nào |
|---|---|---|
| `201` | `{ "userId": "…", "email": "owner@example.com", "emailVerificationRequired": true }` | Tạo thành công, đã gửi email xác minh |
| `400` | `VALIDATION_ERROR` | Dữ liệu sai |
| `409` | `EMAIL_TAKEN` | Email đã tồn tại (đã chuẩn hóa) |
| `429` | `RATE_LIMITED` | Vượt ngưỡng |

> [Ghi chú] Trả `409 EMAIL_TAKEN` có thể giúp dò email tồn tại. HTML yêu cầu email unique; chấp nhận đánh đổi ở đăng ký, nhưng `forgot-password` **không** được tiết lộ.

### 3.2. `POST /api/auth/verify-email`

- **Auth:** Anonymous (token trong body)

```json
{ "token": "<token-thô-từ-email>" }
```

| Status | Body / code |
|---|---|
| `200` | `{ "emailVerified": true }` |
| `400` | `TOKEN_INVALID` (sai/đã dùng) |
| `410` | `TOKEN_EXPIRED` |

### 3.3. `POST /api/auth/resend-verification` **[ĐỀ XUẤT]**

```json
{ "email": "owner@example.com" }
```

| Status | Body |
|---|---|
| `202` | `{ "message": "Nếu email tồn tại và chưa xác minh, chúng tôi đã gửi lại." }` (luôn 202) |
| `429` | `RATE_LIMITED` |

### 3.4. `POST /api/auth/login`

```json
{ "email": "owner@example.com", "password": "P@ssw0rd-123" }
```

**Response `200` — cookie phiên (D-02)**

Trả `Set-Cookie` HttpOnly/Secure/SameSite=Lax kèm body (không có token trong body):

```json
{
  "user": {
    "id": "3f1c…",
    "email": "owner@example.com",
    "emailVerified": true
  }
}
```

| Status | Code | Khi nào |
|---|---|---|
| `401` | `INVALID_CREDENTIALS` | Sai email/mật khẩu (thông điệp chung) |
| `403` | `EMAIL_NOT_VERIFIED` | Đúng mật khẩu nhưng chưa xác minh |
| `429` | `RATE_LIMITED` | Brute force |

### 3.5. `POST /api/auth/refresh` — không áp dụng

D-02 đã chốt cookie session; hợp đồng không có endpoint refresh/JWT và không có bảng `RefreshTokens`. Nếu quyết định này thay đổi, phải cập nhật đồng bộ 4 tài liệu trước khi code.

### 3.6. `POST /api/auth/logout`

- **Auth:** Đã đăng nhập · **Response:** `204 No Content`
- Xóa cookie phiên và thu hồi phiên phía server; không có refresh token (D-02). Gọi lặp lại vẫn trả `204`.

### 3.7. `POST /api/auth/forgot-password`

```json
{ "email": "owner@example.com" }
```

| Status | Body |
|---|---|
| `202` | `{ "message": "Nếu email tồn tại, hướng dẫn đặt lại đã được gửi." }` — **luôn** trả 202 |
| `429` | `RATE_LIMITED` |

### 3.8. `POST /api/auth/reset-password`

```json
{ "token": "<token-thô>", "newPassword": "N3w-P@ssw0rd" }
```

| Status | Code | Khi nào |
|---|---|---|
| `200` | — | Đổi thành công; thu hồi mọi phiên cũ (D-02: không có refresh token) |
| `400` | `TOKEN_INVALID` / `VALIDATION_ERROR` | Token sai/đã dùng hoặc mật khẩu không đạt |
| `410` | `TOKEN_EXPIRED` | Token hết hạn |

### 3.9. `GET /api/users/me`

- **Auth:** Đã đăng nhập · **Task:** P1-03

```json
{
  "id": "3f1c…",
  "email": "owner@example.com",
  "emailVerified": true,
  "createdAt": "2026-10-01T08:00:00Z",
  "ownedDevice": {
    "id": "a1b2…",
    "serialNumber": "SMB-000123",
    "pairedAt": "2026-10-02T09:00:00Z"
  }
}
```

`ownedDevice` = `null` nếu chưa ghép. Không trả hash/khóa/`DeviceId` thiết bị.

---

## 4. Thiết bị & ghép QR (Web)

<a id="devices"></a>

### 4.1. `POST /api/devices/pair`

- **Auth:** Người dùng đã đăng nhập + `EmailVerified=true` · **Task:** P2-04

**Request** — nội dung quét từ QR (payload **[TBD-D08]**):

```json
{ "code": "PAIR-7F3K-9Q2M" }
```

**Response `200`**

```json
{
  "device": {
    "id": "a1b2c3d4-…",
    "serialNumber": "SMB-000123",
    "pairedAt": "2026-10-29T09:30:00Z",
    "scheduleVersion": 0,
    "compartments": [
      { "slotNumber": 1, "name": "Sáng", "isEnabled": true },
      { "slotNumber": 2, "name": "Trưa", "isEnabled": true },
      { "slotNumber": 3, "name": "Tối", "isEnabled": true }
    ]
  }
}
```

| Status | Code | Khi nào |
|---|---|---|
| `400` | `PAIRING_CODE_INVALID` | Mã sai/không tồn tại (không tiết lộ thiết bị) |
| `403` | `EMAIL_NOT_VERIFIED` | Người dùng chưa xác minh |
| `409` | `PAIRING_CODE_USED` | Mã đã dùng (kể cả quét lại) |
| `409` | `USER_ALREADY_OWNS_DEVICE` | Tài khoản đã sở hữu hộp |
| `409` | `DEVICE_ALREADY_PAIRED` | Thiết bị đã có chủ |
| `410` | `PAIRING_CODE_EXPIRED` | Mã hết hạn |
| `429` | `RATE_LIMITED` | Thử mã quá nhiều lần (kèm `Retry-After`) |

**Idempotency/transaction:** kiểm tra + gán `OwnerUserId` + set `UsedAt`/`UsedByUserId` + audit trong **một transaction**; hai request đồng thời chỉ một thành công. Response **không** chứa `DeviceApiKey`. Mã sai và mã không tồn tại trả cùng `400 PAIRING_CODE_INVALID` để không dò thiết bị.

### 4.2. `GET /api/devices/mine`

- **Auth:** Chủ hộp · **Task:** P2-05

```json
{
  "id": "a1b2c3d4-…",
  "serialNumber": "SMB-000123",
  "scheduleVersion": 4,
  "lastSeenAt": "2026-10-29T09:31:12Z",
  "isOnline": true,
  "compartments": [
    { "slotNumber": 1, "name": "Sáng", "isEnabled": true },
    { "slotNumber": 2, "name": "Trưa", "isEnabled": true },
    { "slotNumber": 3, "name": "Tối", "isEnabled": true }
  ]
}
```

| Status | Code |
|---|---|
| `404` | `DEVICE_NOT_FOUND` |

> `isOnline` tính theo ngưỡng `LastSeenAt` — **[TBD-D12]**.

---

## 5. Lịch uống

<a id="schedules"></a>

### 5.1. `GET /api/schedules?deviceId={guid}`

- **Auth:** Owner (hộp mình) hoặc Relative đã Accepted · **Task:** P3-02

```json
{
  "items": [
    {
      "id": "s1…",
      "deviceId": "a1b2…",
      "compartmentSlot": 1,
      "compartmentName": "Sáng",
      "timeOfDay": "07:00",
      "daysOfWeek": [2, 4, 6],
      "startDate": "2026-10-01",
      "endDate": "2026-12-31",
      "gracePeriodMinutes": 15,
      "timeZoneId": "Asia/Ho_Chi_Minh",
      "items": [
        {
          "id": "i1…",
          "medicineName": "Vitamin C",
          "quantity": 1,
          "unit": "viên",
          "instructions": "Uống sau ăn"
        }
      ],
      "updatedAt": "2026-10-28T08:00:00Z"
    }
  ]
}
```

| Status | Code |
|---|---|
| `403` | `FORBIDDEN` (không thuộc hộp) |
| `404` | `DEVICE_NOT_FOUND` |

Chỉ trả lịch chưa xóa mềm. `deviceId` là **bắt buộc** để phân biệt khi người dùng theo dõi nhiều hộp; chủ hộp luôn có đúng một hộp.

### 5.2. `GET /api/schedules/{id}`

- **Auth:** Owner/Relative theo hộp của lịch · Trả một object như trên, kèm `version` (ScheduleVersion của thiết bị) nếu cần.

### 5.3. `POST /api/schedules`

- **Auth:** Chủ hộp · **Task:** P3-02 · **Response:** `201` + `Location`

**Request**

```json
{
  "compartmentSlot": 1,
  "timeOfDay": "07:00",
  "daysOfWeek": [2, 4, 6],
  "startDate": "2026-10-01",
  "endDate": "2026-12-31",
  "gracePeriodMinutes": 15,
  "timeZoneId": "Asia/Ho_Chi_Minh",
  "items": [
    {
      "medicineName": "Vitamin C",
      "quantity": 1,
      "unit": "viên",
      "instructions": "Uống sau ăn"
    },
    {
      "medicineName": "Canxi",
      "quantity": 2,
      "unit": "viên",
      "instructions": "Uống với nhiều nước"
    }
  ]
}
```

**Response `201`**

```json
{ "id": "s1…", "scheduleVersion": 5, "createdAt": "2026-10-29T09:40:00Z" }
```

| Status | Code |
|---|---|
| `400` | `VALIDATION_ERROR` |
| `400` | `COMPARTMENT_INVALID` (slot không thuộc hộp) |
| `403` | `FORBIDDEN` (relative) |
| `409` | `DEVICE_NOT_PAIRED` |

> Server lấy hộp của chủ từ tài khoản; không nhận `deviceId` trong body. `endDate` cho phép `null` (không giới hạn).

### 5.4. `PUT /api/schedules/{id}`

- **Auth:** Chủ hộp · Body giống `POST` (thay thế toàn bộ ngày/thuốc) · **Response:** `200 { "id": "…", "scheduleVersion": 6 }`
- Tăng `ScheduleVersion` trong transaction; **không** sửa `DoseInstances`/`DoseItemSnapshots` đã sinh.
- **[TBD-D22]** Hành vi với các `DoseInstance` `Pending` đã sinh trong tương lai chưa được chốt: hợp đồng này **không** cho phép client hủy/tái sinh; server giữ nguyên cho tới khi có quyết định.
- **[TBD-D23]** Hai lịch chồng lấn cùng ngăn: chỉ chặn trùng tuyệt đối (cùng slot + giờ + khung ngày) **[ĐỀ XUẤT]**; chồng lấn một phần chưa chốt.
- [ĐỀ XUẤT] Hỗ trợ `If-Match: <version>` để chống ghi đè đồng thời; không khớp → `409 SCHEDULE_VERSION_CONFLICT`.

| Status | Code |
|---|---|
| `400` | `VALIDATION_ERROR` |
| `403` | `FORBIDDEN` |
| `404` | `SCHEDULE_NOT_FOUND` |
| `409` | `SCHEDULE_VERSION_CONFLICT` **[nếu áp dụng If-Match]** |

### 5.5. `DELETE /api/schedules/{id}`

- **Auth:** Chủ hộp · **Response:** `204 No Content`
- Xóa mềm; tăng `ScheduleVersion` (version cấp thiết bị) **trong cùng transaction**; không xóa lần uống/snapshot đã sinh; audit.
- `403 FORBIDDEN` nếu relative; `404 SCHEDULE_NOT_FOUND`. Gọi lại trên lịch đã xóa: `404` (không tạo thêm hiệu ứng) **[ĐỀ XUẤT]**.

---

## 6. Người thân & lời mời

<a id="relatives"></a>

### 6.1. `POST /api/relatives/invitations`

- **Auth:** Chủ hộp · **Task:** P2-09 · **Rate limit:** bắt buộc

```json
{ "email": "relative@example.com" }
```

**Response `201`**

```json
{
  "id": "inv1…",
  "inviteeEmail": "relative@example.com",
  "status": "Pending",
  "expiresAt": "2026-11-05T09:00:00Z"
}
```

| Status | Code |
|---|---|
| `400` | `VALIDATION_ERROR` (email sai hoặc trùng email chủ) |
| `403` | `FORBIDDEN` (relative) |
| `409` | `INVITE_ALREADY_PENDING` |
| `409` | `RELATIONSHIP_EXISTS` (đã là người thân) |
| `429` | `RATE_LIMITED` |

- Gửi email chứa token **một lần** (server lưu hash, hạn **[TBD-D14]**).
- Chỉ chủ hộp mời; không giới hạn số lượng người thân, chỉ giới hạn tần suất.

### 6.2. `GET /api/relatives/invitations?deviceId={guid}` **[ĐỀ XUẤT]**

- **Auth:** Chủ hộp · `200 { "items": [ … ] }` — danh sách lời mời của hộp, kèm trạng thái.

### 6.3. `POST /api/relatives/invitations/{id}/revoke`

- **Auth:** Chủ hộp · `204 No Content`; set `Status=Revoked`; token không dùng được nữa.
- `403 FORBIDDEN`, `404 INVITE_NOT_FOUND`, `409 INVITE_NOT_PENDING`.

### 6.4. `POST /api/relatives/invitations/accept`

- **Auth:** Người được mời, **đã đăng nhập** và email tài khoản khớp email lời mời.

```json
{ "token": "<token-thô-từ-email>" }
```

**Response `200`**

```json
{
  "relationshipId": "rel1…",
  "deviceId": "a1b2…",
  "status": "Accepted",
  "acceptedAt": "2026-10-29T09:50:00Z"
}
```

| Status | Code | Khi nào |
|---|---|---|
| `403` | `INVITE_EMAIL_MISMATCH` | Email tài khoản khác email mời |
| `404` | `INVITE_NOT_FOUND` | Token sai |
| `409` | `INVITE_NOT_PENDING` | Đã dùng/thu hồi |
| `410` | `INVITE_EXPIRED` | Hết hạn |
| `409` | `RELATIONSHIP_EXISTS` | Đã là người thân |

- Tạo `CareRelationships` **một lần**, set invitation `Accepted`; transaction.
- Người chưa có tài khoản: email mời dẫn tới đăng ký + xác minh đúng email, sau đó quay lại `accept` với token; server **không** tự tạo tài khoản hay tự chấp nhận.
- `accept` phải rate-limit theo token + người gọi; token sai và token không tồn tại trả cùng `404 INVITE_NOT_FOUND`.

### 6.5. `GET /api/relatives/relationships?deviceId={guid}`

- **Auth:** Người tham gia quan hệ · **Response:**

```json
{
  "items": [
    {
      "id": "rel1…",
      "deviceId": "a1b2…",
      "serialNumber": "SMB-000123",
      "relativeUserId": "u2…",
      "relativeEmail": "relative@example.com",
      "status": "Active",
      "acceptedAt": "2026-10-29T09:50:00Z"
    }
  ]
}
```

- Bỏ `deviceId` để lấy tất cả quan hệ người dùng tham gia (owner hoặc relative) **[ĐỀ XUẤT]**.
- Không trả email người thân cho người thân khác (chỉ chủ hộp thấy danh sách đầy đủ) **[ĐỀ XUẤT]**.

### 6.6. `DELETE /api/relatives/relationships/{id}`

- **Auth:** Chủ hộp (thu hồi) hoặc chính người thân (ngừng theo dõi) · **Response:** `204`
- Set `RevokedAt`/`Status=Revoked`; hiệu lực **tức thì** với mọi endpoint đọc/cảnh báo.
- `403 FORBIDDEN` nếu không phải người tham gia; `404 RELATIONSHIP_NOT_FOUND`.

---

## 7. Dashboard, lịch sử, cảnh báo

<a id="dashboard"></a>

### 7.1. `GET /api/dashboard?deviceId={guid}`

- **Auth:** Owner/Relative · **Task:** P3-06

```json
{
  "device": {
    "id": "a1b2…",
    "serialNumber": "SMB-000123",
    "isOnline": false,
    "lastSeenAt": "2026-10-29T08:55:00Z",
    "scheduleVersion": 4
  },
  "today": {
    "date": "2026-10-29",
    "timeZoneId": "Asia/Ho_Chi_Minh",
    "doses": [
      {
        "id": "d1…",
        "compartmentSlot": 1,
        "scheduledAt": "2026-10-29T00:00:00Z",
        "deadlineAt": "2026-10-29T00:15:00Z",
        "status": "Unknown",
        "confirmedAt": null,
        "gracePeriodMinutes": 15,
        "items": [
          {
            "medicineName": "Vitamin C",
            "quantity": 1,
            "unit": "viên",
            "instructions": "Uống sau ăn"
          }
        ]
      }
    ]
  },
  "nextDose": null,
  "alerts": {
    "unreadCount": 2,
    "latest": [
      {
        "id": "al1…",
        "alertType": "UnconfirmedOffline",
        "message": "Không thể xác nhận do mất kết nối.",
        "createdAt": "2026-10-29T01:00:00Z",
        "readAt": null
      }
    ]
  }
}
```

- Trả **snapshot** thuốc tại thời điểm sinh dose, không phải dữ liệu lịch hiện tại.
- `today.timeZoneId` chỉ rõ cách nhóm "hôm nay"; nếu một hộp có nhiều timezone (D-10 chưa chốt), client không được tự cộng/trừ giờ — server phải trả mốc UTC và timezone áp dụng cho từng dose.
- `status` ∈ `Pending | Confirmed | Overdue | Unknown`; quy tắc chuyển trạng thái tại §10.4.
- `device.isOnline` là trạng thái **hiện tại** theo `LastSeenAt`; không dùng field này để suy ngược trạng thái lịch sử của dose.
- Không có trường/bản dịch nào khẳng định "đã uống".

### 7.2. `GET /api/dose-history`

Query: `deviceId` (bắt buộc), `from`, `to` (ISO date, tùy chọn), `status` (tùy chọn), `page`, `pageSize`.

```json
{
  "items": [
    {
      "id": "d1…",
      "scheduledAt": "2026-10-28T00:00:00Z",
      "deadlineAt": "2026-10-28T00:15:00Z",
      "status": "Confirmed",
      "confirmedAt": "2026-10-28T00:03:00Z",
      "compartmentSlot": 1,
      "items": [
        { "medicineName": "Canxi", "quantity": 2, "unit": "viên", "instructions": "Uống với nhiều nước" }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 42
}
```

- `status=Unknown` không được diễn giải thành "quên uống" ở bất kỳ tầng nào.
- `403 FORBIDDEN` nếu không thuộc hộp; `400 VALIDATION_ERROR` nếu `from > to`.

### 7.3. `GET /api/alerts`

Query: `deviceId` (bắt buộc), `unreadOnly` (mặc định `false`), `page`, `pageSize`.

```json
{
  "items": [
    {
      "id": "al1…",
      "doseInstanceId": "d1…",
      "alertType": "OverdueUnconfirmed",
      "message": "Lần uống 07:00 ngăn Sáng chưa được xác nhận.",
      "createdAt": "2026-10-28T00:20:00Z",
      "resolvedAt": null,
      "readAt": null
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 2,
  "unreadCount": 2
}
```

- Chỉ cảnh báo của hộp người gọi có quyền; relative thấy cảnh báo của mình (nếu cần `readAt` riêng) — `readAt` thuộc `AlertRecipients` của **người gọi** **[ĐỀ XUẤT]**.
- `resolvedAt` hiện là `null` và **chưa có quy tắc set** — chờ [TBD-D24]. Client không được suy diễn `resolvedAt = null` là "chưa uống".

### 7.4. `POST /api/alerts/{id}/read` **[ĐỀ XUẤT]**

- **Auth:** Owner/Relative có cảnh báo · **Response:** `200 { "readAt": "2026-10-29T10:00:00Z" }`
- Cập nhật `AlertRecipients.ReadAt` của người gọi; gọi lặp lại giữ nguyên thời điểm đầu **[ĐỀ XUẤT]**.
- `403 FORBIDDEN`, `404 ALERT_NOT_FOUND`.

---

## 8. Ảnh đơn thuốc

<a id="prescriptions"></a>

### 8.1. `GET /api/prescriptions/images` **[ĐỀ XUẤT]**

- **Auth:** Chủ hộp · `200 { "items": [ … ] }`

```json
{
  "items": [
    {
      "id": "p1…",
      "originalFileName": "don-thuoc.jpg",
      "contentType": "image/jpeg",
      "sizeBytes": 1048576,
      "uploadedAt": "2026-10-29T09:00:00Z"
    }
  ]
}
```

### 8.2. `POST /api/prescriptions/images`

- **Auth:** Chủ hộp · **Content-Type:** `multipart/form-data`, field `file` · **Task:** P6-02

| Status | Code | Khi nào |
|---|---|---|
| `201` | — | `{ "id": "p1…", "originalFileName": "…", "contentType": "image/jpeg", "sizeBytes": 1048576, "uploadedAt": "…" }` |
| `400` | `IMAGE_INVALID` | Ảnh hỏng/không giải mã được |
| `403` | `FORBIDDEN` | Relative hoặc người ngoài |
| `413` | `FILE_TOO_LARGE` | > 5 MB (5,242,880 bytes) |
| `415` | `FILE_TYPE_NOT_ALLOWED` | Không phải JPEG/PNG/WebP thật (magic bytes) |

- Loại bỏ EXIF trước khi lưu; tên lưu trữ ngẫu nhiên; CSDL chỉ metadata + storage key; file ngoài `wwwroot`.
- `contentType` lưu/trả theo loại **phát hiện từ magic bytes**, không tin `Content-Type`/đuôi file client gửi.
- Giới hạn request body/multipart để từ chối sớm file quá lớn; không buffer toàn bộ file vào RAM nếu vượt ngưỡng.
- [TBD-D16] Ảnh trùng lặp/giới hạn số lượng.

### 8.3. `GET /api/prescriptions/images/{id}/content`

- **Auth:** Chủ hộp · **Response:** binary với `Content-Type` gốc, `Cache-Control: private, no-store`, `Content-Disposition: inline; filename="…"`
- `401 UNAUTHORIZED`, `403 FORBIDDEN` (relative/người ngoài), `404 IMAGE_NOT_FOUND` (kể cả đã xóa mềm).
- Không có URL tĩnh/công khai; mọi lần đọc qua API kiểm tra quyền.

### 8.4. `DELETE /api/prescriptions/images/{id}`

- **Auth:** Chủ hộp · **Response:** `204`; xóa mềm + audit; chặn truy cập sau xóa; xử lý file theo [TBD-D16].

---

## 9. API cho ESP32

<a id="device-api"></a>

**Header bắt buộc cho cả 3 endpoint**

```
X-Device-Id: SMB-000123
X-Device-Api-Key: <khóa-thô-lưu-trong-thiết-bị>
```

- Sai/thiếu → `401 DEVICE_UNAUTHORIZED`.
- Mọi thời gian gửi lên dùng UTC ISO 8601.

### 9.1. `POST /api/device/heartbeat`

- **Auth:** Device (hoạt động cả khi **Unpaired**) · **Task:** P2-07

**Request**

```json
{
  "firmwareVersion": "0.1.0",
  "rtcTimeUtc": "2026-10-29T09:59:58Z",
  "pendingEventCount": 3
}
```

**Response `200`**

```json
{
  "pairingState": "Paired",
  "serverTimeUtc": "2026-10-29T10:00:00Z",
  "scheduleVersion": 4,
  "nextHeartbeatSeconds": 60
}
```

- Khi `Unpaired`: `scheduleVersion` = `0`/`null`, không kèm dữ liệu lịch.
- Server cập nhật `Devices.LastSeenAt` (dùng cho online/offline, D-12) **kể cả khi Unpaired**.
- `serverTimeUtc` là nguồn chỉnh RTC; thiết bị không được coi `rtcTimeUtc` của mình là chân lý. Ngưỡng lệch và tần suất sync **[TBD-D26]**.
- Idempotent; gọi lặp không tạo dữ liệu nghiệp vụ.

### 9.2. `GET /api/device/schedule-sync?version={int}`

- **Auth:** Device (Paired) · **Task:** P3-04

**Response `200` — có bản mới**

```json
{
  "version": 5,
  "serverTimeUtc": "2026-10-29T10:00:00Z",
  "generatedAtUtc": "2026-10-29T10:00:00Z",
  "schedules": [
    {
      "id": "s1…",
      "compartmentSlot": 1,
      "timeOfDay": "07:00",
      "daysOfWeek": [2, 4, 6],
      "startDate": "2026-10-01",
      "endDate": "2026-12-31",
      "gracePeriodMinutes": 15,
      "timeZoneId": "Asia/Ho_Chi_Minh",
      "items": [
        {
          "medicineName": "Vitamin C",
          "quantity": 1,
          "unit": "viên",
          "instructions": "Uống sau ăn"
        }
      ]
    }
  ]
}
```

- `timeZoneId` thuộc **từng lịch** (mỗi lịch có thể khác múi giờ); không có `timeZoneId` cấp gói.
- `version` là **ScheduleVersion cấp thiết bị**; tăng khi tạo/sửa/xóa mềm bất kỳ lịch nào, trong cùng transaction.
- `version` client gửi = phiên bản đang dùng; server trả **gói đầy đủ** khi khác, kèm danh sách lịch hiện hành (đã loại lịch xóa mềm).
- **Không đổi:** `204 No Content`.
- `409 DEVICE_NOT_PAIRED`: thiết bị chưa có chủ.
- Thiết bị chỉ thay lịch cục bộ khi nhận và áp dụng trọn gói; tải lỗi/parse lỗi phải giữ lịch cũ.
- **[TBD-D22]** Gói lịch mới và tập `DoseInstances` server dùng để ghép phải nhất quán khi lịch bị sửa; chưa chốt cách xử lý dose tương lai.

### 9.3. `POST /api/device/events/batch`

- **Auth:** Device (Paired) · **Task:** P4-02

**Request**

```json
{
  "batchId": "b1e2…",
  "events": [
    {
      "eventId": "018f2a7c-9d1e-7c3a-b1b2-3c4d5e6f7a8b",
      "compartmentSlot": 2,
      "eventType": "LidOpened",
      "occurredAt": "2026-10-02T05:03:11Z"
    },
    {
      "eventId": "018f2a7c-9d1e-7c3a-b1b2-3c4d5e6f7a8c",
      "compartmentSlot": 2,
      "eventType": "LidClosed",
      "occurredAt": "2026-10-02T05:03:20Z"
    }
  ]
}
```

**Response `200`** — kết quả từng sự kiện

```json
{
  "serverTimeUtc": "2026-10-02T05:05:00Z",
  "results": [
    {
      "eventId": "018f2a7c-9d1e-7c3a-b1b2-3c4d5e6f7a8b",
      "status": "Accepted",
      "matchedDoseInstanceId": "d1…",
      "doseStatus": "Confirmed"
    },
    {
      "eventId": "018f2a7c-9d1e-7c3a-b1b2-3c4d5e6f7a8c",
      "status": "Accepted",
      "matchedDoseInstanceId": null,
      "doseStatus": null
    }
  ]
}
```

- `status` mỗi phần tử: `Accepted` (đã ghi, có/không khớp dose), `Duplicate` (đã có `(DeviceId, EventId)`), `Rejected` (kèm `code`).
- Mã lỗi phần tử ví dụ: `EVENT_TIME_INVALID`, `COMPARTMENT_INVALID`, `EVENT_TYPE_INVALID`.
- **Idempotent:** `UNIQUE (DeviceId, EventId)`; gửi lại cùng batch nhiều lần không tạo thêm bản ghi/hiệu ứng nghiệp vụ (AC-05).
- Batch lỗi cấu trúc → `400 VALIDATION_ERROR`; khóa sai → `401`; chưa ghép → `409 DEVICE_NOT_PAIRED`.
- Phần tử `Rejected` **không** rollback các phần tử hợp lệ khác; mỗi event hợp lệ được ghi độc lập. Response luôn có đủ một kết quả cho mỗi `eventId`.
- Giới hạn **[ĐỀ XUẤT]** 1–100 sự kiện/batch.

**Ngữ nghĩa ghép dose (server-side)**

- Server xử lý các event hợp lệ theo thứ tự `occurredAt` tăng dần, tie-break bằng `eventId` — **không** theo thứ tự trong mảng; kết quả phải giống nhau dù batch bị xáo.
- Chỉ `LidOpened` được xét khớp; chỉ khi `ScheduledAt ≤ occurredAt ≤ DeadlineAt` và đúng ngăn của dose.
- **Lần mở hợp lệ đầu tiên** xác nhận dose; các lần mở hợp lệ sau vẫn được lưu event nhưng không đổi trạng thái/`ConfirmedAt`.
- Sai ngăn/mở sớm: lưu event, **không** xác nhận.
- Nắp mở sẵn: firmware không gửi `LidOpened` hợp lệ cho tới khi có cạnh đóng→mở (HW-03); server không tự suy diễn.
- Xác nhận muộn sau `Overdue`/`Unknown`: **[TBD-D24]** — hiện giữ nguyên trạng thái, không tự chuyển `Confirmed`.
- Lệch giờ thiết bị: `occurredAt` ở tương lai quá ngưỡng bị `Rejected EVENT_TIME_INVALID`; event cũ vẫn được ghi và ghép nếu rơi đúng cửa sổ. Ngưỡng/thời hạn tối đa **[TBD-D26]**.

---

## 10. Mô hình dùng chung & enum

<a id="models"></a>

### 10.1. `DoseDto` (rút gọn)

```json
{
  "id": "guid",
  "compartmentSlot": 1,
  "scheduledAt": "ISO-8601 UTC",
  "deadlineAt": "ISO-8601 UTC",
  "status": "Pending | Confirmed | Overdue | Unknown",
  "confirmedAt": "ISO-8601 UTC | null",
  "gracePeriodMinutes": 15,
  "items": [
    { "medicineName": "string", "quantity": 0, "unit": "string", "instructions": "string" }
  ]
}
```

### 10.2. Enum

| Enum | Giá trị **[ĐỀ XUẤT — D-09]** | Ghi chú |
|---|---|---|
| `DoseStatus` | `Pending`, `Confirmed`, `Overdue`, `Unknown` | Lưu chuỗi; nhãn UI: Chờ xác nhận / Đã mở / Quá hạn / Không thể xác nhận |
| `PairingState` | `Unpaired`, `Paired` | Heartbeat |
| `AlertType` | `OverdueUnconfirmed`, `UnconfirmedOffline` | Lưu `nvarchar(40)` |
| `DeviceEventType` | `LidOpened`, `LidClosed` | Chỉ `LidOpened` được ghép dose |
| `InvitationStatus` | `Pending`, `Accepted`, `Revoked`, `Expired` | |
| `RelationshipStatus` | `Active`, `Revoked` | |
| `EmailDeliveryStatus` | `Pending`, `Sent`, `Failed`, `Exhausted` | Nội bộ, không public API |

> Mọi giá trị enum phải khớp schema `nvarchar(30–40)` trong HTML và được chốt tại `P0-03` trước khi code.

### 10.3. `PagedResult<T>`

```json
{ "items": [], "page": 1, "pageSize": 20, "total": 0 }
```

### 10.4. Vòng đời trạng thái lần uống

<a id="dose-lifecycle"></a>

| Từ | Đến | Điều kiện | Ghi chú |
|---|---|---|---|
| `Pending` | `Confirmed` | `LidOpened` đúng ngăn, `ScheduledAt ≤ occurredAt ≤ DeadlineAt`, là lần mở hợp lệ đầu tiên | `ConfirmedAt = occurredAt`; nghĩa "đã mở", **không** phải "đã uống" |
| `Pending` | `Overdue` | Qua `DeadlineAt` mà thiết bị được coi là **online tại deadline** và chưa có xác nhận | Tạo cảnh báo `OverdueUnconfirmed` |
| `Pending` | `Unknown` | Qua `DeadlineAt` mà thiết bị **offline tại deadline** hoặc chưa từng thấy | Tạo cảnh báo `UnconfirmedOffline`; không kết luận "quên uống" |
| `Confirmed` | — | Trạng thái cuối | Không đổi ngược |
| `Overdue` / `Unknown` | `Confirmed`? | **[TBD-D24]** | Hợp đồng hiện **không** cho chuyển; event muộn vẫn được lưu |

- **Online tại deadline** nghĩa là `LastSeenAt >= DeadlineAt - ngưỡng offline`; không dùng thời điểm job chạy. `LastSeenAt = NULL` coi là offline.
- Job đánh giá có thể chạy muộn; kết luận phải dựa trên dữ liệu tại `DeadlineAt`, không phải trạng thái hiện tại của thiết bị.
- Không có trạng thái/field nào mang nghĩa "đã uống". `Confirmed` chỉ là "đã mở/đã xác nhận".
- Sửa/xóa lịch không được đổi bất kỳ dòng nào ở trên; dose tương lai đã sinh xử lý theo **[TBD-D22]**.

### 10.5. Vòng đời cảnh báo

<a id="alert-lifecycle"></a>

- Một cảnh báo nghiệp vụ được xác định bởi `(DoseInstanceId, AlertType)` **[ĐỀ XUẤT]**; chạy job lặp không tạo bản ghi mới.
- Tạo `Alerts` + toàn bộ `AlertRecipients` trong **một transaction**; người nhận = chủ hộp + mọi relative `Accepted` tại thời điểm đánh giá.
- `AlertRecipients` là snapshot người nhận của cảnh báo; retry chỉ tạo `EmailDeliveries`, không thêm/bớt recipient.
- Thu hồi quan hệ **trước** thời điểm đánh giá → không nhận cảnh báo. Thu hồi **sau** khi alert đã tạo nhưng trước khi gửi → **[TBD-D24]** (hiện không tự hủy delivery đã xếp hàng).
- `ResolvedAt`: **[TBD-D24]** — trường có trong response mẫu nhưng chưa có quy tắc set; không tự set khi xác nhận muộn.
- `ReadAt` là trạng thái đọc **theo từng người nhận**, không phải trạng thái của alert.

---

## 11. Ma trận phân quyền

<a id="authorization-matrix"></a>

| Nhóm endpoint | Anonymous | Chủ hộp | Người thân Accepted | Thiết bị |
|---|---|---|---|---|
| `auth/*`, `forgot-password`, `reset-password`, `verify-email` | ✔ | ✔ | ✔ | ✖ |
| `users/me` | ✖ | ✔ | ✔ | ✖ |
| `devices/pair` | ✖ | ✔ (email đã xác minh, chưa có hộp) | ✔ (nếu chưa có hộp) | ✖ |
| `devices/mine` | ✖ | ✔ | ✖ | ✖ |
| `schedules` GET | ✖ | ✔ | ✔ | ✖ |
| `schedules` POST/PUT/DELETE | ✖ | ✔ | ✖ (403) | ✖ |
| `relatives/invitations` tạo/list/revoke | ✖ | ✔ | ✖ (403) | ✖ |
| `relatives/invitations/accept` | ✖ | ✔ (nếu đúng email) | ✔ (nếu đúng email) | ✖ |
| `relatives/relationships` GET/DELETE | ✖ | ✔ (hộp mình) | ✔ (quan hệ của mình) | ✖ |
| `dashboard`, `dose-history`, `alerts` | ✖ | ✔ | ✔ (chỉ hộp theo dõi) | ✖ |
| `prescriptions/*` | ✖ | ✔ | ✖ (403) | ✖ |
| `device/heartbeat` | ✖ | ✖ | ✖ | ✔ (kể cả Unpaired) |
| `device/schedule-sync`, `device/events/batch` | ✖ | ✖ | ✖ | ✔ (Paired) |

Nguyên tắc: kiểm tra quyền theo **hộp**, không theo `userId` client gửi; người ngoài nhận `403` hoặc `404` thống nhất để tránh lộ tồn tại.

---

## 12. Validation rules

<a id="validation"></a>

| Trường/nguồn | Quy tắc | Mã lỗi |
|---|---|---|
| `email` | Bắt buộc, đúng định dạng, ≤ 320, trim/lowercase | `VALIDATION_ERROR` |
| `password` | **[TBD-D14]** đề xuất ≥ 8, ≤ 128, có chữ + số | `VALIDATION_ERROR` |
| `pairing code` | **[TBD-D08]** đúng định dạng, chưa dùng, chưa hết hạn | `PAIRING_CODE_INVALID` / `PAIRING_CODE_USED` / `PAIRING_CODE_EXPIRED` |
| `compartmentSlot` | Số nguyên 1–3, thuộc đúng thiết bị | `COMPARTMENT_INVALID` |
| `timeOfDay` | `HH:mm`, 00:00–23:59 | `VALIDATION_ERROR` |
| `daysOfWeek` | 1–7, không trùng, ≥ 1 phần tử | `VALIDATION_ERROR` |
| `startDate` / `endDate` | Ngày hợp lệ; `endDate` null hoặc ≥ `startDate` | `VALIDATION_ERROR` |
| lịch trùng | Cùng `compartmentSlot` + `timeOfDay` + khung ngày giao nhau → từ chối **[ĐỀ XUẤT]**; chồng lấn một phần **[TBD-D23]** | `VALIDATION_ERROR` / `SCHEDULE_OVERLAP` **[nếu chốt]** |
| `gracePeriodMinutes` | Số nguyên 1–120 | `VALIDATION_ERROR` |
| `timeZoneId` | IANA hợp lệ; thuộc từng lịch; nguồn chọn **[TBD-D10]** | `SCHEDULE_INVALID_TIMEZONE` |
| `items` | ≥ 1 phần tử | `VALIDATION_ERROR` |
| `medicineName` | Bắt buộc, ≤ 200 | `VALIDATION_ERROR` |
| `quantity` | > 0, tối đa `decimal(10,2)` | `VALIDATION_ERROR` |
| `unit` | Bắt buộc, ≤ 50 | `VALIDATION_ERROR` |
| `instructions` | ≤ 500 | `VALIDATION_ERROR` |
| ảnh | ≤ 5 MB; JPEG/PNG/WebP theo magic bytes; giải mã được | `FILE_TOO_LARGE` / `FILE_TYPE_NOT_ALLOWED` / `IMAGE_INVALID` |
| event batch | ≥ 1 và ≤ 100 **[ĐỀ XUẤT]**; `eventId` ≤ 100 ký tự; `eventType` hợp lệ; `occurredAt` không ở tương lai quá ngưỡng **[TBD-D26]**, không cũ hơn thời hạn lưu/ghép **[TBD-D26]** | `VALIDATION_ERROR` / `EVENT_TIME_INVALID` / `EVENT_TYPE_INVALID` |
| event trùng trong cùng batch | Cùng `eventId` xuất hiện nhiều lần: lần đầu xử lý, các lần sau trả `Duplicate` (không lỗi cả batch) | — |
| `page` / `pageSize` | `page ≥ 1`; `1 ≤ pageSize ≤ 100` | `VALIDATION_ERROR` |
| `from` / `to` | `from ≤ to` | `VALIDATION_ERROR` |

---

## 13. Idempotency

<a id="idempotency"></a>

| Nghiệp vụ | Cơ chế | Hành vi khi lặp |
|---|---|---|
| Nhận sự kiện thiết bị | `UNIQUE (DeviceId, EventId)` | Trả `Duplicate`, không tạo bản ghi/hiệu ứng mới |
| Ghép QR | Pairing code hash + `UsedAt` + transaction | `409 PAIRING_CODE_USED`, không đổi chủ |
| Xác minh email / đặt lại mật khẩu | Token hash một lần + hạn | Token dùng lại bị từ chối |
| Chấp nhận lời mời | Token một lần + filtered unique active invite/relationship | `409 INVITE_NOT_PENDING` / `RELATIONSHIP_EXISTS` |
| Tạo cảnh báo | **[ĐỀ XUẤT]** `UNIQUE (DoseInstanceId, AlertType)` | Không sinh alert trùng |
| Người nhận cảnh báo | `UNIQUE (AlertId, RecipientUserId, Channel)` + tạo alert/recipients trong một transaction | Không thêm recipient trùng; không có alert thiếu người nhận |
| Retry email | Mỗi lần thử là dòng `EmailDeliveries` mới; `UNIQUE (AlertRecipientId, AttemptNumber)`; claim bằng `LockedUntil` | Không gửi trùng nghiệp vụ; retry độc lập từng người; hai worker không cùng gửi một delivery |
| Heartbeat | Chỉ cập nhật `LastSeenAt` | Gọi lặp an toàn |
| Schedule sync | GET theo `version` | Không đổi trạng thái |
| Schedule CRUD | `PUT` thay thế toàn bộ; version cấp thiết bị tăng trong transaction | Gọi lặp không nhân đôi ngày/thuốc; DELETE lặp trả 404 |
| Upload ảnh | Không có khóa idempotency | **[TBD-D16]** có thể tạo bản ghi trùng nếu client gửi lại |
| Đăng ký | Unique email | `409 EMAIL_TAKEN` |

---

## 14. Error code catalog

<a id="errors"></a>

| Code | HTTP | Ý nghĩa |
|---|---|---|
| `VALIDATION_ERROR` | 400 | Dữ liệu không hợp lệ (kèm `errors`) |
| `UNAUTHORIZED` | 401 | Thiếu xác thực |
| `TOKEN_INVALID` | 400/401 | Token sai/đã dùng |
| `TOKEN_EXPIRED` | 410 | Token hết hạn |
| `INVALID_CREDENTIALS` | 401 | Sai email/mật khẩu |
| `EMAIL_NOT_VERIFIED` | 403 | Chưa xác minh email |
| `FORBIDDEN` | 403 | Không có quyền |
| `NOT_FOUND` | 404 | Không tìm thấy |
| `DEVICE_NOT_FOUND` | 404 | Hộp không tồn tại/không thuộc quyền |
| `DEVICE_NOT_PAIRED` | 409 | Thiết bị chưa được ghép |
| `DEVICE_UNAUTHORIZED` | 401 | Sai `DeviceId`/`DeviceApiKey` |
| `EMAIL_TAKEN` | 409 | Email đã đăng ký |
| `PAIRING_CODE_INVALID` | 400 | Mã ghép sai |
| `PAIRING_CODE_USED` | 409 | Mã ghép đã dùng |
| `PAIRING_CODE_EXPIRED` | 410 | Mã ghép hết hạn |
| `USER_ALREADY_OWNS_DEVICE` | 409 | Tài khoản đã có hộp |
| `DEVICE_ALREADY_PAIRED` | 409 | Hộp đã có chủ |
| `INVITE_ALREADY_PENDING` | 409 | Đã có lời mời đang chờ |
| `INVITE_NOT_FOUND` | 404 | Token/lời mời không tồn tại |
| `INVITE_NOT_PENDING` | 409 | Lời mời đã dùng/thu hồi |
| `INVITE_EXPIRED` | 410 | Lời mời hết hạn |
| `INVITE_EMAIL_MISMATCH` | 403 | Email tài khoản không khớp |
| `RELATIONSHIP_EXISTS` | 409 | Đã là người thân |
| `RELATIONSHIP_NOT_FOUND` | 404 | Quan hệ không tồn tại |
| `SCHEDULE_NOT_FOUND` | 404 | Lịch không tồn tại |
| `SCHEDULE_INVALID_TIMEZONE` | 400 | Timezone không hợp lệ |
| `SCHEDULE_OVERLAP` | 409 | Lịch trùng/chồng lấn cùng ngăn **[chỉ dùng nếu D-23 chốt quy tắc]** |
| `SCHEDULE_VERSION_CONFLICT` | 409 | Xung đột phiên bản khi PUT **[nếu áp dụng]** |
| `COMPARTMENT_INVALID` | 400 | Ngăn không hợp lệ/không thuộc hộp |
| `EVENT_TIME_INVALID` | 200 (phần tử `Rejected`) | Thời điểm sự kiện không hợp lệ |
| `EVENT_TYPE_INVALID` | 200 (phần tử `Rejected`) | Loại sự kiện không hỗ trợ |
| `ALERT_NOT_FOUND` | 404 | Cảnh báo không tồn tại |
| `FILE_TOO_LARGE` | 413 | File > 5 MB |
| `FILE_TYPE_NOT_ALLOWED` | 415 | Không phải ảnh cho phép |
| `IMAGE_INVALID` | 400 | Ảnh hỏng |
| `IMAGE_NOT_FOUND` | 404 | Ảnh không tồn tại/đã xóa |
| `RATE_LIMITED` | 429 | Vượt ngưỡng |
| `CONFLICT` | 409 | Xung đột chung |
| `INTERNAL_ERROR` | 500 | Lỗi hệ thống |

---

## 15. TBD & quyết định cần chốt

<a id="tbd"></a>

| Mã | Nội dung | Ảnh hưởng endpoint | Chốt tại |
|---|---|---|---|
| D-01 | Web UI: Razor/MVC hay frontend riêng | Toàn bộ Web | **Đã chốt 2026-10-29: Razor/MVC trong `SmartMedBox.Api`** |
| D-02 | Cookie session hay JWT + refresh | `/api/auth/*`, `/api/users/me` | **Đã chốt 2026-10-29: cookie session + CSRF; không có `/auth/refresh`, không có `RefreshTokens`** |
| D-03 | Email provider | Toàn bộ luồng email | P5-03 |
| D-04 | Lưu file local hay blob | `/api/prescriptions/*` | P6-01 |
| D-05 | Công cụ provision thiết bị | Không public API | P2-02 |
| D-07 | Header xác thực thiết bị | `/api/device/*` | P2-06 |
| D-08 | Định dạng pairing code, payload QR, hạn dùng | `/api/devices/pair` | P2-02, P2-03 |
| D-09 | Giá trị enum & `daysOfWeek` quy ước | Nhiều endpoint | P0-03 |
| D-10 | Nguồn timezone; một hay nhiều timezone/hộp; dashboard "hôm nay" nhóm theo timezone nào | `/api/schedules`, dashboard | P3-01, P3-06 |
| D-11 | Horizon/tần suất sinh dose | `/api/dashboard`, history | P3-03 |
| D-12 | Ngưỡng online/offline | `/api/devices/mine`, dashboard, alerts | P4-04 |
| D-13 | Retry policy email | Không public API | P5-03 |
| D-14 | Chính sách mật khẩu, TTL token, rate limit, hạn lời mời | Auth, invitations | P1-04, P1-06, P2-09 |
| D-15 | Xác nhận muộn sau Overdue | Events batch, history | Đã gộp vào D-24 |
| D-16 | Ảnh trùng & retention sau xóa | Prescriptions | P6-02, P6-04 |
| D-17 | Ẩn danh dữ liệu khi xóa tài khoản | (chưa có endpoint) | P7-06 |
| D-18 | Trường hồ sơ người dùng & có endpoint cập nhật hay không | `/api/users/me` | P1-03 |
| D-19 | Retention log/audit/ảnh | Không public API | P6-05, P7-01, P7-04, P7-06 |
| D-20 | Hình thức quét QR trên Web | `/api/devices/pair` (UI) | P2-03, UI-02 |
| D-21 | Độ bền queue sự kiện khi mất điện | Không public API | HW-03 |
| D-22 | Sửa lịch & dose tương lai đã sinh; nhất quán với schedule-sync | `/api/schedules/*`, `/api/device/schedule-sync`, `/api/device/events/batch` | P3-02, P3-03, P3-04 |
| D-23 | Lịch trùng/chồng lấn cùng ngăn | `/api/schedules` | P3-02, P3-03 |
| D-24 | Vòng đời cảnh báo, xác nhận muộn, `ResolvedAt`, recipient bị thu hồi sau khi tạo alert | `/api/device/events/batch`, `/api/alerts` | P3-05, P4-03, P5-02 |
| D-25 | Cách ESP32 nhận Wi‑Fi/server URL khi onboarding | Không public API | HW-01, HW-04 |
| D-26 | Ngưỡng lệch giờ/RTC, event quá cũ/tương lai | `/api/device/*` | P4-02, HW-02, HW-04 |

---

## 16. Ghi chú nhất quán

- Tên endpoint ở trên có thể được điều chỉnh khi triển khai, nhưng phải cập nhật đồng thời [PLAN.md](PLAN.md), [TASKS.md](TASKS.md) và tài liệu này.
- Mọi endpoint người dùng phải kiểm tra quyền theo hộp theo §11, kể cả endpoint đọc.
- Mọi endpoint thiết bị phải xác thực `DeviceId + DeviceApiKey`; không chấp nhận token người dùng.
- Không endpoint nào được trả `DeviceApiKey`, hash, pairing code thô hoặc đường dẫn vật lý.
- Nội dung hiển thị/email phải theo [AGENTS.md](AGENTS.md) §2.2: "đã mở/đã xác nhận", không "đã uống".
- `ScheduleVersion` là **cấp thiết bị**, tăng khi tạo/sửa/xóa mềm bất kỳ lịch; `timeZoneId` thuộc từng lịch.
- Đánh giá `Overdue`/`Unknown` dùng trạng thái kết nối tại `DeadlineAt`, không dùng thời điểm job chạy.
- `resolvedAt` chưa có quy tắc set (D-24); client không được suy diễn từ `null`.
- `occurredAt` của thiết bị là nguồn thời gian ghép; server trả `serverTimeUtc` để đồng bộ RTC (D-26).
- Gửi email là at-least-once cho **cùng** một người trong tình huống crash hiếm; tuyệt đối không lặp sang người khác. Ghi rõ trong tài liệu vận hành.
