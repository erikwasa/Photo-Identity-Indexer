"""Static safeguards for the native full-viewport quality-review contract.

These catch accidental reintroduction of the tall gallery or fragile
document-level keyboard module. Browser/viewport behavior still requires
the maintained application's local visual smoke test.
"""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
PAGE = ROOT / "src/PhotoIdentity.Web/Pages/QualitySuggestions.razor"
CSS = ROOT / "src/PhotoIdentity.Web/Pages/QualitySuggestions.razor.css"
OLD_KEYS = ROOT / "src/PhotoIdentity.Web/wwwroot/js/quality-review-keys.js"


class NativeQualityViewportContractTests(unittest.TestCase):
    def test_review_is_a_focused_blazor_keyboard_surface(self):
        code = PAGE.read_text(encoding="utf-8")
        self.assertIn('tabindex="0" @ref="ReviewSurface"', code)
        self.assertIn('@onkeydown="HandleKeyboardAsync"', code)
        self.assertIn('ReviewSurface.FocusAsync(preventScroll: true)', code)
        self.assertIn('ModalCancelButton.FocusAsync(preventScroll: true)', code)
        self.assertIn('@onkeydown:stopPropagation="true"', code)
        for key in ('case "1":', 'case "2":', 'case "3":',
                    'case "ArrowLeft":', 'case "ArrowRight":'):
            self.assertIn(key, code)
        self.assertNotIn('IJSObjectReference', code)
        self.assertNotIn('import", "./js/quality-review-keys.js"', code)
        self.assertFalse(OLD_KEYS.exists())

    def test_photo_and_controls_share_the_visible_viewport(self):
        code = PAGE.read_text(encoding="utf-8")
        styles = CSS.read_text(encoding="utf-8")
        for token in ('quality-review-screen', 'quality-photo-stage',
                      'quality-review-footer', 'quality-review-buttons'):
            self.assertIn(token, code)
            self.assertIn("." + token, styles)
        self.assertIn("position: fixed;", styles)
        self.assertIn("height: 100dvh;", styles)
        self.assertIn("grid-template-rows: auto minmax(0, 1fr) auto;", styles)
        self.assertIn("overflow: hidden;", styles)
        self.assertIn("object-fit: contain;", styles)
        self.assertNotIn("63vh", styles)
        self.assertNotIn("quality-suggestion-grid", styles)

    def test_confirmation_is_overlay_and_never_number_key_purge(self):
        code = PAGE.read_text(encoding="utf-8")
        styles = CSS.read_text(encoding="utf-8")
        self.assertIn('aria-modal="true"', code)
        self.assertIn('role="dialog"', code)
        self.assertIn('Confirm exclude &amp; purge', code)
        self.assertIn('PendingExclusion is not null', code)
        self.assertIn('if (args.Key == "Escape"', code)
        self.assertIn('.quality-modal-backdrop', styles)
        self.assertIn('position: absolute;', styles)
        self.assertIn('align-items: center;', styles)
        self.assertIn('await Http.PostAsJsonAsync(', code)
        self.assertIn('"api/archive/exclusions/revision"', code)
        keyboard = code.split("private async Task HandleKeyboardAsync", 1)[1].split(
            "private static string DecisionLabel", 1)[0]
        self.assertNotIn("ConfirmExcludeAsync", keyboard)

    def test_scan_options_are_overlay_not_above_photo(self):
        code = PAGE.read_text(encoding="utf-8")
        styles = CSS.read_text(encoding="utf-8")
        self.assertIn('aria-expanded="@ShowScanSettings"', code)
        self.assertIn('class="quality-scan-drawer"', code)
        self.assertIn('class="quality-modal-backdrop"', code)
        self.assertIn('top: 57px;', styles)
        self.assertIn('.quality-scan-drawer {', styles)


if __name__ == "__main__":
    unittest.main()
