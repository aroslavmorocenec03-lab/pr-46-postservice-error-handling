# Практична робота №46

## Обробка помилок у клієнтському застосунку

Windows Forms-клієнт **PostService.WindowsApp** із класом `PostServiceClient`, серверною валідацією, повторенням запиту та глобальним обробником необроблених винятків.

Робота містить самодостатній навчальний сервер із контрактом поштових відправлень з попередньої практичної: `From`, `To`, `Content`, `DeliveryType`, `Weight`, `Width`, `Height`, `Depth`, `Value`, `Price`. Сервер зберігає навчальні дані в пам’яті; після перезапуску повертаються три початкові записи.

**[Звіт із перевіркою всіх ситуацій та відповідями на контрольні запитання](reports/REPORT.md)**.

## Запуск

Потрібні **Windows і .NET SDK 10**. Зовнішніх NuGet-пакетів немає. Завантажте через **Code → Download ZIP**, розпакуйте й відкрийте термінал у кореневій папці.

```powershell
dotnet build PostService.slnx
```

У першому терміналі запустіть сервер і залиште його працювати:

```powershell
dotnet run --project src/PostService.Server
```

У другому терміналі запустіть Windows-клієнт:

```powershell
dotnet run --project src/PostService.WindowsApp
```

Адреса за замовчуванням — `http://localhost:5211/`. «Оновити» завантажує таблицю; «Додати» відкриває нову форму; «Редагувати» спочатку отримує актуальний запис із сервера; «Видалити» запитує підтвердження.

Іншу адресу клієнта можна задати перед запуском у його терміналі:

```powershell
$env:POST_SERVICE_URL = 'http://localhost:5211/'
dotnet run --project src/PostService.WindowsApp
```

## Архітектура і контракт

| Проєкт | Призначення |
| --- | --- |
| `PostService.Contracts` | Спільні DTO запитів і відповідей без залежності від ASP.NET Core |
| `PostService.Client` | `IPostServiceClient`, `PostServiceClient`, виняток валідації та діагностика |
| `PostService.Server` | Навчальний HTTP-сервер та правила валідації |
| `PostService.WindowsApp` | Таблиця, редактор, діалог повтору та глобальні обробники |
| `PostService.Checks` | Перевірки клієнта й інтеграційні запити до реального сервера |

| Ендпойнт | Метод IPostServiceClient | Результат |
| --- | --- | --- |
| `GET /postings` | `GetPostingsAsync()` | `PostingGetDto[]` |
| `GET /postings/{id}` | `GetPostingByIdAsync(int id)` | `PostingGetDto` |
| `POST /postings` | `CreatePostingAsync(PostingPostDto posting)` | `PostingGetDto`, HTTP 201 |
| `PUT /postings/{id}` | `UpdatePostingAsync(int id, PostingPutDto posting)` | `PostingGetDto` |
| `DELETE /postings/{id}` | `DeletePostingAsync(int id)` | HTTP 204 без читання JSON |

`HttpClient` створює, налаштовує та звільняє саме `PostServiceClient`; форми отримують лише `IPostServiceClient` і не виконують HTTP-запитів напряму. Один екземпляр клієнта використовується протягом життя застосунку. Для автоматичних перевірок можна передати тестовий `HttpMessageHandler`.

Кожна відповідь проходить через єдиний `EnsureSuccessAsync`. При 400 читається власний `ValidationErrorResponse.Errors` і генерується `PostingValidationException`. Для інших неуспішних кодів створюється `HttpRequestException` зі збереженим `StatusCode`. Якщо успішна відповідь або відповідь 400 має неправильний формат, помилка JSON передається далі. Windows-клієнт не підключає серверні типи `ValidationProblemDetails`.

## Реакція застосунку

