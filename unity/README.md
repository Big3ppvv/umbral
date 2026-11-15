# UMBRA para Unity y C#

Esta carpeta contiene una edición de UMBRA en **C# para Unity**, además de la versión web de TypeScript y Python que se encuentra en la raíz del repositorio. El mundo se dibuja con `SpriteRenderer`, la canalización de renderizado integrada de Unity y UGUI para la interfaz; la ilustración se genera por código y no requiere descargar recursos gráficos externos.

## Abrir y jugar

1. Instala **Unity Hub** y **Unity 2022.3.62f1 LTS**. Añade el módulo de compilación de tu plataforma si quieres generar un ejecutable.
2. En Unity Hub, elige **Add / Añadir proyecto desde disco** y selecciona la carpeta `unity/UMBRA` de este repositorio, que contiene `Assets`, `Packages` y `ProjectSettings`.
3. Abre el proyecto y espera a que termine la importación de paquetes y scripts.
4. El editor crea automáticamente la escena `Assets/Scenes/Umbra.unity`. Si no la ves, usa **UMBRA → Preparar proyecto** y abre esa escena desde Project.
5. Pulsa **Play**. El controlador crea el escenario y la interfaz al iniciar.

La preparación también genera `Assets/Resources/UmbraSprite.mat` con el shader `Sprites/Default`. Este material se incluye en las compilaciones para conservar el shader que usa el dibujo generado por código; si ya existe, no se reemplaza.

La escena inicial está vacía de forma intencional. No necesitas colocar cámaras, prefabs ni componentes a mano. La preparación conserva otras escenas abiertas y sus cambios sin guardar; la escena generada queda registrada en Build Settings.

La versión objetivo es Unity 2022.3 LTS. Unity 6 podría pedir actualizar el proyecto; esa migración no se ha verificado.

## Controles y partidas

- **WASD o flechas:** moverse.
- **Ratón:** apuntar; mantén clic para dirigir los ataques. El personaje también ataca automáticamente a los enemigos cercanos.
- **Espacio:** esquivar.
- **Escape:** pausar o continuar.
- Usa los botones de la interfaz para iniciar una incursión, elegir reliquias y comprar mejoras permanentes.

Atraviesa seis salas y derrota al Devorador. Elige una reliquia después de cada combate y utiliza los fragmentos obtenidos para mejorar al Portador entre intentos.

El progreso de Unity es independiente del perfil del navegador y de la base de datos del servidor Python. Ejecutar el proyecto de Unity no requiere Node.js, Python ni un servidor web local.

## Generar una compilación

En el menú del editor puedes elegir:

- **UMBRA → Build → Linux**: ejecutable Linux de 64 bits.
- **UMBRA → Build → Windows**: ejecutable Windows de 64 bits.
- **UMBRA → Build → WebGL**: carpeta publicable en un servidor web compatible con Unity WebGL.

Selecciona una carpeta de destino. Si falta un módulo de plataforma, instálalo desde Unity Hub usando la misma versión del editor. Conserva todos los archivos generados junto al ejecutable; no basta con copiar únicamente el `.exe` o `.x86_64`.

Para WebGL, sirve la carpeta generada mediante HTTP/HTTPS; abrir `index.html` directamente desde el disco no es una prueba válida del reproductor. La edición WebGL de Unity es una compilación distinta de la versión HTML/TypeScript de la raíz.

También hay métodos de compilación por línea de comandos. Ejemplo para Linux, adaptando la ruta del editor y del repositorio:

```sh
/ruta/a/Unity \
  -batchmode -quit \
  -projectPath /ruta/al/repositorio/unity/UMBRA \
  -buildTarget Linux64 \
  -executeMethod Umbra.Editor.ProjectSetup.BuildLinux \
  -umbraBuildPath /ruta/de/salida/umbra-linux \
  -logFile /ruta/de/salida/unity-build.log
```

Para Windows usa `-buildTarget Win64` y `Umbra.Editor.ProjectSetup.BuildWindows`. Para WebGL usa `-buildTarget WebGL` y `Umbra.Editor.ProjectSetup.BuildWebGL`. Estos comandos requieren una instalación de Unity con licencia activa y el módulo correspondiente. Si omites `-umbraBuildPath`, la salida queda en `unity/UMBRA/Builds/<plataforma>`; las carpetas de compilación se excluyen de Git.

## Archivos principales

- `UMBRA/Assets/Umbra/Runtime/Core/`: reglas y simulación del roguelite en C#.
- `UMBRA/Assets/Umbra/Runtime/`: integración con Unity, controles e interfaz.
- `UMBRA/Assets/Umbra/Editor/ProjectSetup.cs`: preparación inicial y compilaciones.
- `UMBRA/Packages/manifest.json`: paquetes de Unity requeridos.
- `UMBRA/ProjectSettings/ProjectVersion.txt`: versión objetivo del editor.
- `CoreTests/`: pruebas de la simulación independiente de Unity.

Los archivos `.meta` que cree Unity dentro de `Assets` forman parte del proyecto y deben conservarse en Git. `Library`, `Temp`, `Logs`, configuraciones personales y compilaciones son archivos regenerables y se excluyen.

## Verificación

La simulación independiente puede comprobarse con el **SDK de .NET 8**, desde la raíz del repositorio:

```sh
dotnet run --project unity/CoreTests/Umbra.CoreChecks.csproj
```

Si estás dentro de la carpeta `unity`, usa `dotnet run --project CoreTests/Umbra.CoreChecks.csproj`. Estas pruebas verifican reglas de juego, pero no sustituyen abrir el editor y comprobar renderizado, entradas, audio y compilación de plataformas.

**No se ha ejecutado Unity Editor en el entorno de desarrollo de esta entrega.** La importación, el modo Play y los ejecutables de Unity quedan pendientes de validación con Unity 2022.3.62f1 y los módulos de destino instalados.
