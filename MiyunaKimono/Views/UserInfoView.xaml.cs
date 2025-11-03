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
using System.Windows.Media.Imaging;
using System.IO;
using MiyunaKimono.Helpers;

namespace MiyunaKimono.Views
{
    public partial class UserInfoView : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string FullName { get; private set; }
        public string Username { get; private set; }
        public string Email { get; private set; }
        public BitmapImage AvatarImg { get; private set; }

        public ObservableCollection<OrderRow> AllOrders { get; } = new();
        public ObservableCollection<OrderRow> FilteredOrders { get; } = new();

        public List<string> MonthOptions { get; } =
            new List<string> { "All" }
            .Concat(Enumerable.Range(1, 12)
            .Select(i => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(i))).ToList();

        public List<string> YearOptions { get; } =
            new List<string> { "All" }
            .Concat(Enumerable.Range(new ThaiBuddhistCalendar().GetYear(DateTime.Now) - 6, 9).Select(y => y.ToString())).ToList();

        private string _selectedMonth = "All";
        public string SelectedMonth
        {
            get => _selectedMonth;
            set { _selectedMonth = value; Raise(); ApplyFilter(); }
        }

        private string _selectedYear = "All";
        public string SelectedYear
        {
            get => _selectedYear;
            set { _selectedYear = value; Raise(); ApplyFilter(); }
        }

        public event Action BackRequested;
        public event Action EditProfileRequested;
        public event Action<string> OrderDetailsRequested;
        public UserInfoView()
        {
            InitializeComponent();
            DataContext = this;
            Session.ProfileChanged += OnProfileChanged;
            Unloaded += (_, __) =>
            {
                Session.ProfileChanged -= OnProfileChanged;
                Session.NewOrderPlaced -= OnNewOrderPlaced; // เพิ่มบรรทัดนี้
            };

            LoadProfileFromSession();

            // ⭐️ 2. Subscribe Event ใหม่ที่เราสร้าง
            Session.NewOrderPlaced += OnNewOrderPlaced;
        }
        private async void OnNewOrderPlaced()
        {
            // เราใช้ Dispatcher.InvokeAsync เพื่อให้แน่ใจว่า
            // โค้ดนี้จะทำงานบน UI Thread (เพราะมันไปแก้ ObservableCollection)
            await Dispatcher.InvokeAsync(async () =>
            {
                await ReloadAsync();
            });
        }
        private async void UserInfoView_Loaded(object sender, RoutedEventArgs e)
        {
            // เราเรียก ReloadAsync() ซึ่งจะไปเรียก LoadOrdersAsync() ให้เอง
            // เพื่อดึงข้อมูล Order ล่าสุดจาก Database ทุกครั้งที่หน้านี้ถูกเปิด
            await ReloadAsync();
        }

        private void OnProfileChanged() => Dispatcher.Invoke(LoadProfileFromSession);

        private void EditProfile_Click(object sender, RoutedEventArgs e)
        {
            EditProfileRequested?.Invoke();
        }

        private void LoadProfileFromSession()
        {
            var u = Session.CurrentUser;

            FullName = $"{u?.First_Name} {u?.Last_Name}".Trim();
            Username = u?.Username ?? "—";
            Email = u?.Email ?? "—";

            // เคลียร์ก่อนกันภาพค้าง
            AvatarImg = null;
            Raise(nameof(AvatarImg));

            AvatarImg = ImageHelper.LoadBitmapNoCache(u?.AvatarPath); // โหลดรูปจริงแบบ no-cache
            Raise(nameof(FullName));
            Raise(nameof(Username));
            Raise(nameof(Email));
            Raise(nameof(AvatarImg));
        }


        public async Task ReloadAsync()
        {
            LoadProfileFromSession();
            await LoadOrdersAsync();
            ApplyFilter();
        }

        private async Task LoadOrdersAsync()
        {
            AllOrders.Clear();
            var userId = AuthService.CurrentUserIdSafe();
            var orders = await OrderService.Instance.GetOrdersByUserAsync(userId);

            foreach (var o in orders.OrderByDescending(x => x.CreatedAt))
                AllOrders.Add(new OrderRow(o));
        }

        private void ApplyFilter()
        {
            FilteredOrders.Clear();
            IEnumerable<OrderRow> q = AllOrders;

            if (SelectedYear != "All" && int.TryParse(SelectedYear, out var y))
                q = q.Where(r => r.CreatedAt.Year == y);

            if (SelectedMonth != "All")
            {
                int m = MonthOptions.FindIndex(s => s == SelectedMonth);
                if (m >= 1) q = q.Where(r => r.CreatedAt.Month == m);
            }

            foreach (var r in q) FilteredOrders.Add(r);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

        private void OpenDetails_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is OrderRow row)
            {
                //MessageBox.Show($"Open order #{row.OrderId}", "Order",
                //    MessageBoxButton.OK, MessageBoxImage.Information);

                // ยิงอีเวนต์ไปหา UserMainWindow พร้อมส่ง OrderId
                OrderDetailsRequested?.Invoke(row.OrderId);
            }
        }
    }

    public class OrderRow
    {
        public string OrderId { get; }
        public string DisplayId => $"#{OrderId}";
        public string AmountText { get; }
        public string Status { get; }
        public DateTime CreatedAt { get; }
        public string DateText => CreatedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public OrderRow(OrderService.OrderInfo o)
        {
            OrderId = o.Id;
            AmountText = $"{o.Amount:N0} THB";
            Status = string.IsNullOrWhiteSpace(o.Status) ? "—" : o.Status;
            CreatedAt = o.CreatedAt;
        }
    }
}
