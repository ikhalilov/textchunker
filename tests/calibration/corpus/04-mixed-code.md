# API настройки

Клиент отправляет JSON с полями text и strategy. Сервер валидирует параметры и возвращает массив чанков. Ошибки приходят в формате problem+json с кодом 400 или 503.

```json
{"text":"Пример","strategy":"semantic","maxTokens":512}
```

Поле prefix учитывается в лимите токенов. Если префикс сам больше лимита, запрос отклоняется. Смещения startChar и endChar указывают в исходный текст.

# Deployment notes

The service listens on port 5080 by default. Set ASPNETCORE_URLS to bind another interface. Health checks are available at /api/health and return the current version.

Logs go to stdout in structured JSON. Container orchestration collects them automatically. Avoid writing secrets to logs because they may be shipped to a central store.

# Поддержка

При падении сервера проверьте свободную память и порт. Перезапуск выполняется командой systemctl restart textchunker. Если ошибка повторяется, приложите журнал за последний час.
