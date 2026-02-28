// gen_seed.csx — 產生含正確 BCrypt hash 的 seed.sql
// 執行方式：
//   1. 在 NexusAI.Api 專案目錄下執行
//   2. dotnet script gen_seed.csx  （需安裝：dotnet tool install -g dotnet-script）
//   或直接把下方 Program.cs 片段暫時加入後執行一次

#r "nuget: BCrypt.Net-Next, 4.0.3"

using BCrypt.Net;

var users = new[]
{
    ("admin",    "admin123", "系統管理員", "Admin"),
    ("engineer", "eng123",   "張工程師",   "Engineer"),
    ("operator", "op123",    "陳操作員",   "Operator"),
};

Console.WriteLine("-- ============================================================");
Console.WriteLine("-- NexusAI — MariaDB 初始資料 (Seed)");
Console.WriteLine("-- 由 gen_seed.csx 自動產生，BCrypt cost=12");
Console.WriteLine("-- ============================================================");
Console.WriteLine();
Console.WriteLine("DELETE FROM uploaded_files;");
Console.WriteLine("DELETE FROM messages;");
Console.WriteLine("DELETE FROM conversations;");
Console.WriteLine("DELETE FROM users;");
Console.WriteLine();
Console.WriteLine("INSERT INTO users (username, password_hash, display_name, role, is_active, created_at)");
Console.WriteLine("VALUES");

for (int i = 0; i < users.Length; i++)
{
    var (username, password, displayName, role) = users[i];
    var hash = BCrypt.Net.BCrypt.HashPassword(password, 12);
    var comma = i < users.Length - 1 ? "," : ";";
    Console.WriteLine($"  ('{username}', '{hash}', '{displayName}', '{role}', 1, UTC_TIMESTAMP()){comma}");
}

Console.WriteLine();
Console.WriteLine("SELECT id, username, display_name, role, created_at FROM users;");
