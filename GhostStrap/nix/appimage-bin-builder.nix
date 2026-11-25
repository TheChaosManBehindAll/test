# SPDX-FileCopyrightText: 2026 Froststrap
#
# SPDX-License-Identifier: MPL-2.0

{
  lib,
  fontconfig,
  appimageTools,
  fetchurl,
  makeDesktopItem,
  version,
  sha256,
}:
let
  src = fetchurl {
    url = "https://github.com/GhostStrap/GhostStrap/releases/download/v${version}/GhostStrap-linux-x64.AppImage";
    inherit sha256;
  };

  appimageContents = appimageTools.extractType2 {
    inherit version src;
    pname = "ghoststrap";
  };

  desktopItem = makeDesktopItem {
    name = "ghoststrap";
    desktopName = "GhostStrap";
    comment = "A cross-platform Roblox bootstrapper, focused on performance and customization.";
    exec = "GhostStrap %u";
    icon = "ghoststrap";
    categories = [ "Game" ];
    mimeTypes = [
      "x-scheme-handler/roblox"
      "x-scheme-handler/roblox-player"
      "x-scheme-handler/roblox-studio"
      "x-scheme-handler/roblox-studio-auth"
    ];
  };
in
appimageTools.wrapType2 {
  pname = "ghoststrap";
  inherit version src;

  extraPkgs =
    pkgs: with pkgs; [
      icu
      fontconfig
      dbus
    ];

  # list values must end with ";". makeDesktopItem omits it.
  extraInstallCommands = ''
    install -Dm644 ${desktopItem}/share/applications/ghoststrap.desktop \
        $out/share/applications/ghoststrap.desktop

    sed -i -E '/^(Categories|MimeType)=/ s/([^;])$/\1;/' \
        $out/share/applications/ghoststrap.desktop

    install -Dm644 ${appimageContents}/usr/share/icons/hicolor/scalable/apps/GhostStrap.png \
        $out/share/icons/hicolor/512x512/apps/ghoststrap.png
  '';

  meta = {
    description = "A cross-platform Roblox bootstrapper, focused on performance and customization.";
    homepage = "https://github.com/GhostStrap/GhostStrap";
    license = with lib.licenses; [ mpl20 mit ];
    platforms = [ "x86_64-linux" ];
  };
}
