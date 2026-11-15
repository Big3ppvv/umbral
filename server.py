#!/usr/bin/env python3
"""Servidor local de UMBRA. Python 3.10+, sin dependencias externas."""
from __future__ import annotations

import argparse
from functools import partial
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import math
from pathlib import Path
import re
import sqlite3
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent
MAX_BODY_BYTES = 4096
RUN_ID = re.compile(r"[A-Za-z0-9_-]{8,80}\Z")


class ApiError(Exception):
    def __init__(self, status: int, message: str):
        self.status = status
        self.message = message


class Store:
    """SQLite serializa escrituras y garantiza la idempotencia de cada partida."""

    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.connect() as db:
            db.executescript("""
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS profile (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    shards INTEGER NOT NULL DEFAULT 0,
                    vitality INTEGER NOT NULL DEFAULT 0,
                    power INTEGER NOT NULL DEFAULT 0,
                    runs INTEGER NOT NULL DEFAULT 0,
                    wins INTEGER NOT NULL DEFAULT 0,
                    best_room INTEGER NOT NULL DEFAULT 0,
                    best_time REAL
                );
                INSERT OR IGNORE INTO profile (id) VALUES (1);
                CREATE TABLE IF NOT EXISTS runs (
                    id TEXT PRIMARY KEY,
                    won INTEGER NOT NULL,
                    room INTEGER NOT NULL,
                    kills INTEGER NOT NULL,
                    duration REAL NOT NULL,
                    shards INTEGER NOT NULL
                );
            """)

    def connect(self):
        db = sqlite3.connect(self.path, timeout=10)
        db.row_factory = sqlite3.Row
        return db

    @staticmethod
    def _profile(db):
        p = db.execute("SELECT * FROM profile WHERE id = 1").fetchone()
        return {
            "version": 1,
            "shards": p["shards"],
            "vitality": p["vitality"],
            "power": p["power"],
            "runs": p["runs"],
            "wins": p["wins"],
            "bestRoom": p["best_room"],
            "bestTime": p["best_time"],
        }

    def profile(self):
        with self.connect() as db:
            return self._profile(db)

    def record_run(self, payload):
        if not isinstance(payload, dict):
            raise ApiError(400, "La partida debe ser un objeto JSON.")
        run_id = payload.get("id")
        if not isinstance(run_id, str) or not RUN_ID.fullmatch(run_id):
            raise ApiError(400, "Identificador de partida inválido.")
        won = payload.get("won")
        if type(won) is not bool:
            raise ApiError(400, "El resultado de la partida debe ser verdadero o falso.")
        for key, low, high in (("room", 1, 6), ("kills", 0, 100000), ("shards", 0, 1000)):
            value = payload.get(key)
            if type(value) is not int or not low <= value <= high:
                raise ApiError(400, f"Valor inválido para {key}.")
        duration = payload.get("duration")
        if type(duration) not in (int, float) or not math.isfinite(duration) or not 0 <= duration <= 86400:
            raise ApiError(400, "La duración debe estar entre 0 y 86400 segundos.")
        if won and payload["room"] != 6:
            raise ApiError(400, "La victoria requiere completar la sala 6.")
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            old = db.execute("SELECT * FROM runs WHERE id = ?", (run_id,)).fetchone()
            values = (int(won), payload["room"], payload["kills"], duration, payload["shards"])
            if old is not None:
                if tuple(old[key] for key in ("won", "room", "kills", "duration", "shards")) != values:
                    raise ApiError(409, "Ese identificador ya pertenece a otra partida.")
                return self._profile(db)
            db.execute("INSERT INTO runs VALUES (?, ?, ?, ?, ?, ?)", (run_id, *values))
            profile = self._profile(db)
            best_time = profile["bestTime"]
            if won:
                best_time = duration if best_time is None else min(best_time, duration)
            db.execute(
                """UPDATE profile SET shards = shards + ?, runs = runs + 1,
                wins = wins + ?, best_room = MAX(best_room, ?), best_time = ? WHERE id = 1""",
                (payload["shards"], int(won), payload["room"], best_time),
            )
            return self._profile(db)

    def upgrade(self, payload):
        if not isinstance(payload, dict) or payload.get("kind") not in ("vitality", "power"):
            raise ApiError(400, "Elige una mejora válida: vitality o power.")
        kind = payload["kind"]
        with self.connect() as db:
            db.execute("BEGIN IMMEDIATE")
            p = self._profile(db)
            level = p[kind]
            if level >= 5:
                raise ApiError(409, "Esta mejora ya alcanzó el nivel máximo.")
            cost = 15 * (level + 1)
            if p["shards"] < cost:
                raise ApiError(409, f"Necesitas {cost} fragmentos para esta mejora.")
            # kind procede exclusivamente de la lista permitida anterior.
            db.execute(f"UPDATE profile SET {kind} = {kind} + 1, shards = shards - ? WHERE id = 1", (cost,))
            return self._profile(db)


