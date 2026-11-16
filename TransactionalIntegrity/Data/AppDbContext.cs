using Microsoft.EntityFrameworkCore;
using TransactionalIntegrity.Models;

namespace TransactionalIntegrity.Data;

/// <summary>
/// Главный класс контекста базы данных Entity Framework Core (ORM).
/// Отвечает за связь C#-классов (Wallet, SocAnalyst) с реальными таблицами в PostgreSQL.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Таблица кошельков "Wallets" в PostgreSQL.
    /// </summary>
    public DbSet<Wallet> Wallets => Set<Wallet>();

    /// <summary>
    /// Таблица дежурных аналитиков "SocAnalysts" в PostgreSQL.
    /// </summary>
    public DbSet<SocAnalyst> SocAnalysts => Set<SocAnalyst>();

    /// <summary>
    /// Настройка схемы БД и начальных тестовых данных (Seed Data),
    /// которые автоматически попадают в миграцию при команде `dotnet ef migrations add`.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Начальные кошельки клиентов банка для экспериментов:
        // У Alice 1000$ (единственный VIP на старте — удобно для демонстрации Phantom Read и Double Spend),
        // плюс счета других клиентов и компаний (отлично видно при демонстрации утечки через SQL-инъекцию).
        modelBuilder.Entity<Wallet>().HasData(
            new Wallet { Id = 1, OwnerName = "Alice", Balance = 1000m, IsVip = true },
            new Wallet { Id = 2, OwnerName = "Bob", Balance = 500m, IsVip = false },
            new Wallet { Id = 3, OwnerName = "Charlie_Corp", Balance = 50000m, IsVip = false },
            new Wallet { Id = 4, OwnerName = "Diana_Saver", Balance = 3200m, IsVip = false },
            new Wallet { Id = 5, OwnerName = "Evan_Trader", Balance = 12500m, IsVip = false }
        );

        // Смена аналитиков кибербезопасности SOC (Security Operations Center):
        // На дежурстве сейчас 2 офицера (Officer_Max и Officer_Kate), ещё 2 офицера отдыхают после смены.
        modelBuilder.Entity<SocAnalyst>().HasData(
            new SocAnalyst { Id = 1, Name = "Officer_Max", IsOnDuty = true },
            new SocAnalyst { Id = 2, Name = "Officer_Kate", IsOnDuty = true },
            new SocAnalyst { Id = 3, Name = "Officer_Alex", IsOnDuty = false },
            new SocAnalyst { Id = 4, Name = "Officer_Olga", IsOnDuty = false }
        );
    }
}
