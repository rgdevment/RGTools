# RGTools — Optimizer & GameGuard

Herramienta personal de bandeja (system tray) para Windows 11 que protege la privacidad del equipo
personal frente al entorno corporativo y gestiona perfiles de uso (Trabajo / Gaming).

> **.NET 10 · WPF · requiere Administrador.** App de un solo `.exe`, sin dependencias externas.

---

## Instalación / Actualización

1. Ejecuta **`RGTools-Setup-1.0.0.exe`** (carpeta `releases/`).
2. Acepta el UAC (la app necesita privilegios de administrador).
3. Al terminar, la app queda en la bandeja. El **inicio con Windows** se activa desde el dashboard
   (toggle *"Iniciar con Windows"*) — el instalador no lo gestiona, para tener una sola fuente de verdad.

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
| 💼 **Trabajo** | Estado base. Restaura todo lo que Gaming haya modificado. |
| 🎮 **Gaming** | Cierra Docker (servicio + backend)/WSL2/LM Studio/Slack/Discord/Spark/WhatsApp/qBittorrent · plan Ultimate Performance (con fallback) · silencia notificaciones de Windows · fuerza el refresh máximo del monitor · optimiza red (throttling/Nagle off) · GPU Priority (con tu permiso). **No** toca Teams/VS Code/navegador ni abre launchers de juego. |

Todas las operaciones que tocan el sistema (registro, hosts, plan de energía, servicios) usan
**snapshot → aplicar → restaurar** con escritura atómica. Si la app se cierra de forma forzada
(crash, corte de luz), al volver a abrir **sanea el sistema automáticamente** al estado de Trabajo.

---

## Desarrollo

Solución de 3 proyectos (.NET 10):

```
RGTools.Core    Librería: toda la lógica (servicios, modos, infraestructura)
RGTools.App     App WPF de bandeja (composición DI con Generic Host)
RGTools.Tests   xUnit + NSubstitute (33 tests)
```

### Compilar y testear

```powershell
dotnet build  RGTools.slnx -c Debug      # compilar
dotnet test   RGTools.slnx               # tests (33/33)
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


PC - Stack
### 🚀 Núcleo

- **Procesador:** Intel Core Ultra 7 265K (Desbloqueado, Socket LGA 1851).
- **B Madre:** ASUS TUF GAMING Z890-PLUS WIFI (Chipset Z890).
- **Memoria RAM:** 64GB Total (2x32GB) ADATA XPG Lancer DDR5 6400MHz RGB Black.

### 🎮 Gráfica & Visual

- **Tarjeta de Video:** Palit GeForce RTX 5070 Ti GamingPro-S 16GB GDDR7 (256-bit).
- Monitor Gamer Xiaomi con pantalla de 34" 180Hz y resolución WQHD (Principal)
- LG HDR WFHD 75HZ Vertical (Secundaria)

### ❄️ Refrigeración & Chasis

- **Gabinete:** Darkflash DS900 Black (Tipo "Pecera").
- **Refrigeración Líquida:** Segotep Beaced 360 ARGB Black (Compatible LGA 1851).
- **Ventiladores:** 6x Darkflash DM12 Pro PWM (Configuración de flujo optimizado).

### 💾 Almacenamiento & Energía (Actualizado)

- **SSD Primario (Sistema/WSL):** Kingston Fury Renegade G5 2TB (**PCIe 5.0**, 14.700 MB/s, con DRAM Cache).
- **SSD Secundario (Juegos/Media):** Western Digital WD Black SN850X 1TB (PCIe 4.0, gama alta).
- **Fuente de Poder:** Be Quiet! Pure Power 13 M 850W (80 Plus Gold, ATX 3.1 nativa).

### 🕹️ Periféricos & Audio (Nuevos)

- **Audio:** Sony Inzone H9 II (Noise Cancelling, drivers de carbono, conexión simultánea 2.4GHz + BT).
- **Control:** ThundeRobot G80 Ultimate/Pro (Joysticks de Efecto Hall, tasa de sondeo 1000Hz, botones mecánicos y base de carga).
- **Teclado:** Teclado Inalámbrico Logitech K860 Ergo Black Color Del Teclado Negro Idioma Español España
- Mouse Bluetooth Ergonomico Logitech Mx Vertical Color Negro

### 🛠️ Accesorios & Herramientas

- **Soporte de GPU:** Soporte Universal Ajustable Magnético.

### 🛡️ Protección Eléctrica

- **UPS:** Forza FX-1500LCD-C (1500VA / 840W).
