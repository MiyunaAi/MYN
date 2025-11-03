using MiyunaKimono.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace MiyunaKimono.Views.Parts
{
    public partial class ReportView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // เก็บรายการ Report ทั้งหมดที่ดึงมาจาก DB
        private List<ProductReportItem> _allReportItems = new List<ProductReportItem>();

        // รายการที่แสดงผลจริง (หลังฟิลเตอร์)
        public ObservableCollection<ReportItemViewModel> FilteredItems { get; } = new ObservableCollection<ReportItemViewModel>();

        // --- Options สำหรับ Dropdowns ---
        public List<string> SortOptions { get; } = new List<string> { "Date", "Mostly", "lowly" };
        public List<string> MonthOptions { get; }
        public List<string> YearOptions { get; }

        // --- ค่าที่ถูกเลือก (Binding) ---
        private string _selectedSort = "Date";
        public string SelectedSort
        {
            get => _selectedSort;
            set { _selectedSort = value; Raise(); ApplyFilterAndSort(); }
        }

        private string _selectedMonth = "All Month";
        public string SelectedMonth
        {
            get => _selectedMonth;
            set { _selectedMonth = value; Raise(); ApplyFilterAndSort(); }
        }

        private string _selectedYear = "All Year";
        public string SelectedYear
        {
            get => _selectedYear;
            set { _selectedYear = value; Raise(); ApplyFilterAndSort(); }
        }

        public ReportView()
        {
            InitializeComponent();
            DataContext = this;

            // สร้าง Dropdown เดือน (All Month, January, ...)
            MonthOptions = new List<string> { "All Month" };
            MonthOptions.AddRange(Enumerable.Range(1, 12)
                .Select(i => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(i)));

            // สร้าง Dropdown ปี (All Year, 2568, 2567, ...)
            YearOptions = new List<string> { "All Year" };
            int currentBEYear = new ThaiBuddhistCalendar().GetYear(DateTime.Now);
            YearOptions.AddRange(Enumerable.Range(currentBEYear - 5, 6)
                .Select(y => y.ToString()).Reverse());

            Loaded += async (s, e) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                _allReportItems = await OrderService.Instance.GetProductReportAsync();
                ApplyFilterAndSort();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load report: " + ex.Message, "Error");
            }
        }

        private void ApplyFilterAndSort()
        {
            if (_allReportItems == null) return;

            IEnumerable<ProductReportItem> query = _allReportItems;

            // --- 1. กรองตามวันที่ (Month/Year) ---
            // --- 1. กรองตามวันที่ (Month/Year) ---
            if (SelectedYear != "All Year" && int.TryParse(SelectedYear, out var beYear))
            {
                // ⭐️ แปลง พ.ศ. (beYear เช่น 2568) ให้เป็น ค.ศ. (adYear เช่น 2025)
                int adYear = beYear - 543;

                // ⭐️ ใช้ปี ค.ศ. ในการกรอง
                query = query.Where(r => r.CreatedAt.Year == adYear);
            }
            if (SelectedMonth != "All Month")
            {
                int monthIndex = MonthOptions.FindIndex(m => m == SelectedMonth); // (January = 1)
                if (monthIndex > 0)
                {
                    query = query.Where(r => r.CreatedAt.Month == monthIndex);
                }
            }

            // --- 2. จัดกลุ่มและเรียงลำดับ ---

            // (แปลงเป็น List ก่อนเพื่อ Group)
            var filteredList = query.ToList();
            FilteredItems.Clear();

            if (SelectedSort == "Date")
            {
                // โหมด "Date": แสดงทุกรายการ เรียงตามวันที่
                var sorted = filteredList.OrderByDescending(r => r.CreatedAt);
                foreach (var item in sorted)
                {
                    FilteredItems.Add(new ReportItemViewModel(item, isGrouped: false));
                }
            }
            else
            {
                // โหมด "Mostly" หรือ "lowly": รวมยอด
                var grouped = filteredList
                    .GroupBy(r => r.ProductId) // รวมตาม ID สินค้า
                    .Select(g => new ReportItemViewModel(
                        new ProductReportItem
                        {
                            ProductId = g.Key,
                            ProductCode = g.First().ProductCode,
                            ProductName = g.First().ProductName,
                            Category = g.First().Category,
                            Price = g.First().Price, // ราคาต่อหน่วย
                            Quantity = g.Sum(item => item.Quantity), // ⬅️ ยอดรวม Qty
                            Total = g.Sum(item => item.Total),       // ⬅️ ยอดรวม Total
                            CreatedAt = g.Max(item => item.CreatedAt) // ⬅️ เอาวันที่ล่าสุด
                        }, isGrouped: true // ⬅️ บอก ViewModel ว่านี่คือข้อมูลที่รวมแล้ว
                    ));

                // จัดเรียง
                if (SelectedSort == "Mostly")
                {
                    grouped = grouped.OrderByDescending(g => g.Quantity);
                }
                else // "lowly"
                {
                    grouped = grouped.OrderBy(g => g.Quantity);
                }

                foreach (var item in grouped)
                {
                    FilteredItems.Add(item);
                }
            }
        }
    }

    // --- ViewModel สำหรับ 1 แถวใน DataGrid ---
    public class ReportItemViewModel
    {
        private readonly ProductReportItem _source;
        private readonly bool _isGrouped;

        public string ProductCode => $"#{_source.ProductCode}";
        public string ProductName => _source.ProductName;
        public string Category => _isGrouped ? "N/A" : (_source.Category ?? "N/A");
        public int Quantity => _source.Quantity;
        public string QuantityText => $"X {Quantity}";
        public string PriceText => $"{_source.Price:N0} THB";
        public string TotalText => $"{_source.Total:N0} THB";

        // ถ้า Grouped (Mostly, lowly) เดือน/ปี จะไม่แสดง
        public string Month => _isGrouped ? "N/A" : _source.CreatedAt.ToString("MMMM", CultureInfo.InvariantCulture);
        public string Year => _isGrouped ? "N/A" : _source.CreatedAt.ToString("yyyy");

        public ReportItemViewModel(ProductReportItem source, bool isGrouped)
        {
            _source = source;
            _isGrouped = isGrouped;
        }
    }
}