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
        // รับ 5 พารามิเตอร์ (เวอร์ชันแก้ไขสมบูรณ์)
        public static string Create(string orderId,
                                    List<CartLine> lines,
                                    decimal grandTotal,
                                    IUserProfileProvider profile,
                                    string customerAddress)
        {
            // --- 1. คำนวณตัวเลข ---
            decimal subTotalBeforeDiscount = lines.Sum(l => l.Product.Price * l.Quantity);
            decimal totalDiscount = subTotalBeforeDiscount - grandTotal;

            // คำนวณ VAT 7% (แบบรวมใน)
            decimal vatAmount = (grandTotal * 7) / 107;
            decimal priceBeforeVat = grandTotal - vatAmount;

            // --- 2. สร้าง PDF ---
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

            // --- HEADER ---
            var headerTable = section.AddTable();
            headerTable.Borders.Width = 0;
            headerTable.AddColumn("9cm");
            headerTable.AddColumn("9cm");

            var row = headerTable.AddRow();
            var left = row.Cells[0];
            left.VerticalAlignment = VerticalAlignment.Top;

            // Logo
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

            // Shop Info
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
            pShop.AddText("Tax ID: 0123456789012 (Head Office)");

            // Receipt Info
            var right = row.Cells[1];
            right.Format.Alignment = ParagraphAlignment.Right;
            var pTitle = right.AddParagraph("RECEIPT / TAX INVOICE");
            pTitle.Format.Font.Size = 16;
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

            // --- ADDRESS ---
            var addrTable = section.AddTable();
            addrTable.Borders.Width = 0;
            addrTable.AddColumn("18cm");
            var rAddr = addrTable.AddRow();
            rAddr.Cells[0].AddParagraph("BILL TO / SHIP TO").Format.Font.Bold = true;
            rAddr.Cells[0].AddParagraph(customerAddress ?? "-");

            section.AddParagraph().AddLineBreak();

            // --- ITEMS TABLE ---
            var table = section.AddTable();
            table.Borders.Color = Md.Colors.LightGray;
            table.Borders.Width = 0.5;
            table.Borders.Left.Width = 0;
            table.Borders.Right.Width = 0;

            table.AddColumn("9cm"); // Item
            table.AddColumn("3cm").Format.Alignment = ParagraphAlignment.Center; // Price
            table.AddColumn("2cm").Format.Alignment = ParagraphAlignment.Center; // Qty
            table.AddColumn("4cm").Format.Alignment = ParagraphAlignment.Right;  // Total

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
                tr.Cells[1].AddParagraph($"{l.UnitPrice:N0}");
                tr.Cells[2].AddParagraph(l.Quantity.ToString());
                tr.Cells[3].AddParagraph($"{l.LineTotal:N0}");
            }

            section.AddParagraph().AddLineBreak();

            // --- SUMMARY ---
            var sumTable = section.AddTable();
            sumTable.Borders.Width = 0;
            sumTable.AddColumn("11cm");
            sumTable.AddColumn("4cm");
            sumTable.AddColumn("3cm");

            void AddSum(string label, string val, bool bold = false, bool isTotal = false, bool isRed = false)
            {
                var r = sumTable.AddRow();
                r.Cells[1].AddParagraph(label).Format.Alignment = ParagraphAlignment.Right;
                var v = r.Cells[2].AddParagraph(val);
                v.Format.Alignment = ParagraphAlignment.Right;

                if (bold) r.Format.Font.Bold = true;
                if (isRed) r.Cells[2].Format.Font.Color = Md.Colors.Red;
                if (isTotal)
                {
                    r.Cells[1].Format.Font.Size = 12;
                    r.Cells[2].Format.Font.Size = 12;
                    r.Cells[2].Format.Font.Color = Md.Colors.PaleVioletRed;
                    r.TopPadding = "0.2cm";
                    r.Borders.Top.Width = 1;
                }
            }

            AddSum("Subtotal:", $"{subTotalBeforeDiscount:N2}");
            if (totalDiscount > 0) AddSum("Discount:", $"-{totalDiscount:N2}", isRed: true);
            AddSum("Pre-VAT Amount:", $"{priceBeforeVat:N2}");
            AddSum("VAT (7%):", $"{vatAmount:N2}");
            AddSum("Grand Total:", $"{grandTotal:N2}", bold: true, isTotal: true);

            section.AddParagraph().AddLineBreak();
            section.AddParagraph().AddLineBreak();

            // --- FOOTER ---
            var pFoot = section.AddParagraph("Thank you for shopping with Miyuna Kimono!");
            pFoot.Format.Alignment = ParagraphAlignment.Center;
            pFoot.Format.Font.Color = Md.Colors.Gray;

            var pVat = section.AddParagraph("(Included VAT)");
            pVat.Format.Alignment = ParagraphAlignment.Center;
            pVat.Format.Font.Size = 8;
            pVat.Format.Font.Color = Md.Colors.Gray;

            // Save
            var file = Path.Combine(Path.GetTempPath(), $"Receipt_{orderId}.pdf");
            var renderer = new PdfDocumentRenderer(unicode: true);
            renderer.Document = doc;
            renderer.RenderDocument();
            renderer.Save(file);

            return file;
        }
    }
    // Interface (คงไว้เพื่อให้ไฟล์นี้ทำงานได้จบในตัวเดียว)
    public interface IUserProfileProvider
    {
        int CurrentUserId { get; }
        string FullName(int userId);
        string Username(int userId);
        string Phone(int userId);
    }
}