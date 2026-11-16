using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TransactionalIntegrity.Data;
using TransactionalIntegrity.Models;

namespace TransactionalIntegrity.Controllers;

/// <summary>
/// Главный контроллер Лекции №9:
/// Демонстрация аномалий конкурентного доступа (Non-repeatable Read, Phantom Read, Double Spend, Write Skew)
/// и архитектуры уровней изоляции транзакций в PostgreSQL 17 (включая механику SSI — Serializable Snapshot Isolation).
/// </summary>
[ApiController]
[Route("api/anomalies")]
public class IsolationAnomaliesController : ControllerBase
{
    private readonly AppDbContext _db;

    public IsolationAnomaliesController(AppDbContext db)
    {
        _db = db;
    }

    // =========================================================================
    // ОПЫТ 1: NON-REPEATABLE READ (Неповторяющееся чтение)
    // =========================================================================

    /// <summary>
    /// Опыт 1 (Вкладка 1): Демонстрация аномалии Non-repeatable Read (ReadCommitted против RepeatableRead).
    /// </summary>
    /// <remarks>
    /// Как показать на лекции:
    /// 1. Запустите этот метод во Вкладке 1 (он прочитает баланс Alice = 1000$ и подождёт delaySeconds секунд).
    /// 2. Пока идёт ожидание, во Вкладке 2 вызовите метод 1-helper-change-alice-balance (newBalance = 200).
    /// 3. При useRepeatableRead = false (ReadCommitted) второе чтение внутри нашей транзакции увидит 200$ (Аномалия!).
    /// 4. При useRepeatableRead = true (RepeatableRead) второе чтение увидит исходные 1000$ (Защищено снимком Snapshot!).
    /// </remarks>
    [HttpGet("1-non-repeatable-read")]
    public async Task<IActionResult> DemoNonRepeatableRead(
        [FromQuery] bool useRepeatableRead = false,
        [FromQuery] int delaySeconds = 8)
    {
        var isolationLevel = useRepeatableRead
            ? IsolationLevel.RepeatableRead
            : IsolationLevel.ReadCommitted;

        await using var transaction = await _db.Database.BeginTransactionAsync(isolationLevel);

        // Чтение №1 (AsNoTracking нужен, чтобы EF Core реально отправил SELECT в БД, а не взял кэш из памяти C#)
        var firstReadWallet = await _db.Wallets.AsNoTracking().FirstAsync(w => w.OwnerName == "Alice");
        decimal firstBalance = firstReadWallet.Balance;

        // Пауза, чтобы во второй вкладке Swagger преподаватель спокойно успел изменить баланс Alice
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

        // Чтение №2 той же самой строки ВНУТРИ ТОЙ ЖЕ САМОЙ ТРАНЗАКЦИИ!
        var secondReadWallet = await _db.Wallets.AsNoTracking().FirstAsync(w => w.OwnerName == "Alice");
        decimal secondBalance = secondReadWallet.Balance;

        await transaction.CommitAsync();

        bool anomalyOccurred = firstBalance != secondBalance;

        return Ok(new
        {
            IsolationLevelUsed = isolationLevel.ToString(),
            FirstReadBalance = firstBalance,
            SecondReadBalance = secondBalance,
            AnomalyDetected = anomalyOccurred,
            Explanation = anomalyOccurred
                ? "❌ АНОМАЛИЯ NON-REPEATABLE READ! Внутри одной транзакции один и тот же SELECT вернул РАЗНЫЙ баланс, потому что в ReadCommitted каждый SELECT делает новый снимок базы."
                : "✅ ЗАЩИЩЕНО! Баланс при обоих чтениях одинаковый. В RepeatableRead PostgreSQL заморозил снимок (Snapshot) на момент старта транзакции."
        });
    }

