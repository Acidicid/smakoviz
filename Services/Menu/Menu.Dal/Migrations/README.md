# Міграції EF Core (Code-First)

Тут лежать міграції бази `SmakoViz_Menu`, згенеровані з моделей (`Menu.Domain/Entities`)
і конфігурацій Fluent API (`Menu.Dal/Configurations`):

| Файл | Що це |
|------|-------|
| `*_InitialCreate.cs` | початкова міграція: усі таблиці, ключі, індекси, CHECK, HasData категорій |
| `*_InitialCreate.Designer.cs` | метадані міграції |
| `MenuDbContextModelSnapshot.cs` | знімок поточної моделі — з ним EF порівнює модель при наступній міграції |
| `InitialCreate.sql` | SQL-скрипт міграції (ідемпотентний) — для перегляду або ручного виконання |

Створення (з кореня репозиторію):

```powershell
powershell -ExecutionPolicy Bypass -File Services\Menu\create-migration.ps1
```

або вручну:

```bash
dotnet tool install --global dotnet-ef --version 9.0.*
dotnet ef migrations add InitialCreate --project Services/Menu/Menu.Dal --startup-project Services/Menu/Menu.Api
dotnet ef migrations script --idempotent --project Services/Menu/Menu.Dal --startup-project Services/Menu/Menu.Api -o Services/Menu/Menu.Dal/Migrations/InitialCreate.sql
```

Застосування до БД — автоматично при старті `Menu.Api` (`MigrateAsync()` у `Program.cs`), або вручну:

```bash
dotnet ef database update --project Services/Menu/Menu.Dal --startup-project Services/Menu/Menu.Api
```
