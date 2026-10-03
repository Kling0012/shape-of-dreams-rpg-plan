"""Round-trip and safety regression coverage for split source asset extraction."""
import importlib.util
import io
import json
from pathlib import Path
import stat
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('materialize_mob_assets', ROOT / 'tools/materialize_mob_assets.py')
assets = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(assets)
PACK = ROOT / 'assets/dreamborne'


def fixture(root, name='model/mesh.fbx', data=b'example', symlink=False, extra=False):
    root.mkdir()
    entry = {'path': name, 'bytes': len(data), 'sha256': assets.sha256(data)}
    manifest = json.dumps({'files': [entry]}).encode()
    (root / 'manifest.json').write_bytes(manifest)
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as archive:
        info = zipfile.ZipInfo(name); info.create_system = 3
        info.external_attr = ((stat.S_IFLNK if symlink else stat.S_IFREG) | 0o644) << 16
        archive.writestr(info, data)
        if extra:
            archive.writestr('extra.txt', b'unexpected')
    raw = stream.getvalue()
    (root / 'source-pack.zip.001').write_bytes(raw)
    metadata = {'schemaVersion': 1, 'format': 'zip', 'archiveBytes': len(raw),
                'archiveSha256': assets.sha256(raw), 'manifestSha256': assets.sha256(manifest),
                'fileCount': 1, 'uncompressedBytes': len(data), 'chunkBytes': 65536,
                'chunks': [{'path': 'source-pack.zip.001', 'bytes': len(raw), 'sha256': assets.sha256(raw)}]}
    (root / 'source-archive.json').write_text(json.dumps(metadata))


class MaterializationTests(unittest.TestCase):
    def test_real_archive_round_trip(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / 'output'
            self.assertEqual(63, assets.materialize(PACK, root))
            self.assertEqual(63, assets.materialize(PACK, root, verify_only=True))
            manifest = json.loads((PACK / 'manifest.json').read_text())
            self.assertEqual(63, sum(path.is_file() for path in root.rglob('*')))
            for entry in manifest['files']:
                self.assertEqual((PACK / entry['path']).read_bytes(), (root / entry['path']).read_bytes())

    def test_missing_source_verify_only_does_not_write(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack')
            with self.assertRaisesRegex(assets.ArchiveError, 'missing'):
                assets.materialize(root / 'pack', root / 'out', verify_only=True)
            self.assertFalse((root / 'out').exists())

    def test_changed_existing_source_is_preserved(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack')
            target = root / 'out/model/mesh.fbx'; target.parent.mkdir(parents=True); target.write_bytes(b'local edit')
            with self.assertRaisesRegex(assets.ArchiveError, 'Existing source differs'):
                assets.materialize(root / 'pack', root / 'out')
            self.assertEqual(b'local edit', target.read_bytes())

    def test_corrupt_chunk_rejected_before_writes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack')
            (root / 'pack/source-pack.zip.001').write_bytes(b'corrupt')
            with self.assertRaisesRegex(assets.ArchiveError, 'mismatch'):
                assets.materialize(root / 'pack', root / 'out')
            self.assertFalse((root / 'out').exists())

    def test_traversal_and_absolute_names_rejected(self):
        for name in ('../escape', '/escape', 'nested/../../escape', 'nested\\escape', 'C:/escape', './escape'):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp); fixture(root / 'pack', name=name)
                with self.assertRaisesRegex(assets.ArchiveError, 'Unsafe archive path'):
                    assets.materialize(root / 'pack', root / 'out')
                self.assertFalse((root / 'out').exists())

    def test_archive_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack', symlink=True)
            with self.assertRaisesRegex(assets.ArchiveError, 'Non-regular'):
                assets.materialize(root / 'pack', root / 'out')

    def test_destination_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack')
            (root / 'outside').mkdir(); (root / 'out').symlink_to(root / 'outside', target_is_directory=True)
            with self.assertRaisesRegex(assets.ArchiveError, 'symlink'):
                assets.materialize(root / 'pack', root / 'out')
            self.assertEqual([], list((root / 'outside').iterdir()))

    def test_unlisted_zip_entry_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack', extra=True)
            with self.assertRaisesRegex(assets.ArchiveError, 'exact source manifest'):
                assets.materialize(root / 'pack', root / 'out')

    def test_manifest_tamper_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); fixture(root / 'pack')
            with (root / 'pack/manifest.json').open('ab') as stream:
                stream.write(b' ')
            with self.assertRaisesRegex(assets.ArchiveError, 'manifest SHA-256'):
                assets.materialize(root / 'pack', root / 'out')


if __name__ == '__main__':
    unittest.main()