    /// <summary>
    /// Опыт 1 (Вкладка 2): Вспомогательный запрос — изменить баланс Alice, пока Вкладка 1 ждёт.
    /// </summary>
    [HttpPost("1-helper-change-alice-balance")]
    public async Task<IActionResult> HelperChangeAliceBalance([FromQuery] decimal newBalance = 200m)
    {
        await _db.Wallets
            .Where(w => w.OwnerName == "Alice")
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Balance, newBalance));

        return Ok(new
        {
            Message = $"Параллельная транзакция успешно изменила и закоммитила баланс Alice = {newBalance}$!"
        });
    }

    // =========================================================================
    // ОПЫТ 2: PHANTOM READ (Фантомное чтение)
    // =========================================================================

    /// <summary>
    /// Опыт 2 (Вкладка 1): Демонстрация Phantom Read (появление новых строк-фантомов при выборке по WHERE).
    /// </summary>
    /// <remarks>
    /// Главное отличие от Non-repeatable Read:
    /// - Non-repeatable Read = изменилась одна существующая строка (UPDATE).
    /// - Phantom Read = при выборке по условию (WHERE IsVip = true) добавилась НОВАЯ строка (INSERT).
    /// СЮРПРИЗ POSTGRESQL: По стандарту SQL уровень RepeatableRead НЕ спасает от фантомов,
    /// но в PostgreSQL RepeatableRead работает на базе Snapshot Isolation и ЗАЩИЩАЕТ даже от фантомов!
    /// </remarks>
    [HttpGet("2-phantom-read")]
    public async Task<IActionResult> DemoPhantomRead(
        [FromQuery] bool useRepeatableRead = false,
        [FromQuery] int delaySeconds = 8)
    {
        var isolationLevel = useRepeatableRead
            ? IsolationLevel.RepeatableRead
            : IsolationLevel.ReadCommitted;

        await using var transaction = await _db.Database.BeginTransactionAsync(isolationLevel);

        // Чтение №1: запрашиваем список всех VIP-клиентов
        var firstVipList = await _db.Wallets.AsNoTracking()
            .Where(w => w.IsVip)
            .Select(w => w.OwnerName)
            .ToListAsync();

        // Пауза: в это время во Вкладке 2 добавляем нового VIP-клиента (INSERT)
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

        // Чтение №2: повторно запрашиваем список VIP-клиентов в той же транзакции
        var secondVipList = await _db.Wallets.AsNoTracking()
            .Where(w => w.IsVip)
            .Select(w => w.OwnerName)
            .ToListAsync();

        await transaction.CommitAsync();

        bool phantomAppeared = firstVipList.Count != secondVipList.Count;

        return Ok(new
        {
            IsolationLevelUsed = isolationLevel.ToString(),
            FirstReadVipCount = firstVipList.Count,
            FirstReadVipNames = firstVipList,
            SecondReadVipCount = secondVipList.Count,
            SecondReadVipNames = secondVipList,
            PhantomReadDetected = phantomAppeared,
            Explanation = phantomAppeared
                ? "❌ АНОМАЛИЯ PHANTOM READ! При повторном SELECT внутри той же транзакции появилась новая строка-призрак (фантом)!"
                : "✅ ЗАЩИЩЕНО! В PostgreSQL уровень RepeatableRead (в отличие от бумажного стандарта SQL!) защищает даже от фантомных строк за счёт единого снимка Snapshot."
        });
    }

    /// <summary>
    /// Опыт 2 (Вкладка 2): Вспомогательный запрос — вставить нового VIP-клиента (INSERT), пока Вкладка 1 ждёт.
    /// </summary>
    [HttpPost("2-helper-insert-vip-phantom")]
    public async Task<IActionResult> HelperInsertVipPhantom([FromQuery] string phantomOwnerName = "Hacker_Eve")
    {
        var newVip = new Wallet
        {
            OwnerName = string.IsNullOrWhiteSpace(phantomOwnerName) ? "Hacker_Eve" : phantomOwnerName,
            Balance = 9999m,
            IsVip = true
        };

        _db.Wallets.Add(newVip);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            Message = $"В таблицу Wallets добавлена новая VIP-строка (INSERT): {newVip.OwnerName}!"
        });
    }

    // =========================================================================
    // ОПЫТ 3: DOUBLE SPEND / RACE CONDITION (Уязвимость гонки при выводе денег)
    // =========================================================================

    /// <summary>
    /// Опыт 3 (Запускать в 2 вкладках одновременно!): Атака Double Spend на вывод 800$ при балансе 1000$.
    /// </summary>
    /// <remarks>
    /// У Alice на балансе 1000$. Откройте 2 вкладки Swagger и одновременно нажмите Execute (amount = 800).
    /// - При useRepeatableRead = false (ReadCommitted): Оба запроса прочитают 1000$, пройдут проверку и оба спишут 800$. Баланс уйдёт в -600$!
    /// - При useRepeatableRead = true (RepeatableRead): Второй запрос будет автоматически убит PostgreSQL с кодом 40001!
    /// </remarks>
    [HttpPost("3-double-spend-withdraw")]
    public async Task<IActionResult> DemoDoubleSpendWithdraw(
        [FromQuery] decimal amount = 800m,
        [FromQuery] bool useRepeatableRead = false,
        [FromQuery] int delaySeconds = 6)
    {
        var isolationLevel = useRepeatableRead
            ? IsolationLevel.RepeatableRead
            : IsolationLevel.ReadCommitted;

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(isolationLevel);

            // Шаг 1 (Time-of-Check): Проверяем, хватает ли денег на счёте Alice
            var aliceBefore = await _db.Wallets.AsNoTracking().FirstAsync(w => w.OwnerName == "Alice");
            decimal balanceBefore = aliceBefore.Balance;

            if (balanceBefore < amount)
            {
                return BadRequest(new
                {
                    Status = "Отклонено банком",
                    Reason = $"Недостаточно средств! Запрошено: {amount}$, на балансе: {balanceBefore}$"
                });
            }

            // Имитируем проверку перевода антифрод-системой (по умолчанию 6 секунд),
            // чтобы второй параллельный запрос хакера успел прочитать старый баланс 1000$!
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            // Шаг 2 (Time-of-Use): Списываем деньги со счёта в БД
            await _db.Wallets
                .Where(w => w.OwnerName == "Alice")
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Balance, w => w.Balance - amount));

            await transaction.CommitAsync();

            var aliceAfter = await _db.Wallets.AsNoTracking().FirstAsync(w => w.OwnerName == "Alice");

            return Ok(new
            {
                Status = "Перевод выполнен!",
                IsolationLevelUsed = isolationLevel.ToString(),
                WithdrawnAmount = amount,
                BalanceBefore = balanceBefore,
                BalanceAfter = aliceAfter.Balance,
                SecurityNote = aliceAfter.Balance < 0
                    ? $"🚨 КРИТИЧЕСКАЯ УЯЗВИМОСТЬ DOUBLE SPEND (TOCTOU)! Хакер вывел {amount}$ дважды при стартовом балансе {balanceBefore}$, и счёт ушёл в минус ({aliceAfter.Balance}$)!"
                    : "Операция завершена."
            });
        }
        catch (Exception ex) when (FindPostgresException(ex) is { SqlState: "40001" } pgEx)
        {
            return Conflict(new
            {
                Status = "🛡️ АТАКА DOUBLE SPEND ОТБИТА БАЗОЙ ДАННЫХ!",
                PostgresErrorCode = pgEx.SqlState,
                PostgresMessage = pgEx.MessageText,
                Explanation = "Уровень RepeatableRead обнаружил, что параллельная транзакция уже изменила эту же строку (кошелёк Alice), и аварийно отменил второе списание!"
            });
        }
    }

    // =========================================================================
    // ОПЫТ 4: SERIALIZATION ANOMALY (WRITE SKEW) И МЕХАНИКА SSI (SERIALIZABLE)
    // =========================================================================

    /// <summary>
    /// Опыт 4 (Вкладка 1: Officer_Max, Вкладка 2: Officer_Kate): Аномалия Write Skew и защита SSI (Serializable).
    /// </summary>
    /// <remarks>
    /// Правило безопасности SOC: на смене всегда должен оставаться минимум 1 офицер (Count(IsOnDuty) &gt;= 1).
    /// Почему RepeatableRead здесь НЕ спасает? Потому что Макс обновляет строку №1, а Катя — строку №2 (разные строки!).
    /// Только уровень Serializable (SSI) через предикатные метки SIREAD обнаруживает перекрёстную зависимость чтение-запись!
    /// </remarks>
    [HttpPost("4-write-skew-soc-duty")]
    public async Task<IActionResult> DemoWriteSkewSocDuty(
        [FromQuery] string analystName = "Officer_Max",
        [FromQuery] bool useSerializable = false,
        [FromQuery] bool enableRetryOn40001 = false,
        [FromQuery] int delaySeconds = 6)
    {
        var isolationLevel = useSerializable
            ? IsolationLevel.Serializable
            : IsolationLevel.RepeatableRead;

        int maxAttempts = enableRetryOn40001 ? 3 : 1;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Очищаем отслеживание объектов EF Core перед каждой попыткой транзакции
                _db.ChangeTracker.Clear();

                await using var transaction = await _db.Database.BeginTransactionAsync(isolationLevel);

                // Шаг 1. Проверяем правило безопасности: сколько офицеров сейчас на дежурстве?
                int onDutyCount = await _db.SocAnalysts.CountAsync(a => a.IsOnDuty);

                if (onDutyCount <= 1)
                {
                    return BadRequest(new
                    {
                        Status = "🚫 ОТКАЗ СИСТЕМЫ БЕЗОПАСНОСТИ SOC!",
                        Attempt = attempt,
                        IsolationLevelUsed = isolationLevel.ToString(),
                        AnalystsCurrentlyOnDuty = onDutyCount,
                        Explanation = $"Аналитику {analystName} запрещено покидать пост! На смене остался всего {onDutyCount} дежурный (по регламенту минимум 1)."
                    });
                }

                // Пауза, чтобы второй аналитик во второй вкладке Swagger тоже успел прочитать onDutyCount == 2!
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

                // Шаг 2. Аналитик снимает с дежурства ТОЛЬКО СВОЮ строку в таблице
                var analyst = await _db.SocAnalysts.FirstOrDefaultAsync(a => a.Name == analystName);
                if (analyst == null)
                {
                    return NotFound($"Аналитик '{analystName}' не найден. Введите 'Officer_Max' или 'Officer_Kate'.");
                }

                analyst.IsOnDuty = false;
                await _db.SaveChangesAsync();

                // В момент CommitAsync алгоритм SSI в PostgreSQL проверяет граф SIREAD-блокировок!
                await transaction.CommitAsync();

                int remainingAfterCommit = await _db.SocAnalysts.AsNoTracking().CountAsync(a => a.IsOnDuty);

                return Ok(new
                {
                    Status = $"✅ Аналитик {analystName} успешно ушёл со смены!",
                    Attempt = attempt,
                    IsolationLevelUsed = isolationLevel.ToString(),
                    RemainingAnalystsOnDuty = remainingAfterCommit,
                    SecurityAlert = remainingAfterCommit == 0
                        ? "🚨 ПРОИЗОШЛА АНОМАЛИЯ СЕРИАЛИЗАЦИИ (WRITE SKEW)! Оба аналитика ушли одновременно, на смене 0 человек! Уровень RepeatableRead не заметил подвоха, так как обновлялись РАЗНЫЕ строки!"
                        : "Смена в безопасности (на посту остался напарник)."
                });
            }
            catch (Exception ex) when (FindPostgresException(ex) is { SqlState: "40001" } pgEx)
            {
                // Если Retry выключен ИЛИ попытки закончились — показываем студентам пойманную ошибку 40001 от SSI
                if (!enableRetryOn40001 || attempt == maxAttempts)
                {
                    return Conflict(new
                    {
                        Status = "🛡️ СРАБОТАЛА ЗАЩИТА SSI (Serializable Snapshot Isolation)!",
                        Attempt = attempt,
                        PostgresErrorCode = pgEx.SqlState,
                        PostgresMessage = pgEx.MessageText,
                        Explanation = "PostgreSQL обнаружил цикл rw-зависимостей (Write Skew) через SIREAD-метки и заблокировал коммит второй транзакции! Включите enableRetryOn40001 = true, чтобы приложение автоматически повторило попытку."
                    });
                }
                // Если enableRetryOn40001 = true, цикл автоматически перейдёт на attempt = 2 и увидит, что на смене остался 1 человек!
            }
        }

        return StatusCode(500);
    }

    /// <summary>
    /// Вспомогательный метод для извлечения PostgresException из обёртки DbUpdateException EF Core.
    /// </summary>
    private static PostgresException? FindPostgresException(Exception ex)
    {
        Exception? current = ex;
        while (current != null)
        {
            if (current is PostgresException pgEx)
                return pgEx;
            current = current.InnerException;
        }
        return null;
    }
}
