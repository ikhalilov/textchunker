Архитектура сервиса

Сервис принимает HTTP-запросы через балансировщик и передаёт их в пул воркеров. Каждый воркер обрабатывает задачу независимо и пишет результат в очередь. Очередь сглаживает пики нагрузки и позволяет повторять неудачные операции. Метрики собираются в Prometheus, логи уходят в центральное хранилище.

Состояние хранится в PostgreSQL с репликой для чтения. Кэш Redis снижает нагрузку на базу и ускоряет повторные запросы. При недоступности кэша сервис продолжает работать, но медленнее. Схема обновляется миграциями, которые можно откатить.

Service ownership

Each service has a single owning team and a documented on-call rotation. The owner approves breaking changes and maintains the runbook. Dependencies are declared explicitly so impact analysis is possible.

Dashboards show latency, error rate, saturation, and traffic. Alerts fire on symptoms that affect users, not on every internal metric. Every incident produces a blameless postmortem with concrete follow-up actions.
