<p align="center"><img src="assets/brisk.png" width="128" alt="Brisk icon"></p>

# Brisk

A small, fast, private web browser for Windows, with ad blocking built in.

The whole app is about **5 MB**. It uses the Edge WebView2 engine that already ships with Windows 11, so it doesn't bundle its own copy of Chromium. The window opens in under a third of a second.

## Ad blocking

There are two layers.

1. **Brave's [adblock-rust](https://github.com/brave/adblock-rust) engine** is the main blocker. It uses EasyList, EasyPrivacy, Peter Lowe's list and uBlock Origin's own lists, and checks every request a page makes before it leaves your machine. It also hides the empty boxes left where ads were blocked. The lists refresh in the background every few days, and the compiled engine is cached so startup stays fast.
2. **Ad-blocking DNS** is the backup layer. Every hostname is looked up over encrypted DNS-over-HTTPS through an ad-blocking DNS service: [AdGuard DNS](https://adguard-dns.io/) by default, or Mullvad DNS or Control D (**Settings → Privacy**). Ad and tracker domains don't resolve at all. If the service can't be reached, Brisk falls back to your normal DNS so pages keep loading, and shows an orange dot on the shield until it's back.

### Cookie banners

With **Hide cookie notices** on (the default, under **Settings → Privacy**), Brisk also uses the EasyList Cookie List and uBlock Origin's cookie-notice list. Most "accept cookies" pop-ups never appear, and on sites that insist on an answer the lists turn them down where they can.

### YouTube ads and page scripts

Some ads, like YouTube's video ads, come from the same servers as the content, so they can't be blocked by address. uBlock Origin's lists handle these with small scripts that run in the page and strip the ads out, for example removing the ad entries from YouTube's player data. Brisk runs the same scripts, the way Brave does:

- They aren't bundled with Brisk. Once a week Brisk downloads uBlock Origin's script library from its latest release and converts it inside a hidden, sandboxed browser view with no network access.
- When you open a site that has script rules, Brisk injects them before the site's own code runs.
- Turning ad blocking off for a site turns its scripts off too.

### Turning it off for a site

Click the **shield** next to the address bar. It turns grey and the page reloads with ad blocking off for that site. Brisk remembers your choice, so the site stays unblocked after a restart. Click the shield again to turn blocking back on.

**Settings → Privacy** lists every site where you've turned ad blocking off, so you can switch it back on from there.

The shield covers the DNS layer too. On a site where blocking is off, Brisk checks each address the page asks for with the DNS service, and if that service is what's blocking it, Brisk fetches it through your normal DNS instead. The engine's own *Strict* tracking prevention still applies everywhere, so the best-known ad and tracker servers stay blocked even on those sites.

## Privacy

- Tracking prevention is set to *Strict*.
- Tracking parameters (`utm_*`, `fbclid`, `gclid`, `msclkid` and others) are removed from links before the page loads.
- WebRTC won't reveal your local IP address.
- Passwords are only saved when you say so (see below), and address/card autofill is off.
- SmartScreen is off, so the URLs you visit aren't sent to Microsoft for reputation checks.
- The default search engine is DuckDuckGo. You can switch to Google, Bing, Brave Search, Startpage or Ecosia under **Settings → General**.

## Other features

- **Start page.** New tabs open on a start page with your Favorites (bookmarks), Frequently Visited sites and a privacy report of how many ads and trackers Brisk has blocked. It's generated inside Brisk and never touches the network.
- **History and bookmarks.** Click the star in the address bar (or press Ctrl+D) to bookmark a page. Both are in the **⋯** menu, and as you type in the address bar Brisk suggests matching bookmarks and history (use the arrow keys and Enter, or click).
- **Private windows** (Ctrl+Shift+N). Nothing is saved to history, and cookies and site data are deleted when the window closes. Ad blocking works as usual.
- **Tabs.** Drag tabs to reorder them. Right-click a tab to pin it, duplicate it or close other tabs; pinned tabs stay at the left as small icons and come back after a restart. Tabs restored from your last session don't load until you click them.
- **Sleeping tabs.** Tabs you haven't looked at for 10 minutes are put to sleep so they stop using memory and processor time; tabs playing audio are left alone. Change the delay or turn it off under **Settings → General**.
- **Saved passwords.** When you log in, Brisk asks whether to save the password and fills it in next time. Passwords stay on your computer, encrypted with your Windows account, and are never saved in private windows. **⋯ → Passwords…** lists them: search, show or copy a password (copied passwords are kept out of clipboard history), or delete one or all of them.
- **Find on page** (Ctrl+F). A find bar under the toolbar shows how many matches there are; Enter and Shift+Enter step through them.
- **Tab search** (Ctrl+Shift+A). Type part of a tab's title or address to jump straight to it.
- **Settings** (Ctrl+,). Search engine, sleeping tabs, default browser, cookie banners, ad-blocking DNS, filter lists, sites with ad blocking off, and saved passwords.
- Links that open in a new window open in a new tab instead.
- Full-screen video. Press F11 for full screen.

| Shortcut | Action |
|---|---|
| Ctrl+T | New tab |
| Ctrl+Shift+N | New private window |
| Ctrl+D | Bookmark the page |
| Ctrl+F | Find on page (F3 / Shift+F3 for next / previous) |
| Ctrl+Shift+A | Search open tabs |
| Ctrl+, | Settings |
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

## Installing

Download `Brisk-Setup-<version>.exe` from the [latest release](https://github.com/Jumbox249/brisk/releases/latest) and run it. It installs Brisk for your account only (no administrator rights needed), adds it to the Start menu, and registers it so you can make it your default browser. If the .NET 10 Desktop Runtime is missing, the installer downloads it from Microsoft first; that step asks for permission because the runtime is shared by all users. You can uninstall Brisk from **Settings → Apps**, and choose whether to keep your history, bookmarks and passwords.

The release also has a zip of the same files if you'd rather not install anything.

## Making Brisk your default browser

Open **Settings → General → Make Brisk the Default…**. Brisk registers itself with Windows for web links and `.html` files, then opens its page in **Settings → Apps → Default apps**, where you choose **Set default**. Windows requires that last step to be done by you.

Links from other apps open as new tabs in the Brisk window you already have open. If Brisk isn't running, it starts with the link in front and your other tabs restored behind it.

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- WebView2 Runtime (preinstalled on Windows 11)

## Building

You need [Rust](https://rustup.rs) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
./build.ps1              # builds dist\Brisk.exe
./build.ps1 -Installer   # also builds out\Brisk-Setup-<version>.exe (needs Inno Setup 6)
```

Every push to `main` also builds on GitHub Actions, and pushing a `v*` tag attaches the installer and a zip to the release.

## How it's put together

```
native/adblock-ffi/   Rust: C ABI over the adblock crate (brisk_adblock.dll)
src/Brisk/
  BrowserForm.cs      window, tabs, request blocking, find bar, tab search
  AdBlocker.cs        P/Invoke wrapper, filter-list download and engine cache
  DnsBypass.cs        lets requests past the DNS layer on sites with blocking off
  PasswordStore.cs    reads the engine's saved passwords for the password list
  SettingsForm.cs     the Settings window
  Assets/cosmetic.js  in-page script that hides leftover ad elements
  Controls.cs         tab strip, toolbar buttons, shield
  Settings.cs         settings and per-site choices (%LOCALAPPDATA%\Brisk\settings.json)
installer/brisk.iss   Inno Setup script for the installer
```

## License

MIT
