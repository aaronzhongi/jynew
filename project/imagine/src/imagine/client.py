import base64
import os
import pathlib

import dotenv
import xai_sdk

dotenv.load_dotenv()

_client: xai_sdk.Client | None = None


def _get_client() -> xai_sdk.Client:
    global _client
    if _client is None:
        api_key = os.environ.get("XAI_API_KEY")
        if not api_key:
            raise RuntimeError("XAI_API_KEY not set in environment")
        _client = xai_sdk.Client(api_key=api_key)
    return _client


def edit_image(image_path: pathlib.Path, prompt: str):
    raw = image_path.read_bytes()
    b64 = base64.b64encode(raw).decode()
    data_uri = f"data:image/png;base64,{b64}"
    return _get_client().image.sample(
        prompt=prompt,
        model="grok-imagine-image-quality",
        image_url=data_uri,
    )
