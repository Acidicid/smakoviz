#!/usr/bin/env bash
# =====================================================================
# SmakoViz · розгортання всіх 3 баз з нуля (Linux / macOS / Git Bash)
#   bash db/deploy.sh
# Потрібен лише Docker. Повторний запуск безпечний (скрипти ідемпотентні).
# =====================================================================
set -euo pipefail
cd "$(dirname "$0")"

PASSWORD='SmakoViz_Dev_2026!'
SQLCMD=/opt/mssql-tools18/bin/sqlcmd

echo "1) Запускаю контейнери SQL Server і MongoDB..."
docker compose -f docker-compose.yml up -d

echo "2) Чекаю, поки SQL Server буде готовий (до 2 хв)..."
for i in $(seq 1 40); do
  if docker exec smakoviz-sql $SQLCMD -C -S localhost -U sa -P "$PASSWORD" -Q "SELECT 1" >/dev/null 2>&1; then break; fi
  [ "$i" -eq 40 ] && { echo "SQL Server не стартував: docker logs smakoviz-sql"; exit 1; }
  sleep 3
done

run_sql() {
  echo "   -> $1"
  # -C довіряти сертифікату, -I QUOTED_IDENTIFIER ON, -b зупинитися при помилці, -f 65001 UTF-8
  docker exec smakoviz-sql $SQLCMD -C -I -b -f 65001 -S localhost -U sa -P "$PASSWORD" -i "/db/$1"
}

echo "3) Проєкт №1 — SmakoViz_Delivery"
run_sql p1/schema.sql
run_sql p1/procedures.sql
run_sql p1/seed.sql

echo "4) Проєкт №2 — SmakoViz_Menu"
run_sql p2/schema.sql
run_sql p2/seed.sql

echo "5) Проєкт №3 — MongoDB smakoviz_feedback"
docker exec smakoviz-mongo mongosh --quiet /db/p3/collections.js
docker exec smakoviz-mongo mongosh --quiet /db/p3/seed.js

echo
echo "Готово! SQL Server: localhost,1433 (sa / $PASSWORD) · MongoDB: mongodb://localhost:27017"
