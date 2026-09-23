#!/usr/bin/env python3
"""Scan and fill representative ATS form shapes without invoking or submitting them."""

import argparse
import functools
import json
import shutil
import tempfile
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from selenium import webdriver
from selenium.webdriver.chrome.options import Options


HERE = Path(__file__).resolve().parent
EXTENSION_ROOT = HERE.parents[1]
REPOSITORY_ROOT = HERE.parents[3]

CASES = {
    "greenhouse-form.html": {
        "count": 9,
        "labels": {
            "First name": "text",
            "Last name": "text",
            "Email": "text",
            "Phone": "text",
            "Current company": "text",
            "Describe a time you led an engineering team through change": "textarea",
            "Are you legally authorised to work in the United Kingdom?": "radio",
            "Will you require visa sponsorship?": "select",
            "Voluntary demographic information Prefer not to say": "checkbox",
        },
        "fill": {"First name": "Alex", "Email": "alex@example.com"},
    },
    "lever-form.html": {
        "count": 7,
        "labels": {
            "Full name": "text",
            "Email address": "text",
            "Phone number": "text",
            "Current company": "text",
            "LinkedIn URL": "text",
            "GitHub URL": "text",
            "Additional information": "textarea",
        },
        "fill": {"Full name": "Alex Example", "Email address": "alex@example.com"},
    },
    "workable-form.html": {
        "count": 9,
        "labels": {
            "Candidate name": "text",
            "Email": "text",
            "Portfolio website": "text",
            "Describe your engineering leadership": "textarea",
            "Years of commercial TypeScript experience": "select",
            "Desired annual salary": "text",
            "Available start date": "text",
            "Do you require visa sponsorship?": "radio",
            "I agree to the applicant privacy notice": "checkbox",
        },
        "fill": {"Candidate name": "Alex Example", "Email": "alex@example.com"},
    },
}


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--chrome",
        type=Path,
        default=Path("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=REPOSITORY_ROOT / "output/playwright/form-matrix",
    )
    return parser.parse_args()


def prepare_extension() -> Path:
    source = EXTENSION_ROOT / "dist"
    if not (source / "manifest.json").is_file():
        raise RuntimeError("Build the extension with `npm run build` first.")
    destination = Path(tempfile.mkdtemp(prefix="lucidresume-form-matrix-extension-"))
    shutil.copytree(source, destination, dirs_exist_ok=True)
    manifest_path = destination / "manifest.json"
    manifest = json.loads(manifest_path.read_text())
    manifest["host_permissions"] = ["http://127.0.0.1/*"]
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    return destination


def start_server() -> tuple[ThreadingHTTPServer, str]:
    handler = functools.partial(SimpleHTTPRequestHandler, directory=HERE / "fixtures")
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_port}"


def scan(driver: webdriver.Chrome, form_url: str) -> dict:
    return driver.execute_async_script(
        """
        const formUrl = arguments[0], done = arguments[arguments.length - 1];
        chrome.tabs.query({url: formUrl}, async tabs => {
          try {
            const tab = tabs[0];
            if (!tab?.id) throw new Error(`Form tab not found: ${formUrl}`);
            await chrome.scripting.executeScript({target: {tabId: tab.id}, files: ['content.js']});
            const response = await chrome.tabs.sendMessage(tab.id, {type: 'scan'});
            done(response);
          } catch (error) {
            done({ok: false, error: String(error)});
          }
        });
        """,
        form_url,
    )


def fill(driver: webdriver.Chrome, form_url: str, values: list[dict]) -> dict:
    return driver.execute_async_script(
        """
        const formUrl = arguments[0], values = arguments[1], done = arguments[arguments.length - 1];
        chrome.tabs.query({url: formUrl}, async tabs => {
          try {
            const response = await chrome.tabs.sendMessage(tabs[0].id, {type: 'fill', values});
            done(response);
          } catch (error) {
            done({ok: false, error: String(error)});
          }
        });
        """,
        form_url,
        values,
    )


def main() -> None:
    args = arguments()
    extension = prepare_extension()
    profile = Path(tempfile.mkdtemp(prefix="lucidresume-form-matrix-profile-"))
    server, base_url = start_server()
    args.output.mkdir(parents=True, exist_ok=True)

    options = Options()
    options.binary_location = str(args.chrome)
    options.enable_bidi = True
    options.enable_webextensions = True
    for value in (
        f"--user-data-dir={profile}",
        "--password-store=basic",
        "--use-mock-keychain",
        "--disable-sync",
        "--no-first-run",
        "--no-default-browser-check",
    ):
        options.add_argument(value)

    driver = webdriver.Chrome(options=options)
    reports = []
    try:
        extension_id = driver.webextension.install(path=str(extension))["extension"]
        driver.get(f"chrome-extension://{extension_id}/sidepanel.html")
        extension_handle = driver.current_window_handle

        for filename, expected in CASES.items():
            driver.switch_to.new_window("tab")
            form_handle = driver.current_window_handle
            form_url = f"{base_url}/{filename}"
            driver.get(form_url)
            driver.execute_script(
                "window.__submitted=0; document.querySelector('form').addEventListener('submit',"
                "event => { event.preventDefault(); window.__submitted++; });"
            )

            driver.switch_to.window(extension_handle)
            result = scan(driver, form_url)
            assert result.get("ok"), {"fixture": filename, "scan": result}
            fields = result["fields"]
            assert len(fields) == expected["count"], {"fixture": filename, "fields": fields}
            actual = {field["label"]: field for field in fields}
            for label, kind in expected["labels"].items():
                assert label in actual, {"fixture": filename, "missing": label, "fields": fields}
                assert actual[label]["kind"] == kind, {"fixture": filename, "field": actual[label]}

            radio = next((field for field in fields if field["kind"] == "radio"), None)
            if radio:
                assert [item["label"] for item in radio["options"]] == ["Yes", "No"], radio
            select = next((field for field in fields if field["kind"] == "select"), None)
            if select:
                assert all(item["value"] not in ("select", "choose") for item in select["options"]), select

            instructions = [
                {"fieldId": actual[label]["id"], "value": value}
                for label, value in expected["fill"].items()
            ]
            fill_result = fill(driver, form_url, instructions)
            assert fill_result.get("ok"), {"fixture": filename, "fill": fill_result}
            assert len(fill_result["applied"]) == len(instructions), fill_result

            driver.switch_to.window(form_handle)
            state = driver.execute_script(
                "return {submitted: window.__submitted, values: [...document.querySelectorAll('input')]"
                ".filter(x => x.value === 'Alex' || x.value === 'Alex Example' || x.value === 'alex@example.com')"
                ".map(x => x.value)};"
            )
            assert state["submitted"] == 0, {"fixture": filename, "state": state}
            assert len(state["values"]) == len(instructions), {"fixture": filename, "state": state}
            driver.save_screenshot(str(args.output / f"{Path(filename).stem}.png"))
            reports.append({
                "fixture": filename,
                "fields": len(fields),
                "labels": list(actual),
                "filled": len(fill_result["applied"]),
                "submitted": state["submitted"],
            })
            driver.close()
            driver.switch_to.window(extension_handle)

        print(json.dumps({"extension_id": extension_id, "reports": reports}, indent=2))
    finally:
        driver.quit()
        server.shutdown()
        shutil.rmtree(extension, ignore_errors=True)
        shutil.rmtree(profile, ignore_errors=True)


if __name__ == "__main__":
    main()
