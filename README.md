# ARAM Tracker Companion

A small Windows app that sends your finished **ARAM Mayhem** games to your [ARAM Tracker](https://aramtracker.com/) profile. Riot doesn't share Mayhem games publicly, so the companion reads them from your League client after each game and uploads them for you.

## What it does, and what it doesn't

- Only tracks ARAM Mayhem. Normal ARAM and other modes are ignored.
- Only reads **finished** games from the League client. It never touches a game in progress.
- Uploads only game results: champions, scores, items, augments and the Riot IDs of the ten players.
- Never asks for or stores your Riot username or password.
- Your ARAM Tracker login is encrypted by Windows, so only your Windows account can read it.
- All the code is in this repository, so anyone can check exactly what it does.

## Install

Requires Windows 10 or 11 (64-bit).

1. Download **ARAMTrackerCompanion.exe** from [aramtracker.com](https://aramtracker.com/) or the [latest release](../../releases/latest).
2. Put it in a folder where it can stay, and run it.
3. Windows may warn you because the app isn't signed. Choose **More info → Run anyway**.
4. Sign in to ARAM Tracker, go to **Settings → Companion** and press **Connect the companion**.

The app then sits in your system tray. Games you play while it's closed are picked up the next time it runs, as long as they're among your last 20 games and less than 13 days old. Games played on a Riot account you haven't linked yet are kept, and sent once you link it.

## Uninstall

1. Press **Disconnect account** in the companion.
2. Exit it from the tray icon.
3. Delete the exe and the folder `%AppData%\ARAMTrackerCompanion`.

---

ARAM Tracker Companion is an independent project, not endorsed by Riot Games. League of Legends is a trademark of Riot Games, Inc. · [MIT license](LICENSE)