| Ситуація | Поведінка |
| --- | --- |
| Немає відповіді сервера, `StatusCode == null` | Діалог «Повторити» / «Скасувати» |
| HTTP 503 | Такий самий діалог повторної спроби |
| Тайм-аут, 5 секунд | Повідомлення про вичерпаний час, повтор або скасування |
| HTTP 400 з `errors` | Повідомлення з усіма помилками, позначення полів; редактор лишається відкритим |
| HTTP 404 під час отримання запису для редагування, оновлення чи видалення | Повідомлення про застарілі дані та оновлення таблиці |
| HTTP 500, 404 самого списку, неправильний JSON, інший непередбачений виняток | Глобальне повідомлення, запис діагностики, завершення застосунку |

Кнопки блокуються на час операції та розблоковуються у `finally`, зокрема після скасування або валідації. При скасуванні збереження введені дані залишаються у формі.

## Глобальна обробка

До створення першого вікна `Program.cs` підключає `AppDomain.CurrentDomain.UnhandledException`. Для UI-потоку WinForms також підключено `Application.ThreadException` і встановлено `UnhandledExceptionMode.CatchException`; після повідомлення цей обробник викликає `Application.Exit()`.

Це враховує маршрутизацію винятків у Windows Forms: [Microsoft Learn — Application.ThreadException](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.threadexception?view=windowsdesktop-10.0).

Обидва глобальні шляхи використовують одну функцію діагностики, яка розгортає `TargetInvocationException` і зберігає повний початковий виняток з `InnerException`. Час, тип, повідомлення та стек виводяться через `Debug.WriteLine` у відладочний вивід **Debug-збірки**. Додатково вони записуються у файл:

```text
%LOCALAPPDATA%/PostService.WindowsApp/errors.log
```

Користувач бачить коротке повідомлення без стеку й назв внутрішніх типів. Після глобального збою робота не продовжується.

## Відтворення помилок і .rest

Для перевірок замість звичайного запуску сервера використайте:

```powershell
dotnet run --project src/PostService.Server -- --demo-faults
```

Лише в цьому режимі доступний `POST /__demo/fault/{mode}`. Режими `500`, `503`, `bad-json`, `timeout` впливають на `GET /postings`; `none` відновлює звичайну відповідь. Для 500 сервер справді виконує `throw new InvalidOperationException(...)` у цьому ендпойнті. Режими не з’являються у звичайному інтерфейсі клієнта.

Файл [requests/postservice.rest](requests/postservice.rest) містить запити до списку, відсутнього запису, запит із некоректними даними та перевірку 500 з відновленням. Його фактично виконано доданим сценарієм **PowerShell 7**:

```powershell
./scripts/Invoke-Rest.ps1
```

Для перевірки зупиненого сервера завершіть його процес у першому терміналі через Ctrl+C і виконайте:

```powershell
./scripts/Invoke-Rest.ps1 -File ./requests/server-stopped.rest -Report ./reports/offline-rest.txt
```

У цій перевірці не очікується HTTP-статус: запит завершується помилкою з’єднання або тайм-аутом.

## Перевірки

Без сервера запускаються 12 перевірок:

```powershell
dotnet run --project tests/PostService.Checks
```

Із сервером у режимі `--demo-faults` запускаються також 6 інтеграційних перевірок:

```powershell
dotnet run --project tests/PostService.Checks -- --integration http://localhost:5211/
```

**Результат: 18/18 успішно**, збірка без помилок і попереджень. Перевірено всі п’ять HTTP-методів, DTO, 204, 400/404/500/503, мережеву помилку, тайм-аут, некоректний JSON та розгортання вкладеного винятку. Окремо перевірено поведінку реальних вікон для 400, 404, вимкненого сервера, повтору після 503 і глобального завершення після 500.

Докази: [автоматичні перевірки](reports/checks-output.txt), [відповіді .rest](reports/rest-results.txt), [зупинений сервер](reports/offline-rest.txt), [фактичний журнал 500 зі скороченими локальними шляхами](reports/fatal-log-redacted.txt), [звіт](reports/REPORT.md).
