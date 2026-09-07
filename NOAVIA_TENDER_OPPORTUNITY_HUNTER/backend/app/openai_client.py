import json
from typing import Any

from openai import OpenAI

from .config import get_settings


class OpenAIResponsesClient:
    """OpenRouter-backed client using its OpenAI-compatible Responses API."""

    def __init__(self, client: OpenAI | None = None):
        settings = get_settings()
        headers = {}
        if settings.openrouter_site_url:
            headers["HTTP-Referer"] = settings.openrouter_site_url
        if settings.openrouter_app_name:
            headers["X-Title"] = settings.openrouter_app_name
        self.client = client or (
            OpenAI(
                api_key=settings.openrouter_api_key,
                base_url=settings.openrouter_base_url,
                default_headers=headers or None,
            )
            if settings.openrouter_api_key
            else None
        )
        self.model = settings.openrouter_model

    def extract_tender(self, text: str) -> dict[str, Any]:
        if self.client is None:
            raise RuntimeError("OPENROUTER_API_KEY is not configured")
        response = self.client.responses.create(
            model=self.model,
            input=[
                {
                    "role": "system",
                    "content": "Extract tender facts. Return null for unknown values.",
                },
                {"role": "user", "content": text},
            ],
            text={
                "format": {
                    "type": "json_schema",
                    "name": "tender_facts",
                    "strict": True,
                    "schema": {
                        "type": "object",
                        "properties": {
                            "title": {"type": ["string", "null"]},
                            "buyer": {"type": ["string", "null"]},
                            "deadline": {"type": ["string", "null"]},
                            "summary": {"type": ["string", "null"]},
                        },
                        "required": ["title", "buyer", "deadline", "summary"],
                        "additionalProperties": False,
                    },
                }
            },
            store=False,
        )
        return json.loads(response.output_text)
