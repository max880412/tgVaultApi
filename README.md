# tgVaultApi

API .NET 9 para gestionar sesiones de cuentas de Telegram (WTelegramClient), autenticación JWT, y eventos en tiempo real (SignalR).

## Características
- .NET 9 Web API con Swagger (UI en /swagger).
- Autenticación JWT por usuario/contraseña.
- Usuario admin hardcoded en appsettings (solo para desarrollo) con permiso para crear usuarios.
- Login de cuentas de Telegram en 2 pasos (teléfono [+ password 2FA] + código enviado por 777000).
- Sesiones de Telegram persistidas como archivos .session (carpeta configurable).
- SignalR para emitir en tiempo real el código recibido (chat 777000).
- SQLite para usuarios de la API (app.db).

## Requisitos
- .NET SDK 9

## Configuración (appsettings.json)
- Jwt
  - Key, Issuer, Audience, ExpireMinutes
- Telegram
  - ApiId: int (desde https://my.telegram.org)
  - ApiHash: string
  - SessionDir: carpeta para archivos .session
- Admin
  - Username: usuario admin hardcoded
  - Password: contraseña admin hardcoded
- Server
  - Port: puerto HTTP a exponer (ej. 5080)
- ConnectionStrings
  - Default: "Data Source=app.db"

Ejemplo:
```json
{
  "Jwt": { "Key": "<clave>", "Issuer": "tgVaultApi", "Audience": "tgVaultApiClients", "ExpireMinutes": 60 },
  "Telegram": { "ApiId": 12345, "ApiHash": "xxxx", "SessionDir": "Sessions" },
  "Admin": { "Username": "admin", "Password": "Admin123!" },
  "Server": { "Port": 5080 },
  "ConnectionStrings": { "Default": "Data Source=app.db" }
}
```

## Ejecución
```bash
# desde la carpeta del proyecto
 dotnet run
```
La API se expone en: http://0.0.0.0:<Server:Port> (por defecto 5080). Swagger: http://localhost:<puerto>/swagger

Al iniciar, se aplican migraciones a SQLite (crea/actualiza app.db).

## Autenticación
1) Obtener JWT (admin o usuario creado por admin)
- POST /api/users/login
  - Body: { "username": "admin", "password": "Admin123!" }
  - 200: { access_token, token_type, expires_in }
2) Usar el token en Authorization: Bearer {access_token}

## Endpoints

Usuarios (API)
- POST /api/users/login
  - Body: { username: string, password: string }
  - Respuesta: { access_token: string, token_type: "Bearer", expires_in: number }
- POST /api/users (requiere JWT de admin)
  - Body: { username: string, password: string }
  - Crea un usuario que podrá autenticarse y acceder a las cuentas TG.

Telegram
- POST /api/telegram/login/start (JWT requerido)
  - Body: { phoneNumber: "+54911...", password?: "opcional" }
  - Respuesta: { loginId: string, status: "logged_in" | "code_required" }
- POST /api/telegram/login/submit-code (JWT requerido)
  - Body: { loginId: string, code: string }
  - Respuesta: TelegramAccountInfo { userId, phoneNumber, username, firstName, lastName }
- GET /api/telegram/accounts (JWT requerido)
  - Respuesta: TelegramAccountInfo[]

Notas sobre sesiones y persistencia de TG
- WTelegramClient guarda las sesiones por teléfono en Telegram.SessionDir como archivos .session.
- La lista de cuentas devueltas refleja las sesiones activas (en memoria) del proceso.
- La base SQLite persiste usuarios de la API. Si deseas persistir también TelegramAccountInfo en DB, puede añadirse fácilmente.

## SignalR (tiempo real)
- Hub: /hubs/updates
- Autenticación: pasar access_token={JWT} en querystring o Authorization: Bearer en el WebSocket.
- Server-to-client methods:
  - LoginCodeReceived(payload)
    - payload: { account: string, code: string, receivedAt: string ISO-8601 }
- El evento se emite cuando llega un mensaje del sistema (chat 777000) con el código de login.

## Ejemplos

Obtener token (admin):
```bash
curl -s -X POST http://localhost:5080/api/users/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"Admin123!"}'
```

Iniciar login de TG:
```bash
curl -s -X POST http://localhost:5080/api/telegram/login/start \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"phoneNumber":"+5491122334455"}'
```

Completar login con código:
```bash
curl -s -X POST http://localhost:5080/api/telegram/login/submit-code \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"loginId":"<GUID>","code":"12345"}'
```

Listar cuentas TG:
```bash
curl -s http://localhost:5080/api/telegram/accounts \
  -H "Authorization: Bearer <TOKEN>"
```

Conexión a SignalR (JS ejemplo mínimo):
```js
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5080/hubs/updates?access_token=TOKEN")
  .build();

connection.on("LoginCodeReceived", (payload) => {
  console.log("code", payload);
});

await connection.start();
```

## Seguridad
- Cambia la clave JWT en appsettings para producción.
- No expongas ApiId/ApiHash públicamente.
- Admin hardcoded es solo para desarrollo; reemplázalo por un flujo seguro en producción.
