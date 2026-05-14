VARIANT_BASE_PROMPT: str = (
    "photorealistic cinematic portrait of a Han Chinese character with East Asian "
    "facial features (almond eyes) in ancient Chinese wuxia attire, soft natural "
    "lighting, neutral background, head-and-shoulders bust, 35mm photograph, "
    "shallow depth of field, no anime, no illustration"
)

SHARED_STYLE_PROMPT: str = (
    "photorealistic cinematic portrait of a Han Chinese character with East Asian "
    "facial features (almond eyes) in ancient Chinese wuxia attire. Faithfully "
    "preserve the source image's gender, apparent age, hair color, hair style, "
    "beard or facial hair, headwear and accessories, clothing colors and style — "
    "only the rendering style changes from illustration to photoreal, not the "
    "identifying details. Do NOT mature or age up the character: if the source "
    "depicts a teenager or young unmarried character (typical for a wuxia "
    "protagonist), render them as a youthful 18-year-old with smooth fresh skin, "
    "minimal or no visible makeup, soft features, and age-appropriate styling "
    "(loose hair with simple ribbons, double buns, or a simple ponytail/braid for "
    "young women; clean-shaven or beardless for young men). Do NOT add, "
    "embellish, or extend any element that is not present in the source: no "
    "extra veils, sashes, scarves, ribbons, animals, "
    "ornaments, or background props. If the source shows narrow ribbons, keep them "
    "narrow; if the source has a simple braid, keep it a simple braid. Render only "
    "what is in the source, photorealistically. Soft natural lighting, neutral "
    "background, head-and-shoulders bust, 35mm photograph, shallow depth of field, "
    "no anime, no illustration"
)

PROMPT_OVERRIDES: dict[int, str] = {
    6: (
        "photorealistic cinematic portrait of an elderly Han Chinese Buddhist nun, "
        "bald, with a deeply lined weathered face showing decades of monastic "
        "discipline and quiet gravitas, wearing simple gray ancient-Chinese monastic "
        "robes, soft natural lighting, neutral background, head-and-shoulders bust, "
        "35mm photograph, shallow depth of field, no anime, no illustration"
    ),
}


def prompt_for(id: int) -> str:
    if id in PROMPT_OVERRIDES:
        return PROMPT_OVERRIDES[id]
    return SHARED_STYLE_PROMPT
