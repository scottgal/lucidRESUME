const configureSidePanel = () => chrome.sidePanel.setPanelBehavior({ openPanelOnActionClick: true })
  .catch(error => console.warn("Could not configure the lucidRESUME side panel.", error));

chrome.runtime.onInstalled.addListener(() => { void configureSidePanel(); });
chrome.runtime.onStartup.addListener(() => { void configureSidePanel(); });
