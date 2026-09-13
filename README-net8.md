# DMD Extensions para Proton (build .NET 8)

Esta rama (`net8`) es un fork reducido de [DMD Extensions](https://github.com/freezy/dmd-extensions)
para mostrar el DMD de **Pinball FX Classic** (ex Pinball FX3) en un cabinet con
**Bazzite Linux**, donde el juego corre bajo **Steam Proton**.

La idea central: `dmdext.exe` se publica como **un solo ejecutable win-x64 autocontenido
(.NET 8)**, que corre dentro del prefijo de Proton del juego sin instalar .NET Framework
4.7.2 ni ningún runtime. Sobre eso se agregaron arreglos para Wine, arreglos a bugs del
dmdext original y funciones nuevas (ventana de backglass, estado "sin mesa" por red).

Para el uso normal en Windows, con hardware o con `DmdDevice.dll`, usa el dmdext
original: este build **no** lo reemplaza.

---

## 1. Qué soporta y qué no

### Probado en el cabinet

| Función | Estado |
|---|---|
| `dmdext mirror -s pinballfxclassic` bajo Proton (lectura de memoria del juego) | ✅ |
| DMD virtual (ventana) | ✅ |
| Stream por red (WebSocket) a otro equipo, junto con el DMD virtual | ✅ |
| Nombre de la mesa por red (`gameName`) y estado "sin mesa" | ✅ |
| Imagen en el DMD mientras no hay mesa (`--idle-play`, PNG o GIF animado) | ✅ |
| Ventana de backglass con la imagen de cada mesa | ✅ |
| Backglass por defecto cuando no hay mesa (`PinballFX3.png`) | ✅ |
| Cerrar el juego sin que Steam lo siga marcando como abierto | ✅ |

Entorno de prueba: Bazzite `bazzite-deck` 44 (Stable F44.20260907), KDE Plasma 6 (KWin
6.7, Wayland), Proton 11.0, Steam Linux Runtime 4, dos pantallas (vertical para el
playfield y horizontal 1920×1080 para el backglass).

### Incluido en el build, pero no probado bajo Proton

- Fuentes `pinballfx2`, `pinballarcade`, `futurepinball` y `screen`.
- Comando `test`.
- Salidas `alphanumeric` (display alfanumérico virtual), `-o` (bitmaps a carpeta),
  `--dump-frames` y `--pinup`.

### No incluido

| Qué | Por qué |
|---|---|
| Salidas de hardware por USB, serie o WiFi: PinDMD v1/v2/v3, PIN2DMD (XL, HD), ZeDMD (incl. HD y WiFi), Pixelcade | Fuera del alcance: el objetivo es el DMD virtual y la red |
| `DmdDevice.dll` / `DmdDevice64.dll` (Pinball FX nuevo, Visual PinMAME) | Solo se porta el ejecutable |
| Fuente `propinball` | Depende de un puente C++/CLI que no existe en .NET 8. Aún aparece en la ayuda de `-s`, pero no funciona |
| Comandos `play` y `server` | Fuera del alcance |
| Browser stream, VPDB stream, salida a video | Fuera del alcance |
| Analytics y reporte de errores (Raygun) | Desactivados (`AnalyticsStub.cs`) |

Todo esto se excluye con el símbolo `DMDEXT_MIRROR_ONLY` y los `Compile Remove` de
`LibDmd/LibDmd.Net8.csproj` y `Console/Console.Net8.csproj`.

---

## 2. Compilar

Requiere el SDK de .NET 8 en Windows.

```
dotnet publish Console\Console.Net8.csproj -c Release -o publish\dmdext-net8-win-x64
```

Queda en `publish\dmdext-net8-win-x64\`:

| Archivo | Qué es |
|---|---|
| `dmdext.exe` | Ejecutable único, sin dependencias |
| `dmdext.log.config` | Configuración de logs (NLog) |
| `dmdext-proton.sh` | Script de lanzamiento para Steam (sección 4) |
| `idle.png` | Imagen del DMD sin mesa: la imagen de prueba de dmdext (`Console/Test/TestImage.png`). Se usa si no hay un `DEFAULT_IDLE.gif`, `.png` o `.jpg` junto a `dmdext.exe` |

Los proyectos .NET 8 conviven con los originales (.NET Framework) y usan sus propias
carpetas `bin.net8/` y `obj.net8/`.

---

## 3. Cambios respecto al dmdext original

### Para que funcione bajo Wine / Proton

- **Detección de Wine** (`InteropUtil.IsRunningOnWine`).
- **Render por software** en las ventanas WPF: con Direct3D se veía solo un triángulo de color.
- **Ventanas opacas**: Wine no compone bien las ventanas con transparencia por píxel.
- **OpenGL del DMD** dibuja en el framebuffer de SharpGL; en el back buffer de la ventana oculta quedaba negro.
- **Se vuelve a dibujar el último frame** a los 100 ms, porque la ventana a veces seguía mostrando el anterior.
- **Imágenes congeladas** (`Freeze`) para poder usarlas desde cualquier hilo; si no, dmdext se colgaba.
- **DMD virtual no redimensionable**: una ventana redimensionable sin transparencia mostraba un borde blanco fino. La posición y el tamaño vienen de las opciones.
- **Backglass sin la última fila de píxeles del monitor**: si cubre el monitor completo, Wine la marca como pantalla completa, y KWin la pone encima del DMD "siempre encima" mientras el juego está activo en otra pantalla.
- **Logs de diagnóstico** cuando no se encuentra el proceso del juego o no se puede leer su memoria.
- **Script `dmdext-proton.sh`**: arranca dmdext dentro de la sesión Proton del juego. Un Wine aparte (por ejemplo protontricks) no ve el proceso del juego.

### Bugs del original arreglados

- **Dedupe invertido** en el lector de Pinball FX3: mandaba los frames repetidos y descartaba los nuevos.
- **`gameName` nunca se mandaba por red** en modo mirror.
- **`gameName` sin nulo final**, aunque el propio deserializador lo espera.
- **Opciones `--virtual-*` ignoradas en modo mirror** (tamaño y brillo de los puntos, padding, texturas): el DMD virtual siempre usaba el estilo por defecto.
- **Al terminar el modo reposo** se liberaban las salidas que comparte con el render normal.
- **Frames que quedaban en cola** pisaban la imagen de reposo.
- **Conexión de red bloqueante**: con el receptor apagado, dmdext tardaba ~37 s en arrancar y ~36 s en salir. Bajo Proton eso dejaba a Steam con el juego "abierto" y la pantalla en negro.

### Funciones nuevas

- **Estado "sin mesa"**: se detecta porque el puntero al DMD en la memoria del juego es nulo, no porque dejen de llegar frames (con el DMD quieto tampoco llegan). Se avisa una sola vez y dmdext pasa a reposo (`--idle-play` o pantalla en blanco).
- **`--url` suma la red** al destino elegido: DMD virtual y red a la vez.
- **Ventana de backglass** (`--backglass`, sección 5).

---

## 4. Instalación en Bazzite

1. Copia la carpeta publicada al cabinet, por ejemplo a `~/Pinball/dmdext-net8-win-x64/`.
2. Dale permiso de ejecución al script:
   ```bash
   chmod +x ~/Pinball/dmdext-net8-win-x64/dmdext-proton.sh
   ```
3. En Steam: Pinball FX Classic → Propiedades → Opciones de lanzamiento:
   ```
   /var/home/<usuario>/Pinball/dmdext-net8-win-x64/dmdext-proton.sh %command%
   ```
4. Ajusta las variables al inicio del script (tabla abajo) y lanza el juego desde Steam.
   El log de dmdext queda en `~/dmdext-mirror.log`, y se vacía en cada lanzamiento.

El script lanza el juego, espera a que aparezca su sesión Proton
(`com.steampowered.App442120`) y arranca dmdext dentro de ella con
`steam-runtime-launch-client`. dmdext usa `-q`, así que se cierra solo cuando se cierra
el juego.

### Variables del script

| Variable | Qué hace |
|---|---|
| `SOURCE` | Fuente de mirror: `pinballfxclassic` (o `pinballfx2`) |
| `VIRTUAL_DMD` | `true` para mostrar el DMD virtual en este equipo |
| `BACKGLASS` | `true` para mostrar la ventana de backglass |
| `BACKGLASS_X`, `BACKGLASS_Y`, `BACKGLASS_WIDTH`, `BACKGLASS_HEIGHT` | Área de la pantalla del backglass, en píxeles del escritorio. El DMD se ubica relativo a ella |
| `BACKGLASS_PATH` | Carpeta de las imágenes. Vacío = la carpeta `data/steam` del juego |
| `BACKGLASS_IDLE` | Imagen sin mesa. Vacío = `DEFAULT_IDLE.png` de la carpeta de imágenes; si no está, la imagen por defecto del juego (`PinballFX3.png`); si tampoco, negro |
| `DMD_WIDTH` | Ancho de la ventana del DMD en píxeles; el alto sale de la proporción |
| `DMD_PADDING` | Borde negro alrededor de los puntos, **en puntos del DMD** |
| `DMD_BOTTOM_MARGIN` | Espacio entre el DMD y el borde inferior del área del backglass |
| `NETWORK_HOST`, `NETWORK_PORT`, `NETWORK_PATH` | Receptor WebSocket. `NETWORK_HOST` vacío = sin red |
| `IDLE_PLAY` | Imagen del DMD sin mesa (PNG, JPG o GIF animado). Vacío = `DEFAULT_IDLE.gif`, `.png` o `.jpg` junto a `dmdext.exe`, si existe; si no, `idle.png`. `none` = DMD en blanco |
| `EXTRA_ARGS` | Otros argumentos de dmdext, p. ej. `(--virtual-dot-glow 0.5)` |

Por defecto el DMD queda centrado abajo en el área del backglass. Para ver la posición de
cada pantalla usa `kscreen-doctor -o` (campo `Geometry`).

### Rutas bajo Wine

dmdext ve el sistema de archivos Linux como unidades de Windows:

| Unidad | Ruta Linux |
|---|---|
| `Z:` | `/` (el script convierte las rutas solo) |
| `S:` | `~/.local/share/Steam` |

---

## 5. Backglass

Con `--backglass`, dmdext abre una ventana sin bordes que muestra la imagen de la mesa
que está corriendo:

1. **Con mesa:** muestra `<nombre de la mesa>.png` (o `.jpg`), p. ej. `UNIVERSAL_Jaws.png`,
   `WMS_Indiana_Jones.png`. Es el mismo nombre que llega por red en `gameName`.
2. **Sin mesa, o si la mesa no tiene imagen:** muestra `--backglass-idle` si está definido.
3. **Si no hay `--backglass-idle`:** muestra `DEFAULT_IDLE.png` (o `.jpg`) de la carpeta de
   imágenes, si existe. Sirve para poner una imagen propia sin tocar las opciones.
4. **Si no existe `DEFAULT_IDLE`:** muestra la imagen por defecto del juego, `PinballFX3.png`
   para Pinball FX3 y Classic.
5. **Si tampoco existe:** negro.

**Carpeta de imágenes:** si no pasas `--backglass-path`, dmdext toma la carpeta del
ejecutable del juego y usa `data\steam`, donde están las mesas (`*.pxp`). En el cabinet:
`~/.local/share/Steam/steamapps/common/Pinball FX Classic/data/steam/`.

**Comportamiento de la ventana:**
- Nunca le quita el foco al juego.
- Queda debajo del DMD virtual, que va siempre encima.
- Carga las imágenes en segundo plano, sin frenar la captura.

| Opción | Qué hace |
|---|---|
| `--backglass` | Activa la ventana |
| `--backglass-position <Left> <Top> <Width> <Height>` | Posición y tamaño. Default `0 0 1920 1080` |
| `--backglass-path <carpeta>` | Carpeta de imágenes |
| `--backglass-idle <imagen>` | Imagen sin mesa |

Las imágenes **no se mandan por red**. Un receptor remoto que quiera mostrar el backglass
necesita su propia copia de las imágenes, y las busca por `gameName`.

---

## 6. Stream por red

Protocolo: el WebSocket binario de dmdext, un mensaje por `<nombre>\0<payload>`. Lo que
cambia con este fork:

| Mensaje / situación | Qué llega |
|---|---|
| Se carga una mesa | `gameName` con el nombre, p. ej. `WMS_Medieval_Madness` |
| Se vuelve al menú | `gameName` vacío, **una sola vez** |
| En reposo con `--idle-play` | Un frame `rgb24` con la imagen, o varios si es un GIF animado |
| En reposo sin `--idle-play` | Un frame `gray2Planes` en blanco |
| Frames repetidos | No se mandan |
| Al cerrar dmdext | Un `gray2Planes` en blanco y se cierra la conexión |
| Reconexión (`--retry`) | Se reenvía el último `gameName`, `dimensions` y `color` |

`gameName` vacío significa **"no hay mesa"**. No es lo mismo que "el DMD no cambió":
jugando con el DMD quieto no llegan frames, pero `gameName` sigue con el nombre de la mesa.

---

## 7. Sistema operativo: Game Mode vs. Desktop Mode

### Por qué se necesita Desktop Mode

Bazzite (imagen `bazzite-deck`) puede iniciar en dos sesiones:

| Sesión | Qué es | DMD virtual y backglass |
|---|---|---|
| **Game Mode** (`gamescope-session`) | Interfaz tipo Steam Deck, compositor gamescope | ❌ gamescope muestra una sola pantalla, así que no hay dónde poner esas ventanas |
| **Desktop Mode** (`plasma`) | Escritorio KDE Plasma (KWin), varias pantallas | ✅ Es donde se probó todo |

En Desktop Mode, Steam puede abrirse en la interfaz clásica o en **Big Picture**; ambas
sirven.

**En Game Mode (no probado):** podría funcionar una configuración **solo de red**
(`VIRTUAL_DMD=false`, `BACKGLASS=false`, `NETWORK_HOST` definido). dmdext mandaría el DMD
y el nombre de la mesa a otro dispositivo, por ejemplo una Raspberry Pi, que mostraría el
DMD y el backglass con sus propias imágenes.

### Elegir la sesión de inicio

Se corren en una terminal del cabinet (no piden contraseña):

| Acción | Comando |
|---|---|
| Iniciar en Desktop Mode | `ujust set-default-desktop` |
| Iniciar en Game Mode | `ujust set-default-game-mode` |
| Ver la sesión configurada | `steamosctl get-default-login-mode` |

Por dentro, `ujust set-default-desktop` corre `steamosctl set-default-login-mode desktop`,
que deja `Session=plasma.desktop` en `/etc/sddm.conf.d/zz-holo-autologin.conf`.

⚠️ El acceso directo **"Return to Gaming Mode"** del escritorio vuelve a dejar Game Mode
como sesión de inicio. Después de usarlo hay que correr `ujust set-default-desktop` de nuevo.

### Steam en Big Picture al iniciar el escritorio

Bazzite abre Steam al entrar al escritorio con `/etc/xdg/autostart/steam.desktop`
(`bazzite-steam -silent`: vista clásica, minimizado). Un archivo con el mismo nombre en
`~/.config/autostart/` reemplaza al del sistema, y ese es también el archivo que maneja la
opción de Steam de abrirse al iniciar el computador.

En la imagen `bazzite-deck` el lanzador agrega `-steamdeck`, y la opción de Steam de iniciar
en Big Picture no se aplicaba. Por eso `~/.config/autostart/steam.desktop` se reemplaza por:

```ini
[Desktop Entry]
Type=Application
Name=Steam
Comment=Opens Steam in Big Picture mode when the desktop starts. Overrides Bazzite's /etc/xdg/autostart/steam.desktop (bazzite-steam -silent).
Exec=/usr/bin/bazzite-steam steam://open/bigpicture
Icon=steam
Terminal=false
```

⚠️ Qué **no** hacer:
- **No cambies la opción de Steam de abrirse al iniciar el computador.**
  - Si la apagas, Steam borra este archivo y vuelve el arranque `-silent` del sistema.
  - Si la prendes, Steam lo reescribe con su versión y abre en la vista clásica.
- **No agregues otra entrada de autostart que abra Steam.** Con dos arrancan dos clientes de
  Steam a la vez: uno se queda sin interfaz, aparece "steamwebhelper is not responding", y en
  una prueba el joystick tardó ~2 minutos en responder.

**Para deshacerlo:** borra `~/.config/autostart/steam.desktop`; vuelve el arranque del sistema.

### Problemas conocidos del sistema

- **Big Picture pegado en negro con "ABORT GAME":** pasaba cuando dmdext tardaba en cerrarse
  (ya arreglado). Si vuelve a pasar y el control no responde, se puede cerrar Big Picture
  con `steam steam://close/bigpicture`.
- **Steam pegado en "Launching…" con las pantallas apagadas por ahorro de energía:** enciende
  las pantallas (`kscreen-doctor --dpms on`) o desactiva el ahorro de energía del escritorio
  en el cabinet.
- **Procesos lanzados por SSH:** logind tiene `KillUserProcesses=true`, así que se cierran al
  terminar la sesión SSH. Para probar dmdext a distancia, lanza el juego desde Steam
  (`steam steam://rungameid/442120`) y deja que el script arranque dmdext.

---

## 8. Limitaciones y pendientes

- **Solo probado con Pinball FX Classic** bajo Proton.
- **Backglass bajo Wine:** deja sin cubrir la última fila de píxeles de su monitor (sección 3).
- **DMD virtual bajo Wine:** no se puede redimensionar con el mouse; se configura con opciones.
- **Bugs del original que siguen:**
  - El parámetro `dedupe` del lector de Pinball FX3 se ignora. No afecta: ahora siempre deduplica.
  - `NetworkStream` no declara `IColoredGray6Destination`.
  - `coloredGray6` escribe 24 bytes de relleno que su propio deserializador no salta.
  - `NetworkStream` es singleton y re-inicializarlo no libera el cliente anterior.
- **Ayuda de `-s`:** todavía lista `propinball`, que no está incluido.
- **Licencia:** GPLv2, igual que el original. Si distribuyes el binario, también tienes que
  entregar el código fuente.
