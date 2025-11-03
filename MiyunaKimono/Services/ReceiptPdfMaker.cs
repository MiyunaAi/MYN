using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// เพิ่ม alias เพื่ออ้าง type ของ MigraDoc แบบชัดเจน
using Md = MigraDoc.DocumentObjectModel;

namespace MiyunaKimono.Services
{
    public static class ReceiptPdfMaker
    {
        // ----- ⬇️ (FIXED) แก้ไข Signature ของ Method ⬇️ -----
        public static string Create(string orderId,
                                    List<CartLine> lines,
                                    decimal subTotal,    // (ยอดก่อน VAT)
                                    decimal vatAmount,   // (ยอด VAT)
                                    decimal netTotal,    // (ยอดสุทธิ)
                                    IUserProfileProvider profile,
                                    string address)
        // ----- ⬆️ (FIXED) จบการแก้ไข ⬆️ -----
        {
            var doc = new Document();
            doc.Info.Title = "Ecommerce Receipt";
            var s = doc.AddSection();
            s.PageSetup.LeftMargin = "2cm";
            s.PageSetup.RightMargin = "2cm";

            var h = s.AddParagraph("ECOMMERCE RECEIPT");
            h.Format.Font.Size = 16;
            h.Format.Font.Bold = true;
            h.Format.SpaceAfter = "0.8cm";

            var name = profile.FullName(profile.CurrentUserId);
            if (string.IsNullOrWhiteSpace(name)) name = "Customer";

            s.AddParagraph($"Order #{orderId}").Format.Font.Bold = true;
            s.AddParagraph($"Dear {name},");
            s.AddParagraph().AddLineBreak();

            // ที่อยู่
            var grid = s.AddTable();
            grid.Borders.Width = 0;
            grid.AddColumn("8cm");
            grid.AddColumn("8cm");
            var r = grid.AddRow();
            r.Cells[0].AddParagraph("SHIPPING ADDRESS").Format.Font.Bold = true;
            r.Cells[1].AddParagraph("BILLING ADDRESS").Format.Font.Bold = true;
            r = grid.AddRow();
            r.Cells[0].AddParagraph(address ?? "-");
            r.Cells[1].AddParagraph(address ?? "-");

            s.AddParagraph().AddLineBreak();
            s.AddParagraph("SUMMARY").Format.Font.Bold = true;

            // ตารางสินค้า
            var t = s.AddTable();
            t.Borders.Color = Md.Colors.Black;   // ← ใช้ Md.Colors
            t.Borders.Width = 0.25;
            t.AddColumn("9cm");
            t.AddColumn("3cm");
            t.AddColumn("4cm");
            var tr = t.AddRow();
            tr.Format.Font.Bold = true;
            tr.Cells[0].AddParagraph("PRODUCT");
            tr.Cells[1].AddParagraph("QUANTITY");
            tr.Cells[2].AddParagraph("PRICE");

            foreach (var l in lines)
            {
                tr = t.AddRow();
                tr.Cells[0].AddParagraph(l.Product.ProductName);
                tr.Cells[1].AddParagraph(l.Quantity.ToString());
                // (ใช้ LineTotal ซึ่งเป็นราคาก่อน VAT)
                tr.Cells[2].AddParagraph($"{l.LineTotal:N2}");
            }

            s.AddParagraph().AddLineBreak();

            var sum = s.AddTable();
            sum.Borders.Width = 0;
            sum.AddColumn("12cm");
            sum.AddColumn("4cm");

            void Right(string label, string value, bool bold = false)
            {
                var rr = sum.AddRow();
                rr.Cells[0].AddParagraph(label).Format.Alignment = Md.ParagraphAlignment.Right; // ← ใช้ Md.ParagraphAlignment
                var p = rr.Cells[1].AddParagraph(value);
                p.Format.Alignment = Md.ParagraphAlignment.Right;
                if (bold) p.Format.Font.Bold = true;
            }

            // ----- ⬇️ (FIXED) แก้ไขส่วนสรุปยอด ⬇️ -----
            Right("Subtotal:", $"{subTotal:N2}");
            Right("VAT (7%):", $"{vatAmount:N2}");
            Right("Total Payable:", $"{netTotal:N2}", true);
            // ----- ⬆️ (FIXED) จบการแก้ไข ⬆️ -----

            // render
            var file = Path.Combine(Path.GetTempPath(), $"receipt_{orderId}.pdf");

            var renderer = new PdfDocumentRenderer(unicode: true); // หรือ new PdfDocumentRenderer();
            renderer.Document = doc;

            renderer.RenderDocument();
            renderer.Save(file);

            return file;
        }
    }

    public interface IUserProfileProvider
    {
        int CurrentUserId { get; }
        string FullName(int userId);
        string Username(int userId);
        string Phone(int userId);
    }
}