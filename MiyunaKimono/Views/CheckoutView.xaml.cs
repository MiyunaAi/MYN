using Microsoft.Win32;
using MiyunaKimono.Services;
using System;
using System.Configuration;
using System.ComponentModel;
using System.IO; // <-- ต้องมี
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging; // <-- ต้องมี
using System.Windows.Threading;

namespace MiyunaKimono.Views
{
    public partial class CheckoutView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        // ---- Bindings ----
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

        // --- (ใหม่) ตัวแปรสำหรับสลิป ---
        private byte[] _receiptBytes;   // (บีบอัดแล้ว)
        private string _receiptPath;    // (แค่แสดงในกล่อง)
        private string _finalReceiptFileName; // (ชื่อไฟล์ที่จะบันทึกลง DB)

        public CheckoutView()
        {
            InitializeComponent();
            DataContext = this;
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
            // (โค้ด MakeQr ไม่เปลี่ยนแปลง)
            const string PROMPTPAY_MOBILE = "0800316386";
            var amount = GrandTotal;
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

        // ----- ⬇️ (FIXED) อัปเดต Method นี้ทั้งหมด (เพิ่มการบีบอัด) ⬇️ -----
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
                ReceiptPathBox.Text = _receiptPath; // แสดงชื่อไฟล์เดิม
                _finalReceiptFileName = Path.GetFileName(_receiptPath); // เก็บชื่อไฟล์เดิม

                string extension = Path.GetExtension(_receiptPath).ToLower();
                byte[] originalBytes = File.ReadAllBytes(_receiptPath);

                // ถ้าเป็น PDF หรือ ไฟล์เล็กอยู่แล้ว (น้อยกว่า 500KB) ให้อัปโหลดไปเลย
                if (extension == ".pdf" || originalBytes.Length < 500 * 1024)
                {
                    _receiptBytes = originalBytes;
                }
                else // ถ้าเป็นรูปภาพขนาดใหญ่ ให้บีบอัด
                {
                    try
                    {
                        // 1. โหลดรูป
                        using (var msIn = new MemoryStream(originalBytes))
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = msIn;
                            bmp.EndInit();

                            // 2. สร้างตัวบีบอัดเป็น JPEG (คุณภาพ 50%)
                            var encoder = new JpegBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bmp));
                            encoder.QualityLevel = 50;

                            // 3. บันทึกผลลัพธ์ลง MemoryStream ใหม่
                            using (var msOut = new MemoryStream())
                            {
                                encoder.Save(msOut);
                                _receiptBytes = msOut.ToArray(); // ได้ byte[] ที่เล็ก_ลง

                                // 4. เปลี่ยนชื่อไฟล์ที่จะบันทึกเป็น .jpg
                                _finalReceiptFileName = Path.GetFileNameWithoutExtension(_receiptPath) + ".jpg";
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // ถ้าไฟล์นามสกุล .jpg แต่ไม่ใช่รูปภาพ (เช่น ไฟล์เสีย)
                        // ให้ใช้ไฟล์เดิมไปเลย
                        _receiptBytes = originalBytes;
                    }
                }
            }
        }
        // ----- ⬆️ (FIXED) จบการแก้ไข ⬆️ -----

        public event Action BackRequested;
        public event Action OrderCompleted;

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

        // ----- ⬇️ (FIXED) อัปเดต Method นี้ (ลบ Checkpoint) ⬇️ -----
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
                var userId = AuthService.CurrentUserIdSafe();
                var addr = CartPersistenceService.Instance.LastAddressForOrder ?? "";
                var u = Session.CurrentUser;
                var fullName = $"{u?.First_Name} {u?.Last_Name}".Trim();
                var username = u?.Username ?? "";
                var telOrEmail = u?.Email ?? "";
                var userEmail = u?.Email;

                // (ลบ Checkpoint 1)
                var orderId = await OrderService.Instance.CreateOrderFullAsync(
                    userId: userId,
                    customerFullName: fullName,
                    username: username,
                    address: addr,
                    tel: telOrEmail,
                    lines: Lines.ToList(),
                    total: GrandTotal,
                    discount: DiscountTotal,
                    receiptBytes: _receiptBytes, // (บีบอัดแล้ว)
                    receiptFileName: _finalReceiptFileName // (ชื่อไฟล์ .jpg หรือ .pdf)
                );

                // (ลบ Checkpoint 2)
                var pdfPath = ReceiptPdfMaker.Create(
                    orderId,
                    Lines.ToList(),
                    GrandTotal,
                    new SessionProfileProvider(),
                    addr
                );
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });

                // (ลบ Checkpoint 3)
                if (!string.IsNullOrEmpty(userEmail))
                {
                    try
                    {
                        var emailService = new EmailService();
                        var subject = $"ขอบคุณสำหรับคำสั่งซื้อ #{orderId} - MiyunaKimono";

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

                        await emailService.SendAsync(userEmail, subject, htmlBody);
                        // (ลบ Checkpoint 4)
                    }
                    catch (Exception emailEx)
                    {
                        MessageBox.Show("การสั่งซื้อสำเร็จ แต่ส่งอีเมลยืนยันไม่สำเร็จ: " + emailEx.Message,
                                        "Email Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // (ลบ Checkpoint 5)
                CartService.Instance.Clear();
                CartPersistenceService.Instance.Save(userId, Lines.ToList());

                MessageBox.Show("ทำรายการสั่งซื้อสำเร็จ โปรดตรวจสอบสถานะสินค้า และอีเมลยืนยัน", "Success",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                OrderCompleted?.Invoke();
                BackRequested?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show("ทำรายการไม่สำเร็จ (Catch บล็อกใหญ่): " + ex.Message);
            }
        }
    }

    // (คลาส SessionProfileProvider และ PromptPayQr ไม่เปลี่ยนแปลง)
    // ...
    internal sealed class SessionProfileProvider : IUserProfileProvider
    {
        public int CurrentUserId => AuthService.CurrentUserIdSafe();
        public string FullName(int userId)
        {
            var u = Session.CurrentUser;
            return $"{u?.First_Name} {u?.Last_Name}".Trim();
        }
        public string Username(int userId) => Session.CurrentUser?.Username ?? "";
        public string Phone(int userId) => Session.CurrentUser?.Email ?? "";
    }

    internal static class PromptPayQr
    {
        private static string TLV(string id, string value)
            => id + value.Length.ToString("00") + value;

        public static string BuildMobilePayload(string mobileInput, decimal amount)
        {
            var digits = new string(mobileInput.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("0066")) { digits = digits; }
            else if (digits.StartsWith("66")) { digits = "00" + digits; }
            else if (digits.StartsWith('0')) { digits = "0066" + digits.Substring(1); }
            else { digits = "0066" + digits; }

            string merchantAcc = TLV("00", "A000000677010111") + TLV("01", digits);
            string tag29 = TLV("29", merchantAcc);
            string amt = amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            string payloadNoCrc =
                TLV("00", "01") + TLV("01", "12") + tag29 +
                TLV("52", "0000") + TLV("53", "764") + TLV("54", amt) +
                TLV("58", "TH") + TLV("59", "Miyuna") + TLV("60", "Bangkok") +
                "6304";
            string crc = Crc16CcittFalse(payloadNoCrc);
            return payloadNoCrc + crc;
        }

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
            return reg.ToString("X4");
        }
    }
}