using MiyunaKimono.Models;
using MiyunaKimono.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MiyunaKimono.Views.Parts
{
    public partial class AdminOrderDetailsView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // Events สำหรับแจ้ง AdminWindow
        public event Action RequestBack;
        public event Action Saved;

        private readonly string _orderId;
        private OrderDetailsModel _details;

        // --- Properties for Binding ---
        public string OrderId => _details?.OrderId ?? "Loading...";
        public string DisplayId => $"#{OrderId}";
        public string CustomerName => _details?.CustomerName ?? "...";
        public string TelCustomer => _details?.Tel ?? "...";
        public bool HasPaymentSlip => _details?.PaymentSlipBytes != null && _details.PaymentSlipBytes.Length > 0;
        public string TotalAmountText => $"{_details?.TotalAmount:N0} THB";
        public ObservableCollection<OrderItemViewModel> Items { get; } = new ObservableCollection<OrderItemViewModel>();

        public List<string> StatusOptions { get; } = new List<string> {
            "Ordering", "Packing", "Shipping", "Delivering", "Completed", "Cancelled"
        };

        // --- Editable Properties ---
        private string _selectedStatus;
        public string SelectedStatus
        {
            get => _selectedStatus;
            set { _selectedStatus = value; Raise(); }
        }

        private string _trackingNumber;
        public string TrackingNumber
        {
            get => _trackingNumber;
            set { _trackingNumber = value; Raise(); }
        }

        private string _address;
        public string Address
        {
            get => _address;
            set { _address = value; Raise(); }
        }

        private string _adminNote;
        public string AdminNote
        {
            get => _adminNote;
            set { _adminNote = value; Raise(); }
        }
        // -----------------------------

        public AdminOrderDetailsView(string orderId)
        {
            InitializeComponent();
            DataContext = this;
            _orderId = orderId;
        }

        public async Task LoadAsync()
        {
            try
            {
                _details = await OrderService.Instance.GetOrderDetailsAsync(_orderId);
                if (_details == null)
                {
                    MessageBox.Show("Order not found.");
                    RequestBack?.Invoke();
                    return;
                }

                // อัปเดต UI (ทั้งส่วนแสดงผลและส่วนแก้ไข)
                Raise(nameof(OrderId));
                Raise(nameof(DisplayId));
                Raise(nameof(CustomerName));
                Raise(nameof(TelCustomer));
                Raise(nameof(HasPaymentSlip));
                Raise(nameof(TotalAmountText));

                // ตั้งค่าช่องที่แก้ไขได้
                SelectedStatus = StatusOptions.Contains(_details.Status) ? _details.Status : StatusOptions[0];
                TrackingNumber = _details.TrackingNumber;
                Address = _details.Address;
                AdminNote = _details.AdminNote;

                // โหลดรายการสินค้า
                Items.Clear();
                int index = 1;
                foreach (var item in _details.Items)
                {
                    Items.Add(new OrderItemViewModel
                    {
                        Index = index++,
                        ProductName = item.ProductName,
                        Quantity = item.Quantity,
                        Price = item.Price,
                        Total = item.Total
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load order details: " + ex.Message);
            }
        }

        // --- ปุ่ม Top Bar ---
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            RequestBack?.Invoke();
        }

        public async Task<bool> SaveAsync()
        {
            try
            {
                bool success = await OrderService.Instance.UpdateAdminOrderAsync(
                    _orderId,
                    SelectedStatus,
                    TrackingNumber,
                    Address,
                    AdminNote
                );

                if (success)
                {
                    MessageBox.Show("Order updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to update order.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return success;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save: " + ex.Message, "Error");
                return false;
            }
        }

        // (เมธอดนี้ถูกเรียกโดยปุ่ม Save ใน AdminWindow)
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            bool saved = await SaveAsync();
            if (saved)
            {
                Saved?.Invoke(); // แจ้ง AdminWindow ให้กลับไปหน้า List
            }
        }


        // --- ปุ่ม Here! / !here (เหมือนของ User) ---
        private void PaymentSlip_Click(object sender, RoutedEventArgs e)
        {
            if (!HasPaymentSlip)
            {
                MessageBox.Show("No payment slip was uploaded for this order.");
                return;
            }
            try
            {
                string fileName = _details.ReceiptFileName;

                if (string.IsNullOrEmpty(fileName) || !fileName.Contains("."))
                {
                    if (_details.PaymentSlipBytes.Length > 4 &&
                        _details.PaymentSlipBytes[0] == 0x25 &&
                        _details.PaymentSlipBytes[1] == 0x50 &&
                        _details.PaymentSlipBytes[2] == 0x44 &&
                        _details.PaymentSlipBytes[3] == 0x46)
                    {
                        fileName = "receipt.pdf";
                    }
                    else
                    {
                        fileName = "receipt.jpg";
                    }
                }

                string safeFileName = $"payment_{_orderId}_{Path.GetFileNameWithoutExtension(fileName)}{Path.GetExtension(fileName)}";
                string tempPath = Path.Combine(Path.GetTempPath(), safeFileName);

                File.WriteAllBytes(tempPath, _details.PaymentSlipBytes);
                Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open payment slip: " + ex.Message);
            }
        }

        // ----- ⬇️ (FIXED) แก้ไข Method นี้ ⬇️ -----
        private void PdfReceipt_Click(object sender, RoutedEventArgs e)
        {
            if (_details == null) return;
            try
            {
                var dummyCartLines = _details.Items.Select(item =>
                {
                    var p = new Product { ProductName = item.ProductName, Price = item.Price };
                    var line = new CartLine(p, item.Quantity);
                    p.Price = item.Total / Math.Max(1, item.Quantity);
                    return line;
                }).ToList();

                var profileProvider = new TempProfileProvider(_details.CustomerName, _details.Tel);

                // (FIXED: เพิ่ม VAT = 0 และ SubTotal = TotalAmount)
                // (เพื่อให้ Method Signature ตรงกัน)
                var pdfPath = ReceiptPdfMaker.Create(
                     _orderId,
                     dummyCartLines,
                     _details.TotalAmount,
                     profileProvider,
                     _details.Address
                 );
                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to generate PDF receipt: " + ex.Message);
            }
        }
        // ----- ⬆️ (FIXED) จบการแก้ไข ⬆️ -----


        // คลาสเล็กๆ นี้จำเป็นสำหรับ ReceiptPdfMaker
        private class TempProfileProvider : IUserProfileProvider
        {
            private readonly string _name, _phone;
            public TempProfileProvider(string name, string phone)
            {
                _name = name; _phone = phone;
            }
            public int CurrentUserId => 0; // Admin ไม่จำเป็นต้องมี ID user
            public string FullName(int userId) => _name;
            public string Username(int userId) => _name;
            public string Phone(int userId) => _phone;
        }
    }
}