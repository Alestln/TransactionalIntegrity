namespace TransactionalIntegrity.Models;

/// <summary>
/// Сущность дежурного аналитика центра кибербезопасности (SOC — Security Operations Center).
/// Используется на лекции для демонстрации:
/// Аномалии сериализации (Serialization Anomaly / Write Skew) и работы алгоритма SSI (Serializable Snapshot Isolation).
/// Бизнес-правило безопасности: на смене (IsOnDuty = true) ВСЕГДА должен оставаться минимум 1 аналитик!
/// </summary>
public class SocAnalyst
{
    /// <summary>
    /// Первичный ключ в таблице SocAnalysts.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Имя дежурного офицера безопасности (например, Officer_Max или Officer_Kate).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Находится ли аналитик прямо сейчас на дежурной смене.
    /// </summary>
    public bool IsOnDuty { get; set; }
}
