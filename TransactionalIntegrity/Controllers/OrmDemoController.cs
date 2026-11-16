using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransactionalIntegrity.Data;
using TransactionalIntegrity.Models;

namespace TransactionalIntegrity.Controllers;

/// <summary>
/// Контроллер для демонстрации работы ORM:
/// 1. Разница между полноценной ORM (EF Core) и микро-ORM (Dapper).
/// 2. Демонстрация уязвимости SQL-инъекции (SQLi) и как EF Core защищает от неё по умолчанию.
/// </summary>
[ApiController]
[Route("api/orm-demo")]
public class OrmDemoController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly string _connectionString;

    public OrmDemoController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _connectionString = configuration.GetConnectionString("DefaultConnection")!;
    }

    /// <summary>
    /// 1. Безопасный поиск кошелька через полноценную ORM Entity Framework Core (LINQ).
    /// </summary>
    /// <param name="ownerName">Имя владельца (например: Alice)</param>
    [HttpGet("ef-core")]
    public async Task<IActionResult> SearchViaEfCore([FromQuery] string ownerName = "Alice")
    {
        // ЧТО ПРОИСХОДИТ: Мы пишем запрос на чистом C# с помощью LINQ.
        // ГДЕ ПРИМЕНЯЕТСЯ: 85-90% всей бизнес-логики на реальных проектах.
        // ПОЧЕМУ ЭТО БЕЗОПАСНО: EF Core никогда не склеивает строки! Он превращает переменную ownerName
        // в отдельный параметр SQL (@__ownerName_0). Даже если хакер передаст "' OR '1'='1", SQL-инъекция невозможна!
        var wallets = await _db.Wallets
            .AsNoTracking()
            .Where(w => w.OwnerName == ownerName)
            .ToListAsync();

        return Ok(new
        {
            Technology = "Entity Framework Core (Full ORM + LINQ)",
            Security = "✅ Защищено от SQL-инъекций (автоматическая параметризация запроса)",
            Result = wallets
        });
    }

    /// <summary>
    /// 2. Быстрый поиск кошелька через микро-ORM Dapper (ручной параметризованный SQL).
    /// </summary>
    /// <param name="ownerName">Имя владельца (например: Alice)</param>
    [HttpGet("dapper")]
    public async Task<IActionResult> SearchViaDapper([FromQuery] string ownerName = "Alice")
    {
        // ЧТО ПРОИСХОДИТ: Dapper — это Micro-ORM. Мы сами руками пишем текст SQL-запроса,
        // а Dapper с максимальной скоростью перекладывает колонки ответа в свойства класса Wallet.
        // ГДЕ ПРИМЕНЯЕТСЯ: Тяжёлые аналитические отчёты, сложные JOIN на 100 строк, высоконагруженные микросервисы.
        await using var connection = new NpgsqlConnection(_connectionString);

        const string sql = """
            SELECT "Id", "OwnerName", "Balance", "IsVip"
            FROM "Wallets"
            WHERE "OwnerName" = @OwnerName
            """;

        var wallets = await connection.QueryAsync<Wallet>(sql, new { OwnerName = ownerName });

        return Ok(new
        {
            Technology = "Dapper (Micro-ORM)",
            Security = "✅ Защищено от SQL-инъекций при условии передачи параметров через @OwnerName",
            Result = wallets
        });
    }

    /// <summary>
    /// 3. ❌ УЯЗВИМЫЙ МЕТОД: Взлом базы через SQL-инъекцию (введите: Alice' OR '1'='1).
    /// </summary>
    /// <param name="unsafeUserInput">Попробуйте ввести: Alice' OR '1'='1</param>
    [HttpGet("sql-injection-vulnerable")]
    public async Task<IActionResult> SearchVulnerableToSqlInjection([FromQuery] string unsafeUserInput = "Alice' OR '1'='1")
    {
        // ❌ СМЕРТЕЛЬНАЯ ОШИБКА БЕЗОПАСНОСТИ (CWE-89: SQL Injection):
        // Разработчик склеил строку SQL напрямую с пользовательским вводом через интерполяцию ($"...").
        // Если передать: Alice' OR '1'='1
        // В базу улетит команда: SELECT * FROM "Wallets" WHERE "OwnerName" = 'Alice' OR '1'='1'
        // Так как '1'='1' всегда истинно, PostgreSQL отдаст хакеру ВСЕ кошельки всех клиентов банка!
        string rawSql = $"SELECT * FROM \"Wallets\" WHERE \"OwnerName\" = '{unsafeUserInput}'";

        var leakedWallets = await _db.Wallets
            .FromSqlRaw(rawSql)
            .AsNoTracking()
            .ToListAsync();

        return Ok(new
        {
            Warning = "🚨 УЯЗВИМОСТЬ SQL INJECTION (CWE-89)!",
            ExecutedSqlInDatabase = rawSql,
            HintForStudents = "Вставьте в поле unsafeUserInput строку: Alice' OR '1'='1",
            LeakedWallets = leakedWallets
        });
    }
}
