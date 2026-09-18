# Thư viện bên thứ ba (nhúng sẵn)

| Tệp | Thư viện | Phiên bản | Giấy phép | Nguồn |
|---|---|---|---|---|
| `chart.umd.min.js` | Chart.js | 4.4.1 | MIT | `dist/chart.umd.js` trong gói npm `chart.js@4.4.1` |

Kiểm tra lại tệp `chart.umd.min.js`:

- Gói npm `chart.js-4.4.1.tgz` có integrity
  `sha512-C74QN1bxwV1v2PEujhmKjOZ7iUM4w6BWs23Md/6aOZZSlwMzeCIDGuZay++rBgChYru7/+QFeoQW0fQoP534Dg==`
  (khớp với giá trị registry.npmjs.org công bố).
- SHA-256 của tệp: `74401d738dd3e03ee5dfb3b6841210fe2c4ead8a960c4011ca4ba0b78a9fd8f3`.

Tệp được chép NGUYÊN VĂN từ gói, kể cả dòng thông báo bản quyền ở đầu — giấy phép MIT yêu
cầu giữ dòng đó. Không nén lại hay sửa tay: làm vậy thì không còn đối chiếu được với bản gốc,
và cũng không biết mình đang chạy phiên bản nào khi có bản vá bảo mật.

Nâng phiên bản: tải `chart.js@<phiên bản>` từ npm, chép `dist/chart.umd.js` đè lên tệp này,
rồi cập nhật bảng và hai giá trị băm ở trên.
