"""Pruebas de persistencia, límites de entrada y aislamiento de archivos."""
import concurrent.futures
from http.client import HTTPConnection
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from server import create_server


class ServerTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.static = self.root / "dist"
        self.static.mkdir()
        (self.static / "index.html").write_text("<!doctype html><title>UMBRA</title>")
        (self.static / "assets").mkdir()
        (self.static / "assets" / "main.js").write_text("export const game = true;")
        (self.static / "assets" / "styles.css").write_text("body { color: white; }")
        with zipfile.ZipFile(self.static / "umbra-source.zip", "w") as archive:
            archive.writestr("umbra/README.md", "# UMBRA")
        (self.root / "secret.txt").write_text("private")
        (self.static / "link.txt").symlink_to(self.root / "secret.txt")
        self.server = create_server(port=0, data_dir=self.root / "data", static_dir=self.static)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.port = self.server.server_port

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()
        self.tmp.cleanup()

    def request(self, path, payload=None, headers=None):
        connection = HTTPConnection("127.0.0.1", self.port, timeout=5)
        data = json.dumps(payload) if payload is not None else None
        connection.request("POST" if payload is not None else "GET", path, data,
                           headers or ({"Content-Type": "application/json"} if data else {}))
        response = connection.getresponse()
        body = response.read()
        result = (response.status, body, response.getheader("Content-Type"))
        connection.close()
        return result

    def post(self, path, payload):
        status, body, _ = self.request(path, payload)
        return status, json.loads(body)

    def run_payload(self, **extra):
        return dict({"id": "run_12345678", "won": False, "room": 3, "kills": 20, "duration": 90.5, "shards": 40}, **extra)

    def test_static_and_paths(self):
        status, body, mime = self.request("/")
        self.assertEqual(status, 200)
        self.assertIn("text/html", mime)
        self.assertIn(b"UMBRA", body)
        for path in ("/../secret.txt", "/%2e%2e/secret.txt", "/link.txt", "/foo%5c..%5csecret.txt"):
            self.assertEqual(self.request(path)[0], 403)
        self.assertEqual(self.request("/api/unknown")[0], 404)
        self.assertEqual(self.request("/missing.js")[0], 404)

    def test_download_and_asset_content_types(self):
        for path, allowed_types in (("/assets/main.js", ("text/javascript", "application/javascript")),
                                    ("/assets/styles.css", ("text/css",)),
                                    ("/umbra-source.zip", ("application/zip",))):
            status, body, mime = self.request(path)
            self.assertEqual(status, 200, path)
            self.assertTrue(any(mime.startswith(expected) for expected in allowed_types), mime)
            self.assertTrue(body)
        status, body, _ = self.request("/umbra-source.zip")
        import io
        with zipfile.ZipFile(io.BytesIO(body)) as archive:
            self.assertIn("umbra/README.md", archive.namelist())

    def test_upgrade_level_cap_and_failed_purchase_are_atomic(self):
        self.post("/api/run", self.run_payload(shards=1000))
        for level in range(1, 6):
            status, profile = self.post("/api/upgrade", {"kind": "power"})
            self.assertEqual(status, 200)
            self.assertEqual(profile["power"], level)
        _, body, _ = self.request("/api/profile")
        before = json.loads(body)
        self.assertEqual(before["shards"], 775)
        self.assertEqual(self.post("/api/upgrade", {"kind": "power"})[0], 409)
        _, body, _ = self.request("/api/profile")
        self.assertEqual(json.loads(body), before)

    def test_run_idempotency_and_upgrades(self):
        payload = self.run_payload()
        status, profile = self.post("/api/run", payload)
        self.assertEqual(status, 200)
        self.assertEqual((profile["runs"], profile["shards"], profile["bestRoom"]), (1, 40, 3))
        _, repeated = self.post("/api/run", payload)
        self.assertEqual(repeated, profile)
        status, _ = self.post("/api/run", self.run_payload(shards=41))
        self.assertEqual(status, 409)
        status, upgraded = self.post("/api/upgrade", {"kind": "vitality"})
        self.assertEqual(status, 200)
        self.assertEqual((upgraded["vitality"], upgraded["shards"]), (1, 25))
        status, _ = self.post("/api/upgrade", {"kind": "vitality"})
        self.assertEqual(status, 409)
        self.assertEqual(self.post("/api/upgrade", {"kind": "invalid"})[0], 400)
        self.assertEqual(self.post("/api/upgrade", {"kind": []})[0], 400)

    def test_input_validation(self):
        for bad in ({"shards": -1}, {"shards": True}, {"room": 7}, {"room": False},
                    {"kills": 1.5}, {"duration": float("inf")}, {"duration": -1},
                    {"won": "false"}, {"id": "../oops"}, {"won": True, "room": 1}):
            self.assertEqual(self.post("/api/run", self.run_payload(**bad))[0], 400, bad)
        self.assertEqual(self.post("/api/run", [1, 2])[0], 400)
        self.assertEqual(self.post("/api/upgrade", [])[0], 400)
        self.assertEqual(self.request("/api/run", self.run_payload(), {"Content-Type": "text/plain"})[0], 415)
        self.assertEqual(self.request("/api/run", self.run_payload(), {"Content-Type": "application/json", "Origin": "https://evil.example"})[0], 403)

    def test_concurrent_duplicate_run_and_persistence(self):
        payload = self.run_payload(won=True, room=6, duration=120)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            results = list(executor.map(lambda _: self.post("/api/run", payload), range(8)))
        self.assertTrue(all(status == 200 for status, _ in results))
        status, body, _ = self.request("/api/profile")
        profile = json.loads(body)
        self.assertEqual((profile["runs"], profile["wins"], profile["shards"]), (1, 1, 40))
        self.assertEqual(profile["bestTime"], 120)
        self.post("/api/run", self.run_payload(id="another_123456", won=True, room=6, duration=80))
        from server import Store
        stored = Store(self.root / "data" / "umbra.sqlite3").profile()
        self.assertEqual((stored["bestTime"], stored["runs"]), (80, 2))


if __name__ == "__main__":
    unittest.main()
