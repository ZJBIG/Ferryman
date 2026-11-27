"""Package the Windows and WebGL players produced by PrototypeValidation."""

import argparse
from base64 import b64encode
import gzip
import os
from pathlib import Path
import re
import shutil
import subprocess
from tempfile import TemporaryDirectory
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / ".local"
LICENSE = ROOT / "Assets/Ferryman/Resources/Voyage/NotoSansSC-OFL.txt"


def package_windows(output):
    source = WORK / "build-prototype"
    required = {"Ferryman.exe", "UnityPlayer.dll", "Ferryman_Data/boot.config"}
    compiler = Path(os.environ["SystemRoot"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    with TemporaryDirectory(dir=output.parent) as directory:
        package = Path(directory) / output.name
        subprocess.run(
            [
                compiler, "/nologo", "/target:winexe", "/platform:x64", "/optimize+",
                "/reference:System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll",
                f"/out:{package}", Path(__file__).with_name("PortableLauncher.cs"),
            ],
            check=True,
        )
        with ZipFile(package, "a", compression=ZIP_DEFLATED, compresslevel=9) as game:
            for file in sorted(source.rglob("*")):
                relative = file.relative_to(source)
                if (file.is_file() and file.suffix.lower() not in {".pdb", ".mdb"}
                        and not any("DoNotShip" in part or "DontShipItWithYourGame" in part
                                    for part in relative.parts)):
                    game.write(file, relative.as_posix())
            game.write(LICENSE, LICENSE.name)
        with ZipFile(package) as game:
            if not required.issubset(game.namelist()) or game.testzip():
                raise ValueError("Windows player is incomplete or corrupt.")
        shutil.move(package, output)


def package_web(output):
    build = WORK / "build-web" / "Build"
    if any(file.is_file() for file in (build.parent / "StreamingAssets").rglob("*")):
        raise ValueError("The standalone HTML cannot load external StreamingAssets.")
    loader = (build / "build-web.loader.js").read_text(encoding="utf-8")
    if re.search(r"</script", loader, re.IGNORECASE):
        raise ValueError("Unity loader contains a script end tag.")
    with TemporaryDirectory(dir=output.parent) as directory:
        page = Path(directory) / output.name
        with page.open("w", encoding="utf-8", newline="\n") as html:
            html.write("""<!doctype html>
<html lang="zh-CN">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Ferryman · 摆渡人</title>
<style>
html,body{height:100%;margin:0;background:#181619;color:#fff;font:16px system-ui}
body{display:grid;place-items:center}
main{position:relative;width:min(100vw,177.78vh);aspect-ratio:16/9}
canvas{display:block;width:100%;height:100%;background:#231f20}
#status{position:absolute;top:45%;width:100%;text-align:center}
button{position:absolute;right:8px;bottom:8px}
</style>
<main>
<canvas id="unity-canvas" width="1280" height="720" tabindex="-1"></canvas>
<div id="status" role="status">正在加载…</div>
<button id="fullscreen" hidden>全屏</button>
</main>
""")
            html.write(f"<script>{loader}</script>\n")
            for asset in ("data", "framework.js", "wasm"):
                payload = gzip.compress((build / f"build-web.{asset}").read_bytes(), compresslevel=9, mtime=0)
                html.write(f'<script type="application/octet-stream" id="{asset}">')
                html.write(b64encode(payload).decode("ascii"))
                html.write("</script>\n")
            html.write("""<script>
async function unpack(id, type) {
    const element = document.getElementById(id);
    const encoded = atob(element.textContent);
    element.remove();
    const bytes = new Uint8Array(encoded.length);
    for (let index = 0; index < encoded.length; index++) bytes[index] = encoded.charCodeAt(index);
    const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"));
    const blob = await new Response(stream, { headers: { "Content-Type": type } }).blob();
    return URL.createObjectURL(blob);
}

async function start() {
    const status = document.getElementById("status");
    const urls = [];
    try {
        urls.push(await unpack("data", "application/octet-stream"));
        urls.push(await unpack("framework.js", "application/javascript"));
        urls.push(await unpack("wasm", "application/wasm"));
        const game = await createUnityInstance(document.getElementById("unity-canvas"), {
            dataUrl: urls[0],
            frameworkUrl: urls[1],
            codeUrl: urls[2],
            companyName: "DefaultCompany",
            productName: "Ferryman",
            productVersion: "1.0",
            cacheControl: () => "no-store",
            autoSyncPersistentDataPath: true,
            showBanner: (message, type) => {
                if (type === "error") status.textContent = message;
            }
        }, progress => { status.textContent = "正在加载 " + Math.round(progress * 100) + "%"; });
        status.hidden = true;
        const button = document.getElementById("fullscreen");
        button.hidden = false;
        button.onclick = () => game.SetFullscreen(1);
        document.documentElement.dataset.ready = "true";
    } catch (error) {
        status.textContent = "游戏加载失败。请使用最新版 Edge 或 Chrome。 " + error;
        console.error(error);
    } finally {
        urls.forEach(url => URL.revokeObjectURL(url));
    }
}
start();
</script>
""")
            html.write(f"<!--\n{LICENSE.read_text(encoding='utf-8')}\n-->\n</html>\n")
        shutil.move(page, output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", choices=("windows", "web", "all"))
    target = parser.parse_args().target
    release = WORK / "portable-release"
    release.mkdir(exist_ok=True)
    if target in ("windows", "all"):
        package_windows(release / "Ferryman-Windows-x64.exe")
    if target in ("web", "all"):
        package_web(release / "Ferryman-WebGL.html")
