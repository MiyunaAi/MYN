// Services/Session.cs
using MiyunaKimono.Models;
using System;

namespace MiyunaKimono.Services
{
    public static class Session
    {
        public static User CurrentUser { get; set; }

        public static event Action ProfileChanged;
        public static void RaiseProfileChanged() => ProfileChanged?.Invoke();
        public static event Action NewOrderPlaced;
        // ⭐️ 2. เพิ่ม Method นี้สำหรับยิง Event
        public static void RaiseNewOrderPlaced() => NewOrderPlaced?.Invoke();


        // ใช้เวลาบันทึกรูปใหม่เสร็จ
        public static void UpdateAvatarPath(string path)
        {
            if (CurrentUser != null) CurrentUser.AvatarPath = path;
            RaiseProfileChanged();
        }
    }
}
