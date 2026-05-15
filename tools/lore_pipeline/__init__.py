"""AI Tavern offline lore-extraction pipeline (Phase 3, T3A.6).

Map-reduce over 《射雕英雄傳》 (shediao.txt, 繁體, 40 回) using the official
xAI SDK (xai-sdk). Emits world.json / bios.json / dossiers.json that the
Unity-side LoreImporter (T3A.5) reads. Pure offline tool; the USER runs it
(it costs xAI API tokens). See README.md.
"""

__all__ = ["config", "chunker", "xai_client", "extract", "run", "validate"]
