<p align="center">
  <img src="docs/img/icon.png" width="96" alt="">
</p>

<h1 align="center">Console Mode</h1>

<p align="center">
  Cambia tu PC con Windows entre <b>modo PC</b> y <b>modo consola</b> con un toque desde el móvil.
</p>

---

Al pasar a **modo consola**, Console Mode pone la TV como pantalla principal, cambia el audio a la salida que
elijas y abre **Steam Big Picture** en esa pantalla. Al volver a **modo PC** cierra Big Picture y deja tu
monitor y tu audio de siempre.

Tiene dos partes:

| | |
|---|---|
| **App de PC** (`pc/`) | App de Windows (C# / .NET 8) muy sencilla. Arranca en segundo plano, con o sin icono en la bandeja, y sirve para vincular móviles, elegir la pantalla y el audio de cada modo y cambiar de modo. |
| **App móvil** (`mobile/`) | App Flutter (Android / iOS). Encuentra el PC con un QR o buscando en la red, lo guarda y muestra un interruptor PC / Consola. |

## Funciones

- **Interruptor PC / Consola** en el móvil, en la ventana del PC y en el menú del icono de la bandeja.
- **Por cada modo**: pantalla principal y salida de audio predeterminada (o «No cambiar»).
- **Steam Big Picture**: se abre al entrar en modo consola, se coloca a la fuerza en la pantalla de la consola y
  se cierra al volver a modo PC (cada cosa se puede desactivar).
- **Vincular móviles** con código QR o buscando en la red local (broadcast UDP).
- **Dispositivos de confianza**: la primera vez el PC pregunta «¿Confiar en este dispositivo?». Después no vuelve a
  preguntar. Puedes quitar dispositivos cuando quieras.
- **Segundo plano**: al cerrar la ventana la app sigue funcionando. El icono de la bandeja (en «iconos ocultos»)
  es opcional.
- **Iniciar con Windows** (opcional), directamente en segundo plano.
- **Overlay con el mando**: mantén **View + Menú** durante 1 segundo en un mando Xbox para cambiar la salida de audio
  o la pantalla principal desde el sofá (ver más abajo).
- Si el PC cambia de IP, el móvil lo vuelve a encontrar solo.
- **Instalador** o **versión portable**, a tu elección.

## ¿Se puede forzar Big Picture en la pantalla de la TV?

Sí. Se hace en dos pasos:

1. Antes de abrir Big Picture, Console Mode convierte la TV en **pantalla principal**. Steam abre Big Picture en la
   pantalla principal, así que normalmente ya aparece ahí.
2. Si Steam la abre en otra pantalla (a veces recuerda la última posición), Console Mode busca la ventana de
   Big Picture durante 60 segundos y la **mueve y la ajusta** al tamaño de la pantalla de la consola. Esto es la
   opción «Forzar Big Picture en la pantalla del modo consola».

## Instalación

Descarga los archivos desde [Releases](https://github.com/katarem/console-mode/releases):

| Archivo | Para qué |
|---|---|
| `ConsoleMode-Setup-X.Y.Z.exe` | **Instalador** para Windows (recomendado). |
| `ConsoleMode-X.Y.Z-portable.exe` | **Versión portable** para Windows: un solo `.exe`, sin instalación. |
| `ConsoleMode-X.Y.Z.apk` | App para Android. |

Las dos versiones de Windows usan la misma configuración (`%APPDATA%\ConsoleMode`), así que puedes pasar de una a otra
sin volver a configurar nada. No necesitan tener .NET instalado.

Como los ejecutables no están firmados, Windows SmartScreen puede avisar: pulsa «Más información» → «Ejecutar de
todas formas».

### PC con instalador (Windows 10 / 11)

Ejecuta `ConsoleMode-Setup-X.Y.Z.exe`. El instalador:

- instala Console Mode en `Archivos de programa` y crea un acceso directo en el menú Inicio (y en el escritorio, si
  lo eliges);
- crea la regla del **firewall de Windows** para redes privadas, así que no hace falta configurar nada más;
- puede dejar activado **Iniciar con Windows**. Luego lo puedes cambiar desde *Ajustes* en la app;
- en una actualización, cierra la versión en marcha, la sustituye y conserva la configuración.

Para desinstalarlo, ve a *Configuración → Aplicaciones*. Al desinstalar se borran la regla del firewall y el inicio
automático, pero se conserva la configuración.

### PC portable

1. Copia `ConsoleMode-X.Y.Z-portable.exe` a una carpeta fija, por ejemplo `C:\Programas\ConsoleMode\`, y ábrelo.
2. Cuando el **firewall de Windows** pregunte, permite el acceso en **redes privadas**. Si no aparece el aviso, o lo
   cerraste, ejecuta esto en una consola como administrador:

   ```powershell
   netsh advfirewall firewall add rule name="Console Mode" dir=in action=allow program="C:\Programas\ConsoleMode\ConsoleMode-X.Y.Z-portable.exe" enable=yes profile=private
   ```

Con cualquiera de las dos versiones, comprueba que tu Wi-Fi o Ethernet esté como **red privada** y no como red
pública.

### Móvil

- **Android**: instala `ConsoleMode.apk` (activa «instalar apps de origen desconocido»).
- **iOS**: compílalo tú con Xcode (`cd mobile && flutter run --release`). En iOS, la búsqueda en la red necesita
  el permiso especial de Apple `com.apple.developer.networking.multicast`. Sin él, usa el **código QR**.

## Uso

1. **Configura los modos**, en la pestaña *Modos* de la app de PC:
   - Modo PC: tu monitor como pantalla principal y tus auriculares o altavoces.
   - Modo Consola: la TV como pantalla principal y la salida de audio de la TV o del receptor AV.

   La TV tiene que estar **encendida y conectada** (como pantalla extendida) para que aparezca en la lista y para
   poder cambiar a ella.
2. **Vincula el móvil**: en la app móvil, pulsa «Escanear código QR» y apunta al QR de la pestaña *Móviles* del PC,
   o pulsa «Buscar en la red». Acepta la solicitud que aparece en el PC.
3. **Cambia de modo** con el interruptor del móvil, con los botones de la ventana del PC o con el menú del icono de
   la bandeja.

### Overlay con el mando

Con un mando **Xbox o compatible XInput** (incluidos los 8BitDo en modo X), mantén **View + Menú** (los dos botones
pequeños del centro) durante **1 segundo**. Aparece un panel en la esquina inferior derecha con:

- **Audio**: todas las salidas de audio. El punto ● marca la actual.
- **Pantalla principal**: todas las pantallas. Si Big Picture está abierto, se mueve a la pantalla elegida.

Usa la **cruceta o el stick** para moverte, **A** para elegir y **B** (o View + Menú otra vez) para cerrar. El panel
también se cierra solo tras 20 segundos sin usarlo.

Lo que elijas aquí es **temporal**: no cambia la configuración de tus modos. La próxima vez que cambies de modo se
aplicará lo que tengas configurado en la pestaña *Modos*.

Notas:

- Mientras el panel está abierto se queda con el foco, para que A y B no lleguen también a Big Picture o al juego. Al
  cerrarlo, el foco vuelve donde estaba. Al mantener View + Menú, el juego también recibe esas pulsaciones (por
  ejemplo, puede abrir su menú de pausa).
- No se ve encima de juegos en **pantalla completa exclusiva**. Con «ventana sin bordes» y en Big Picture sí.
- Se puede desactivar en *Modos → Modo Consola*. También se abre desde el menú del icono de la bandeja.

### Segundo plano e inicio con Windows

En la pestaña *Ajustes*:

- **Mostrar icono en la bandeja del sistema**: si lo desactivas, la app sigue funcionando pero sin icono. Para volver
  a ver la ventana, ejecuta otra vez `ConsoleMode.exe`. No se abre una segunda copia: se muestra la ventana de la
  que ya está en marcha.
- **Iniciar con Windows**: añade la app a `HKCU\…\Run` con el argumento `--background`, así que arranca sin abrir la
  ventana.
- **Salir de Console Mode**: cierra la app del todo (también está en el menú del icono).

La configuración se guarda en `%APPDATA%\ConsoleMode\settings.json` y el registro en `console-mode.log`, en la misma
carpeta.

## Cómo funciona

| Acción | Cómo |
|---|---|
| Pantalla principal | `QueryDisplayConfig` para identificar cada monitor de forma estable (nombre EDID y ruta del dispositivo) y `ChangeDisplaySettingsEx` con `CDS_SET_PRIMARY`, recolocando todos los monitores para que el nuevo principal quede en (0,0). |
| Audio predeterminado | Interfaz COM `IPolicyConfig` (la misma que usa el panel de Sonido de Windows), para los roles consola, multimedia y comunicaciones. El audio se cambia después de la pantalla y se reintenta unos segundos, porque la salida HDMI de una TV puede tardar en aparecer. |
| Abrir Big Picture | `steam://open/bigpicture` |
| Colocar Big Picture | Se busca la ventana de `steamwebhelper` cuyo título contiene «Big Picture» (Steam puede separar las palabras con un espacio de no separación, U+00A0) y se mueve con `SetWindowPos` al rectángulo de la pantalla elegida. |
| Cerrar Big Picture | `steam://close/bigpicture`. Si no responde, se envía `WM_CLOSE` a la ventana. |
| Overlay del mando | Se lee XInput (`xinput1_4.dll`) en segundo plano a ~30 Hz, sin necesitar el foco. Los huecos de mando vacíos solo se consultan cada 2 s. |
| Red | TCP (JSON por líneas) en el puerto 47820 y descubrimiento UDP en el 47821. Ver [docs/PROTOCOL.md](docs/PROTOCOL.md). |

### Seguridad

- Solo funciona en tu red local. No hay servidores ni cuentas.
- Un móvil nuevo no puede hacer nada hasta que lo aceptas en el PC. Desde ese momento, el móvil demuestra que es él con
  un HMAC sobre un reto aleatorio, sin volver a enviar su clave.
- En el PC, las claves se guardan cifradas con DPAPI, ligadas a tu usuario de Windows.
- El tráfico no va cifrado (solo lleva órdenes de cambio de modo). No expongas el puerto a Internet.

## Compilar desde el código

```bash
# App de PC (necesita el SDK de .NET 8; se puede compilar desde Linux/macOS)
dotnet publish pc/ConsoleMode/ConsoleMode.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish

# App móvil (Flutter 3.35)
cd mobile
flutter pub get
flutter test
flutter build apk --release
```

Para generar el instalador hace falta [Inno Setup 6](https://jrsoftware.org/isinfo.php) (solo en Windows):

```powershell
iscc /DAppVersion=1.1.0 /DSourceExe=..\publish\ConsoleMode.exe installer\ConsoleMode.iss
```

Al subir una etiqueta `vX.Y.Z`, GitHub Actions compila todo y crea una release con el instalador, la versión
portable y el APK.

## Estructura

```
pc/ConsoleMode/
  Core/       Configuración, cambio de modo, inicio con Windows y log
  Native/     Pantallas, audio y Steam Big Picture (Win32 / COM)
  Network/    Servidor TCP, emparejamiento y descubrimiento UDP
  UI/         Ventana principal, icono de bandeja y diálogo de emparejamiento
mobile/lib/
  services/   Protocolo, emparejamiento, conexión y descubrimiento
  screens/    Inicio, escáner QR, búsqueda, emparejamiento y control
  widgets/    Interruptor PC / Consola
installer/    Script del instalador (Inno Setup)
docs/         Protocolo
```

## Limitaciones conocidas

- La TV tiene que estar activa como pantalla extendida. Console Mode no enciende la TV ni cambia el modo de proyección
  (por ejemplo «Solo segunda pantalla»).
- El HDR, la resolución y la frecuencia de refresco no se cambian. Se mantienen los de cada pantalla.
- `IPolicyConfig` no está documentada oficialmente, aunque funciona en Windows 10 y 11 desde hace años.

## Licencia

[MIT](LICENSE)
