#!/usr/bin/env bash
# Builds radiacode-companion_<version>_<arch>.deb from a self-contained "dotnet publish" output.
# Usage: packaging/linux/build-deb.sh <publish-dir> <version> <amd64|arm64> <out-dir>
set -euo pipefail

PUBLISH_DIR="$1"
VERSION="$2"
ARCH="$3"
OUT_DIR="$4"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
PKG="radiacode-companion"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

install -d "$STAGE/opt/$PKG" "$STAGE/usr/bin" "$STAGE/usr/share/applications" \
           "$STAGE/usr/share/icons/hicolor/scalable/apps" "$STAGE/usr/lib/udev/rules.d" \
           "$STAGE/usr/share/doc/$PKG" "$STAGE/DEBIAN"

cp -a "$PUBLISH_DIR/." "$STAGE/opt/$PKG/"
chmod 0755 "$STAGE/opt/$PKG/Radiacode.App"
ln -s "/opt/$PKG/Radiacode.App" "$STAGE/usr/bin/$PKG"
install -m 0644 "$HERE/$PKG.desktop" "$STAGE/usr/share/applications/$PKG.desktop"
install -m 0644 "$ROOT/assets/logo/cherenkov-logo.svg" "$STAGE/usr/share/icons/hicolor/scalable/apps/$PKG.svg"
install -m 0644 "$HERE/70-radiacode.rules" "$STAGE/usr/lib/udev/rules.d/70-radiacode.rules"
[ -f "$ROOT/LICENSE" ] && install -m 0644 "$ROOT/LICENSE" "$STAGE/usr/share/doc/$PKG/copyright"

INSTALLED_KB=$(du -sk "$STAGE" | cut -f1)
cat > "$STAGE/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Architecture: $ARCH
Maintainer: Lauri <noreply@example.invalid>
Installed-Size: $INSTALLED_KB
Depends: libusb-1.0-0, libfontconfig1, libx11-6
Recommends: bluez, libnotify-bin, libgl1
Section: science
Priority: optional
Description: Unofficial companion app for RadiaCode detectors
 Live dose rate, count rate and energy spectrum from RadiaCode dosimeters
 over USB or Bluetooth LE, with isotope hints, K-40 gain correction,
 background comparison, dose-rate alarms and CSV export.
 .
 Not affiliated with RadiaCode.
EOF

cat > "$STAGE/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v udevadm >/dev/null 2>&1; then
  udevadm control --reload-rules || true
  udevadm trigger --subsystem-match=usb --attr-match=idVendor=0483 || true
fi
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q /usr/share/icons/hicolor || true
exit 0
EOF
cp "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"
chmod 0755 "$STAGE/DEBIAN/postinst" "$STAGE/DEBIAN/postrm"

mkdir -p "$OUT_DIR"
dpkg-deb --root-owner-group --build "$STAGE" "$OUT_DIR/${PKG}_${VERSION}_${ARCH}.deb"
echo "Built $OUT_DIR/${PKG}_${VERSION}_${ARCH}.deb"
