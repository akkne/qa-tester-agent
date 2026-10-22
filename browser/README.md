# Демо браузерная проверка

```bash
npm ci --prefix browser
npx --prefix browser playwright install chromium
node browser/run.mjs --base-url http://127.0.0.1:5081 --artifacts artifacts/browser
```

CLI проверяет регистрацию, вход, изоляцию кабинета, повторный email и выход в Chromium с desktop (1280×800) и mobile (390×844) viewport. Последняя строка stdout — JSON со статусом `PASSED`, `FAILED` или `BLOCKED`. Код завершения 0 означает, что оба сценария прошли; недоступный стенд или браузер возвращает `BLOCKED` и код 1. При падении создаются screenshot, Playwright trace и JSON с шагами, ошибками консоли, JavaScript, сети и ответами 5xx.
