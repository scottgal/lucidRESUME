# Privacy notice

lucidRESUME Evidence Filler processes personal résumé data from the full JobML
endpoint that you choose and visible empty field descriptions on the application
page where you invoke it.

- Processing occurs locally in Chrome.
- Chrome's built-in Prompt API runs its model on the device.
- The extension does not send résumé data, page content, form values or model
  requests to lucidRESUME, OpenAI, Google cloud services or another developer
  service.
- The extension fetches only the JobML endpoint you supply. It does not follow
  external evidence links contained in JobML.
- The endpoint URL is stored in `chrome.storage.local` for convenience. The
  fetched ledger and detected form fields are held in the side-panel page's
  memory and disappear when that page is destroyed.
- Existing form values are not read into the fact catalogue and are not
  overwritten. The extension never submits an application.

Chrome grants temporary access to the current page after you invoke the
extension. Access to the JobML endpoint host is requested separately at runtime.
You can revoke host access in Chrome's extension settings. Removing the extension
deletes its locally stored endpoint setting.

No analytics, advertising SDKs, telemetry or remote executable code are included.
