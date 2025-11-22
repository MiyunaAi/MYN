using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Alias สำหรับ MigraDoc
using Md = MigraDoc.DocumentObjectModel;

namespace MiyunaKimono.Services
{
    public static class ReceiptPdfMaker
    {
        // รับพารามิเตอร์แค่ 5 ตัวเหมือนเดิม เพื่อไม่ให้กระทบกับไฟล์ Views
        public static string Create(string orderId,
                                    List<CartLine> lines,
                                    decimal grandTotal,
                                    IUserProfileProvider profile,
                                    string customerAddress)
        {
            // --- ส่วนที่ 1: คำนวณตัวเลขต่างๆ ---
            // คำนวณยอดรวมราคาเต็ม (ก่อนลด)
            decimal subTotalBeforeDiscount = lines.Sum(l => l.Product.Price * l.Quantity);

            // คำนวณยอดส่วนลดรวม
            decimal totalDiscount = subTotalBeforeDiscount - grandTotal;

            // คำนวณ VAT (สมมติว่าเป็นราคารวม VAT 7% แล้ว)
            // สูตร: VAT = ราคาสุทธิ * 7 / 107
            decimal vatAmount = (grandTotal * 7) / 107;
            decimal priceBeforeVat = grandTotal - vatAmount;


            // --- ส่วนที่ 2: สร้างเอกสาร ---
            var doc = new Document();
            doc.Info.Title = $"Receipt #{orderId}";

            var style = doc.Styles[StyleNames.Normal];
            style.Font.Name = "Arial";
            style.Font.Size = 10;

            var section = doc.AddSection();
            section.PageSetup.LeftMargin = "1.5cm";
            section.PageSetup.RightMargin = "1.5cm";
            section.PageSetup.TopMargin = "1.0cm";
            section.PageSetup.BottomMargin = "2.0cm";

            // ------------------------------------------
            // HEADER: โลโก้ + ที่อยู่ร้าน
            // ------------------------------------------
            var headerTable = section.AddTable();
            headerTable.Borders.Width = 0;
            headerTable.AddColumn("9cm");
            headerTable.AddColumn("9cm");

            var row = headerTable.AddRow();
            var left = row.Cells[0];
            left.VerticalAlignment = VerticalAlignment.Top;

            // โลโก้
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo_miyunaa.png");
            if (File.Exists(logoPath))
            {
                var img = left.AddImage(logoPath);
                img.Width = "3.5cm";
                img.LockAspectRatio = true;
            }
            else
            {
                var pName = left.AddParagraph("Miyuna Kimono");
                pName.Format.Font.Size = 18;
                pName.Format.Font.Bold = true;
                pName.Format.Font.Color = Md.Colors.PaleVioletRed;
            }

            // ที่อยู่ร้าน
            var pShop = left.AddParagraph();
            pShop.Format.SpaceBefore = "0.3cm";
            pShop.AddFormattedText("Miyuna Kimono Shop", TextFormat.Bold);
            pShop.AddLineBreak();
            pShop.AddText("123 Khon Kaen University,");
            pShop.AddLineBreak();
            pShop.AddText("Mueang District, Khon Kaen 40002");
            pShop.AddLineBreak();
            pShop.AddText("Tel: 095-862-0453");
            pShop.AddLineBreak();
            pShop.AddText("Tax ID: 0123456789012 (Head Office)"); // เพิ่มเลขผู้เสียภาษีสมมติ

            // ขวา: ข้อมูลใบเสร็จ
            var right = row.Cells[1];
            right.Format.Alignment = ParagraphAlignment.Right;
            var pTitle = right.AddParagraph("RECEIPT / TAX INVOICE"); // ปรับชื่อให้ดูเป็นทางการ
            pTitle.Format.Font.Size = 16; // ปรับขนาดลงนิดหน่อยให้พอดี
            pTitle.Format.Font.Bold = true;
            pTitle.Format.Font.Color = Md.Colors.PaleVioletRed;

            var pInfo = right.AddParagraph();
            pInfo.Format.SpaceBefore = "0.5cm";
            pInfo.AddFormattedText("No: ", TextFormat.Bold).AddText($"#{orderId}");
            pInfo.AddLineBreak();
            pInfo.AddFormattedText("Date: ", TextFormat.Bold).AddText(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            pInfo.AddLineBreak();

            string custName = profile.FullName(profile.CurrentUserId);
            if (string.IsNullOrWhiteSpace(custName)) custName = "Cash Customer";
            pInfo.AddFormattedText("Customer: ", TextFormat.Bold).AddText(custName);

            section.AddParagraph().AddLineBreak();

            // ------------------------------------------
            // ที่อยู่ลูกค้า
            // ------------------------------------------
            var addrTable = section.AddTable();
            addrTable.Borders.Width = 0;
            addrTable.AddColumn("18cm");

            var rAddr = addrTable.AddRow();
            rAddr.Cells[0].AddParagraph("BILL TO / SHIP TO").Format.Font.Bold = true;
            var pAddr = rAddr.Cells[0].AddParagraph();
            pAddr.AddText(customerAddress ?? "-");

            section.AddParagraph().AddLineBreak();

            // ------------------------------------------
            // ตารางสินค้า
            // ------------------------------------------
            var table = section.AddTable();
            table.Borders.Color = Md.Colors.LightGray;
            table.Borders.Width = 0.5;
            table.Borders.Left.Width = 0;
            table.Borders.Right.Width = 0;

            table.AddColumn("9cm");
            table.AddColumn("3cm").Format.Alignment = ParagraphAlignment.Center;
            table.AddColumn("2cm").Format.Alignment = ParagraphAlignment.Center;
            table.AddColumn("4cm").Format.Alignment = ParagraphAlignment.Right;

            var hRow = table.AddRow();
            hRow.HeadingFormat = true;
            hRow.Format.Font.Bold = true;
            hRow.Format.Font.Color = Md.Colors.White;
            hRow.Shading.Color = Md.Colors.PaleVioletRed;
            hRow.TopPadding = "0.15cm";
            hRow.BottomPadding = "0.15cm";

            hRow.Cells[0].AddParagraph("DESCRIPTION");
            hRow.Cells[1].AddParagraph("PRICE");
            hRow.Cells[2].AddParagraph("QTY");
            hRow.Cells[3].AddParagraph("AMOUNT");

            foreach (var l in lines)
            {
                var tr = table.AddRow();
                tr.TopPadding = "0.15cm";
                tr.BottomPadding = "0.15cm";
                tr.VerticalAlignment = VerticalAlignment.Center;

                tr.Cells[0].AddParagraph(l.Product.ProductName);
                // แสดงราคาต่อหน่วย (ใช้ราคาหลังลด ถ้ามีการลด)
                tr.Cells[1].AddParagraph($"{l.UnitPrice:N0}");
                tr.Cells[2].AddParagraph(l.Quantity.ToString());
                tr.Cells[3].AddParagraph($"{l.LineTotal:N0}");
            }

            section.AddParagraph().AddLineBreak();

            // ------------------------------------------
            // สรุปยอดเงิน (เพิ่ม VAT และ Discount)
            // ------------------------------------------
            var sumTable = section.AddTable();
            sumTable.Borders.Width = 0;
            // จัดคอลัมน์ให้ชิดขวา
            sumTable.AddColumn("11cm"); // ว่าง
            sumTable.AddColumn("4cm");  // Label
            sumTable.AddColumn("3cm");  // Value

            void AddSummaryRow(string label, string val, bool isBold = false, bool isTotal = false, bool isDiscount = false)
            {
                var r = sumTable.AddRow();
                r.Cells[1].AddParagraph(label).Format.Alignment = ParagraphAlignment.Right;
                var v = r.Cells[2].AddParagraph(val);
                v.Format.Alignment = ParagraphAlignment.Right;

                if (isBold) r.Format.Font.Bold = true;
                if (isDiscount) r.Cells[2].Format.Font.Color = Md.Colors.Red; // ส่วนลดสีแดง

                if (isTotal)
                {
                    r.Cells[1].Format.Font.Size = 12;
                    r.Cells[2].Format.Font.Size = 12;
                    r.Cells[2].Format.Font.Color = Md.Colors.PaleVioletRed;
                    r.TopPadding = "0.2cm";
                    // ขีดเส้นใต้คู่ยอดรวม (ถ้าทำได้) หรือเส้นทึบ
                    r.Borders.Top.Width = 1;
                    r.Borders.Bottom.Width = 0; // MigraDoc พื้นฐานทำ double line ยาก ใช้เส้นทึบแทน
                }
            }

            // 1. ยอดรวมก่อนส่วนลด
            AddSummaryRow("Subtotal:", $"{subTotalBeforeDiscount:N2}");

            // 2. ส่วนลด (ถ้ามี)
            if (totalDiscount > 0)
            {
                AddSummaryRow("Discount:", $"-{totalDiscount:N2}", isDiscount: true);
            }

            // 3. ยอดก่อน VAT
            AddSummaryRow("Pre-VAT Amount:", $"{priceBeforeVat:N2}");

            // 4. VAT 7%
            AddSummaryRow("VAT (7%):", $"{vatAmount:N2}");

            // 5. ยอดสุทธิ
            AddSummaryRow("Grand Total:", $"{grandTotal:N2}", isBold: true, isTotal: true);

            section.AddParagraph().AddLineBreak();
            section.AddParagraph().AddLineBreak();

            // ------------------------------------------
            // Footer
            // ------------------------------------------
            var pFoot = section.AddParagraph("Thank you for shopping with Miyuna Kimono!");
            pFoot.Format.Alignment = ParagraphAlignment.Center;
            pFoot.Format.Font.Color = Md.Colors.Gray;

            var pNote = section.AddParagraph("(Included VAT)");
            pNote.Format.Alignment = ParagraphAlignment.Center;
            pNote.Format.Font.Size = 8;
            pNote.Format.Font.Color = Md.Colors.Gray;

            // Save PDF
            var file = Path.Combine(Path.GetTempPath(), $"Receipt_{orderId}.pdf");
            var renderer = new PdfDocumentRenderer(unicode: true);
            renderer.Document = doc;
            renderer.RenderDocument();
            renderer.Save(file);

            return file;
        }
    }
}