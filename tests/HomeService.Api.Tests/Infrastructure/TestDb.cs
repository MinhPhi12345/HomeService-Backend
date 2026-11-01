using HomeService.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Tests.Infrastructure;

/// <summary>
/// CSDL SQLite chạy trong bộ nhớ cho từng test: mỗi test có 1 database riêng, tạo bảng bằng EnsureCreated
/// (dùng đúng AppDbContext của Backend nên quan hệ, unique index, concurrency token… được kiểm tra thật).
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public DbContextOptions<AppDbContext> Options { get; }

    /// <summary>DbContext dùng chung cho service/controller đang được test.</summary>
    public AppDbContext Db { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var init = new AppDbContext(Options)) init.Database.EnsureCreated();
        Db = new AppDbContext(Options);
    }

    /// <summary>DbContext mới (không cache) để đọc lại dữ liệu đã thật sự lưu xuống CSDL.</summary>
    public AppDbContext Fresh() => new(Options);

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
