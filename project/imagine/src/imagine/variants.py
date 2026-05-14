AGE_UP_SUFFIX: str = (
    "Age the character from a teenager (around 18 years old) to a mature adult "
    "in their mid-30s (around 35 years old). Strictly preserve the source's "
    "gender — if the source is female, render as a 35-year-old woman; if male, "
    "as a 35-year-old man. Do NOT change gender. Subtle aging only: slightly "
    "more mature and defined facial features, very faint early signs of life "
    "experience (light laugh lines), possibly modest beard growth ONLY if the "
    "source is male. Hair color stays the same as the source — NOT gray, NOT "
    "white. NOT elderly, NOT weathered, NOT stooped. Keep the same facial "
    "structure, eye shape, and distinguishing features."
)

MAKEOVER_SUFFIX: str = (
    "With more elaborate makeup, more intricate hair styling (ornate hairpins, "
    "polished court look), but same facial structure, same age, same identity. "
    "NOT a younger version."
)

FEMINIZE_SUFFIX: str = (
    "Feminized appearance — softer features, longer styled hair, ancient-Chinese "
    "women's wuxia attire, but keep the same facial identity, eye shape, and "
    "distinguishing features. (Reflects the character's in-novel transformation "
    "from a martial art with feminizing side-effects.)"
)

VARIANTS: dict[str, tuple[int, str]] = {
    "2old":  (2,  AGE_UP_SUFFIX),
    "17old": (17, AGE_UP_SUFFIX),
    "47old": (47, AGE_UP_SUFFIX),
    "56old": (56, AGE_UP_SUFFIX),
    "59old": (59, AGE_UP_SUFFIX),
    "63old": (63, AGE_UP_SUFFIX),
    "25new": (25, MAKEOVER_SUFFIX),
    "27a":   (27, FEMINIZE_SUFFIX),
}


def build_variant_prompt(name: str) -> str:
    if name not in VARIANTS:
        raise KeyError(f"unknown variant: {name}")
    _, suffix = VARIANTS[name]
    from imagine.prompts import VARIANT_BASE_PROMPT
    return f"{VARIANT_BASE_PROMPT} {suffix}"
