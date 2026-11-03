using Microsoft.EntityFrameworkCore;

namespace ZakiSocial.Data;

/// <summary>
/// سياق قاعدة البيانات. الجداول تجي بالخطوة 02 (migrations)، وكل جدول فيه workspace_id.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
