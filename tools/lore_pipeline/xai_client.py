"""Thin wrapper over the official xai-sdk chat API.

USER STANDING RULE (non-negotiable): xAI work in Python uses the official
`xai-sdk` package. NOT the `openai` package, NOT raw `requests`/HTTP.
This module is the ONLY place that talks to xAI; nothing else imports a
network library.

`--dry-run` (dry=True) must work WITHOUT an API key and WITHOUT xai-sdk
installed: it prints the prompts + a token estimate and returns "" so the
user can preview cost before spending a single token.
"""

import os
import sys
import time

from . import config

# Rough chars-per-token for mixed CJK+ASCII. Grok tokenizes CJK denser
# than this; this is a deliberately conservative (high) estimate so the
# previewed cost is an upper bound, not a surprise.
_CHARS_PER_TOKEN = 2.0


def estimate_tokens(*texts: str) -> int:
    return int(sum(len(t) for t in texts) / _CHARS_PER_TOKEN) + 1


class XaiError(RuntimeError):
    """Actionable, no-stack-trace error for the CLI to print and exit."""


class XaiClient:
    """Lazily constructs the xai-sdk client. In dry mode it is never
    constructed, so neither the SDK nor the API key is required to preview.

    Tracks cumulative estimated input/output tokens for the cost summary.
    """

    def __init__(self, dry: bool = False, verbose: bool = True):
        self.dry = dry
        self.verbose = verbose
        self._client = None
        self.in_tokens = 0
        self.out_tokens = 0
        self.calls = 0

    # -- lazy real client -------------------------------------------------

    def _ensure_client(self):
        if self._client is not None:
            return self._client
        api_key = os.environ.get("XAI_API_KEY")
        if not api_key:
            raise XaiError(
                "XAI_API_KEY is not set. Export your xAI API key before a "
                "real run:\n"
                "  PowerShell:  $env:XAI_API_KEY = \"xai-...\"\n"
                "  bash:        export XAI_API_KEY=xai-...\n"
                "Or preview without spending tokens:  "
                "python -m lore_pipeline.run --dry-run"
            )
        try:
            # Official xAI SDK only. (pip install xai-sdk)
            from xai_sdk import Client
            from xai_sdk.chat import system as sys_msg, user as user_msg
        except ImportError as exc:
            raise XaiError(
                "The official 'xai-sdk' package is not installed (this "
                "pipeline does NOT use openai/requests by design — user "
                "standing rule). Install it:\n"
                "  pip install -r tools/lore_pipeline/requirements.txt\n"
                f"(import error: {exc})"
            )
        self._client = Client(api_key=api_key)
        self._sys_msg = sys_msg
        self._user_msg = user_msg
        return self._client

    # -- the one call other modules use -----------------------------------

    def complete(self, system: str, user: str, *,
                 temperature: float = None,
                 max_tokens: int = None,
                 label: str = "") -> str:
        """One chat completion. Returns the assistant text.

        dry=True: prints the prompt + token estimate, returns "" (no
        network, no SDK, no key needed). Real mode: retries transient
        errors with exponential backoff.
        """
        temperature = config.TEMPERATURE if temperature is None else temperature
        max_tokens = (config.MAX_TOKENS_EXTRACT
                      if max_tokens is None else max_tokens)
        est_in = estimate_tokens(system, user)
        self.in_tokens += est_in
        self.calls += 1

        if self.dry:
            self.out_tokens += max_tokens  # assume worst case for preview
            if self.verbose:
                print(f"\n{'=' * 72}\n[DRY] call #{self.calls} "
                      f"{label}  (model={config.MODEL}, T={temperature}, "
                      f"~{est_in} in / ≤{max_tokens} out tok)")
                print(f"--- system ---\n{system}")
                print(f"--- user ---\n{user[:1200]}"
                      + (" …[truncated]" if len(user) > 1200 else ""))
            return ""

        client = self._ensure_client()
        last_exc = None
        for attempt in range(config.MAX_RETRIES):
            try:
                chat = client.chat.create(
                    model=config.MODEL,
                    temperature=temperature,
                    max_tokens=max_tokens,
                )
                chat.append(self._sys_msg(system))
                chat.append(self._user_msg(user))
                response = chat.sample()
                text = (response.content or "").strip()
                self.out_tokens += estimate_tokens(text)
                if self.verbose:
                    print(f"[xai] call #{self.calls} {label} ok "
                          f"(~{est_in} in / ~{estimate_tokens(text)} out tok)")
                return text
            except XaiError:
                raise
            except Exception as exc:  # noqa: BLE001 - transient API/network
                last_exc = exc
                wait = config.RETRY_BASE_SECONDS * (2 ** attempt)
                print(f"[xai] call #{self.calls} {label} attempt "
                      f"{attempt + 1}/{config.MAX_RETRIES} failed: {exc} "
                      f"— retrying in {wait:.0f}s", file=sys.stderr)
                time.sleep(wait)
        raise XaiError(
            f"xAI call '{label}' failed after {config.MAX_RETRIES} attempts. "
            f"Last error: {last_exc}. Re-run is idempotent — fix the cause "
            f"(network / key / model id '{config.MODEL}') and run again."
        )

    # -- cost summary -----------------------------------------------------

    def cost_summary(self) -> str:
        return (f"{self.calls} call(s); ~{self.in_tokens:,} input tok, "
                f"~{self.out_tokens:,} output tok (estimated, "
                f"{'DRY — nothing spent' if self.dry else 'billed'}).")