class Handler(SimpleHTTPRequestHandler):
    server_version = "Umbra/1.0"

    def __init__(self, *args, store: Store, directory: str, **kwargs):
        self.store = store
        self.static_root = Path(directory).resolve()
        super().__init__(*args, directory=directory, **kwargs)

    def end_headers(self):
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "same-origin")
        super().end_headers()

    def json_response(self, status, data):
        body = json.dumps(data, ensure_ascii=False, allow_nan=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = urlsplit(self.path).path
        if path == "/api/profile":
            self.json_response(200, self.store.profile())
        elif path == "/api/health":
            self.json_response(200, {"ok": True, "storage": "sqlite"})
        elif path == "/api" or path.startswith("/api/"):
            self.json_response(404, {"error": "Ruta no encontrada."})
        elif self.safe_static():
            super().do_GET()

    def do_HEAD(self):
        if urlsplit(self.path).path.startswith("/api"):
            self.send_response(405)
            self.send_header("Allow", "GET, POST")
            self.end_headers()
        elif self.safe_static():
            super().do_HEAD()

    def safe_static(self):
        path = unquote(urlsplit(self.path).path)
        if "\x00" in path or "\\" in path or ".." in path.split("/"):
            self.send_error(HTTPStatus.FORBIDDEN, "Ruta no permitida")
            return False
        target = (self.static_root / path.lstrip("/")).resolve()
        if not target.is_relative_to(self.static_root):
            self.send_error(HTTPStatus.FORBIDDEN, "Ruta no permitida")
            return False
        if target.is_dir() and not (target / "index.html").is_file():
            self.send_error(HTTPStatus.NOT_FOUND, "Archivo no encontrado")
            return False
        return True

    def do_POST(self):
        path = urlsplit(self.path).path
        if path not in ("/api/run", "/api/upgrade"):
            self.json_response(404, {"error": "Ruta no encontrada."})
            return
        try:
            # Evita que una página ajena modifique el progreso del servidor local.
            origin = self.headers.get("Origin")
            host = self.headers.get("Host")
            if self.headers.get("Sec-Fetch-Site") == "cross-site" or (origin and urlsplit(origin).netloc != host):
                raise ApiError(403, "Origen de solicitud no permitido.")
            if self.headers.get_content_type() != "application/json":
                raise ApiError(415, "Envía los datos como application/json.")
            try:
                length = int(self.headers.get("Content-Length", "0"))
            except ValueError:
                raise ApiError(400, "Longitud de solicitud inválida.")
            if not 0 < length <= MAX_BODY_BYTES:
                raise ApiError(413, "La solicitud está vacía o supera los 4096 bytes.")
            try:
                payload = json.loads(self.rfile.read(length).decode("utf-8"))
            except (ValueError, UnicodeError):
                raise ApiError(400, "El cuerpo no contiene un JSON válido.")
            result = self.store.record_run(payload) if path == "/api/run" else self.store.upgrade(payload)
            self.json_response(200, result)
        except ApiError as error:
            self.json_response(error.status, {"error": error.message})
        except sqlite3.Error:
            self.json_response(503, {"error": "No se pudo guardar el progreso. Inténtalo de nuevo."})


def create_server(host="127.0.0.1", port=8000, data_dir=None, static_dir=None):
    static_dir = Path(static_dir) if static_dir else ROOT / "dist"
    data_dir = Path(data_dir) if data_dir else ROOT / ".data"
    store = Store(data_dir / "umbra.sqlite3")
    handler = partial(Handler, directory=str(static_dir), store=store)
    return ThreadingHTTPServer((host, port), handler)


def main():
    parser = argparse.ArgumentParser(description="Sirve UMBRA y guarda el progreso en SQLite.")
    parser.add_argument("--host", default="127.0.0.1", help="Dirección de escucha (por defecto: 127.0.0.1)")
    parser.add_argument("--port", default=8000, type=int)
    parser.add_argument("--data-dir", type=Path, default=ROOT / ".data")
    args = parser.parse_args()
    if not (ROOT / "dist" / "index.html").is_file():
        parser.error("Falta dist/index.html. Ejecuta npm install y npm run build primero.")
    with create_server(args.host, args.port, args.data_dir) as server:
        print(f"UMBRA disponible en http://{args.host}:{args.port}", flush=True)
        print(f"Progreso: {args.data_dir.resolve() / 'umbra.sqlite3'}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print("\nServidor detenido.")


if __name__ == "__main__":
    main()
