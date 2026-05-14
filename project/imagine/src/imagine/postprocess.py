import io

from PIL import Image
import rembg

_SESSION = None


def _get_session():
    global _SESSION
    if _SESSION is None:
        _SESSION = rembg.new_session("u2net_human_seg")
    return _SESSION


def detect_extension(raw_bytes: bytes) -> str:
    return "jpg" if raw_bytes[:2] == b"\xff\xd8" else "png"


def to_final_png(raw_bytes: bytes) -> bytes:
    image = Image.open(io.BytesIO(raw_bytes))
    image = rembg.remove(image, session=_get_session())
    if image.mode != "RGBA":
        image = image.convert("RGBA")
    w, h = image.size
    if w != h:
        # xAI may return non-square images; the prompt requests head-and-shoulders
        # bust so the subject is roughly centered — center-crop preserves the face.
        s = min(w, h)
        left = (w - s) // 2
        top = (h - s) // 2
        image = image.crop((left, top, left + s, top + s))
    image = image.resize((384, 384), Image.Resampling.LANCZOS)
    image = _sharpen_alpha(image)
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


def _sharpen_alpha(rgba_image):
    # Kill rembg halo fog while preserving anti-aliased hair edges.
    r, g, b, alpha = rgba_image.split()
    def remap(v):
        if v < 64:
            return 0
        if v >= 192:
            return 255
        return int((v - 64) * 255 / 128)
    sharp = alpha.point(remap)
    return Image.merge("RGBA", (r, g, b, sharp))
