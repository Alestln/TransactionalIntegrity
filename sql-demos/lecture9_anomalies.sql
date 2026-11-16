-- =============================================================================
-- ЛЕКЦИЯ №9: SQL-скрипты для пошаговой демонстрации аномалий в 2 консолях БД
-- =============================================================================
-- Как использовать в JetBrains Rider:
-- 1. Откройте вкладку Database (справа) -> + -> Data Source -> PostgreSQL
--    Host: localhost, Port: 5432, Database: cyberbank, User: postgres, Password: SecretPassword123!
-- 2. Откройте две консоли запросов (Query Console 1 — "Транзакция 1" и Query Console 2 — "Транзакция 2")
-- 3. Выполняйте команды по шагам (Ctrl+Enter на выделенной строке).
-- =============================================================================

-- Проверка текущего состояния таблиц:
SELECT * FROM "Wallets" ORDER BY "Id";
SELECT * FROM "SocAnalysts" ORDER BY "Id";

-- Сброс данных к исходному состоянию (можно выполнять между опытами):
DELETE FROM "Wallets" WHERE "Id" > 2;
UPDATE "Wallets" SET "Balance" = 1000, "IsVip" = true WHERE "Id" = 1;
UPDATE "Wallets" SET "Balance" = 500, "IsVip" = false WHERE "Id" = 2;
UPDATE "SocAnalysts" SET "IsOnDuty" = true;


-- =============================================================================
-- ОПЫТ 1. DIRTY READ (Грязное чтение) — В PostgreSQL НЕВОЗМОЖЕН даже на READ UNCOMMITTED!
-- =============================================================================
-- [Окно 1 - Магазин]:
BEGIN TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

-- [Окно 2 - Хакер]:
BEGIN;
UPDATE "Wallets" SET "Balance" = 999999 WHERE "OwnerName" = 'Alice';
-- ВНИМАНИЕ: COMMIT во втором окне ещё НЕ нажат!

-- [Окно 1 - Магазин]:
SELECT "Balance" FROM "Wallets" WHERE "OwnerName" = 'Alice';
-- РЕЗУЛЬТАТ: Видим 1000.00, а НЕ 999999! PostgreSQL защищает от Dirty Read всегда!

-- [Завершение в обоих окнах]:
-- Окно 1:
COMMIT;
-- Окно 2:
ROLLBACK;


-- =============================================================================
-- ОПЫТ 2. NON-REPEATABLE READ (Неповторяющееся чтение)
-- =============================================================================
-- ЧАСТЬ А: Воспроизводим аномалию на стандартном уровне READ COMMITTED
-- [Окно 1]:
BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT "Balance" FROM "Wallets" WHERE "OwnerName" = 'Alice'; -- Чтение №1: Видим 1000.00

-- [Окно 2]:
UPDATE "Wallets" SET "Balance" = 200 WHERE "OwnerName" = 'Alice'; -- Авто-коммит

-- [Окно 1]:
SELECT "Balance" FROM "Wallets" WHERE "OwnerName" = 'Alice'; -- Чтение №2 в той же транзакции: Видим 200.00! (АНОМАЛИЯ!)
COMMIT;

-- Вернём баланс 1000:
UPDATE "Wallets" SET "Balance" = 1000 WHERE "OwnerName" = 'Alice';

-- ЧАСТЬ Б: Защищаемся уровнем REPEATABLE READ
-- [Окно 1]:
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SELECT "Balance" FROM "Wallets" WHERE "OwnerName" = 'Alice'; -- Чтение №1 (Сделан Snapshot!): Видим 1000.00

-- [Окно 2]:
UPDATE "Wallets" SET "Balance" = 200 WHERE "OwnerName" = 'Alice'; -- Авто-коммит

-- [Окно 1]:
SELECT "Balance" FROM "Wallets" WHERE "OwnerName" = 'Alice'; -- Чтение №2: Всё ещё видим 1000.00! (ЗАЩИЩЕНО!)
COMMIT;

