// Services/ProductService.cs
using MiyunaKimono.Models;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Data.Common;
using System.Linq;


namespace MiyunaKimono.Services
{
    public class ProductCreateDto
    {
        public string ProductName { get; set; }
        public string ProductCode { get; set; }
        public string Status { get; set; }
        public string Brand { get; set; }
        public string Category { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public int Discount { get; set; }
        public string Description { get; set; }
        public bool Visible { get; set; }
        public List<string> ImagePaths { get; } = new();
    }



    public class ProductService
    {
        // ✅ singleton
        public static ProductService Instance { get; } = new ProductService();

        // ✅ cache เพื่อ “คืนออบเจ็กต์เดิมเสมอ”
        private readonly Dictionary<int, Product> _cache = new();

        public Product GetTrackedById(int id) => _cache.TryGetValue(id, out var p) ? p : null;

        private Product GetOrCreate(int id)
        {
            if (!_cache.TryGetValue(id, out var p))
            {
                p = new Product();
                _cache[id] = p;
            }
            return p;
        }

        private static void ApplyRowToProduct(Product p, DbDataReader rd)
        {
            p.Id = Convert.ToInt32(rd["id"]);
            p.ProductCode = rd["product_code"]?.ToString();
            p.ProductName = rd["product_name"]?.ToString();
            p.Status = rd["status"]?.ToString();
            p.Brand = rd["brand"]?.ToString();
            p.Category = rd["category"]?.ToString();
            p.Quantity = rd["quantity"] == DBNull.Value ? 0 : Convert.ToInt32(rd["quantity"]);
            p.Price = rd["price"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["price"]);
            p.Discount = rd["discount"] == DBNull.Value ? 0m : Convert.ToDecimal(rd["discount"]);
            p.Description = rd["description"] as string;
            p.Image1Path = HasColumn(rd, "image1_path") ? rd["image1_path"] as string : null;
            p.Image2Path = HasColumn(rd, "image2_path") ? rd["image2_path"] as string : null;
            p.Image3Path = HasColumn(rd, "image3_path") ? rd["image3_path"] as string : null;
            p.Visible = rd["visible"] != DBNull.Value && Convert.ToBoolean(rd["visible"]);
            p.UpdatedAt = rd["updated_at"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["updated_at"]);
            p.IsFavorite = FavoritesService.Instance.IsFavorite(p.Id);
        }
        public Task<List<string>> GetStatusesAsync()
            => Task.FromResult(new List<string> { "Active", "Inactive", "Draft" });

        public Task<List<string>> GetBrandsAsync()
            => Task.FromResult(new List<string> { "Miyuna", "Yamato", "Sakura" });

        public async Task<List<string>> GetCategoriesAsync()
        {
            var categories = new List<string>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT DISTINCT category 
                FROM products 
                WHERE category IS NOT NULL AND category != '' 
                ORDER BY category", conn);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                categories.Add(rd.GetString(0));
            }
            return categories;
        }



