# Protocolo PC ↔ móvil

Todo ocurre en la red local. No hay servidores externos.

| Puerto | Protocolo | Uso |
|-------:|-----------|-----|
| 47820  | TCP       | Conexión de control (configurable en la app de PC) |
| 47821  | UDP       | Descubrimiento por broadcast |

## Descubrimiento (UDP 47821)

El móvil envía por broadcast el texto `CONSOLEMODE_DISCOVER_V1`. Cada PC con Console Mode responde
directamente al móvil con:

```json
{"type":"consolemode","version":1,"pcId":"…","pcName":"MI-PC","port":47820}
```

## Código QR

```
consolemode://pair?h=192.168.1.10,10.0.0.5&p=47820&id=<pcId>&n=<nombre>
```

`h` lleva todas las IPv4 locales del PC (primero las de interfaces con puerta de enlace). El móvil prueba
cada una hasta que conecta.

## Conexión de control (TCP 47820)

Un objeto JSON por línea, en UTF-8 y terminado en `\n`.

Al conectar, el PC envía:

```json
{"type":"hello","version":1,"pcId":"…","pcName":"MI-PC","nonce":"<aleatorio>"}
```

### Emparejamiento (la primera vez)

```
móvil → {"type":"pair_request","deviceId":"…","deviceName":"Pixel 8"}
PC    → {"type":"pair_pending"}                       (el PC muestra «¿Confiar en este dispositivo?»)
PC    → {"type":"pair_ok","token":"…","pcId":"…","pcName":"MI-PC"}
     o  {"type":"pair_denied","reason":"rejected"|"busy"}
```

El PC guarda el token cifrado con DPAPI (solo lo puede leer tu usuario de Windows). El móvil lo guarda en
su almacenamiento local. Solo se atiende una solicitud a la vez y caduca a los 90 segundos.

### Autenticación (siguientes veces, sin preguntar)

```
móvil → {"type":"auth","deviceId":"…","deviceName":"Pixel 8","proof":"<hex>"}
PC    → {"type":"auth_ok","pcName":"MI-PC"}  +  {"type":"status",…}
     o  {"type":"auth_failed","reason":"unknown_device"|"bad_proof"}
```

`proof = hex(HMAC-SHA256(clave = token, mensaje = nonce))`. El token no viaja por la red después del
emparejamiento y el nonce es distinto en cada conexión, así que una conexión capturada no se puede reutilizar.

### Una vez autenticado

```
móvil → {"type":"set_mode","mode":"pc"|"console"}
móvil → {"type":"get_status"}
móvil → {"type":"ping"}                → PC: {"type":"pong"}
PC    → {"type":"status","mode":"pc"|"console","busy":true|false,"error":null|"…"}
PC    → {"type":"error","message":"…"}
```

### Cambio rápido de audio y pantalla (protocolo 2)

El `hello` del PC indica `"version":2` o superior. Estos cambios son temporales: no se guardan en los modos.

```
móvil → {"type":"get_devices"}
móvil → {"type":"set_audio","id":"<id del dispositivo>"}
móvil → {"type":"set_display","id":"<id de la pantalla>"}
PC    → {"type":"devices",
         "audio":[{"id":"…","name":"SAMSUNG (NVIDIA High Definition Audio)","current":true}, …],
         "displays":[{"id":"…","name":"SAMSUNG","current":true}, …]}
```

El PC envía `devices` a todos los móviles conectados cuando cambia el audio o la pantalla (desde un móvil, desde el
overlay del mando o por un cambio de modo).

El PC envía `status` a todos los móviles conectados cada vez que cambia el modo, venga el cambio de un móvil,
de la ventana del PC o del icono de la bandeja. Las conexiones sin actividad durante 60 s se cierran (el móvil
envía `ping` cada 15 s).
