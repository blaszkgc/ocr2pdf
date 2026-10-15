# OCR2PDF

A Windows desktop app built with C# / .NET 10 and WPF. Capture a screen region, import images, or paste an image, then export a searchable PDF and optional plain text. Processing happens locally.

## Run

The published build is in `artifacts/app/OCR2PDF.exe`. Keep that executable together with the other files in its folder. This self-contained x64 build includes .NET; Tesseract is a separate dependency.

Install Tesseract 5 with the language data you need. See the [Tesseract Windows installation guidance](https://tesseract-ocr.github.io/tessdoc/Installation.html). The app detects the usual `C:\Program Files\Tesseract-OCR\tesseract.exe` location; use Browse to select a different installation. English (`eng`) is the default. Additional installed languages can be combined, such as `eng+spa`.

1. Choose **Capture region** and drag a rectangle over the content. Drag inside the selection to move it, or drag an edge/corner to resize it. Dimensions appear in pixels. Press Enter or **Capture** to confirm; Esc cancels. Arrow keys nudge the selection (Shift moves it farther). The app hides itself before capture.
2. Alternatively, choose **Import images** (PNG, JPEG, BMP, TIFF, including multiple TIFF frames) or **Paste image**.
3. Select numbered pages to preview them. Use arrows or drag pages to reorder; **Undo remove** restores discarded pages. Images can also be dropped on the preview area.
4. Choose Letter or A4, portrait or landscape, and whether to export TXT. Select installed recognition languages by name. The executable path and Refresh are under **Advanced**. Export becomes available after images and a working recognition language are ready.
5. Choose **Export PDF** and a destination. Cancel stops OCR and discards incomplete outputs. **Open PDF** and **Open folder** become available after a successful export. Closing during import or export cancels the operation and then closes automatically.

The preview shows the actual white PDF page, including image placement and unused space. **Fit** follows window resizing; **100%** shows the page at its logical physical size. Use +/− or Ctrl+mouse wheel to zoom, and middle-button drag or Space+drag to pan. The dividing bars resize the panels; **Hide settings** gives the preview more room. Import and snapshot encoding run in the background, and the page list uses small thumbnails while the selected preview loads at full resolution.

Keyboard shortcuts: Ctrl+Shift+C captures; Ctrl+O imports; Ctrl+V pastes; Ctrl+E exports; Ctrl+Z undoes removal; Ctrl+0 fits the page; Ctrl+1 sets 100%; Alt+Up/Down reorders; Delete removes a page when the page list has focus. Text inputs retain their usual editing shortcuts. Colors use Windows system resources, including contrast-theme colors; icon controls have accessible names and keyboard focus indicators.

## Automated snipping

Choose **Automate…** next to **Capture region**. Select **Click a button**, **Scroll**, or **Press a key**, the number of captures (including the current page), and the wait after each action. Scroll mode offers direction and wheel notches; key mode offers PageDown, PageUp, arrow keys, Space, and Enter.

Select the fixed snipping region. For click mode, select the Next-button location; for scroll mode, select a point inside the scrollable content. These selection clicks only choose coordinates. A three-second countdown lets you focus the target application. The first page is captured before any advance action, and there is no advance after the final capture.

Press **Esc** anywhere or click **Stop** to end the run. The small progress window is hidden during each action and screenshot so it does not appear in the captured image. Completed captures are retained in your page list for review and normal searchable PDF/TXT export. Automation stops if the target loses focus, the action location is covered by another window, or Windows rejects input.

Keep the target window and content in the selected positions throughout the run. This uses screen coordinates and fixed waits; it does not detect the last page or wait for a page-load event. Choose enough wait time for your application. Windows may block input to an application running at a higher privilege level. If another application reserves Esc globally, the run will not start until that conflict is resolved.

## PDF behavior

Captured images are stored as lossless PNG snapshots for the session. PDFsharp embeds the full-resolution image, and a Tesseract text-only PDF supplies invisible OCR text. Both layers use the same rectangle and scale. Images fit the selected page size without stretching or cropping, with unused space left white. TXT pages are separated by a form-feed character.

The screenshot determines the visual appearance. OCR accuracy depends on text size, clarity, language data, and layout. Only visible content can be captured. This does not reconstruct editable document formatting or produce a fully tagged accessible PDF. Settings persist in `%LOCALAPPDATA%\OCR2PDF\settings.json`; temporary session captures are deleted on normal exit. An unexpected process termination can leave temporary files in `%TEMP%\OCR2PDF`.

## Build and verify

Requires Windows and the .NET 10 SDK:

```powershell
dotnet build OCR2PDF.slnx -c Release
dotnet run --project OCR2PDF/OCR2PDF.csproj
./publish.ps1
dotnet run --project Verification/Verification.csproj -c Release
python Verification/check_pdf.py
```

The C# verification exercises real Tesseract exports, page ordering, Letter/A4 dimensions, TXT options, cancellation, and preservation of an existing PDF on OCR failure. It also checks preview placement, thumbnail size, language discovery/readiness, undo, reordering, background import, resizing/Fit, settings collapse, close-after-cancellation, selection pixel mapping, and the capture overlay mask. It generates interface images at normal and small window sizes in `artifacts/verification`. The independent Python check requires PyMuPDF and Pillow and verifies PDF text extraction and exact embedded source pixels. Neither Python nor these verification dependencies are needed by the app.

Screen-region selection on different monitor arrangements and DPI settings, and a full Narrator/contrast-theme interaction pass, still need interactive testing on those configurations.

Dependencies: [PDFsharp](https://docs.pdfsharp.net/) (MIT) and an external [Tesseract](https://github.com/tesseract-ocr/tesseract) installation (Apache 2.0).
