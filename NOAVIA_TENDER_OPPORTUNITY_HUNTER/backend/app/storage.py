import hashlib
import os
import uuid
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class StoredObject:
    sha256: str
    size_bytes: int
    key: str


class ContentAddressedStorage:
    """Small local blob store keyed by SHA-256, suitable for a single-node MVP."""

    def __init__(self, root: str | Path):
        self.root = Path(root).resolve()
        self.root.mkdir(parents=True, exist_ok=True)

    def _path_for(self, digest: str) -> Path:
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise ValueError("invalid SHA-256 digest")
        return self.root / digest[:2] / digest[2:4] / digest

    def put(self, content: bytes) -> StoredObject:
        digest = hashlib.sha256(content).hexdigest()
        path = self._path_for(digest)
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists():
            temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.part")
            with temporary.open("wb") as handle:
                handle.write(content)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, path)
        return StoredObject(digest, len(content), str(path.relative_to(self.root)))

    def get(self, digest: str) -> bytes:
        path = self._path_for(digest)
        return path.read_bytes()

    def exists(self, digest: str) -> bool:
        return self._path_for(digest).is_file()
