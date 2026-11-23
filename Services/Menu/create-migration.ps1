# =====================================================================
# SmakoViz · Menu — створення початкової міграції EF Core (Code-First).
# Запускати з КОРЕНЯ репозиторію:
#   powershell -ExecutionPolicy Bypass -File Services\Menu\create-migration.ps1
#
# Що робить:
#   1) ставить інструмент dotnet-ef (якщо його ще немає);
#   2) dotnet ef migrations add InitialCreate  → Menu.Dal/Migrations/ (міграція + знімок моделі);
#   3) dotnet ef migrations script --idempotent → Menu.Dal/Migrations/InitialCreate.sql (SQL для перегляду).
# Базу даних для цього НЕ потрібно: міграцію до БД застосує сам застосунок при старті (MigrateAsync).
# =====================================================================
$ErrorActionPreference = "Continue"
$dal = "Services/Menu/Menu.Dal"
$api = "Services/Menu/Menu.Api"

Write-Host "1) Перевіряю інструмент dotnet-ef..."
dotnet ef --version *> $null
if ($LASTEXITCODE -ne 0) {
    dotnet tool install --global dotnet-ef --version 9.0.*
    if ($LASTEXITCODE -ne 0) { throw "Не вдалося встановити dotnet-ef" }
    Write-Host "   dotnet-ef встановлено. Якщо наступний крок скаже 'dotnet ef не знайдено' — закрий і знову відкрий PowerShell."
}

if (Test-Path "$dal/Migrations/*_InitialCreate.cs") {
    Write-Host "Міграція InitialCreate уже існує — пропускаю створення."
} else {
    Write-Host "2) Створюю міграцію InitialCreate..."
    dotnet ef migrations add InitialCreate --project $dal --startup-project $api --output-dir Migrations
    if ($LASTEXITCODE -ne 0) { throw "Помилка створення міграції (див. текст вище)" }
}

Write-Host "3) Генерую SQL-скрипт міграції..."
dotnet ef migrations script --idempotent --project $dal --startup-project $api --output "$dal/Migrations/InitialCreate.sql"
if ($LASTEXITCODE -ne 0) { throw "Помилка генерації SQL-скрипту" }

Write-Host ""
Write-Host "Готово! У $dal/Migrations тепер є:"
Get-ChildItem "$dal/Migrations" | ForEach-Object { Write-Host "   $($_.Name)" }
