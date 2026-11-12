# Privacy policy — Radiacode Companion

_Last updated: 2026-11-11_

Radiacode Companion is an unofficial, open-source (GPL-3.0) desktop app for RadiaCode radiation detectors. It is made by an individual developer and is not affiliated with RadiaCode.

## What the app collects

Nothing. The app has no accounts, analytics, telemetry, advertising or crash reporting, and it does not send your measurements anywhere.

## Data stored on your computer

- Settings, the last connected device, measurement history and saved background spectra: `%LOCALAPPDATA%\RadiacodeCompanion` on Windows, `~/.local/share/RadiacodeCompanion` on Linux. For the Microsoft Store version Windows may keep a private copy of changed files in the app's package folder (`%LOCALAPPDATA%\Packages\25158THOR-00.RadiacodeCompanion_…`).
- Files you export: the folder you choose, by default `Documents\Radiacode Companion`.

You can delete these folders at any time; uninstalling the app does not delete your measurement files or exports.

## Network access

After you connect a detector, the app downloads the public firmware changelog page from `https://www.radiacode.com/changelog` to tell you whether a newer firmware exists. The request carries no device data; like any web request, it reveals your IP address to that site. Nothing else is sent over the internet.

## Bluetooth and USB

The app talks only to your RadiaCode detector over USB or Bluetooth Low Energy, on your computer.

## Contact

Questions: open an issue at <https://github.com/SCIEN-00/RadiacodeCompanion/issues>.