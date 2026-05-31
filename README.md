# RGTools — Optimizer & GameGuard

Herramienta personal de bandeja (system tray) para Windows 11 que protege la privacidad del equipo
personal frente al entorno corporativo y gestiona perfiles de uso (Trabajo / Gaming / Zen).

> **.NET 10 · WPF · requiere Administrador.** App de un solo `.exe`, sin dependencias externas.

---

## Instalación / Actualización

1. Ejecuta **`RGTools-Setup-1.0.0.exe`** (carpeta `releases/`).
2. Acepta el UAC (la app necesita privilegios de administrador).
3. Marca *"Iniciar RGTools automáticamente con Windows"* si lo quieres en el arranque.

El mismo instalador **actualiza** una versión previa (mismo AppId): instala encima conservando tu
configuración. Para desinstalar: *Configuración → Aplicaciones → RGTools*, o el acceso directo
"Desinstalar RGTools".

La app vive en la **bandeja del sistema** (junto al reloj). **Doble clic** en el icono abre el dashboard.
Si no lo ves, está en los iconos ocultos (flecha `^`).

---

## Servicios

| Servicio | Qué hace |
|---|---|
| **DNS Guardian** | Fuerza el DNS configurado y lo restaura si algo lo cambia. Siempre activo. |
| **VPN** | Enciende/levanta o apaga/destruye el túnel corporativo (FortiClient) a voluntad. |
| **Túnel DB (Jumpbox)** | Lanza el túnel de base de datos vía WSL2 (visible cuando la VPN está activa). |

## Perfiles

| Perfil | Acción |
|---|---|
| 💼 **Trabajo** | Estado base. Restaura todo lo que Gaming/Zen hayan modificado. |
| 🎮 **Gaming** | Cierra Docker/WSL2/LM Studio/Slack/Discord/Spark/WhatsApp/qBittorrent · plan Alto Rendimiento · silencia notificaciones de Windows · GPU Priority (con tu permiso). **No** toca Teams/VS Code/navegador ni abre launchers de juego. |
| 🧘 **Zen** | Silencia notificaciones no críticas · Pomodoro 25/5 · bloqueo opcional de sitios (archivo hosts). |
| 🧹 **Restaurar Estado Limpio** | Vuelve a Trabajo y apaga la VPN. DNS sigue activo y la app NO se cierra. |

Todas las operaciones que tocan el sistema (registro, hosts, plan de energía, servicios) usan
**snapshot → aplicar → restaurar** con escritura atómica. Si la app se cierra de forma forzada
(crash, corte de luz), al volver a abrir **sanea el sistema automáticamente** al estado de Trabajo.

---

## Desarrollo

Solución de 3 proyectos (.NET 10):

```
RGTools.Core    Librería: toda la lógica (servicios, modos, infraestructura)
RGTools.App     App WPF de bandeja (composición DI con Generic Host)
RGTools.Tests   xUnit + NSubstitute (21 tests)
```

### Compilar y testear

```powershell
dotnet build  RGTools.slnx -c Debug      # compilar
dotnet test   RGTools.slnx               # tests (21/21)
```

### Generar una nueva versión (release)

Un solo comando hace todo: tests → publish single-file self-contained (win-x64) → instalador en `releases/`.

```powershell
pwsh -File build-release.ps1                 # usa la versión actual del csproj
pwsh -File build-release.ps1 -Bump patch     # 1.0.0 → 1.0.1  (correcciones)
pwsh -File build-release.ps1 -Bump minor     # 1.0.1 → 1.1.0  (features)
pwsh -File build-release.ps1 -Bump major     # 1.1.0 → 2.0.0  (cambios grandes)
pwsh -File build-release.ps1 -SetVersion 1.5.0   # fija una versión exacta
```

- `-Bump` **auto-incrementa** la `<Version>` en `RGTools.App/RGTools.App.csproj` y la reescribe; sin
  parámetros usa la versión actual tal cual.
- Si los tests fallan, el release se aborta (no genera instalador).
- Resultado: `releases/RGTools-Setup-<version>.exe`. **Instala o actualiza** encima de una versión previa
  (mismo AppId), conservando la configuración del usuario.

Requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

### Rutas en runtime

Configuración, logs y estados de rollback: `%APPDATA%\RGTools\`.

---

## Limitaciones conocidas

- Las apps cerradas por Gaming no se reabren solas al volver a Trabajo (se abren a mano).
- Temperaturas de CPU/GPU no se monitorizan (Windows no las expone de forma fiable sin SDK extra).
- Solo Windows 11 x64.
