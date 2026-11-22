<p align="center"><img src="assets/brisk.png" width="128" alt="Brisk icon"></p>

# Brisk

A small, fast, private web browser for Windows, with ad blocking built in.

The whole app is about **5 MB**. It uses the Edge WebView2 engine that already ships with Windows 11, so it doesn't bundle its own copy of Chromium. The window opens in under a third of a second.

## Ad blocking

There are two layers.

1. **Brave's [adblock-rust](https://github.com/brave/adblock-rust) engine** is the main blocker. It uses EasyList, EasyPrivacy and Peter Lowe's list, and checks every request a page makes before it leaves your machine. It also hides the empty boxes left where ads were blocked. The lists refresh in the background every few days, and the compiled engine is cached so startup stays fast.
2. **Ad-blocking DNS** is the backup layer. Every hostname is looked up over encrypted DNS-over-HTTPS through [AdGuard DNS](https://adguard-dns.io/), in secure mode with no fallback to your ISP's DNS. Ad and tracker domains don't resolve at all.

### Turning it off for a site

Click the **shield** next to the address bar. It turns grey and the page reloads with ad blocking off for that site. Brisk remembers your choice, so the site stays unblocked after a restart. Click the shield again to turn blocking back on.

The **⋯ menu** lists every site where you've turned ad blocking off, so you can switch it back on from there. The DNS layer can be switched on or off from the same menu (it takes effect after a restart).

The per-site switch controls the main blocker. The DNS layer applies to every site, so a site that only works with its ad domains reachable needs the DNS layer switched off as well.

## Privacy

- Tracking prevention is set to *Strict*.
- Tracking parameters (`utm_*`, `fbclid`, `gclid`, `msclkid` and others) are removed from links before the page loads.
- WebRTC won't reveal your local IP address.
- Password saving and form autofill are off.
- SmartScreen is off, so the URLs you visit aren't sent to Microsoft for reputation checks.
- The default search engine is DuckDuckGo.

## Other features

- Tabs. Tabs restored from your last session don't load until you click them, so starting with many tabs is still instant.
- Links that open in a new window open in a new tab instead.
- Full-screen video. Press F11 for full screen.

| Shortcut | Action |
|---|---|
| Ctrl+T | New tab |
| Ctrl+W, Ctrl+F4 | Close tab |
| Ctrl+Shift+T | Reopen the last closed tab |
| **Alt+scroll wheel** | Previous tab (scroll up) / next tab (scroll down) |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+PageDown / Ctrl+PageUp | Next / previous tab |
| Ctrl+1 … Ctrl+8, Ctrl+9 | Jump to a tab / the last tab |
| Ctrl+L, Alt+D, F6 | Focus the address bar |
| F5, Ctrl+R | Reload |
| Alt+← / Alt+→ | Back / forward |
| F11 | Full screen |
| Middle-click a tab | Close it |

Shortcuts work whether the address bar or the page has focus. Ctrl+scroll wheel zooms, as in other browsers.

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- WebView2 Runtime (preinstalled on Windows 11)

## Building

You need [Rust](https://rustup.rs) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
./build.ps1
```

The browser ends up in `dist\Brisk.exe`. Every push to `main` also builds on GitHub Actions, and pushing a `v*` tag publishes a release zip.

## How it's put together

```
native/adblock-ffi/   Rust: C ABI over the adblock crate (brisk_adblock.dll)
src/Brisk/
  BrowserForm.cs      window, tabs, request blocking, DNS setup
  AdBlocker.cs        P/Invoke wrapper, filter-list download and engine cache
  Assets/cosmetic.js  in-page script that hides leftover ad elements
  Controls.cs         tab strip, toolbar buttons, shield
  Settings.cs         settings and per-site choices (%LOCALAPPDATA%\Brisk\settings.json)
```

## License

MIT
