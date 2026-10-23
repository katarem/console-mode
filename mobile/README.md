# Console Mode · app móvil

App Flutter (Android / iOS) para cambiar el PC entre **modo PC** y **modo consola**.
Consulta el [README principal](../README.md) para la instalación y el uso.

```bash
flutter pub get
flutter test
flutter run            # con un móvil conectado
flutter build apk      # APK de Android
```

Estructura:

- `lib/services/protocol.dart` – conexión TCP (JSON por líneas) y prueba HMAC.
- `lib/services/pairing.dart` – emparejamiento con el PC.
- `lib/services/pc_connection.dart` – conexión persistente, reconexión y cambio de modo.
- `lib/services/discovery.dart` – búsqueda de PCs por broadcast UDP.
- `lib/screens/` – pantallas (inicio, escáner QR, búsqueda, emparejamiento, control).
