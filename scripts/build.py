"""Build static assets, verify design tokens, and bundle a runnable source download."""
from pathlib import Path
import json
import re
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DIST = ROOT / "dist"
ASSETS = DIST / "assets"
ASSETS.mkdir(parents=True, exist_ok=True)
design = (ROOT / "DESIGN.md").read_text()
css = (ROOT / "src/styles.css").read_text()
colors = re.search(r"^colors:\n(.*?)^typography:", design, re.M | re.S).group(1)
for name, value in re.findall(r'  ([\w-]+): "(#[0-9a-fA-F]+)"', colors):
    if f"--color-{name}:{value}" not in css:
        raise SystemExit(f"Design token drift: {name} ({value})")
shutil.copy2(ROOT / "index.html", DIST / "index.html")
for name in ("styles.css", "favicon.svg"):
    shutil.copy2(ROOT / "src" / name, ASSETS / name)

def unity_files():
    excluded = {"Library", "Temp", "obj", "bin", "Logs", "UserSettings", "Builds", ".vs", "__pycache__"}
    return sorted(p for p in (ROOT / "unity").rglob("*") if p.is_file() and not excluded.intersection(p.relative_to(ROOT / "unity").parts) and not p.name.endswith((".sln", ".csproj.user")))

unity_archive = DIST / "umbra-unity.zip"
with zipfile.ZipFile(unity_archive, "w", zipfile.ZIP_DEFLATED) as bundle:
    for path in unity_files():
        bundle.write(path, "umbra-unity/" + str(path.relative_to(ROOT / "unity")))

archive = DIST / "umbra-source.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
    includes = ["package.json", "package-lock.json", "tsconfig.json", "index.html", "server.py", "README.md", "DESIGN.md", ".gitignore"]
    files = [ROOT / item for item in includes]
    for folder in ("src", "scripts", "tests"):
        files.extend(p for p in (ROOT / folder).rglob("*") if p.is_file() and "__pycache__" not in p.parts)
    files.extend(unity_files())
    files.extend(p for p in DIST.rglob("*") if p.is_file() and p not in (archive, unity_archive))
    for path in sorted(files):
        if path.exists():
            bundle.write(path, "umbra/" + str(path.relative_to(ROOT)))
print(json.dumps({"build": "ok", "tokens": "verified", "source_archive": str(archive), "bytes": archive.stat().st_size, "unity_archive": str(unity_archive), "unity_bytes": unity_archive.stat().st_size}))
