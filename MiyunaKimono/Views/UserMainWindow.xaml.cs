// Views/UserMainWindow.xaml.cs
using MiyunaKimono.Models;
using MiyunaKimono.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using System.IO;
using MiyunaKimono.Helpers;
// ป้องกันชื่อซ้อนกับคลาสอื่น
using TopPickItemModel = MiyunaKimono.Models.TopPickItem;

namespace MiyunaKimono.Views
{
    public partial class UserMainWindow : Window, INotifyPropertyChanged
    {
        private UserInfoView _userInfoView;   // cache instance
        private EditProfileView _editProfileView; // ★ cache instance


        // (โค้ดเดิมของคุณ: TopPicksHost_Loaded, TopPicksHost_SizeChanged)
        private void TopPicksHost_Loaded(object sender, RoutedEventArgs e)
        {
            ReflowTopPicks();
        }

        private void TopPicksHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ReflowTopPicks();
        }


        // ====== Data for UI ======
        public ObservableCollection<TopPickItemModel> TopPicks { get; } = new();
        public ObservableCollection<TopPickItemModel> BestSellingItems { get; } = new();
        public ObservableCollection<string> HeroImages { get; } = new();

        private int _heroIndex;
        public int HeroIndex
        {
            get => _heroIndex;
            set { _heroIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentHeroImage)); }
        }

        public string CurrentHeroImage => HeroImages.Count == 0 ? null : HeroImages[HeroIndex];

        private readonly DispatcherTimer _timer;

        // Services
        private readonly ProductService _productSvc = new();

        // Commands (ใช้กับปุ่มลูกศรใน HeroCarousel)
        public ICommand PrevHeroCommand { get; }
        public ICommand NextHeroCommand { get; }


        // --- 🔽 1. แก้ไข HideAllSections (เพิ่ม OrderDetailsHost) 🔽 ---
        private void HideAllSections()
        {
            HomeSection.Visibility = Visibility.Collapsed;
            ListSection.Visibility = Visibility.Collapsed;
            CartSection.Visibility = Visibility.Collapsed;
            CheckoutHost.Visibility = Visibility.Collapsed;
            if (UserInfoHost != null) UserInfoHost.Visibility = Visibility.Collapsed;
            if (EditProfileHost != null) EditProfileHost.Visibility = Visibility.Collapsed;

            // --- เพิ่มบรรทัดนี้ ---
            if (OrderDetailsHost != null) OrderDetailsHost.Visibility = Visibility.Collapsed;
        }

        // เรียกตอนกดปุ่ม Edit Profile
        private void ShowEditProfileSection()
        {
            HideAllSections();

            if (_editProfileView == null)
            {
                _editProfileView = new EditProfileView();
                // ปุ่ม Back ในหน้า Edit ให้ย้อนกลับไป UserInfo
                _editProfileView.BackRequested += async () => await ShowUserInfoSectionAsync();
                // ถ้ากด Save สำเร็จ ให้รีโหลดข้อมูลใน UserInfo แล้วค่อยกลับ
                _editProfileView.Saved += async () =>
                {
                    await ShowUserInfoSectionAsync(); // reload ในเมธอดนี้มี await _userInfoView.ReloadAsync()
                };
            }

            EditProfileHost.Content = _editProfileView;
            EditProfileHost.Visibility = Visibility.Visible;
        }



        private void ShowHomeSection()
        {
            HideAllSections(); // <-- เรียกอันนี้ก่อน
            HomeSection.Visibility = Visibility.Visible;
        }

        // --- 🔽 2. แก้ไข ShowUserInfoSectionAsync (เพิ่มการ "ดักฟัง" Event) 🔽 ---
        private async Task ShowUserInfoSectionAsync()
        {
            HideAllSections(); // <-- ย้ายมาไว้บนสุด

            bool needsReload = false;
            if (_userInfoView == null)
            {
                _userInfoView = new UserInfoView();
                _userInfoView.BackRequested += () => ShowHomeSection();
                _userInfoView.EditProfileRequested += () => ShowEditProfileSection();

                // --- ‼️ บรรทัดนี้คือบรรทัดที่ขาดหายไป ‼️ ---
                _userInfoView.OrderDetailsRequested += ShowOrderDetailsSection;
                // -----------------------------------------

                needsReload = true; // ถ้าเพิ่งสร้าง ต้องโหลดข้อมูล
            }

            // (ปรับเงื่อนไข Reload ให้ทำงานเมื่อกลับมาจากหน้า Edit หรือ OrderDetails)
            if (needsReload ||
                (EditProfileHost != null && EditProfileHost.Visibility == Visibility.Visible) ||
                (OrderDetailsHost != null && OrderDetailsHost.Content != null)) // <-- เพิ่มเงื่อนไขนี้
            {
                await _userInfoView.ReloadAsync();
            }

            UserInfoHost.Content = _userInfoView;
            UserInfoHost.Visibility = Visibility.Visible;

            // (ลบการซ่อนหน้าอื่น ๆ เพราะ HideAllSections() ทำไปแล้ว)
        }

        // ปุ่มรูปคนขวาบน
        private async void OpenUserInfo_Click(object sender, RoutedEventArgs e)
        {
            await ShowUserInfoSectionAsync(); // <-- แก้ไขให้เรียกเมธอดที่อัปเดตแล้ว
        }

        private void ShowCheckoutSection()
        {
            HideAllSections(); // <-- เรียกอันนี้ก่อน
            CheckoutHost.Visibility = Visibility.Visible;

            var v = new CheckoutView();
            v.BackRequested += () => ShowHomeSection();

            v.OrderCompleted += async () =>
            {
                await ReloadProductsAfterOrderAsync();
                ShowHomeSection();
            };

            CheckoutHost.Content = v;
        }



        // ไปหน้า Home (Hero + Top Picks)
        public void NavigateHome()
        {
            HomeSection.Visibility = Visibility.Visible;
            ListSection.Visibility = Visibility.Collapsed;
            CartSection.Visibility = Visibility.Collapsed;
            Activate();
        }

        public void NavigateCategory(string category)
        {
            // TODO: ใส่ logic กรองข้อมูลของคุณเอง
            HomeSection.Visibility = Visibility.Collapsed;
            ListSection.Visibility = Visibility.Visible;
            CartSection.Visibility = Visibility.Collapsed;
            Activate();
        }


        // ไปหน้า List ตามหมวด


        // ให้ปุ่มเดิมเรียกใช้เมธอดด้านบน (ลดโค้ดซ้ำ)
        public async void AllKimono_Click(object sender, RoutedEventArgs e)
        {
            await ShowAllProductsAsync();
        }

        public async void Nav_Furisode_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Furisode");
        }

        public async void Nav_Homongi_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Homongi");
        }

        public async void Nav_Hakama_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Hakama");
        }

        public async void Nav_Kurotomesode_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Kurotomesode");
        }

        public async void Nav_Shiromuku_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Shiromuku");
        }

        public async void Nav_Yukata_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Yukata");
        }

        public async void Nav_Accessories_Click(object sender, RoutedEventArgs e)
        {
            await ShowCategoryAsync("Accessories"); // <- แก้จาก "Yukata" เป็น "Accessories"
        }

        private async Task EnsureAllProductsAsync()
        {
            if (_loadedAll) return;
            _allDbProducts = _productSvc.GetAll() ?? new List<Product>();
            _loadedAll = true;
        }


        // ถ้ามีปุ่มเปิด Cart แบบฝังในหน้านี้
        public BitmapImage TopRightAvatar { get; private set; }

        // method
        private void RefreshTopRightAvatar()
        {
            var path = Session.CurrentUser?.AvatarPath;

            // เคลียร์ก่อนกันค้างแคชของ Image control
            TopRightAvatar = null;
            OnPropertyChanged(nameof(TopRightAvatar));

            TopRightAvatar = ImageHelper.LoadBitmapNoCache(path); // โหลดแบบไม่ติดแคช
            OnPropertyChanged(nameof(TopRightAvatar));
        }


        private void ShowCartSection()
        {
            HideAllSections(); // <-- เรียกอันนี้ก่อน
            CartSection.Visibility = Visibility.Visible;
        }

        public UserMainWindow()
        {
            InitializeComponent();
            DataContext = this;

            RefreshTopRightAvatar();
            Session.ProfileChanged += () => Dispatcher.Invoke(RefreshTopRightAvatar); // 👈 ฟังอีเวนต์


            // ----- Commands -----
            PrevHeroCommand = new DelegateCommand(_ => PrevHero());
            NextHeroCommand = new DelegateCommand(_ => NextHero());

            // ----- Hero images -----
            HeroImages.Add("pack://application:,,,/Assets/hero1.jpg");
            HeroImages.Add("pack://application:,,,/Assets/hero2.jpg");
            HeroImages.Add("pack://application:,,,/Assets/hero3.jpg");
            HeroImages.Add("pack://application:,,,/Assets/hero4.jpg");
            HeroIndex = 0;



            // Auto-rotate hero
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += (_, __) => NextHero();
            _timer.Start();

            // โหลด Top Picks ตอน Loaded (กัน UI ค้าง)
            Loaded += async (_, __) => {
                await SafeLoadTopPicksAsync();
                await SafeLoadBestSellingAsync(); // ⬅️ เพิ่มบรรทัดนี้
            };

            Unloaded += (_, __) => _timer.Stop();

            // ----- Hero #2 images (อีกชุดรูป) -----
            HeroImages2.Add("pack://application:,,,/Assets/hero5.jpg");
            HeroImages2.Add("pack://application:,,,/Assets/hero6.jpg");
            HeroImages2.Add("pack://application:,,,/Assets/hero7.jpg");
            HeroImages2.Add("pack://application:,,,/Assets/hero8.jpg");
            HeroIndex2 = 0;

            // คำสั่ง Prev/Next ของ Hero #2
            PrevHero2Command = new DelegateCommand(_ => PrevHero2());
            NextHero2Command = new DelegateCommand(_ => NextHero2());

            // Auto-rotate hero #2
            _timer2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer2.Tick += (_, __) => NextHero2();
            _timer2.Start();

            // เมื่อหน้า Loaded ให้เริ่ม/หยุด timer ให้ครบทั้ง 2 ตัว (มีของเดิมอยู่แล้วสำหรับ hero #1)
            Unloaded += (_, __) => _timer2.Stop();
            CartViewHost.BackRequested += () => ShowHomeSection();

            CartViewHost.ProceedToCheckoutRequested += () => ShowCheckoutSection();

        }







        private async Task ReloadProductsAfterOrderAsync()
        {
            var latest = await Task.Run(() => _productSvc.GetAll() ?? new List<Product>());
            var map = latest.ToDictionary(p => p.Id);

            foreach (var card in TopPicks)
                if (map.TryGetValue(card.Id, out var p)) card.UpdateFrom(p);

            foreach (var card in FilteredProducts)
                if (map.TryGetValue(card.Id, out var p)) card.UpdateFrom(p);
        }


        private void OpenCart_Click(object sender, RoutedEventArgs e)
        {
            ShowCartSection();
        }


        // ===== Heart toggle handlers (สำหรับการ์ดบนหน้า Home/List) =====
        private void Heart_Checked(object sender, RoutedEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;

            // การ์ดบนหน้า Home/List ใช้ TopPickItem ของ Models
            if (dc is MiyunaKimono.Models.TopPickItem t)
                FavoritesService.Instance.Set(t.Id, true);
            // เผื่อบางกรณีเป็น Product ตรง ๆ (เช่นหน้า Details)
            else if (dc is MiyunaKimono.Models.Product p)
                FavoritesService.Instance.Set(p.Id, true);
        }

        private void Heart_Unchecked(object sender, RoutedEventArgs e)
        {
            var dc = (sender as FrameworkElement)?.DataContext;

            if (dc is MiyunaKimono.Models.TopPickItem t)
                FavoritesService.Instance.Set(t.Id, false);
            else if (dc is MiyunaKimono.Models.Product p)
                FavoritesService.Instance.Set(p.Id, false);
        }


        // ====== สำหรับหน้า List ======
        public ObservableCollection<TopPickItem> FilteredProducts { get; } = new();

        private string _listTitle;
        public string ListTitle
        {
            get => _listTitle;
            set { _listTitle = value; OnPropertyChanged(); }
        }

        private int _listCount;
        public int ListCount
        {
            get => _listCount;
            set { _listCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ListCountText)); }
        }

        public string ListCountText => $"({ListCount} Products Available)";

        // cache ข้อมูลทั้งหมดไว้ (ดึงทีเดียวแล้ว reuse)
        private List<Product> _allDbProducts;
        private bool _loadedAll;


        private void ShowHome()
        {
            ShowHomeSection();
        }

        private void ShowList()
        {
            HideAllSections(); // <-- เรียกอันนี้ก่อน
            ListSection.Visibility = Visibility.Visible;
        }

        // (ลบ ShowUserInfo, ShowEditProfile ที่ซ้ำซ้อนออก)


        private static TopPickItem MapToTopPick(Product p)
        {
            var item = new TopPickItem
            {
                Id = p.Id,
                IsFavorite = FavoritesService.Instance.IsFavorite(p.Id)
            };
            item.Attach(p); // <-- สำคัญ! ผูกการ์ดกับ Product เดียวกัน + sync ค่าครั้งแรก
            return item;
        }

        private void Logout_Click(object s, RoutedEventArgs e)
        {
            var uid = MiyunaKimono.Services.AuthService.CurrentUserIdSafe();

            // เซฟ cart รอบสุดท้ายถ้ามี uid
            if (uid > 0)
                CartPersistenceService.Instance.Save(uid, CartService.Instance.Lines.ToList());

            // ถอด autosave และเคลียร์ cart
            CartPersistenceService.Instance.UnwireAutosave();
            CartService.Instance.Clear();

            // ล้าง session
            MiyunaKimono.Services.Session.CurrentUser = null;
            MiyunaKimono.Services.AuthService.SetCurrentUserId(0);

            // กลับหน้า Login โดยไม่ปิดทั้งแอป
            var login = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            Application.Current.MainWindow = login;
            login.Show();
            this.Close();
        }

        private async Task ShowTopPicksPageAsync()
        {
            try
            {
                // 1. ดึงข้อมูลสินค้าแบบสุ่ม 24 รายการจาก ProductService
                // (เรามี GetRandomAsync อยู่แล้ว แค่เปลี่ยนจำนวน)
                var randomProducts = await _productSvc.GetRandomAsync(24);

                // 2. เคลียร์รายการสินค้าที่แสดงผลอยู่ (ถ้ามี)
                FilteredProducts.Clear();

                // 3. แปลง Product -> TopPickItem (ที่การ์ดใน UI ใช้งาน)
                foreach (var p in randomProducts)
                {
                    // ใช้เมธอด MapToTopPick ที่คุณมีอยู่แล้ว
                    FilteredProducts.Add(MapToTopPick(p));
                }

                // 4. ตั้งค่าหัวข้อและจำนวน (ตามที่คุณต้องการ)
                ListTitle = "Top picks for you";
                ListCount = FilteredProducts.Count;

                // 5. สั่งให้แสดงผล ListSection (หน้า All Product เดิม)
                ShowList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load Top Picks: " + ex.Message, "Error");
            }
        }

        private async Task ShowAllProductsAsync()
        {
            await EnsureAllProductsAsync();

            FilteredProducts.Clear();
            foreach (var p in _allDbProducts)
                FilteredProducts.Add(MapToTopPick(p));

            ListTitle = "All Product";
            ListCount = FilteredProducts.Count;
            ShowList();
        }

        private async Task ShowCategoryAsync(string category)
        {
            await EnsureAllProductsAsync();

            FilteredProducts.Clear();
            foreach (var p in _allDbProducts)
            {
                if (string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
                    FilteredProducts.Add(MapToTopPick(p));
            }

            // ---- ตั้งชื่อหัวข้อ: บางหมวดไม่เติมคำว่า "Kimono"
            var noSuffix = category.Equals("Yukata", StringComparison.OrdinalIgnoreCase)
                        || category.Equals("Accessories", StringComparison.OrdinalIgnoreCase);

            ListTitle = noSuffix ? category : $"{category} Kimono";
            // -----------------------------------------------

            ListCount = FilteredProducts.Count;
            ShowList();
        }


        private void Nav_Home_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeSection();
        }

        private async void Nav_All_Click(object sender, RoutedEventArgs e)
        {
            await ShowAllProductsAsync();
        }




        private async void CategoryIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string category && !string.IsNullOrWhiteSpace(category))
            {
                await ShowCategoryAsync(category);
            }
        }

        private void ViewDetails_Click(object sender, RoutedEventArgs e)
        {
            var ctx = (sender as FrameworkElement)?.Tag;

            MiyunaKimono.Models.Product product = null;

            if (ctx is MiyunaKimono.Models.Product p1)
                product = p1;
            else if (ctx is MiyunaKimono.Models.TopPickItem card && card.Source != null)
                product = card.Source;                 // ใช้ instance เดียวกันกับการ์ด
            else if (ctx is MiyunaKimono.Models.TopPickItem card2)
                product = _productSvc.GetById(card2.Id); // fallback

            if (product == null) { MessageBox.Show("ไม่พบข้อมูลสินค้า"); return; }

            var w = new ProductDetailsWindow(product);
            w.Owner = this;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.ShowDialog();

            if (w.WantsToOpenCart)
            {
                ShowCartSection(); // ⬅️ เปิดตะกร้าตามที่หน้า Details ร้องขอ
            }
        }



        // ข้อมูลสำหรับหน้า All Products
        public ObservableCollection<TopPickItemModel> AllProducts { get; } = new();

        // โหลดทั้งหมดจากฐานข้อมูล แล้ว map เป็นการ์ดแบบเดียวกับ Top Picks
        private async Task LoadAllProductsAsync()
        {
            AllProducts.Clear();

            var products = await Task.Run(() => _productSvc.GetAll());

            foreach (var p in products)
            {
                // สร้าง OffText จาก Discount ถ้ามี (สมมติในตารางเก็บ %)
                string off = null;
                if (p.Discount is decimal d && d > 0)
                {
                    // ถ้าเก็บเป็น 0-1 ให้แปลงเป็น %
                    var discPercent = d <= 1m ? (int)Math.Round(d * 100m) : (int)Math.Round(d);
                    off = $"{discPercent}% OFF";
                }

                AllProducts.Add(new TopPickItemModel
                {
                    ProductName = p.ProductName,
                    Category = p.Category,
                    Price = (decimal)p.Price,
                    Quantity = p.Quantity,
                    Image1Path = p.Image1Path,
                    OffText = off
                });
            }
        }





        private void Furisode_Click(object sender, RoutedEventArgs e)
        {
            new ProductListWindow("Furisode Kimono", "Furisode").Show();
            Close(); // ถ้าต้องการปิดหน้า Home เดิม
        }

        private void CF(object sender, RoutedEventArgs e)
        {
            new ProductListWindow("All Product", null).Show();
            Close();
        }


        // ===== Hero #2 =====
        public ObservableCollection<string> HeroImages2 { get; } = new();

        private int _heroIndex2;
        public int HeroIndex2
        {
            get => _heroIndex2;
            set { _heroIndex2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentHeroImage2)); }
        }
        public string CurrentHeroImage2 => HeroImages2.Count == 0 ? null : HeroImages2[HeroIndex2];

        private readonly DispatcherTimer _timer2;
        public ICommand PrevHero2Command { get; }
        public ICommand NextHero2Command { get; }

        private void NextHero2()
        {
            if (HeroImages2.Count == 0) return;
            HeroIndex2 = (HeroIndex2 + 1) % HeroImages2.Count;
        }

        private void PrevHero2()
        {
            if (HeroImages2.Count == 0) return;
            HeroIndex2 = (HeroIndex2 - 1 + HeroImages2.Count) % HeroImages2.Count;
        }




        // ====== Top Picks ======
        private async Task SafeLoadTopPicksAsync()
        {
            try { await LoadTopPicksAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show("Load Top Picks failed: " + ex.Message);
            }
        }

        private async Task LoadTopPicksAsync()
        {
            TopPicks.Clear();

            // ใช้บริการของคุณ (ถ้ามี) ดึงรายการ Top Picks 8 ชิ้น
            // ถ้ายังไม่มีเมธอดนี้ ให้เปลี่ยนเป็น GetAll() แล้ว .Take(8) แทน
            var items = await _productSvc.GetTopPicksAsync(6);

            foreach (TopPickItemModel it in items)
                TopPicks.Add(it);
        }

        private async Task SafeLoadBestSellingAsync()
        {
            try { await LoadBestSellingAsync(12); } // ⬅️ 
            catch (Exception ex)
            {
                MessageBox.Show("Load Best Selling failed: " + ex.Message);
            }
        }

        private async Task LoadBestSellingAsync(int limit)
        {
            await EnsureAllProductsAsync(); // 
            BestSellingItems.Clear();

            // 1. 
            var topSellers = await OrderService.Instance.GetTopSellingProductsAsync(limit);

            // 2. 
            foreach (var ts in topSellers)
            {
                // 
                var product = _productSvc.GetTrackedById(ts.Id);
                if (product != null)
                {
                    // 3. 
                    BestSellingItems.Add(MapToTopPick(product));
                }
            }
        }

        // 4. 
        private async void MoreBestSelling_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 
                await EnsureAllProductsAsync();
                var topSellers = await OrderService.Instance.GetTopSellingProductsAsync(1000); // 

                // 2. 
                FilteredProducts.Clear();
                foreach (var ts in topSellers)
                {
                    var product = _productSvc.GetTrackedById(ts.Id);
                    if (product != null)
                    {
                        FilteredProducts.Add(MapToTopPick(product));
                    }
                }

                // 3. 
                ListTitle = "Best Selling Products";
                ListCount = FilteredProducts.Count;
                ShowList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load all best sellers: " + ex.Message, "Error");
            }
        }

        // ====== Hero control ======
        private void NextHero()
        {
            if (HeroImages.Count == 0) return;
            HeroIndex = (HeroIndex + 1) % HeroImages.Count;
        }
        // เพิ่มในคลาส UserMainWindow
        private void ReflowTopPicks()
        {
            // TODO: ถ้าจะคำนวณจำนวนการ์ดต่อแถวค่อยมาเติม logic ทีหลัง
        }
        // เรียกจากปุ่ม Home ใน XAML: Click="Home_Click"
        private void Home_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeSection(); // แสดง Home ซ่อน List/Cart
        }

        private void PrevHero()
        {
            if (HeroImages.Count == 0) return;
            HeroIndex = (HeroIndex - 1 + HeroImages.Count) % HeroImages.Count;
        }

        // (เผื่อถ้าปุ่มใน XAML ยังใช้ Click อยู่)
        private void NextHero_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            NextHero();
            _timer.Start();
        }



        private void PrevHero_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            PrevHero();
            _timer.Start();
        }

        // ปุ่ม "More >" ในส่วน Top Picks
        private async void MoreTop_Click(object sender, RoutedEventArgs e)
        {
            // เรียกใช้เมธอดใหม่ที่เราสร้าง
            await ShowTopPicksPageAsync();
        }

        // 1. เมธอดสำหรับปุ่มไอคอนหัวใจ (ic_heart) ที่แถบ Top Bar
        private async void Wishlist_Click(object sender, RoutedEventArgs e)
        {
            await ShowFavoritesPageAsync();
        }

        // 2. เมธอดสำหรับเตรียมข้อมูลหน้า "You Like"
        private async Task ShowFavoritesPageAsync()
        {
            try
            {
                // 1. ตรวจสอบให้แน่ใจว่าโหลดสินค้าทั้งหมดเก็บไว้ใน Cache แล้ว
                await EnsureAllProductsAsync();

                // 2. ดึง ID สินค้าที่ User กด Like ไว้
                var favoriteIds = FavoritesService.Instance.GetFavoriteProductIds();

                // 3. เคลียร์รายการสินค้าที่แสดงผลอยู่ (ถ้ามี)
                FilteredProducts.Clear();

                // 4. วนลูปดึงข้อมูล Product จริงจาก ID ที่ได้มา
                foreach (var id in favoriteIds)
                {
                    // (ใช้ GetTrackedById เพื่อดึงจาก Cache ที่โหลดไว้แล้ว จะเร็วกว่า GetById)
                    var product = _productSvc.GetTrackedById(id);

                    if (product == null)
                    {
                        // ถ้าไม่เจอใน Cache (เผื่อไว้) ให้ลองดึงจาก DB โดยตรง
                        product = _productSvc.GetById(id);
                    }

                    if (product != null)
                    {
                        // แปลง Product -> TopPickItem (การ์ด)
                        FilteredProducts.Add(MapToTopPick(product));
                    }
                }

                // 5. ตั้งค่าหัวข้อและจำนวนสินค้า (ตามที่คุณต้องการ)
                ListTitle = "You Like";
                ListCount = FilteredProducts.Count;

                // 6. สั่งให้แสดง ListSection (หน้า All Product เดิม)
                ShowList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load favorites: " + ex.Message, "Error");
            }
        }

        // --- 🔽 4. เพิ่มเมธอดใหม่นี้ 🔽 ---
        /// <summary>
        /// แสดงหน้า Order Details เมื่อถูกเรียกจาก UserInfoView
        /// </summary>
        private async void ShowOrderDetailsSection(string orderId)
        {
            HideAllSections(); // ซ่อนทุกส่วน

            try
            {
                // (ต้องมั่นใจว่าคุณได้สร้างไฟล์ 3 ไฟล์นี้แล้ว)
                var detailsView = new OrderDetailsView(orderId);

                // ตั้งค่าปุ่ม Back ให้กลับไปหน้า UserInfo
                detailsView.BackRequested += async () => await ShowUserInfoSectionAsync();

                // กำหนด Content และแสดงผล (ต้องมี ContentControl ชื่อ "OrderDetailsHost" ใน XAML)
                OrderDetailsHost.Content = detailsView;
                OrderDetailsHost.Visibility = Visibility.Visible;

                // สั่งให้ View ใหม่โหลดข้อมูลของตัวเอง
                await detailsView.LoadOrderDetailsAsync();
            }
            catch (Exception ex)
            {
                // (Error นี้จะเกิดถ้าคุณลืมสร้างไฟล์ 3 ไฟล์ในขั้นตอนก่อนหน้า)
                MessageBox.Show("Error loading OrderDetailsView. Did you create the files?\n" + ex.Message, "Error");
                ShowHomeSection(); // กลับหน้าหลักถ้ามีปัญหา
            }
        }
        // --- 🔼 END: เพิ่มเมธอดใหม่นี้ 🔼 ---


        // ====== INotifyPropertyChanged ======
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ====== คำสั่งง่าย ๆ สำหรับผูกกับปุ่ม (DelegateCommand) ======    
    internal class DelegateCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public DelegateCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        // ===== Hero #2 =====




    }
}