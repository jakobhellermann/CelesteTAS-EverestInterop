{
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs =
    {
      self,
      nixpkgs,
      flake-utils,
    }:
    flake-utils.lib.eachDefaultSystem (
      system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        nativeLibs = with pkgs; [
          icu
          gtk3
          glib
          pango
          cairo
          gdk-pixbuf
          fontconfig
          libgit2
          libnotify
          libGL
          openssl
          stdenv.cc.cc.lib
        ];
      in
      {
        packages = rec {
          celeste-studio = pkgs.buildDotnetModule (finalAttrs: {
            pname = "celeste-studio";
            version = "3.11.0";

            src = self;

            projectFile = "Studio/CelesteStudio.GTK/CelesteStudio.GTK.csproj";
            nugetDeps = ./nuget-deps.json; # Regenerate with: just nuget-deps

            dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
            dotnet-runtime = pkgs.dotnetCorePackages.sdk_10_0;

            selfContainedBuild = true;
            executables = [ "CelesteStudio.GTK" ];

            # Eto 2.9 P/Invokes system GTK directly
            runtimeDeps = with pkgs; [
              gtk3
              glib
              fontconfig
              libnotify
            ];

            nativeBuildInputs = [
              pkgs.copyDesktopItems
              pkgs.wrapGAppsHook3
            ];
            buildInputs = with pkgs; [
              gtk3
              gsettings-desktop-schemas
            ];
            desktopItems = [
              (pkgs.makeDesktopItem {
                desktopName = "Celeste Studio";
                name = "celeste-studio";
                exec = "CelesteStudio";
                icon = "celeste-studio";
                comment = "Celeste TAS Studio";
                type = "Application";
                categories = [
                  "Development"
                  "Utility"
                ];
              })
            ];

            postFixup = ''
              mv $out/bin/CelesteStudio.GTK $out/bin/CelesteStudio
            '';

            postInstall = ''
              for size in 16 32 48 64 128 256; do
                install -Dm444 \
                  $src/Studio/CelesteStudio/Assets/Icon_''${size}x''${size}.png \
                  $out/share/icons/hicolor/''${size}x''${size}/apps/celeste-studio.png
              done
              install -Dm444 \
                $src/Studio/CelesteStudio/Assets/Icon.svg \
                $out/share/icons/hicolor/scalable/apps/celeste-studio.svg
            '';

            meta = {
              description = "Celeste TAS Studio";
              homepage = "https://github.com/EverestAPI/CelesteTAS-EverestInterop";
              license = pkgs.lib.licenses.mit;
              mainProgram = "CelesteStudio";
              platforms = [ "x86_64-linux" ];
            };
          });

          default = celeste-studio;
        };

        devShells.default = pkgs.mkShell {
          packages = with pkgs; [
            dotnet-sdk_10
          ];

          env = {
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath nativeLibs;
          };

          # required for StudioCommunication
          shellHook = ''
            export TMPDIR=/tmp
          '';
        };
      }
    );
}
