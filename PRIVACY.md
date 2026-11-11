# Privacy policy — Radiacode Companion

_Last updated: 2026-11-11_

Radiacode Companion is an unofficial, open-source (GPL-3.0) desktop app for RadiaCode radiation detectors. It is made by an individual developer and is not affiliated with RadiaCode.

## What the app collects

Nothing. The app has no accounts, analytics, telemetry, advertising or crash reporting, and it does not send your measurements anywhere.

## Data stored on your computer

- Settings, the last connected device, measurement history and saved background spectra: `%LOCALAPPDATA%\RadiacodeCompanion` on Windows (inside the app's own package folder for the Microsoft Store version), `~/.local/share/RadiacodeCompanion` on Linux.
- Files you export: the folder you choose, by default `Documents\Radiacode Companion`.

You can delete these folders at any time. Uninstalling the Microsoft Store version removes its package folder; exported files stay where you saved them.

## Network access

After you connect a detector, the app downloads the public firmware changelog page from `https://www.radiacode.com/changelog` to tell you whether a newer firmware exists. The request carries no device data; like any web request, it reveals your IP address to that site. Nothing else is sent over the internet.

## Bluetooth and USB

The app talks only to your RadiaCode detector over USB or Bluetooth Low Energy, on your computer.

## Contact

Questions: open an issue at <https://github.com/SCIEN-00/RadiacodeCompanion/issues>.