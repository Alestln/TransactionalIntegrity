namespace TransactionalIntegrity.Models;

/// <summary>
/// Сущность банковского/крипто-кошелька клиента.
/// Используется на лекции для демонстрации:
/// 1. Разницы между EF Core и Dapper (и защиты от SQL-инъекций).
/// 2. Аномалий Dirty Read, Non-repeatable Read, Phantom Read и уязвимости Double Spend (Lost Update).
/// </summary>
public class Wallet
{
    /// <summary>
    /// Первичный ключ (Primary Key) в таблице Wallets.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Имя владельца кошелька (например, Alice или Bob).
    /// </summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>
    /// Текущий баланс в долларах.
    /// </summary>
    public decimal Balance { get; set; }

    /// <summary>
    /// Статус VIP-клиента (используется для выборки по условию WHERE при демонстрации Phantom Read).
    /// </summary>
    public bool IsVip { get; set; }
}
