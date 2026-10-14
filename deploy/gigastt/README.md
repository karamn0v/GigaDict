# GigaSTT на Ugreen DXP4800+

Compose адаптирует проверенную рабочую конфигурацию для Ugreen DXP4800+ с Pentium Gold 8505 и UGOS Container Manager. Используется CPU-образ GigaSTT `2.22.0`, GigaAM v3 `e2e_rnnt`, профиль `edge`, пять CPU-потоков и постоянный volume модели. Stack содержит один контейнер.

## Запуск в UGOS

1. Откройте **Container Manager → Compose**.
2. Создайте новый stack из `compose.yaml`.
3. Запустите stack. Контейнер `gigastt` должен перейти в состояние `healthy`.

Первый запуск скачивает около 225 МБ INT8-весов. Для этого NAS нужен исходящий HTTPS-доступ. Модель сохраняется в volume `gigastt-models` и при следующих запусках повторно не загружается.

Если stack запускается через Docker Compose CLI, необязательный `.env` создаётся так:

```sh
cp .env.example .env
```

```sh
docker compose pull
docker compose up -d
docker compose logs -f gigastt
```

## Проверка

С NAS:

```sh
curl http://127.0.0.1:9876/health
curl http://127.0.0.1:9876/ready
```

С Windows-компьютера:

```powershell
Invoke-RestMethod http://LAN-IP-NAS:9876/ready
```

В GigaDict укажите `http://LAN-IP-NAS:9876` и нажмите «Проверить соединение».

## Почему конфигурация выглядит именно так

- Контейнер запускается с `user: "0:0"`, поскольку UGOS создаёт named volume с владельцем root. Это позволяет одному контейнеру записывать модель без отдельного init-сервиса.
- Все Linux capabilities удалены, а `no-new-privileges` запрещает повышение привилегий внутри контейнера.
- `gigastt download` заранее скачивает `e2e_rnnt` без модели диаризации, после чего запускается сервер.
- `GIGASTT_PROFILE=edge` оставляет один inference-слот и включает подходящие для edge-систем настройки.
- Пять encoder threads и лимит `cpus: "5.0"` оставляют часть шестипоточного CPU системе NAS.
- Healthcheck ждёт именно `/ready`, а `start_period: 360s` даёт время на первую загрузку модели.
- Логи ротируются: максимум три файла по 10 МБ.
- CORS не раскрывается для произвольных браузерных origin: контейнер предназначен только для нативного клиента GigaDict.

## Безопасность

Порт `9876` доступен в локальной сети, а сервер не предоставляет TLS и аутентификацию. Не настраивайте port forwarding на роутере и не публикуйте этот порт в интернет. Отсутствие `--cors-allow-any` блокирует произвольные браузерные origin, но не мешает GigaDict: нативный клиент не отправляет заголовок `Origin`.

`docker compose down` сохраняет модель. `docker compose down -v` удаляет volume с моделью и потребует повторной загрузки.

Образ закреплён одновременно тегом и OCI digest. Параметры запуска проверены по документации [GigaSTT v2.22.0](https://github.com/ekhodzitsky/gigastt/tree/v2.22.0): [deployment](https://github.com/ekhodzitsky/gigastt/blob/v2.22.0/docs/deployment.md), [CLI](https://github.com/ekhodzitsky/gigastt/blob/v2.22.0/docs/cli.md), [API](https://github.com/ekhodzitsky/gigastt/blob/v2.22.0/docs/api.md).
