using MiyunaKimono.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MiyunaKimono.Views.Parts
{
    public partial class AllOrdersView : UserControl, INotifyPropertyChanged
    {
        public event Action<string> ViewDetailsRequested;

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // เก็บรายการ Order ทั้งหมดที่ดึงมาจาก DB (สำหรับฟิลเตอร์)
        private List<AdminOrderRow> _allOrders = new List<AdminOrderRow>();

        // รายการที่แสดงผลจริง (หลังฟิลเตอร์)
        public ObservableCollection<AdminOrderRow> FilteredOrders { get; } = new ObservableCollection<AdminOrderRow>();

        private bool _isLoading = false;

        // --- Options สำหรับ Dropdowns ---
        public List<string> StatusOptions { get; } = new List<string> {
            "All status", "Ordering", "Packing", "Shipping", "Delivering", "Completed", "Cancelled"
        };
        public List<string> SortOptions { get; } = new List<string> {
        "Mostly", "Lowly", "Date", "Name"
        };

        public List<string> MonthOptions { get; }
        public List<string> YearOptions { get; }

        // --- ค่าที่ถูกเลือก (Binding) ---
        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; Raise(); ApplyFilter(); }
        }

        // ----- ⬇️ (FIXED) แก้ไขค่าเริ่มต้นตรงนี้ ⬇️ -----
        private string _selectedSort = "Date"; // (เปลี่ยนจาก "Mostly" เป็น "Date")
        public string SelectedSort
        // ----- ⬆️ (FIXED) จบการแก้ไข ⬆️ -----
        {
            get => _selectedSort;
            set { _selectedSort = value; Raise(); ApplyFilter(); } // 
        }

        private string _selectedStatus = "All status";
        public string SelectedStatus
        {
            get => _selectedStatus;
            set { _selectedStatus = value; Raise(); ApplyFilter(); }
        }

        private string _selectedMonth = "All Month";
        public string SelectedMonth
        {
            get => _selectedMonth;
            set { _selectedMonth = value; Raise(); ApplyFilter(); }
        }

        private string _selectedYear = "All Years";
        public string SelectedYear
        {
            get => _selectedYear;
            set { _selectedYear = value; Raise(); ApplyFilter(); }
        }


        public AllOrdersView()
        {
            InitializeComponent();
            DataContext = this;

            // สร้าง Dropdown เดือน (All Month, January, ...)
            MonthOptions = new List<string> { "All Month" };
            MonthOptions.AddRange(Enumerable.Range(1, 12)
                .Select(i => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i)));

            // สร้าง Dropdown ปี (All Years, 2568, 2567, ...)
            YearOptions = new List<string> { "All Years" };
            int currentBEYear = new ThaiBuddhistCalendar().GetYear(DateTime.Now);
            YearOptions.AddRange(Enumerable.Range(currentBEYear - 5, 6)
                .Select(y => y.ToString()).Reverse());

            // สั่งให้โหลดข้อมูลใหม่ทุกครั้งที่ View ถูกทำให้มองเห็น
            this.IsVisibleChanged += AllOrdersView_IsVisibleChanged;
        }

        private async void AllOrdersView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // ถ้า View กำลังถูกทำให้ "มองเห็น" (true) และไม่ได้กำลังโหลดอยู่
            if (e.NewValue is true && !_isLoading)
            {
                await LoadDataAsync();
            }
        }

        private async Task LoadDataAsync()
        {
            if (_isLoading) return; // (1. ป้องกันการโหลดซ้ำ)
            _isLoading = true;

            try
            {
                var ordersFromDb = await OrderService.Instance.GetAllOrdersAsync();

                _allOrders = ordersFromDb
                         .Select(o => new AdminOrderRow(o))
                         .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load orders: " + ex.Message, "Error");
            }
            finally
            {
                _isLoading = false; // (2. ปลดล็อค)
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<AdminOrderRow> query = _allOrders;

            // 1. Filter Search Text (ค้นหาจาก Order ID หรือ Customer Name)
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(o =>
                    o.OrderId.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    o.CustomerName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                );
            }

            // 2. Filter Status
            if (SelectedStatus != "All status")
            {
                query = query.Where(o => o.Status == SelectedStatus);
            }

            // 3. Filter Year (FIXED: แปลง พ.ศ. เป็น ค.ศ. ก่อนเทียบ)
            if (SelectedYear != "All Years" && int.TryParse(SelectedYear, out var yearBE))
            {
                // แปลง พ.ศ. (TH) เป็น ค.ศ. (Gregorian) ก่อนเทียบ
                int yearGregorian = yearBE - 543;
                query = query.Where(o => o.CreatedAt.Year == yearGregorian);
            }

            // 4. Filter Month
            if (SelectedMonth != "All Month")
            {
                int monthIndex = MonthOptions.FindIndex(m => m == SelectedMonth); // (January = 1)
                if (monthIndex > 0)
                {
                    query = query.Where(o => o.CreatedAt.Month == monthIndex);
                }
            }

            // 5. Sort
            switch (SelectedSort)
            {
                case "Mostly":
                    query = query.OrderByDescending(o => o.Amount);
                    break;
                case "Lowly":
                    query = query.OrderBy(o => o.Amount);
                    break;
                case "Name":
                    query = query.OrderBy(o => o.CustomerName);
                    break;
                case "Date":
                default:
                    // (นี่คือค่าเริ่มต้นใหม่)
                    query = query.OrderByDescending(o => o.CreatedAt);
                    break;
            }


            // อัปเดต Collection ที่แสดงผล
            FilteredOrders.Clear();
            foreach (var item in query)
            {
                FilteredOrders.Add(item);
            }
        }

        private void Details_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string orderId)
            {
                //MessageBox.Show($"TODO: Open details for Order #{orderId}", "Coming Soon");
                ViewDetailsRequested?.Invoke(orderId); // ⬅️ ยิง Event
            }
        }
    }

    // --- ViewModel สำหรับ 1 แถวใน DataGrid ---
    public class AdminOrderRow
    {
        private readonly OrderService.AdminOrderInfo _source;

        public string OrderId => _source.Id;
        public string DisplayId => $"#{_source.Id}";
        public string CustomerName => _source.CustomerName ?? "N/A";
        public decimal Amount => _source.Amount;
        public string AmountText => $"{_source.Amount:N0} THB";
        public string Status => string.IsNullOrWhiteSpace(_source.Status) ? "Ordering" : _source.Status;
        public DateTime CreatedAt => _source.CreatedAt;

        public string DateText => CreatedAt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        public string TimeText => CreatedAt.ToString("HH:mm");


        public AdminOrderRow(OrderService.AdminOrderInfo source)
        {
            _source = source;
        }
    }
}