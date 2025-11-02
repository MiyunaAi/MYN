using MiyunaKimono.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace MiyunaKimono.Services
{

    public class DashboardStats
    {
        public decimal TotalSales { get; set; }
        public int TotalOrders { get; set; }
    }

    // Model สำหรับตาราง Top Selling
    public class TopSellingProduct
    {
        public int Id { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public int TotalOrders { get; set; } // จำนวนออเดอร์ที่มีสินค้านี้
        public decimal TotalSale { get; set; } // ยอดขายรวมของสินค้านี้
    }

    public class ProductReportItem
    {
        // (เราต้องดึง Product ID มาด้วยเพื่อ Join)
        public int ProductId { get; set; }
        public string ProductCode { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public int Quantity { get; set; } // Qty ที่ขายได้
        public decimal Price { get; set; } // ราคาต่อหน่วย
        public decimal Total { get; set; } // ยอดรวม
        public DateTime CreatedAt { get; set; }
    }
    public class OrderService
    {


        public static OrderService Instance { get; } = new();


        public async Task<DashboardStats> GetDashboardStatsAsync()
        {
            var stats = new DashboardStats();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT 
                    SUM(amount) AS TotalSales, 
                    COUNT(*) AS TotalOrders 
                FROM orders", conn);

            using var rd = await cmd.ExecuteReaderAsync();
            if (await rd.ReadAsync())
            {
                stats.TotalSales = rd["TotalSales"] == DBNull.Value ? 0m : rd.GetDecimal("TotalSales");
                stats.TotalOrders = rd["TotalOrders"] == DBNull.Value ? 0 : rd.GetInt32("TotalOrders");
            }
            return stats;
        }

        public async Task<List<TopSellingProduct>> GetTopSellingProductsAsync(int limit = 10)
        {
            var list = new List<TopSellingProduct>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
        SELECT 
            p.id,                   -- ⬅️ 1. เพิ่ม p.id
            p.product_code, 
            p.product_name, 
            p.category, 
            p.price, 
            COUNT(DISTINCT oi.order_id) AS TotalOrders, 
            SUM(oi.total) AS TotalSale
        FROM order_items oi
        JOIN products p ON oi.product_name = p.product_name 
        GROUP BY p.id, p.product_code, p.product_name, p.category, p.price -- ⬅️ 2. เพิ่ม p.id ใน GROUP BY
        ORDER BY TotalSale DESC
        LIMIT @limit", conn);

            cmd.Parameters.AddWithValue("@limit", limit);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new TopSellingProduct
                {
                    Id = rd.GetInt32("id"), // ⬅️ 3. อ่านค่า Id
                    ProductCode = rd["product_code"]?.ToString(),
                    ProductName = rd["product_name"]?.ToString(),
                    Category = rd["category"]?.ToString(),
                    Price = rd.GetDecimal("price"),
                    TotalOrders = rd.GetInt32("TotalOrders"),
                    TotalSale = rd.GetDecimal("TotalSale")
                });
            }
            return list;
        }

        public async Task<List<AdminOrderInfo>> GetMostExpensiveTransactionsAsync(int limit = 10)
        {
            var list = new List<AdminOrderInfo>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT order_id, customer_name, amount, status, created_at 
                FROM orders
                ORDER BY amount DESC
                LIMIT @limit", conn); // ⬅️ เรียงตาม Amount (แพงที่สุด)

            cmd.Parameters.AddWithValue("@limit", limit);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AdminOrderInfo
                {
                    Id = rd["order_id"]?.ToString(),
                    CustomerName = rd["customer_name"] as string,
                    Amount = rd.GetDecimal("amount"),
                    Status = rd["status"] as string,
                    CreatedAt = rd.GetDateTime("created_at")
                });
            }
            return list;
        }

        public sealed class AdminOrderInfo
        {
            public string Id { get; set; }
            public string CustomerName { get; set; } // จากคอลัมน์ username หรือ customer_name
            public decimal Amount { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public async Task<List<ProductReportItem>> GetProductReportAsync()
        {
            var list = new List<ProductReportItem>();
            using var conn = Db.GetConn();

            // 1. เราต้อง Join 3 ตาราง: orders (สำหรับวันที่), order_items (สำหรับ Qty, Total), และ products (สำหรับ Code)
            // 2. เราใช้ p.product_name, p.price แทน o_item.product_name, o_item.price เพื่อให้ข้อมูลตรงกับตารางหลัก
            using var cmd = new MySqlCommand(@"
                SELECT 
                    p.id, 
                    p.product_code, 
                    p.product_name,
                    p.category,
                    o_item.qty, 
                    p.price, 
                    o_item.total, 
                    o.created_at
                FROM order_items AS o_item
                INNER JOIN orders AS o ON o_item.order_id = o.order_id
                INNER JOIN products AS p ON o_item.product_name = p.product_name", conn);
            // (หมายเหตุ: การ Join ด้วย product_name อาจไม่ดีเท่า Join ด้วย product_id แต่ทำตามสคีมาปัจจุบัน)

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new ProductReportItem
                {
                    ProductId = rd.GetInt32("id"),
                    ProductCode = rd["product_code"]?.ToString(),
                    ProductName = rd["product_name"]?.ToString(),
                    Category = rd["category"]?.ToString(),
                    Quantity = rd.GetInt32("qty"),
                    Price = rd.GetDecimal("price"),
                    Total = rd.GetDecimal("total"),
                    CreatedAt = rd.GetDateTime("created_at")
                });
            }
            return list;
        }


        public async Task<List<AdminOrderInfo>> GetAllOrdersAsync()
        {
            var list = new List<AdminOrderInfo>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT order_id, username, amount, status, created_at
                FROM orders
                ORDER BY created_at DESC;", conn); // ⬅️ เรียงล่าสุดมาก่อน

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                list.Add(new AdminOrderInfo
                {
                    Id = rd["order_id"]?.ToString(),
                    CustomerName = rd["username"] as string, // ⬅️ ใช้ username
                    Amount = rd["amount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["amount"]),
                    Status = rd["status"] as string,
                    CreatedAt = rd["created_at"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(rd["created_at"])
                });
            }
            return list;
        }



        // ====== เวอร์ชันเดิม (ยังใช้ได้) ======
        public void CreateOrder(int userId, string address, IList<CartLine> lines)
        {
            using var conn = Db.GetConn();
            using var tx = conn.BeginTransaction();
            try
            {
                var total = lines.Sum(l => l.LineTotal);
                var discount = lines.Sum(l =>
                {
                    var price = l.Product.Price;
                    var after = l.Product.PriceAfterDiscount ?? price;
                    return (price - after) * l.Quantity;
                });

                int orderAutoId;
                using (var cmd = new MySqlCommand(
                    @"INSERT INTO orders(user_id, address, total_amount, discount_amount, created_at)
                      VALUES(@u,@a,@t,@d,NOW()); SELECT LAST_INSERT_ID();", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    cmd.Parameters.AddWithValue("@a", address ?? "");
                    cmd.Parameters.AddWithValue("@t", total);
                    cmd.Parameters.AddWithValue("@d", discount);
                    orderAutoId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                foreach (var l in lines)
                {
                    var price = l.Product.Price;
                    var after = l.Product.PriceAfterDiscount ?? price;

                    using var it = new MySqlCommand(
                        @"INSERT INTO order_items(order_id, product_id, quantity, unit_price, discount_per_unit)
                          VALUES(@o,@p,@q,@up,@disc);", conn, tx);
                    it.Parameters.AddWithValue("@o", orderAutoId);
                    it.Parameters.AddWithValue("@p", l.Product.Id);
                    it.Parameters.AddWithValue("@q", l.Quantity);
                    it.Parameters.AddWithValue("@up", price);
                    it.Parameters.AddWithValue("@disc", price - after);
                    it.ExecuteNonQuery();
                }

                using (var del = new MySqlCommand("DELETE FROM user_carts WHERE user_id=@u;", conn, tx))
                { del.Parameters.AddWithValue("@u", userId); del.ExecuteNonQuery(); }

                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
        }



        // ===== ข้อมูลออเดอร์แบบย่อ สำหรับหน้า User Info =====
        public sealed class OrderInfo
        {
            // ใช้ string เพื่อรองรับ order_id แบบ "ORD202510..." 
            public string Id { get; set; }
            public decimal Amount { get; set; }
            public decimal Discount { get; set; }
            public string Status { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        /// <summary>
        /// คืนรายการออเดอร์ของผู้ใช้ เรียงล่าสุดก่อน
        /// รองรับทั้งสคีมาเก่า (id, total_amount, discount_amount)
        /// และสคีมาใหม่ (order_id, amount, discount, status, created_at)
        /// </summary>
        public async Task<List<OrderInfo>> GetOrdersByUserAsync(int userId)
        {
            var list = new List<OrderInfo>();
            using var conn = Db.GetConn();

            // พยายามอ่านจากสคีมาใหม่ก่อน
            try
            {
                using var cmd = new MySqlCommand(@"
            SELECT order_id, amount, discount, status, created_at
            FROM orders
            WHERE user_id = @uid
            ORDER BY created_at DESC;", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    list.Add(new OrderInfo
                    {
                        Id = rd["order_id"]?.ToString(),
                        Amount = rd["amount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["amount"]),
                        Discount = rd["discount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["discount"]),
                        Status = rd["status"] as string,
                        CreatedAt = rd["created_at"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(rd["created_at"])
                    });
                }
            }
            catch
            {
                // ถ้าโครงสร้างนี้ไม่มี ให้ลองสคีมาเก่า
            }

            // ถ้ายังว่าง ลองสคีมาเก่า
            if (list.Count == 0)
            {
                using var cmd2 = new MySqlCommand(@"
            SELECT id, total_amount, discount_amount, created_at
            FROM orders
            WHERE user_id = @uid
            ORDER BY created_at DESC;", conn);
                cmd2.Parameters.AddWithValue("@uid", userId);

                using var rd2 = await cmd2.ExecuteReaderAsync();
                while (await rd2.ReadAsync())
                {
                    var idNum = rd2["id"] == DBNull.Value ? 0 : Convert.ToInt32(rd2["id"]);
                    list.Add(new OrderInfo
                    {
                        Id = idNum.ToString(),              // แปลงเป็น string ให้รูปแบบเดียวกัน
                        Amount = rd2["total_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd2["total_amount"]),
                        Discount = rd2["discount_amount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd2["discount_amount"]),
                        Status = "—",                            // สคีมาเก่าอาจไม่มี status
                        CreatedAt = rd2["created_at"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(rd2["created_at"])
                    });
                }
            }

            return list;
        }



        // ====== เวอร์ชันเต็มที่คุณต้องการ (เก็บข้อมูลลูกค้า/สถานะ/สลิป/ไฟล์ ฯลฯ) ======
        public class NewOrderLine
        {
            public string ProductName { get; set; }
            public int Qty { get; set; }
            public decimal Price { get; set; }   // ราคาต่อชิ้น (ราคาเต็ม)
            public decimal Total { get; set; }   // QTY * (ราคาหลังลดของคุณ ถ้าต้องการ)
        }

        /// <summary>
        /// สร้างออเดอร์แบบครบถ้วน: gen order_id (text), เก็บข้อมูลลูกค้า/สลิป, สร้าง order_items
        /// คืนค่า order_id (เช่น "ORD20251027152301")
        /// </summary>
        public async Task<string> CreateOrderFullAsync(
            int userId,
            string customerFullName,
            string username,
            string address,
            string tel,
            List<CartLine> lines,
            decimal total,
            decimal discount,
            byte[] receiptBytes,
            string receiptFileName)
        {
            // order_id แบบอ่านง่าย (text key) ตามที่คุณระบุ
            var id = "ORD" + DateTime.Now.ToString("yyyyMMddHHmmss");

            using var conn = Db.GetConn(); // สมมุติเปิดแล้ว
            using var tx = await conn.BeginTransactionAsync();

            // บันทึกหัวออเดอร์
            var cmd = new MySqlCommand(@"
INSERT INTO orders
(order_id, user_id, customer_name, username, address, tel,
 amount, discount, status, created_at, receipt_file_name, receipt_content)
VALUES(@id,@uid,@cname,@uname,@addr,@tel,@amt,@disc,'Ordering',NOW(),@fn,@file);
", conn, (MySqlTransaction)tx);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@uid", userId);
            cmd.Parameters.AddWithValue("@cname", customerFullName ?? "");
            cmd.Parameters.AddWithValue("@uname", username ?? "");
            cmd.Parameters.AddWithValue("@addr", address ?? "");
            cmd.Parameters.AddWithValue("@tel", tel ?? "");
            cmd.Parameters.AddWithValue("@amt", total);
            cmd.Parameters.AddWithValue("@disc", discount);
            cmd.Parameters.AddWithValue("@fn", string.IsNullOrWhiteSpace(receiptFileName) ? "receipt" : receiptFileName);
            cmd.Parameters.AddWithValue("@file", receiptBytes ?? Array.Empty<byte>());
            await cmd.ExecuteNonQueryAsync();

            // บันทึกรายการสินค้า
            foreach (var l in lines)
            {
                // 1) บันทึก order_items (ของเดิม)
                var cmd2 = new MySqlCommand(@"
        INSERT INTO order_items(order_id, product_name, qty, price, total)
        VALUES(@id,@name,@q,@p,@t)", conn, (MySqlTransaction)tx);
                cmd2.Parameters.AddWithValue("@id", id);
                cmd2.Parameters.AddWithValue("@name", l.Product.ProductName);
                cmd2.Parameters.AddWithValue("@q", l.Quantity);
                cmd2.Parameters.AddWithValue("@p", l.Product.Price);
                cmd2.Parameters.AddWithValue("@t", l.LineTotal);
                await cmd2.ExecuteNonQueryAsync();

                // 2) ล็อกแถวสินค้า แล้วเช็คสต็อก
                var lockCmd = new MySqlCommand(
                    "SELECT quantity FROM products WHERE id=@pid FOR UPDATE",
                    conn, (MySqlTransaction)tx);
                lockCmd.Parameters.AddWithValue("@pid", l.Product.Id);
                var currentQtyObj = await lockCmd.ExecuteScalarAsync();
                var currentQty = currentQtyObj == null ? 0 : Convert.ToInt32(currentQtyObj);

                if (currentQty < l.Quantity)
                    throw new Exception($"สินค้า '{l.Product.ProductName}' คงเหลือไม่พอ (เหลือ {currentQty}, ต้องการ {l.Quantity})");

                // 3) ตัดสต็อก
                var upd = new MySqlCommand(
                    "UPDATE products SET quantity = quantity - @q WHERE id=@pid",
                    conn, (MySqlTransaction)tx);
                upd.Parameters.AddWithValue("@q", l.Quantity);
                upd.Parameters.AddWithValue("@pid", l.Product.Id);
                await upd.ExecuteNonQueryAsync();
            }


            await tx.CommitAsync();

            // ✅ อัปเดตแคชของสินค้าให้ลด QTY แบบ realtime (ยิง PropertyChanged)
            foreach (var line in lines) // lines = parameter ที่ส่งเข้ามา
            {
                var tracked = ProductService.Instance.GetTrackedById(line.Product.Id);
                if (tracked != null)
                    tracked.Quantity = Math.Max(0, tracked.Quantity - line.Quantity);
            }



            return id;
        }

        // ในไฟล์ Services/OrderService.cs
        // (แทนที่เมธอดเดิมทั้งหมด)
        // ในไฟล์ Services/OrderService.cs
        // (แทนที่เมธอดเดิมทั้งหมด)
        // (วางโค้ดนี้แทนที่เมธอด GetMonthlyReportDataAsync เดิมทั้งหมด)
        // ใน Services/OrderService.cs
        public async Task<MonthlyReportData> GetMonthlyReportDataAsync(int gregorianYear, int month)
        {
            var data = new MonthlyReportData();
            using var conn = Db.GetConn();

            // --- 1. สร้าง WHERE clause แบบไดนามิก ---
            var whereConditions = new List<string>();
            if (gregorianYear > 0)
            {
                whereConditions.Add("YEAR(o.created_at) = @year");
            }
            if (month > 0)
            {
                whereConditions.Add("MONTH(o.created_at) = @month");
            }
            string whereClause = "";
            if (whereConditions.Count > 0)
            {
                whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            }

            // --- 2. ดึงข้อมูลตาราง Orders ---
            var sqlOrders = $@"
        SELECT created_at, customer_name, status, amount 
        FROM orders o
        {whereClause}
        ORDER BY created_at ASC";

            using (var cmd = new MySqlCommand(sqlOrders, conn))
            {
                if (gregorianYear > 0) cmd.Parameters.AddWithValue("@year", gregorianYear);
                if (month > 0) cmd.Parameters.AddWithValue("@month", month);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    data.Orders.Add(new MonthlyOrderReportItem
                    {
                        Date = rd.GetDateTime("created_at"),
                        CustomerName = rd["customer_name"] as string,
                        Status = rd["status"] as string ?? "Ordering",
                        Total = rd.GetDecimal("amount")
                    });
                }
            } // ปิด Reader

            // --- 3. โหลด Product ทั้งหมดมาพักไว้ใน C# และ TRIM ---
            var allProducts = ProductService.Instance.GetAll();
            var productLookup = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in allProducts)
            {
                var trimmedName = p.ProductName?.Trim();
                if (!string.IsNullOrEmpty(trimmedName) && !productLookup.ContainsKey(trimmedName))
                {
                    productLookup[trimmedName] = p;
                }
            }

            // --- 4. ดึงข้อมูล Product Sales โดย TRIM และกรอง NULL ใน SQL ---
            var sqlProducts = $@"
        SELECT 
            TRIM(oi.product_name) AS product_name, 
            SUM(oi.qty) AS ItemsOff, 
            SUM(oi.total) AS TotalSale
        FROM order_items oi
        JOIN orders o ON oi.order_id = o.order_id
        {whereClause}
        {(whereClause.Length > 0 ? "AND" : "WHERE")} oi.product_name IS NOT NULL 
        GROUP BY TRIM(oi.product_name)
        ORDER BY TotalSale DESC";

            using (var cmd = new MySqlCommand(sqlProducts, conn))
            {
                if (gregorianYear > 0) cmd.Parameters.AddWithValue("@year", gregorianYear);
                if (month > 0) cmd.Parameters.AddWithValue("@month", month);

                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    var productName = rd["product_name"] as string;

                    if (string.IsNullOrEmpty(productName))
                    {
                        continue; // ข้ามแถวที่เป็น NULL
                    }

                    // --- 5. ใช้ C# ในการ Join ---
                    string brand = "N/A";
                    string category = "N/A";

                    if (productLookup.TryGetValue(productName, out var productInfo))
                    {
                        brand = productInfo.Brand ?? "N/A";
                        category = productInfo.Category ?? "N/A";
                    }

                    data.Products.Add(new MonthlyProductReportItem
                    {
                        ProductName = productName,
                        Brand = brand,
                        Category = category,
                        ItemsOff = rd.GetInt32("ItemsOff"),
                        TotalSale = rd.GetDecimal("TotalSale")
                    });
                }
            } // ปิด Reader

            // 6. คำนวณยอดรวมทั้งหมด
            data.GrandTotalSale = data.Products.Sum(p => p.TotalSale);

            return data;
        }
        public async Task<OrderDetailsModel> GetOrderDetailsAsync(string orderId)
        {
            var details = new OrderDetailsModel { OrderId = orderId };

            using var conn = Db.GetConn();

            // 1. ดึงข้อมูลหลักจากตาราง 'orders'
            // (เราสมมติว่ามีคอลัมน์ tracking_number)
            string sqlOrder = @"
                SELECT customer_name, status, tracking_number, address, amount, receipt_content, tel 
                FROM orders 
                WHERE order_id = @id";

            using (var cmd = new MySqlCommand(sqlOrder, conn))
            {
                cmd.Parameters.AddWithValue("@id", orderId);
                using var rd = await cmd.ExecuteReaderAsync();
                if (!await rd.ReadAsync())
                {
                    return null; // ไม่พบ Order ID นี้
                }

                details.CustomerName = rd["customer_name"]?.ToString();
                details.Status = rd["status"]?.ToString() ?? "Order"; // ค่าเริ่มต้น
                details.TrackingNumber = rd.IsDBNull(rd.GetOrdinal("tracking_number")) ? null : rd.GetString("tracking_number");
                details.Address = rd["address"]?.ToString();
                details.TotalAmount = rd["amount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["amount"]);
                details.PaymentSlipBytes = rd["receipt_content"] as byte[];
                details.Tel = rd["tel"]?.ToString();
            }

            // 2. ดึงรายการสินค้าจาก 'order_items'
            // (เราใช้ตาราง order_items ที่คุณมีอยู่แล้ว)
            string sqlItems = @"
                SELECT product_name, qty, price, total 
                FROM order_items 
                WHERE order_id = @id";

            using (var cmd = new MySqlCommand(sqlItems, conn))
            {
                cmd.Parameters.AddWithValue("@id", orderId);
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                {
                    details.Items.Add(new OrderItemModel
                    {
                        ProductName = rd["product_name"]?.ToString(),
                        Quantity = Convert.ToInt32(rd["qty"]),
                        Price = Convert.ToDecimal(rd["price"]), // ราคาต่อหน่วย
                        Total = Convert.ToDecimal(rd["total"])  // ราคารวม
                    });
                }
            }

            return details;
        }
        // --- 🔽 2. เพิ่มเมธอดใหม่สำหรับปุ่ม SAVE 🔽 ---
        public async Task<bool> UpdateAdminOrderAsync(string orderId, string status, string trackingNumber, string address)
        {
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                UPDATE orders 
                SET 
                    status = @status,
                    tracking_number = @track,
                    address = @addr,
                    updated_at = NOW()
                WHERE order_id = @id", conn);

            cmd.Parameters.AddWithValue("@id", orderId);
            cmd.Parameters.AddWithValue("@status", (object)status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@track", (object)trackingNumber ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@addr", (object)address ?? DBNull.Value);

            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }   



    }

    // (วางโค้ดนี้ต่อจากปีกกาปิด } ของคลาส OrderService)

    /// <summary>
    /// Model สำหรับ 1 แถวในตาราง Order ของ Report
    /// </summary>
    public class MonthlyOrderReportItem
    {
        public DateTime Date { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
        public decimal Total { get; set; }
    }

    /// <summary>
    /// Model สำหรับ 1 แถวในตาราง Product Sales ของ Report
    /// </summary>
    public class MonthlyProductReportItem
    {
        public string ProductName { get; set; }
        public string Brand { get; set; }
        public string Category { get; set; }
        public int ItemsOff { get; set; } // จำนวนที่ขายได้
        public decimal TotalSale { get; set; } // ยอดขายรวม
    }

    /// <summary>
    /// Model หลักสำหรับเก็บข้อมูลทั้งหมดที่จะส่งไปสร้าง PDF
    /// </summary>
    public class MonthlyReportData
    {
        public List<MonthlyOrderReportItem> Orders { get; set; } = new List<MonthlyOrderReportItem>();
        public List<MonthlyProductReportItem> Products { get; set; } = new List<MonthlyProductReportItem>();
        public decimal GrandTotalSale { get; set; }
    }


}