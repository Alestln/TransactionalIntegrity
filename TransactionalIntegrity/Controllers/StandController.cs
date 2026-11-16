using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransactionalIntegrity.Data;

namespace TransactionalIntegrity.Controllers;

/// <summary>
/// Контроллер управления учебным стендом (просмотр таблиц и сброс данных к начальному состоянию).
/// Наследуется от ControllerBase — базового класса для всех Web API контроллеров в ASP.NET Core.
/// </summary>
[ApiController]
[Route("api/stand")]
public class StandController : ControllerBase
{
    private readonly AppDbContext _db;

    /// <summary>
    /// Внедрение зависимостей (Dependency Injection):
    /// ASP.NET Core сам создаёт экземпляр AppDbContext и передаёт его в конструктор контроллера на каждый HTTP-запрос.
    /// </summary>
    public StandController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Показать текущие балансы кошельков и список дежурных аналитиков SOC.
    /// </summary>
    [HttpGet("state")]
    public async Task<IActionResult> GetState()
    {
        var wallets = await _db.Wallets
            .AsNoTracking()
            .OrderBy(w => w.Id)
            .ToListAsync();

        var socAnalysts = await _db.SocAnalysts
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .ToListAsync();

        return Ok(new
        {
            Message = "Текущее состояние таблиц в PostgreSQL",
            Wallets = wallets,
            SocAnalysts = socAnalysts,
            ActiveAnalystsOnDuty = socAnalysts.Count(a => a.IsOnDuty)
        });
    }

    /// <summary>
    /// Сбросить базу данных к начальным значениям перед следующим опытом (Alice = 1000$, на смене SOC 2 аналитика).
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetState()
    {
        // 1. Удаляем всех добавленных во время тестов фантомных клиентов (оставляем только стартовые Id 1..5)
        await _db.Wallets.Where(w => w.Id > 5).ExecuteDeleteAsync();

        // 2. Возвращаем исходный баланс Alice (1000$, VIP) и остальных клиентов
        await _db.Wallets.Where(w => w.Id == 1)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Balance, 1000m)
                .SetProperty(w => w.IsVip, true));

        await _db.Wallets.Where(w => w.Id == 2)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Balance, 500m)
                .SetProperty(w => w.IsVip, false));

        await _db.Wallets.Where(w => w.Id == 3)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Balance, 50000m)
                .SetProperty(w => w.IsVip, false));

        await _db.Wallets.Where(w => w.Id == 4)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Balance, 3200m)
                .SetProperty(w => w.IsVip, false));

        await _db.Wallets.Where(w => w.Id == 5)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Balance, 12500m)
                .SetProperty(w => w.IsVip, false));

        // 3. Возвращаем ночную смену аналитиков SOC (Officer_Max и Officer_Kate на дежурстве, остальные дома)
        await _db.SocAnalysts.Where(a => a.Id <= 2)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsOnDuty, true));

        await _db.SocAnalysts.Where(a => a.Id > 2)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsOnDuty, false));

        return Ok(new
        {
            Message = "Стенд успешно сброшен к начальному состоянию!",
            AliceBalance = 1000m,
            TotalWallets = 5,
            SocAnalystsOnDuty = "Officer_Max (true), Officer_Kate (true)"
        });
    }
}
