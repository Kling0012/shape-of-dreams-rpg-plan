#!/usr/bin/env python3
"""Verify and unpack the repository's split Dreamborne source archive (stdlib only).

Integrity is established by exact sizes, the exact entry set, deterministic ZIP
structure and safe-path checks recorded in the source manifest and archive
metadata. Hash-based verification is intentionally not used.
"""
from __future__ import annotations
import argparse
import io
import json
import os
import time
from pathlib import Path, PurePosixPath
import stat
import sys
import tempfile
import zipfile


class ArchiveError(ValueError):
    """The archive or destination did not satisfy the source manifest."""


def safe_target(root: Path, name: str) -> Path:
    if not isinstance(name, str) or not name or "\\" in name or ":" in name:
        raise ArchiveError(f"Unsafe archive path: {name!r}")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in name.split("/")):
        raise ArchiveError(f"Unsafe archive path: {name!r}")
    target = root
    for part in path.parts:
        target = target / part
        if target.is_symlink():
            raise ArchiveError(f"Refusing symlink destination: {name}")
    return target


def checked_bytes(path: Path, entry: dict) -> bytes:
    data = path.read_bytes()
    if len(data) != entry["bytes"]:
        raise ArchiveError(f"Size mismatch: {path.name}")
    return data


def replace_with_transient_retry(source: Path, target: Path, attempts: int = 10, delay: float = 0.1) -> None:
    """os.replace, tolerating a brief Windows sharing violation from scanners.

    A newly written file can be held open by antivirus/indexing for milliseconds;
    that lock is transient and unrelated to pack integrity. Give up and raise
    after a bounded number of attempts.
    """
    for attempt in range(attempts):
        try:
            os.replace(source, target)
            return
        except PermissionError:
            if attempt == attempts - 1:
                raise
            time.sleep(delay)


def materialize(asset_root: Path, destination: Path | None = None,
                verify_only: bool = False) -> int:
    asset_root = asset_root.resolve()
    destination = asset_root if destination is None else destination.absolute()
    # Check the destination before resolving it, so an existing symlink is never followed.
    relative_destination = destination.relative_to(destination.anchor).as_posix()
    safe_target(Path(destination.anchor), relative_destination)
    destination = destination.resolve()
    metadata = json.loads((asset_root / "source-archive.json").read_text(encoding="utf-8"))
    manifest = json.loads((asset_root / "manifest.json").read_text(encoding="utf-8"))
    if metadata.get("schemaVersion") != 1 or metadata.get("format") != "zip":
        raise ArchiveError("Unsupported source archive format")
    if not isinstance(manifest.get("contentVersion"), str) or not manifest["contentVersion"]:
        raise ArchiveError("Source manifest lacks a contentVersion string")
    entries = manifest["files"]
    expected = {entry["path"]: entry for entry in entries}
    if len(expected) != len(entries) or len(entries) != metadata["fileCount"]:
        raise ArchiveError("Duplicate or inconsistent source file count")
    if sum(entry["bytes"] for entry in entries) != metadata["uncompressedBytes"]:
        raise ArchiveError("Inconsistent uncompressed byte count")
    if not 0 < metadata["uncompressedBytes"] <= 512 * 1024 * 1024:
        raise ArchiveError("Uncompressed archive exceeds the supported size limit")
    for name, entry in expected.items():
        safe_target(destination, name)
        if not isinstance(entry["bytes"], int) or entry["bytes"] < 0:
            raise ArchiveError(f"Invalid source file size: {name}")
    chunks = metadata["chunks"]
    if not chunks or metadata["chunkBytes"] != 65536:
        raise ArchiveError("Invalid source archive chunk size")
    if not 0 < metadata["archiveBytes"] <= 100 * 1024 * 1024:
        raise ArchiveError("Compressed archive exceeds the supported size limit")
    if len(chunks) != (metadata["archiveBytes"] + 65535) // 65536:
        raise ArchiveError("Inconsistent archive chunk count")
    archive_parts = []
    for index, chunk in enumerate(chunks, 1):
        if chunk["path"] != f"source-pack.zip.{index:03d}":
            raise ArchiveError("Archive chunks are missing, reordered, or have unsafe names")
        expected_size = min(65536, metadata["archiveBytes"] - (index - 1) * 65536)
        if chunk["bytes"] != expected_size:
            raise ArchiveError(f"Inconsistent chunk length: {chunk['path']}")
        archive_parts.append(checked_bytes(safe_target(asset_root, chunk["path"]), chunk))
    archive_data = b"".join(archive_parts)
    if len(archive_data) != metadata["archiveBytes"]:
        raise ArchiveError("Combined archive size mismatch")
    payloads = {}
    with zipfile.ZipFile(io.BytesIO(archive_data)) as archive:
        infos = archive.infolist()
        if len(infos) != len(expected) or {info.filename for info in infos} != set(expected):
            raise ArchiveError("Archive entries differ from the exact source manifest")
        for info in infos:
            safe_target(destination, info.filename)
            mode = info.external_attr >> 16
            if info.is_dir() or stat.S_ISLNK(mode) or (stat.S_IFMT(mode) not in (0, stat.S_IFREG)):
                raise ArchiveError(f"Non-regular archive entry: {info.filename}")
            entry = expected[info.filename]
            if info.file_size != entry["bytes"]:
                raise ArchiveError(f"Archive entry size mismatch: {info.filename}")
            data = archive.read(info)
            if len(data) != entry["bytes"]:
                raise ArchiveError(f"Archive entry size mismatch: {info.filename}")
            payloads[info.filename] = data
    # Validate all archive bytes and existing destinations before creating or replacing anything.
    missing = []
    for name, entry in expected.items():
        target = safe_target(destination, name)
        if target.exists():
            if not target.is_file():
                raise ArchiveError(f"Destination is not a regular file: {name}")
            existing = target.read_bytes()
            if len(existing) != entry["bytes"]:
                raise ArchiveError(f"Existing source differs; preserve or remove it before retrying: {name}")
        else:
            missing.append(name)
    if verify_only and missing:
        raise ArchiveError(f"{len(missing)} source files are missing; run without --verify-only")
    for name in missing:
        target = safe_target(destination, name)
        target.parent.mkdir(parents=True, exist_ok=True)
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(dir=target.parent, prefix=".mob-", delete=False) as stream:
                temporary = Path(stream.name)
                stream.write(payloads[name])
            safe_target(destination, name)
            replace_with_transient_retry(temporary, target)
        finally:
            if temporary is not None and temporary.exists():
                try:
                    temporary.unlink()
                except OSError:
                    # Cleanup must never mask the original failure; a locked
                    # temporary file is harmless clutter next to the target.
                    pass
    return len(expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--asset-root", type=Path,
                        default=Path(__file__).resolve().parents[1] / "assets" / "dreamborne")
    parser.add_argument("--destination", type=Path,
                        help="Optional extraction directory; defaults to the source asset directory")
    parser.add_argument("--verify-only", action="store_true",
                        help="Verify both archive and materialized files without writing")
    args = parser.parse_args()
    try:
        count = materialize(args.asset_root, args.destination, args.verify_only)
    except (ArchiveError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        print(f"MOB source archive: {error}", file=sys.stderr)
        return 1
    print(f"Verified {count} Dreamborne source files" + (" (read-only)" if args.verify_only else " and materialized missing files"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
