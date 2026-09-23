#!/usr/bin/env python3
"""Opt-in Chrome Prompt API smoke test using a model-ready isolated profile."""

import argparse
import functools
import json
import shutil
import tempfile
import threading
import time
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from selenium import webdriver
from selenium.common.exceptions import TimeoutException
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait


HERE = Path(__file__).resolve().parent
EXTENSION_ROOT = HERE.parents[1]
REPOSITORY_ROOT = HERE.parents[3]
LIVE_CHROME_DATA = (Path.home() / "Library/Application Support/Google/Chrome").resolve()


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", required=True, type=Path,
                        help="Dedicated Chrome user-data directory with the on-device model ready")
    parser.add_argument("--chrome", type=Path,
                        default=Path("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"))
    parser.add_argument("--output", type=Path,
                        default=REPOSITORY_ROOT / "output/playwright")
    return parser.parse_args()


def prepare_extension() -> Path:
    source = EXTENSION_ROOT / "dist"
    if not (source / "manifest.json").is_file():
        raise RuntimeError("Build the extension with `npm run build` first.")
    destination = Path(tempfile.mkdtemp(prefix="lucidresume-extension-e2e-"))
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


def main() -> None:
    args = arguments()
    profile = args.profile.resolve()
    if profile == LIVE_CHROME_DATA or LIVE_CHROME_DATA in profile.parents:
        raise RuntimeError("Refusing to automate the live Chrome profile. Use a dedicated copy.")
    if not profile.is_dir():
        raise RuntimeError(f"Profile does not exist: {profile}")

    extension = prepare_extension()
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
    try:
        installed = driver.webextension.install(path=str(extension))
        extension_id = installed["extension"]
        form_url = f"{base_url}/application.html"

        driver.get(form_url)
        form_handle = driver.current_window_handle
        driver.execute_script(
            "window.__submitted=0;document.querySelector('form').addEventListener('submit',"
            "e=>{e.preventDefault();window.__submitted++;});"
        )

        driver.switch_to.new_window("tab")
        driver.get(f"chrome-extension://{extension_id}/sidepanel.html")
        endpoint = WebDriverWait(driver, 15).until(
            lambda current: current.find_element(By.ID, "endpoint")
        )
        endpoint.clear()
        endpoint.send_keys(f"{base_url}/jobml.md")
        driver.find_element(By.ID, "load").click()
        WebDriverWait(driver, 15).until(
            lambda current: current.find_element(By.ID, "ledger-status").text != "Loading…"
        )

        driver.execute_async_script(
            "const formUrl=arguments[0],done=arguments[arguments.length-1];"
            "chrome.tabs.query({url:formUrl},tabs=>{"
            "chrome.tabs.update(tabs[0].id,{active:true},()=>{"
            "document.querySelector('#analyse').click();done(true);});});",
            form_url,
        )
        try:
            WebDriverWait(driver, 240).until(
                lambda current: "evidence-backed proposal" in current.find_element(By.ID, "form-status").text
                or current.find_element(By.ID, "form-status").text == "Analysis stopped."
            )
        except TimeoutException as error:
            state = driver.execute_script(
                "return {ledger:document.querySelector('#ledger-status').textContent,"
                "model:document.querySelector('#model-status').textContent,"
                "form:document.querySelector('#form-status').textContent,"
                "error:document.querySelector('#error').textContent};"
            )
            driver.save_screenshot(str(args.output / "real-bidi-timeout.png"))
            raise RuntimeError(f"Prompt API smoke test timed out: {json.dumps(state)}") from error

        report = driver.execute_script(
            "return {ledger:document.querySelector('#ledger-status').textContent,"
            "model:document.querySelector('#model-status').textContent,"
            "form:document.querySelector('#form-status').textContent,"
            "error:document.querySelector('#error').textContent,"
            "proposals:[...document.querySelectorAll('.proposal')].map(x=>({"
            "text:x.textContent.replace(/\\s+/g,' ').trim(),"
            "checked:x.querySelector('input[type=checkbox]')?.checked??null}))};"
        )
        for proposal in driver.find_elements(By.CSS_SELECTOR, ".proposal"):
            if "engineering leadership" in proposal.text.lower():
                boxes = proposal.find_elements(By.CSS_SELECTOR, "input[type=checkbox]")
                if boxes and not boxes[0].is_selected():
                    boxes[0].click()

        driver.save_screenshot(str(args.output / "real-bidi-extension-panel.png"))
        driver.execute_async_script(
            "const formUrl=arguments[0],done=arguments[arguments.length-1];"
            "chrome.tabs.query({url:formUrl},tabs=>{"
            "chrome.tabs.update(tabs[0].id,{active:true},()=>{"
            "document.querySelector('#fill').click();done(true);});});",
            form_url,
        )
        time.sleep(1)
        driver.switch_to.window(form_handle)
        values = driver.execute_script(
            "return Object.fromEntries(['firstName','lastName','email','phone','github','employer',"
            "'leadership','sponsorship','salary'].map(name=>[name,document.querySelector(`[name=${name}]`).value])"
            ".concat([['submitted',window.__submitted]]));"
        )
        driver.save_screenshot(str(args.output / "real-bidi-filled-form.png"))

        deterministic_expected = {
            "firstName": "Alex", "lastName": "Example", "email": "alex@example.com",
            "phone": "+44 7700 900123", "github": "https://github.com/alex",
            "employer": "", "sponsorship": "", "salary": "", "submitted": 0,
        }
        assert report["error"] == "", report
        assert "Prompt API: available" in report["model"], report
        for name, expected in deterministic_expected.items():
            assert values[name] == expected, {"report": report, "values": values}
        assert values["leadership"] in (
            "", "Led a 15 engineer TypeScript team through platform change on AWS."
        ), {"report": report, "values": values}
        assert len(report["proposals"]) == 9, report
        for label in ("visa sponsorship", "Salary expectation"):
            assert any(label in proposal["text"] and "Gap:" in proposal["text"]
                       for proposal in report["proposals"]), report
        leadership = next(item for item in report["proposals"]
                          if "engineering leadership" in item["text"].lower())
        if leadership["checked"] is not None:
            assert "Evidence: Human prose supporting leadership" in leadership["text"], leadership
        print(json.dumps({"extension_id": extension_id, "report": report, "values": values}, indent=2))
    finally:
        driver.quit()
        server.shutdown()
        shutil.rmtree(extension, ignore_errors=True)


if __name__ == "__main__":
    main()
