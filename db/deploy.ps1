# =====================================================================
# SmakoViz · розгортання всіх 3 баз з нуля (Windows PowerShell)
#   powershell -ExecutionPolicy Bypass -File db\deploy.ps1
# Потрібен лише Docker Desktop. Повторний запуск безпечний (скрипти ідемпотентні).
# =====================================================================
# "Continue", а не "Stop": у Windows PowerShell 5.1 будь-який текст, який docker/sqlcmd
# пише в потік помилок, інакше вважається фатальним. Успіх перевіряємо через $LASTEXITCODE.
$ErrorActionPreference = "Continue"
$Password = "SmakoViz_Dev_2026!"
$SqlCmd = "/opt/mssql-tools18/bin/sqlcmd"

Write-Host "1) Запускаю контейнери SQL Server і MongoDB..."
docker compose -f "$PSScriptRoot\docker-compose.yml" up -d
if ($LASTEXITCODE -ne 0) { throw "Не вдалося запустити контейнери. Чи запущено Docker Desktop?" }

Write-Host "2) Чекаю, поки SQL Server буде готовий (до 3 хв, перший запуск довгий)..."
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    # cmd /c ховає повідомлення «ще не готовий», щоб PowerShell не вважав їх помилкою
    cmd /c "docker exec smakoviz-sql $SqlCmd -C -l 5 -S localhost -U sa -P $Password -Q ""SELECT 1"" >nul 2>&1"
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Write-Host "   ...ще стартує ($($i * 3 + 3) с)"
    Start-Sleep -Seconds 3
}
if (-not $ready) { throw "SQL Server не стартував. Подивись: docker logs smakoviz-sql" }

function Invoke-Sql($file) {
    Write-Host "   -> $file"
    # -C довіряти сертифікату, -I QUOTED_IDENTIFIER ON, -b зупинитися при помилці, -f 65001 UTF-8 (кирилиця)
    docker exec smakoviz-sql $SqlCmd -C -I -b -f 65001 -S localhost -U sa -P $Password -i "/db/$file"
    if ($LASTEXITCODE -ne 0) { throw "Помилка у $file" }
}

Write-Host "3) Проєкт №1 — SmakoViz_Delivery (schema -> procedures -> seed)"
Invoke-Sql "p1/schema.sql"
Invoke-Sql "p1/procedures.sql"
Invoke-Sql "p1/seed.sql"

Write-Host "4) Проєкт №2 — SmakoViz_Menu (schema -> seed)"
Invoke-Sql "p2/schema.sql"
Invoke-Sql "p2/seed.sql"

Write-Host "5) Проєкт №3 — MongoDB smakoviz_feedback (collections -> seed)"
docker exec smakoviz-mongo mongosh --quiet /db/p3/collections.js
if ($LASTEXITCODE -ne 0) { throw "Помилка у p3/collections.js" }
docker exec smakoviz-mongo mongosh --quiet /db/p3/seed.js
if ($LASTEXITCODE -ne 0) { throw "Помилка у p3/seed.js" }

Write-Host ""
Write-Host "Готово! SQL Server: localhost,1433 (sa / $Password) · MongoDB: mongodb://localhost:27017"
Write-Host "Перевірка: docker exec smakoviz-sql $SqlCmd -C -I -f 65001 -S localhost -U sa -P $Password -i /db/p1/checks.sql"
