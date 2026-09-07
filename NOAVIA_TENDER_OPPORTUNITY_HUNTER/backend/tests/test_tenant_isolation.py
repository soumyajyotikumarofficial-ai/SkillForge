from types import SimpleNamespace
from uuid import UUID

from fastapi.testclient import TestClient

from app.db import get_db
from app.main import app


ORG_B = UUID("00000000-0000-0000-0000-000000000002")
OPPORTUNITY_A = UUID("10000000-0000-0000-0000-000000000001")


class TenantScopedDB:
    def get(self, model, key):
        return SimpleNamespace(id=key, name="Organization B")

    def scalar(self, statement):
        rendered = str(statement)
        assert "opportunities.organization_id" in rendered
        return None


def test_organization_b_cannot_read_organization_a_opportunity():
    db = TenantScopedDB()
    app.dependency_overrides[get_db] = lambda: db
    try:
        response = TestClient(app).get(
            f"/api/opportunities/{OPPORTUNITY_A}",
            headers={"X-Organization-Id": str(ORG_B)},
        )
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 404
