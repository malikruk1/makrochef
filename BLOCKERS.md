# BLOCKERS.md — потребує людини

Список того, що я не можу зробити сам: акаунти, ключі, OTP, GitHub, рішення.
Код на ці пункти пишеться повністю (див. TASKS.md 1.4), просто не може бути
запущений наживо, поки блокер не знято.

| ID | Що потрібно | Пункт TASKS.md | Статус |
|----|---|---|---|
| B-1 | Дані для Dynamic Client Registration в `auth.silpo.ua` | 3.2 | **ЗНЯТО** 2026-09-13 — DCR працює автоматично, підтверджено живим прогоном |
| B-2 | Реальний акаунт Сільпо + телефон для SMS OTP під час першого OAuth-логіну | 3.2, 3.3, 3.4, 4.1, 6.1, 7.1, 7.3 | **ЗНЯТО** 2026-09-13 — Docker Desktop полагоджено (4.20.1→4.34.3, деталі в CHECKPOINTS.md), токен реально збережено в Postgres, live-виклики до mcp.silpo.ua підтверджено |
| B-3 | Telegram Bot Token від @BotFather для Mini App | 8.2 | OPEN |
| B-5 | Рішення розробника: фрейм профайлера ≥60% (точний БЖВ) чи <60% (індекс + Open Food Facts fallback) | 4.0 | OPEN — перший акаунт був порожній (без чеків), другий акаунт має чеки, але `probe` (3.4) ще не доведений до кінця через нові знахідки нижче |
| B-6 | Перевірка розробником першого реального checkout-лінка очима перед довірою до каскаду знижок | 7.3 | OPEN |
| — | GitHub remote | — (пуш коду) | **ЗНЯТО** 2026-09-14 — https://github.com/malikruk1/makrochef, усі коміти й тег `v1.0-hackathon` запушено |
| — | `ANTHROPIC_API_KEY` — для нормалізації назв і пояснення свопів LLM | .env | OPEN |
| — | `POSTGRES_PASSWORD`, `TOKEN_ENCRYPTION_KEY` — можу згенерувати сам для локальної розробки, підтвердити не потрібно | .env | не блокує |

## Нові технічні знахідки з живого прогону (2026-09-13)
Не блокери для людини, а конкретний код, який ще треба доробити тепер, коли видно реальні дані:

1. **Назви tools мають префікс `silpo_`** (`silpo_get_my_offline_orders`, не `get_my_offline_orders`) — **ВИПРАВЛЕНО**, увесь код і stub-тести оновлені.
2. **`get_shopping_cart_by_id` повертає вкладену структуру** (`cart.shipments[].products[]`, `cart.calculation.validations[]`) — **ВИПРАВЛЕНО**, `CartResponseParser` переписаний, є regression-тест на реальному (очищеному від PII) фікстурі.
3. **`silpo_get_my_offline_orders` вимагає `branchId`/`deliveryType`/`timeslotStart`/`timeslotEnd`** — потребує спершу пройти bootstrap кошика. **ВИПРАВЛЕНО 2026-09-14** — `ProbeCommand` тепер викликає `SessionBootstrap.EnsureAsync()` і передає `SessionContext` у `CoverageProbe`, яка додає ці параметри до `get_my_offline_orders`/`get_my_online_orders` і резолвить slug (через `find_products_batch`) перед кожним `get_product_details`. `create_shopping_cart`-гілка (якщо `exists:false`) досі не реалізована — потребує UI вибору адреси (окремий гап).
4. **`get_product_details` реально приймає `branchId`+`slug`**, а не `productId`. **ВИПРАВЛЕНО 2026-09-14** — `CandidatePoolBuilder`/`SwapGenerator`/`ReoptimizationService`/`ExactMcpNutritionResolver`/`CoverageProbe` усі резолвлять slug через `find_products_batch`/`get_products`/`get_similar_products`/`get_replacements` перед викликом, підтверджено проти живих (очищених від PII) фікстур.
5. Реальні продукти в `get_my_online_orders` мають поле **`id`**, не `productId` — `JsonFieldScanner` оновлено (додано `"id"` як кандидат).

Пункти 3-4 — найбільший наступний шматок роботи, коли буде час: без них `probe`, `CandidatePoolBuilder`, `SwapGenerator` не запрацюють на живих даних, хоча вся логіка після отримання даних (парсинг, солвер, UI) вже написана й протестована.

## Примітка
Не чекати на ці пункти для написання коду. Дивись TASKS.md 10.2 — порядок робіт
без блокерів.
