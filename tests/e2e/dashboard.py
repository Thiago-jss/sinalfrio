#!/usr/bin/env python3
"""Optional Chromium smoke: run after scripts/demo.py on its same isolated stack.

Install Playwright and Chromium separately. Run: python3 tests/e2e/dashboard.py
This is a read-only check of the review already persisted by the API demo.
"""

import os
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from scripts.demo import Api, DemoError, NOTE, require, settings


def main():
    # Protocol debug logs can include the Playwright fill payload.
    os.environ.pop("DEBUG", None)
    os.environ.pop("PWDEBUG", None)
    try:
        from playwright.sync_api import expect, sync_playwright  # pyright: ignore[reportMissingImports]
    except ImportError:
        print("Install optional Playwright and its Chromium browser first.", file=sys.stderr)
        return 1

    stage = "verify the reviewed demo fixture"
    try:
        port, _, operator = settings()
        api = Api(port, operator)
        review = api.detail().get("review") or {}
        require(review.get("note") == NOTE and review.get("decision") == "acknowledged",
                "Run the API demo before the dashboard review check.")
        with sync_playwright() as playwright:
            stage = "launch Chromium"
            browser = playwright.chromium.launch()
            try:
                page = browser.new_page(viewport={"width": 1440, "height": 1050}, locale="pt-BR", timezone_id="UTC")
                page.set_default_timeout(10_000)
                page.route("**/*", lambda route: route.continue_()
                           if route.request.url.startswith(api.origin + "/") else route.abort())
                stage = "open the public dashboard"
                page.goto(api.origin, wait_until="domcontentloaded", timeout=15_000)
                stage = "connect with the operator key"
                page.get_by_label("Chave do operador", exact=True).fill(operator)
                page.get_by_role("button", name="Conectar", exact=True).click()
                stage = "display SF-001 and its recorded review"
                expect(page.get_by_text("SF-001", exact=True).first).to_be_visible(timeout=10_000)
                review_text = page.get_by_text("Revisão registrada", exact=False)
                if not review_text.count():
                    shipment_button = page.get_by_role("button", name="SF-001", exact=False)
                    if shipment_button.count() == 1:
                        shipment_button.click()
                expect(review_text).to_be_visible(timeout=10_000)
                expect(page.get_by_text(NOTE, exact=True)).to_be_visible(timeout=10_000)
                stage = "verify that the production stylesheet applies under the CSP"
                icon = page.locator(".identity svg").bounding_box()
                require(icon is not None and 25 <= icon["width"] <= 60,
                        "The dashboard masthead icon has lost its production styling.")
                if screenshot := os.environ.get("SINALFRIO_SCREENSHOT"):
                    page.screenshot(path=screenshot, full_page=True, animations="disabled")
            finally:
                browser.close()
    except Exception as exc:
        # Playwright call logs can contain values used in fill(). Do not emit them.
        print(f"FAIL: could not {stage} ({type(exc).__name__}).", file=sys.stderr)
        return 1
    print("PASS: the operator dashboard showed the persisted SF-001 review.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
