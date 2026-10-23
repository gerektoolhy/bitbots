# Clean Preview

Chrome DevTools extension that lists captured network requests in its own panel and previews image URLs with the query string removed.

Chrome does not let extensions add a tab inside the Network request details (Headers, Payload, Preview, Response). This panel sits next to Network instead.

## Install

1. Open `chrome://extensions`.
2. Turn on **Developer mode**.
3. Choose **Load unpacked** and select the `clean-preview` folder (`~/work/bitbots/clean-preview`).
4. Open DevTools on a page and select the **Clean Preview** panel.
5. Reload the page so requests are captured after the panel is open.

## What it does

- Records requests with `chrome.devtools.network`.
- Strips `?…` from URLs whose host matches the list at the bottom of the panel (default: `fbcdn.net`, `cdninstagram.com`, `fbcdn.com`).
- Renders an image preview from that cleaned URL.
- Lets you copy or open the cleaned URL.

Some CDNs sign the query string. If the image fails to load, the panel says so — the signature is required and stripping it cannot produce a working URL.

OpenWhen (separate hobby app) lives in `OpenWhen/`.
See `OpenWhen/README.md` for local run and Render deploy notes.
