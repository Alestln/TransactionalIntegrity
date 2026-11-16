using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TransactionalIntegrity.Data;

namespace TransactionalIntegrity;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // =========================================================================
        // 1. РЕГИСТРАЦИЯ СЕРВИСОВ В DI-КОНТЕЙНЕРЕ
        // =========================================================================

        // Подключаем классические контроллеры (папка Controllers/)
        builder.Services.AddControllers();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Лекция №9: Транзакционная целостность, ORM и уровни изоляции PostgreSQL 17",
                Version = "v1",
                Description = "Учебный стенд для магистров кибербезопасности: EF Core vs Dapper, SQL-инъекции, аномалии конкурентного доступа и SSI."
            });

            // Подключаем отображение наших русских комментариев (<summary>) прямо в интерфейсе Swagger
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }
        });

        // Подключаем Entity Framework Core + драйвер Npgsql для PostgreSQL 17.
        // Строка подключения читается из файла appsettings.json ("DefaultConnection").
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        var app = builder.Build();

        // Автоматическое применение миграций и проверка тестовых данных при старте:
        // Как только вы нажмёте Run на "Ракете", приложение само создаст таблицы и начальные данные в PostgreSQL!
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                await db.Database.MigrateAsync();

                // Гарантируем, что все 5 тестовых кошельков и 4 аналитика SOC присутствуют в БД
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Wallets" ("Id", "OwnerName", "Balance", "IsVip") VALUES
                        (1, 'Alice', 1000.0, true),
                        (2, 'Bob', 500.0, false),
                        (3, 'Charlie_Corp', 50000.0, false),
                        (4, 'Diana_Saver', 3200.0, false),
                        (5, 'Evan_Trader', 12500.0, false)
                    ON CONFLICT ("Id") DO NOTHING;

                    INSERT INTO "SocAnalysts" ("Id", "Name", "IsOnDuty") VALUES
                        (1, 'Officer_Max', true),
                        (2, 'Officer_Kate', true),
                        (3, 'Officer_Alex', false),
                        (4, 'Officer_Olga', false)
                    ON CONFLICT ("Id") DO NOTHING;

                    SELECT setval(pg_get_serial_sequence('"Wallets"', 'Id'), GREATEST((SELECT MAX("Id") FROM "Wallets"), 5), true);
                    SELECT setval(pg_get_serial_sequence('"SocAnalysts"', 'Id'), GREATEST((SELECT MAX("Id") FROM "SocAnalysts"), 4), true);
                    """);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning("Не удалось автоматически применить миграции (проверьте, запущен ли Docker): {Message}", ex.Message);
            }
        }

        // =========================================================================
        // 2. НАСТРОЙКА КОНВЕЙЕРА HTTP-ЗАПРОСОВ (Middleware Pipeline)
        // =========================================================================
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // Перенаправляет HTTP -> HTTPS (работает на веб-сервере Kestrel / "Ракета")
        app.UseHttpsRedirection();

        app.UseAuthorization();

        // Маршрутизация запросов на наши контроллеры (StandController, OrmDemoController, IsolationAnomaliesController)
        app.MapControllers();

        await app.RunAsync();
    }
}
