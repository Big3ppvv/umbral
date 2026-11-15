# UMBRA · El último faro

Roguelite de acción para navegador, en español. Explora seis salas de una cripta cambiante, elige reliquias entre combates y derrota al Devorador en la sala final. Cada intento deja fragmentos para mejorar la vitalidad y el poder de tu personaje.

## Versión Unity con C#

El proyecto también incluye una edición independiente para **Unity 2022.3 LTS**, en `unity/UMBRA`. Se abre desde Unity Hub y genera su escena al importar. Conserva seis salas, jefe, reliquias, mejoras, controles táctiles y arte pixelado generado por código.

Consulta [las instrucciones de Unity](unity/README.md). Esta edición usa C# y guardado local de Unity; el servidor Python y el juego web siguen disponibles como antes. Los perfiles son independientes. El enlace «Unity + C#» del sitio descarga esta edición, y el ZIP completo también incluye ambas.

La lógica C# tiene pruebas ejecutables sin el editor, con .NET 8:

```sh
dotnet run --project unity/CoreTests/Umbra.CoreChecks.csproj
```

La importación, los gráficos y las compilaciones finales requieren Unity Editor y no se han ejecutado en el entorno de desarrollo de esta entrega.

## Ejecutar en tu equipo

Necesitas **Node.js 20 o superior** y **Python 3.10 o superior**.

```sh
npm install
npm run build
python3 server.py
```

Abre **http://127.0.0.1:8000**. El servidor Python sirve el juego compilado y guarda el progreso en SQLite. No necesita paquetes de Python. Si descargaste el archivo «Código fuente» desde el juego, ya incluye `dist/`: puedes ejecutar directamente `python3 server.py` tras extraerlo y entrar en la carpeta `umbra`.

Puedes cambiar el puerto o la ubicación del progreso:

```sh
python3 server.py --port 8080 --data-dir ./mis-partidas
```

El servidor escucha en `127.0.0.1` por defecto. `--host 0.0.0.0` permite acceder desde tu red; los jugadores de ese servidor comparten el mismo perfil local.

## Controles

| Acción | Control |
| --- | --- |
| Moverse | WASD o flechas |
| Apuntar | Ratón |
| Atacar | Clic; también hay ataque automático al enemigo cercano |
| Esquivar | Barra espaciadora |
| Pausar / continuar | Escape o botón de pausa |
| Móvil | Controles táctiles en pantalla |

Elige una de tres reliquias al limpiar cada sala. La sexta sala contiene al Devorador. Al terminar, los fragmentos pasan al refugio y puedes comprar mejoras permanentes; cada nivel cuesta `15 × (nivel actual + 1)` fragmentos y hay cinco niveles de cada mejora. Corazón del faro aporta +10 de vitalidad máxima por nivel; Filo de aurora, +2 de poder por nivel.

## Tecnologías y archivos

- **TypeScript:** motor Canvas 2D, combate, salas, enemigos, reliquias e interfaz.
- **HTML y CSS:** interfaz adaptable, controles y paneles accesibles.
- **Python:** servidor HTTP y API de guardado con SQLite.
- **Unity y C#:** edición independiente con simulación, sprites, interfaz y guardado nativo.
- `src/engine.ts`: simulación y dibujo del juego.
- `server.py`: servidor local y almacenamiento.
- `tests/test_server.py`: pruebas de la API, persistencia y aislamiento de archivos.

## Guardado

La versión alojada como sitio estático guarda el progreso en el navegador (`localStorage`). Al ejecutar `server.py`, el juego utiliza la API local y la base `./.data/umbra.sqlite3`. Son perfiles independientes. Conserva el archivo SQLite para conservar el progreso; los datos se quedan en tu equipo.

El guardado es para un juego personal: la API valida límites y evita registrar dos veces una misma partida, pero confía en las puntuaciones calculadas por el cliente. No es un sistema competitivo ni incluye cuentas o clasificación pública.

## Comprobaciones

```sh
npm run typecheck
npm run build
npm test
```

`npm test` ejecuta las pruebas de Python y las pruebas de Node.js del motor y el guardado. Estas últimas importan los módulos compilados de `dist/`, por lo que se debe compilar antes. Para ejecutar solamente el servidor: `python3 -m unittest discover -s tests -v`.

La API expone `GET /api/profile`, `POST /api/run`, `POST /api/upgrade` y `GET /api/health`. Los errores devuelven JSON con la propiedad `error`. La entrega incluye el código fuente y los archivos compilados para jugar sin volver a compilar.
# umbral
