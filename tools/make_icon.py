from PIL import Image, ImageDraw, ImageFont
import io
import os
import struct

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 圆角方块 + 轻微渐变
top = (18, 139, 155)
bottom = (11, 101, 114)
grad = Image.new("RGB", (1, S))
for y in range(S):
    t = y / (S - 1)
    grad.putpixel((0, y), tuple(int(top[i] * (1 - t) + bottom[i] * t) for i in range(3)))
grad = grad.resize((S, S))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, S - 1, S - 1), radius=210, fill=255)
img.paste(grad, (0, 0), mask)

# 飞机符号
glyph = "\u2708"
font = None
for path in (r"C:\Windows\Fonts\seguisym.ttf", r"C:\Windows\Fonts\seguiemj.ttf", r"C:\Windows\Fonts\arial.ttf"):
    if os.path.exists(path):
        try:
            font = ImageFont.truetype(path, 720)
            break
        except Exception:
            font = None

plane = Image.new("RGBA", (S, S), (0, 0, 0, 0))
pd = ImageDraw.Draw(plane)
if font is not None:
    bbox = pd.textbbox((0, 0), glyph, font=font)
    w = bbox[2] - bbox[0]
    h = bbox[3] - bbox[1]
    pd.text(((S - w) / 2 - bbox[0], (S - h) / 2 - bbox[1]), glyph, font=font, fill=(255, 255, 255, 255))
plane = plane.rotate(-45, resample=Image.BICUBIC, center=(S / 2, S / 2))

# 如果字体没有该字形，退化为手绘飞机
alpha = plane.getchannel("A")
if alpha.getbbox() is None:
    plane = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    pd = ImageDraw.Draw(plane)
    cx = S // 2
    pd.rounded_rectangle((cx - 34, 150, cx + 34, 880), radius=40, fill=(255, 255, 255, 255))
    pd.polygon([(cx - 36, 520), (cx - 330, 640), (cx - 330, 720), (cx - 36, 660)], fill=(255, 255, 255, 255))
    pd.polygon([(cx + 36, 520), (cx + 330, 640), (cx + 330, 720), (cx + 36, 660)], fill=(255, 255, 255, 255))
    pd.polygon([(cx - 36, 190), (cx - 170, 260), (cx - 170, 320), (cx - 36, 290)], fill=(255, 255, 255, 255))
    pd.polygon([(cx + 36, 190), (cx + 170, 260), (cx + 170, 320), (cx + 36, 290)], fill=(255, 255, 255, 255))
    plane = plane.rotate(-45, resample=Image.BICUBIC, center=(S / 2, S / 2))

img.alpha_composite(plane)
img = img.resize((256, 256), Image.LANCZOS)

out = r"D:\AI\vibe\ZHCC\src\ZhccCargoBoard.App\Assets\app.ico"
os.makedirs(os.path.dirname(out), exist_ok=True)


def bmp_frame(image):
    """把一帧写成 ICO 里的 BMP(DIB) 数据：BITMAPINFOHEADER + BGRA + AND 掩码。"""
    w, h = image.size
    px = image.load()
    rows = []
    for y in range(h - 1, -1, -1):
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor = b"".join(rows)
    mask_stride = ((w + 31) // 32) * 4
    and_mask = bytes(mask_stride * h)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(xor), 0, 0, 0, 0)
    return header + xor + and_mask


def png_frame(image):
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    return buf.getvalue()


# 关键：小尺寸必须是 BMP 帧。PNG 帧的 ICO 在 System.Drawing.Icon 里拿不到 HICON，
# 托盘图标会因此注册失败（Shell_NotifyIcon 返回错误）；256 用 PNG 保持体积。
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = []
for size in sizes:
    frame_img = img.resize((size, size), Image.LANCZOS)
    data = png_frame(frame_img) if size >= 256 else bmp_frame(frame_img)
    frames.append((size, data))

header = struct.pack("<HHH", 0, 1, len(frames))
offset = 6 + 16 * len(frames)
entries = b""
blobs = b""
for size, data in frames:
    dim = 0 if size >= 256 else size
    entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
    blobs += data
    offset += len(data)

with open(out, "wb") as f:
    f.write(header + entries + blobs)
print("saved", out, os.path.getsize(out))