        // helper
        private static bool HasColumn(DbDataReader rd, string name)
        {
            for (int i = 0; i < rd.FieldCount; i++)
                if (string.Equals(rd.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }


        public Task<Product> GetByIdAsync(int id)
    => Task.FromResult(GetById(id)); // reuse sync ที่คุณมีอยู่

        // ===== READ ALL =====
        public List<Product> GetAll()
        {
            var list = new List<Product>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT id, product_code, product_name, status, brand, category,
                       quantity, price, discount, description,
                       image1_path, image2_path, image3_path, visible, updated_at
                FROM products
                WHERE (deleted_at IS NULL OR deleted_at = 0)
                ORDER BY id DESC;", conn);
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int id = Convert.ToInt32(rd["id"]);
                var p = GetOrCreate(id);
                ApplyRowToProduct(p, rd);
                list.Add(p);              // ✅ คืน instance เดิม
            }
            return list;
        }

        // ===== READ BY ID =====
        public Product GetById(int id)
        {
            if (_cache.TryGetValue(id, out var cached)) return cached;

            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
                SELECT id, product_code, product_name, status, brand, category,
                       quantity, price, discount, description,
                       image1_path, image2_path, image3_path, visible, updated_at
                FROM products WHERE id=@id;", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var rd = cmd.ExecuteReader();
            if (!rd.Read()) return null;

            var p = GetOrCreate(id);
            ApplyRowToProduct(p, rd);
            return p;
        }

        // ===== RANDOM N (ใช้หน้า Home) =====
        public async Task<List<Product>> GetRandomAsync(int count = 4)
        {
            var result = new List<Product>();
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
        SELECT id, product_code, product_name, status, brand, category,
               quantity, price, discount, description,
               image1_path, image2_path, image3_path,  -- ✅ เพิ่มสองคอลัมน์นี้
               visible, updated_at
        FROM products
        WHERE (deleted_at IS NULL OR deleted_at = 0)
        ORDER BY RAND()
        LIMIT @n;", conn);
            cmd.Parameters.AddWithValue("@n", count);

            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
            {
                int id = Convert.ToInt32(rd["id"]);
                var p = GetOrCreate(id);
                ApplyRowToProduct(p, rd);
                result.Add(p);
            }
            return result;
        }


        // ===== TOP PICKS (ทำเป็นการ์ดพร้อม Attach แหล่งข้อมูล) =====
        public async Task<List<MiyunaKimono.Models.TopPickItem>> GetTopPicksAsync(int count = 8)
        {
            var products = await GetRandomAsync(count);
            var list = new List<MiyunaKimono.Models.TopPickItem>();
            foreach (var p in products)
            {
                // แปลง Discount -> ข้อความ %OFF แบบย่อ
                var discPercent = p.Discount <= 1m ? (int)Math.Round(p.Discount * 100m) : (int)Math.Round(p.Discount);
                var offText = discPercent > 0 ? $"{discPercent}% OFF" : null;

                var card = new MiyunaKimono.Models.TopPickItem
                {
                    Id = p.Id,
                    ProductName = p.ProductName,
                    Category = p.Category,
                    Price = p.Price,
                    Quantity = p.Quantity,
                    Image1Path = p.Image1Path,
                    OffText = offText,
                    IsFavorite = FavoritesService.Instance.IsFavorite(p.Id)
                };
                // ถ้า TopPickItem มีเมธอด Attach(Product) ให้ผูกเพื่อรีเฟรชตามแคช
                card.Attach(p);
                list.Add(card);
            }
            return list;
        }

        // ===== UPDATE / DELETE / CREATE (เหมือนเดิมของคุณ) =====
        public async Task<bool> UpdateProductAsync(ProductUpdateDto dto)
        {
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
        UPDATE products SET
          product_name=@name, product_code=@code, status=@status, brand=@brand, category=@cat,
          quantity=@qty, price=@price, discount=@disc, description=@desc, visible=@vis,
          image1_path=@img1, image2_path=@img2, image3_path=@img3, updated_at=NOW()
        WHERE id=@id;", conn);

            var (img1, img2, img3) = Pick3(dto.ImagePaths);
            cmd.Parameters.AddWithValue("@id", dto.Id);
            cmd.Parameters.AddWithValue("@name", dto.ProductName);
            cmd.Parameters.AddWithValue("@code", dto.ProductCode);
            cmd.Parameters.AddWithValue("@status", (object?)dto.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@brand", (object?)dto.Brand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cat", (object?)dto.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@qty", dto.Quantity);
            cmd.Parameters.AddWithValue("@price", dto.Price);
            cmd.Parameters.AddWithValue("@disc", dto.Discount);
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vis", dto.Visible);
            cmd.Parameters.AddWithValue("@img1", (object?)img1 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img2", (object?)img2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img3", (object?)img3 ?? DBNull.Value);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> DeleteProductAsync(int id)
        {
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"DELETE FROM products WHERE id=@id;", conn);
            cmd.Parameters.AddWithValue("@id", id);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task<bool> CreateProductAsync(ProductCreateDto dto)
        {
            using var conn = Db.GetConn();
            using var cmd = new MySqlCommand(@"
        INSERT INTO products
          (product_name, product_code, status, brand, category,
           quantity, price, discount, description, visible,
           image1_path, image2_path, image3_path, updated_at)
        VALUES
          (@name, @code, @status, @brand, @cat,
           @qty, @price, @disc, @desc, @vis,
           @img1, @img2, @img3, NOW());", conn);

            var (img1, img2, img3) = Pick3(dto.ImagePaths);
            cmd.Parameters.AddWithValue("@name", dto.ProductName);
            cmd.Parameters.AddWithValue("@code", dto.ProductCode);
            cmd.Parameters.AddWithValue("@status", (object?)dto.Status ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@brand", (object?)dto.Brand ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cat", (object?)dto.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@qty", dto.Quantity);
            cmd.Parameters.AddWithValue("@price", dto.Price);
            cmd.Parameters.AddWithValue("@disc", dto.Discount);
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vis", dto.Visible);
            cmd.Parameters.AddWithValue("@img1", (object?)img1 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img2", (object?)img2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@img3", (object?)img3 ?? DBNull.Value);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        // helper ด้านล่างใส่ท้ายคลาส ProductService ได้เลย
        private static (string img1, string img2, string img3) Pick3(IList<string> paths)
        {
            string p1 = null, p2 = null, p3 = null;
            if (paths != null && paths.Count > 0)
            {
                if (paths.Count >= 1) p1 = paths[0];
                if (paths.Count >= 2) p2 = paths[1];
                if (paths.Count >= 3) p3 = paths[2];
            }
            return (p1, p2, p3);
        }


        // ถ้าจำเป็น: GetByName เหมือนเดิม แต่ให้ใช้ GetOrCreate/ApplyRowToProduct
        public Product GetByName(string name) { /* optional */ return null; }


    }
}
