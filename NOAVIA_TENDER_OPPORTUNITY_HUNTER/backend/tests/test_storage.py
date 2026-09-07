from app.storage import ContentAddressedStorage


def test_content_addressed_storage_round_trip(tmp_path):
    storage = ContentAddressedStorage(tmp_path)
    stored = storage.put(b"hello noavia")
    assert storage.exists(stored.sha256)
    assert storage.get(stored.sha256) == b"hello noavia"
    assert stored.key.endswith(stored.sha256)
