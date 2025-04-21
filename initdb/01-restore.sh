#!/bin/bash
set -e

echo "Проверяем дамп..."
if [ -f "/docker-entrypoint-initdb.d/company_directory.dump" ]; then
    echo "Начинаем восстановление..."
    pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v "/docker-entrypoint-initdb.d/company_directory.dump"
    echo "Восстановление завершено с кодом $?"
else
    echo "Файл дампа не найден!"
    exit 1
fi