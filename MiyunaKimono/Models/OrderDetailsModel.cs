using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiyunaKimono.Models
{
    // 1. โมเดลสำหรับสินค้าแต่ละรายการในออเดอร์
    public class OrderItemModel
    {
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; } // ราคาต่อหน่วย (Unit Price)
        public decimal Total { get; set; } // ราคารวม (Line Total)
    }

    // 2. โมเดลหลักสำหรับหน้า Order Details
    public class OrderDetailsModel
    {
        public string OrderId { get; set; }
        public string CustomerName { get; set; } // Username
        public string Status { get; set; }
        public string TrackingNumber { get; set; }
        public string Address { get; set; }
        public string AdminNote { get; set; }
        public decimal TotalAmount { get; set; } // ยอดสุทธิ
        public byte[] PaymentSlipBytes { get; set; } // สลิปที่ลูกค้าอัปโหลด
        public string ReceiptFileName { get; set; } // <-- 1. เพิ่มบรรทัดนี้
        public string Tel { get; set; }
        public List<OrderItemModel> Items { get; set; } = new List<OrderItemModel>();
    }

    // 3. โมเดลสำหรับแสดงผลใน List (เพิ่ม Property สำหรับ XAML)
    public class OrderItemViewModel : INotifyPropertyChanged
    {
        public int Index { get; set; }
        public string ProductName { get; set; }
        public string QuantityText => $"x {Quantity}";
        public string PriceText => $"{Price:N0} THB";
        public string TotalText => $"{Total:N0} THB";

        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}