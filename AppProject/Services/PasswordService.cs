// ============================================================================
// ไฟล์: Services/PasswordService.cs (ฝั่งหลังบ้าน - บริการเข้ารหัสและตรวจสอบรหัสผ่าน)
// หน้าที่: แปลงรหัสผ่านข้อความธรรมดาให้เป็นแฮชที่ปลอดภัยด้วยอัลกอริทึม PBKDF2-HMAC-SHA256
//         พร้อมสุ่ม Salt เฉพาะแต่ละบัญชี เพื่อไม่เก็บรหัสผ่านจริงลงฐานข้อมูล
// เชื่อมต่อกับ:
//   - AuthController.cs (ใช้ตอนสมัครสมาชิก, เข้าสู่ระบบ, เปลี่ยนรหัสผ่าน และรีเซ็ตรหัสผ่าน)
//   - DbSeeder.cs (ใช้สร้างรหัสผ่านของบัญชีตัวอย่างตอนเริ่มระบบ)
// ============================================================================

using System.Security.Cryptography;

namespace AppProject.Services;

public static class PasswordService
{
    private const int SaltSize = 16;       // ขนาด Salt แบบสุ่ม 16 ไบต์ (128 บิต)
    private const int KeySize = 32;        // ขนาดผลลัพธ์แฮช 32 ไบต์ (256 บิต)
    private const int Iterations = 100_000; // จำนวนรอบการวนแฮช PBKDF2 เพื่อต้านทาน Brute-force

    // เข้ารหัสรหัสผ่าน: คืนค่าสตริงรูปแบบ "{จำนวนรอบ}.{SaltแบบBase64}.{KeyแบบBase64}"
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize); // สุ่ม Salt ใหม่ทุกครั้ง แม้รหัสผ่านซ้ำแฮชก็จะต่างกัน
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    // ตรวจสอบรหัสผ่านที่ผู้ใช้กรอกจากหน้าบ้าน เทียบกับแฮชที่เก็บอยู่ในฐานข้อมูล (User.PasswordHash)
    public static bool Verify(string password, string storedHash)
    {
        // แยกสตริงแฮชออกเป็น 3 ส่วน: [0]=จำนวนรอบ, [1]=Salt, [2]=Key
        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(parts[2]);
            // นำรหัสผ่านที่กรอกมาแฮชด้วย Salt และจำนวนรอบเดิม
            var candidate = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, key.Length);
            // เปรียบเทียบแบบใช้เวลาคงที่ (FixedTimeEquals) เพื่อป้องกัน Timing Attack
            return CryptographicOperations.FixedTimeEquals(candidate, key);
        }
        catch
        {
            // หากสตริง Base64 เสียหายหรือรูปแบบไม่ถูกต้อง ให้ถือว่าไม่ผ่าน
            return false;
        }
    }
}
