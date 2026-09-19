"""Deploys the compiled Advance Steel plugin into the ApplicationPlugins bundle directory."""

import os
import shutil
from pathlib import Path


def deploy():
    repo_root = Path(__file__).resolve().parents[1]
    bin_release = repo_root / "src" / "as_plugin" / "bin" / "Release" / "net8.0-windows"
    bundle_source = repo_root / "src" / "as_plugin" / "EMV-AdvanceSteel.bundle"
    bundle_release = bundle_source / "Contents" / "Release"

    app_data = Path(os.environ.get("APPDATA", ""))
    target_bundle = app_data / "Autodesk" / "ApplicationPlugins" / "EMV-AdvanceSteel.bundle"
    target_release = target_bundle / "Contents" / "Release"

    program_data = Path(os.environ.get("ProgramData", "C:/ProgramData"))
    pdata_bundle = program_data / "Autodesk" / "ApplicationPlugins" / "EMV-AdvanceSteel.bundle"
    pdata_release = pdata_bundle / "Contents" / "Release"

    print(f"Deploying from: {bin_release}")
    bundle_release.mkdir(parents=True, exist_ok=True)
    target_release.mkdir(parents=True, exist_ok=True)
    pdata_release.mkdir(parents=True, exist_ok=True)

    # Copy PackageContents.xml
    xml_src = bundle_source / "PackageContents.xml"
    shutil.copy2(xml_src, target_bundle / "PackageContents.xml")
    shutil.copy2(xml_src, pdata_bundle / "PackageContents.xml")
    print(f"Copied PackageContents.xml to {target_bundle} and {pdata_bundle}")

    # Copy all binaries
    for file in bin_release.glob("*"):
        if file.is_file():
            shutil.copy2(file, bundle_release / file.name)
            shutil.copy2(file, target_release / file.name)
            shutil.copy2(file, pdata_release / file.name)
            print(f"  Copied {file.name}")

    print("\n[OK] Bundle deployed successfully to APPDATA and ProgramData:")
    print(f"  APPDATA: {target_bundle}")
    print(f"  ProgramData: {pdata_bundle}")
    print(f"  Contents: {len(list(target_release.glob('*')))} files")


if __name__ == "__main__":
    deploy()
