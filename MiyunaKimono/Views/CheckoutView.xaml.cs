using Microsoft.Win32;
using MiyunaKimono.Services;
using System;
using System.Configuration; // ด้านบนไฟล์
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MiyunaKimono.Views
{
    public partial class CheckoutView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // ---- Bind เหมือน CartView ----
        public System.Collections.ObjectModel.ObservableCollection<CartLine> Lines
            => CartService.Instance.Lines;

        public int ItemsCount => Lines.Sum(l => l.Quantity);
        public string ItemsCountText => $"{ItemsCount} Item";
        public decimal DiscountTotal => Lines.Sum(l =>
        {
            var price = l.Product.Price;
            var after = l.Product.PriceAfterDiscount ?? price;
            return (price - after) * l.Quantity;
        });
        public string DiscountTotalText => $"{DiscountTotal:N0}";
        public decimal GrandTotal => Lines.Sum(l => l.LineTotal);
        public string GrandTotalText => $"{GrandTotal:N0}";

        // ---- QR ----
        private readonly DispatcherTimer _qrTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int _qrRemain = 59;
        public string QrRemainText => _qrRemain.ToString();

        private byte[] _receiptBytes;   // เก็บไฟล์สลิป
        private string _receiptPath;    // แสดงในกล่อง

        public CheckoutView()
        {
            InitializeComponent();
            DataContext = this;

            // ทำ QR แรก
            MakeQr();

            _qrTimer.Tick += (_, __) =>
            {
                if (_qrRemain > 0)
                {
                    _qrRemain--;
                    PropertyChanged?.Invoke(this, new(nameof(QrRemainText)));
                    if (_qrRemain == 0) BtnResetQr.Visibility = Visibility.Visible;
                }
            };
            _qrTimer.Start();
        }

        private void MakeQr()
        {
            // ✏️ ใส่เบอร์ PromptPay แบบ 10 หลักขึ้นต้น 0 (ไม่ต้องใส่ขีดหรือช่องว่าง)
            const string PROMPTPAY_MOBILE = "0800316386";  // <-- ใส่เบอร์คุณตรงนี้

            var amount = GrandTotal;

            // โค้ดด้านล่างไม่ต้องแก้ ถ้าใช้ BuildMobilePayload เวอร์ชันที่ผมให้ไป
            var payload = PromptPayQr.BuildMobilePayload(PROMPTPAY_MOBILE, amount);

            var generator = new QRCoder.QRCodeGenerator();
            var data = generator.CreateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
            var code = new QRCoder.PngByteQRCode(data);
            var bytes = code.GetGraphic(7);

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            QrImage.Source = bmp;

            _qrRemain = 59;
            BtnResetQr.Visibility = Visibility.Collapsed;
            PropertyChanged?.Invoke(this, new(nameof(QrRemainText)));
        }


        private void ResetQr_Click(object sender, RoutedEventArgs e) => MakeQr();

        private void UploadReceipt_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select payment receipt",
                Filter = "Images/PDF|*.png;*.jpg;*.jpeg;*.pdf",
                Multiselect = false
            };
            if (dlg.ShowDialog() == true)
            {
                _receiptPath = dlg.FileName;
                ReceiptPathBox.Text = _receiptPath;
                _receiptBytes = File.ReadAllBytes(_receiptPath);
            }
        }

        // แจ้งให้ parent (UserMainWindow) ไปหน้า Home
        public event Action BackRequested;

        // ★ เพิ่มอีเวนต์แจ้งว่าออเดอร์สำเร็จแล้ว (ให้ UserMainWindow รีโหลดสินค้า)
        public event Action OrderCompleted;

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

        private async void Checkout_Click(object sender, RoutedEventArgs e)
        {
            if (_receiptBytes == null || _receiptBytes.Length == 0)
            {
                MessageBox.Show("กรุณาอัปโหลดสลิปก่อน", "Upload required",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (Lines.Count == 0)
            {
                MessageBox.Show("ตะกร้าว่าง", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // ดึงข้อมูลผู้ใช้/ที่อยู่ล่าสุดจาก cart (บันทึกไว้ใน CartView ตอนกด Checkout ของ Cart)
                var userId = AuthService.CurrentUserIdSafe();
                var addr = CartPersistenceService.Instance.LastAddressForOrder ?? "";

                var u = Session.CurrentUser; // มี FirstName, LastName, Username, Email
                var fullName = $"{u?.First_Name} {u?.Last_Name}".Trim();
                var username = u?.Username ?? "";
                var telOrEmail = u?.Email ?? ""; // ถ้าไม่มีเบอร์โทร ใช้อีเมลแทนชั่วคราว
                var userEmail = u?.Email; // ⬅️ **(ใหม่) ดึง Email สำหรับส่ง**

                var orderId = await OrderService.Instance.CreateOrderFullAsync(
                    userId: userId,
                    customerFullName: fullName,
                    username: username,
                    address: addr,
                    tel: telOrEmail,
                    lines: Lines.ToList(),
                    total: GrandTotal,
                    discount: DiscountTotal,
                    receiptBytes: _receiptBytes,
                    receiptFileName: System.IO.Path.GetFileName(_receiptPath)
                );

                // (ของเดิม) ออกรายงาน PDF และแสดงผล
                var pdfPath = ReceiptPdfMaker.Create(
                    orderId,
                    Lines.ToList(),
                    GrandTotal,
                    new SessionProfileProvider(),   // <- โปรไวเดอร์เล็ก ๆ ด้านล่าง
                    addr
                );
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });

                // ----- ⬇️ (ใหม่) ส่วนของการส่งอีเมล ⬇️ -----
                if (!string.IsNullOrEmpty(userEmail))
                {
                    try
                    {
                        var emailService = new EmailService(); //
                        var subject = $"ขอบคุณสำหรับคำสั่งซื้อ #{orderId} - MiyunaKimono";

                        // สร้างรายการสินค้าในอีเมล
                        var productsHtml = new System.Text.StringBuilder();
                        foreach (var line in Lines)
                        {
                            productsHtml.AppendFormat(
                                "<tr><td>{0}</td><td style='text-align: center;'>{1}</td><td style='text-align: right;'>{2:N2}</td></tr>",
                                line.Product.ProductName,
                                line.Quantity,
                                line.LineTotal
                            );
                        }

                        // สร้างเนื้อหาอีเมล
                        var htmlBody = $@"
<html>
<body style='font-family: Arial, sans-serif; font-size: 14px;'>
    <h2>ขอบคุณสำหรับการสั่งซื้อค่ะ 🌸</h2>
    <p>สวัสดีค่ะคุณ {fullName},</p>
    <p>ขอบคุณที่เลือกซื้อสินค้ากับ <b>MiyunaKimono</b> นะคะ</p>
    <p>คำสั่งซื้อของคุณ (<b>#{orderId}</b>) ได้รับเข้าระบบเรียบร้อยแล้ว และ<b>กำลังรอการตรวจสอบสลิปโอนเงิน</b>ค่ะ</p>
    <p>เราจะรีบดำเนินการและแจ้งสถานะการจัดส่งให้ทราบโดยเร็วที่สุดค่ะ</p>
    <br/>
    <h3>สรุปรายการสั่งซื้อ (ใบเสร็จ)</h3>
    <table border='1' cellpadding='8' style='border-collapse: collapse; width: 90%;'>
      <thead style='background-color: #f4f4f4;'>
        <tr>
          <th>สินค้า</th>
          <th>จำนวน</th>
          <th>ราคารวม</th>
        </tr>
      </thead>
      <tbody>
        {productsHtml}
      </tbody>
      <tfoot>
        <tr>
          <td colspan='2' style='text-align: right; font-weight: bold;'>ยอดรวมสุทธิ</td>
          <td style='text-align: right; font-weight: bold;'>{GrandTotal:N2} บาท</td>
        </tr>
      </tfoot>
    </table>
    <br/>
    <p>ขอบคุณที่ให้เราเป็นส่วนหนึ่งในช่วงเวลาดีๆ ของคุณนะคะ</p>
    <p>ด้วยความปรารถนาดี,<br/>ทีมงาน MiyunaKimono</p>
</body>
</html>";

                        // ส่งอีเมล
                        await emailService.SendAsync(userEmail, subject, htmlBody); //
                    }
                    catch (Exception emailEx)
                    {
                        // หากส่งอีเมลไม่สำเร็จ ก็ไม่ควรขัดขวางการสั่งซื้อ
                        // แค่แสดงข้อความเตือนเล็กน้อย
                        MessageBox.Show("การสั่งซื้อสำเร็จ แต่ส่งอีเมลยืนยันไม่สำเร็จ: " + emailEx.Message,
                                        "Email Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                // ----- ⬆️ (ใหม่) จบส่วนของการส่งอีเมล ⬆️ -----


                // (ของเดิม) เคลียร์ตะกร้า + บันทึกสถานะล่าสุด
                CartService.Instance.Clear();
                CartPersistenceService.Instance.Save(userId, Lines.ToList()); // จะว่าง

                MessageBox.Show("ทำรายการสั่งซื้อสำเร็จ โปรดตรวจสอบสถานะสินค้า และอีเมลยืนยัน", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                // ★ แจ้ง UserMainWindow ให้รีโหลดข้อมูลสินค้า/สต็อกใหม่
                OrderCompleted?.Invoke();

                BackRequested?.Invoke(); // กลับ Home
            }
            catch (Exception ex)
            {
                MessageBox.Show("ทำรายการไม่สำเร็จ: " + ex.Message);
            }
        }
    }
    internal sealed class SessionProfileProvider : IUserProfileProvider
    {
        public int CurrentUserId => AuthService.CurrentUserIdSafe();

        public string FullName(int userId)
        {
            var u = Session.CurrentUser; //
            return $"{u?.First_Name} {u?.Last_Name}".Trim(); //
        }

        public string Username(int userId)
            => Session.CurrentUser?.Username ?? ""; //

        public string Phone(int userId)
            => Session.CurrentUser?.Email ?? ""; // ถ้าไม่มีเบอร์โทรจริง ใช้อีเมลแทนชั่วคราว
    }

    // ===== PromptPay EMVCo payload แบบย่อ =====
    // ===== PromptPay EMVCo payload แบบถูกสเปก =====
    // ===== PromptPay EMVCo payload (Mobile) แบบถูกสเปก =====
    internal static class PromptPayQr
    {
        // TLV helper (id + 2-digit length + value)
        private static string TLV(string id, string value)
            => id + value.Length.ToString("00") + value;

        /// <summary>
        /// สร้าง Payload สำหรับ PromptPay (มือถือ) — amount เป็นยอดชำระแบบมีทศนิยม 2 หลัก
        /// mobileInput รับ "0xxxxxxxxx" หรือ "66xxxxxxxxx" หรือ "0066xxxxxxxxx" ก็ได้
        /// </summary>
        public static string BuildMobilePayload(string mobileInput, decimal amount)
        {
            // 1) ทำเบอร์ให้เป็นรูปแบบ 0066 + เบอร์ไม่เอา 0 นำหน้า
            var digits = new string(mobileInput.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0066"))
            {
                digits = digits; // ok
            }
            else if (digits.StartsWith("66"))
            {
                digits = "00" + digits; // -> 0066...
            }
            else if (digits.StartsWith("0"))
            {
                digits = "0066" + digits.Substring(1);
            }
            else
            {
                // ถ้าใส่อย่างอื่นมา (เช่น 8xxxxxxxx) ให้ถือว่าเป็นเบอร์ไทยไม่ใส่ศูนย์ -> เติม 0066 เอง
                digits = "0066" + digits;
            }

            // 2) Merchant Account Info (PromptPay) ใช้ Tag 29
            //   - Subtag 00 = AID "A000000677010111"
            //   - Subtag 01 = mobile (0066xxxxxxxxx)
            string merchantAcc =
                TLV("00", "A000000677010111") +
                TLV("01", digits);
            string tag29 = TLV("29", merchantAcc);

            // 3) จำนวนเงินต้องเป็น 2 ตำแหน่งทศนิยมเสมอ
            string amt = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            // 4) ประกอบ payload ตามสเปก
            //    00 = Payload Format Indicator ("01")
            //    01 = Point of Initiation Method ("12" = Dynamic QR)
            //    29 = Merchant Account Info (PromptPay)
            //    52 = MCC (0000)
            //    53 = Currency (764 = THB)
            //    54 = Amount
            //    58 = Country (TH)
            //    59 = Merchant Name (<=25 ตัวอักษรได้)
            //    60 = City (<=15 ตัวอักษรได้)
            //    63 = CRC (ความยาว 04) — ใส่ "6304" ไว้ก่อน แล้วค่อยคำนวณ CRC ต่อท้าย
            string payloadNoCrc =
                TLV("00", "01") +
                TLV("01", "12") +
                tag29 +
                TLV("52", "0000") +
                TLV("53", "764") +
                TLV("54", amt) +
                TLV("58", "TH") +
                TLV("59", "Miyuna") +
                TLV("60", "Bangkok") +
                "6304";

            string crc = Crc16CcittFalse(payloadNoCrc);
            return payloadNoCrc + crc;
        }

        // CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF)
        private static string Crc16CcittFalse(string s)
        {
            ushort poly = 0x1021;
            ushort reg = 0xFFFF;
            var bytes = System.Text.Encoding.ASCII.GetBytes(s);
            foreach (var b in bytes)
            {
                reg ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    reg = ((reg & 0x8000) != 0)
                        ? (ushort)((reg << 1) ^ poly)
                        : (ushort)(reg << 1);
                }
            }
            return reg.ToString("X4"); // UPPER HEX 4 หลัก
        }
    }


}