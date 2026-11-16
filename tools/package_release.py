"""Package the SDK output for the shared Dalamud plugin feed."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("--tag", required=True)
parser.add_argument("--changelog-file", type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
build = root / "Proteus/bin/Release/win-x64"
out = root / "artifacts"
out.mkdir(exist_ok=True)
package = out / "Proteus.zip"
with zipfile.ZipFile(build / "Proteus/latest.zip") as source:
    entries = source.namelist()
    manifest = json.loads(source.read("Proteus.json"))
    if args.tag != "v" + manifest["AssemblyVersion"] or manifest["InternalName"] != "Proteus":
        raise ValueError("SDK package version must match the planned release")
    binary = source.read("Proteus.dll")
    if binary != (build / "Proteus.dll").read_bytes() or b"Proteus.TestHarness" in binary or b"HarnessSuppressApplySignals" in binary:
        raise ValueError("Expected the release DLL with the local test harness excluded")
    if "images/icon.png" in entries and source.read("images/icon.png") != (root / "Proteus/images/icon.png").read_bytes():
        raise ValueError("Packaged icon differs from the fork icon")
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as dest:
        for name in entries:
            if name in {"LICENSE", "Proteus.json", "images/icon.png"}:
                continue
            dest.writestr(name, source.read(name))
        manifest["Changelog"] = args.changelog_file.read_text(encoding="utf-8-sig").strip()
        dest.writestr("Proteus.json", json.dumps(manifest, indent=2))
        dest.write(root / "LICENSE", "LICENSE")
        dest.write(root / "Proteus/images/icon.png", "images/icon.png")
url = f"https://github.com/kaldigo/proteus/releases/download/{args.tag}/{package.name}"
manifest.update(DownloadLinkInstall=url, DownloadLinkTesting=url, DownloadLinkUpdate=url)
(out / "pluginmaster.json").write_text(json.dumps([manifest], indent=2) + "\n", encoding="utf-8")
(out / "SHA256SUMS.txt").write_text(f"{hashlib.sha256(package.read_bytes()).hexdigest()}  {package.name}\n")