-- Вернём баланс 1000:
UPDATE "Wallets" SET "Balance" = 1000 WHERE "OwnerName" = 'Alice';


-- =============================================================================
-- ОПЫТ 3. PHANTOM READ (Фантомное чтение — появление новых строк INSERT)
-- =============================================================================
-- [Окно 1]:
BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED;
SELECT COUNT(*), STRING_AGG("OwnerName", ', ') FROM "Wallets" WHERE "IsVip" = true; -- Видим: 1 (Alice)

-- [Окно 2]:
INSERT INTO "Wallets" ("OwnerName", "Balance", "IsVip") VALUES ('Hacker_Eve', 9999, true);

-- [Окно 1]:
SELECT COUNT(*), STRING_AGG("OwnerName", ', ') FROM "Wallets" WHERE "IsVip" = true; -- Видим: 2 (Alice, Hacker_Eve) -> ФАНТОМ!
COMMIT;

-- Очистка фантома:
DELETE FROM "Wallets" WHERE "Id" > 2;
-- (Если повторить этот же опыт с BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ в Окне 1, фантом НЕ появится!)


-- =============================================================================
-- ОПЫТ 4. SERIALIZATION ANOMALY (WRITE SKEW) И ЗАЩИТА SSI (SERIALIZABLE)
-- =============================================================================
-- Правило SOC: на смене ("IsOnDuty" = true) всегда должен оставаться минимум 1 офицер!

-- ЧАСТЬ А: Как REPEATABLE READ пропускает аномалию Write Skew (Оголение смены SOC)
-- [Окно 1 - Макс]:
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SELECT COUNT(*) FROM "SocAnalysts" WHERE "IsOnDuty" = true; -- Видит 2 (уходить можно!)

-- [Окно 2 - Катя]:
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ;
SELECT COUNT(*) FROM "SocAnalysts" WHERE "IsOnDuty" = true; -- Тоже видит 2 (уходить можно!)

-- [Окно 1 - Макс обновляет строку №1]:
UPDATE "SocAnalysts" SET "IsOnDuty" = false WHERE "Name" = 'Officer_Max';

-- [Окно 2 - Катя обновляет строку №2 (конфликта одной строки НЕТ!)]:
UPDATE "SocAnalysts" SET "IsOnDuty" = false WHERE "Name" = 'Officer_Kate';

-- [Оба окна делают COMMIT]:
-- Окно 1:
COMMIT;
-- Окно 2:
COMMIT; -- ОБА КОММИТА ПРОШЛИ УСПЕШНО!

-- Проверяем, сколько человек осталось на смене:
SELECT * FROM "SocAnalysts"; -- У обоих IsOnDuty = false! На смене 0 человек! (Аномалия Write Skew!)

-- Возвращаем аналитиков на смену:
UPDATE "SocAnalysts" SET "IsOnDuty" = true;

-- ЧАСТЬ Б: Защита через SERIALIZABLE (Механика SSI и ошибка 40001)
-- [Окно 1 - Макс]:
BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SELECT COUNT(*) FROM "SocAnalysts" WHERE "IsOnDuty" = true; -- Видит 2 (вешается SIREAD-метка)

-- [Окно 2 - Катя]:
BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;
SELECT COUNT(*) FROM "SocAnalysts" WHERE "IsOnDuty" = true; -- Видит 2 (вешается SIREAD-метка)

-- [Окно 1 - Макс]:
UPDATE "SocAnalysts" SET "IsOnDuty" = false WHERE "Name" = 'Officer_Max';

-- [Окно 2 - Катя]:
UPDATE "SocAnalysts" SET "IsOnDuty" = false WHERE "Name" = 'Officer_Kate';

-- [Окно 1 - Макс]:
COMMIT; -- УСПЕШНО!

-- [Окно 2 - Катя]:
COMMIT;
-- 💥 ОШИБКА 40001: ERROR: could not serialize access due to read/write dependencies among transactions!
-- Алгоритм SSI спас смену SOC!
ROLLBACK;
