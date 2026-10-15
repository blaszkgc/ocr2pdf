"""Independent PDF checks. Requires PyMuPDF and Pillow in the verification environment."""
import io
from pathlib import Path
import fitz
from PIL import Image, ImageChops

root = Path(__file__).resolve().parents[1] / 'artifacts' / 'verification'
for filename, expected in [('letter.pdf', ['ALPHA', 'BETA']), ('a4.pdf', ['BETA', 'ALPHA'])]:
    with fitz.open(root / filename) as document:
        for index, page in enumerate(document):
            assert expected[index] in page.get_text(), 'OCR text missing from PDF'
            images = page.get_images()
            assert len(images) == 1, 'Expected one visible original image'
            embedded = document.extract_image(images[0][0])
            source_index = index if filename == 'letter.pdf' else 1-index
            original = Image.open(root / f'source-{source_index}.png').convert('RGB')
            actual = Image.open(io.BytesIO(embedded['image'])).convert('RGB')
            assert actual.size == original.size, 'Image downsampled'
            assert ImageChops.difference(actual, original).getbbox() is None, 'Image pixels changed'
            rect = page.get_image_rects(images[0][0])[0]
            assert all(rect.contains(fitz.Rect(*word[:4])) for word in page.get_text('words')), 'Text outside image'
            page.get_pixmap().save(root / f'{filename[:-4]}-{index}.png')
            print(f'{filename}, page {index+1}: searchable text, original pixels, OCR bounds passed')
