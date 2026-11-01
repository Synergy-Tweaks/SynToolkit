# SynToolkit

Post-installation control panel for [SynergyOS](https://github.com/kwanteks/synergyos).

SynToolkit is a WinUI 3 (C#) toolkit built by [Kwanteks](https://github.com/kwanteks) under [Synergy-Tweaks](https://github.com/Synergy-Tweaks). After you install SynergyOS, use it as the control panel to shape the system to your liking: tweaks, apps, power plans, hardware info, and day-to-day fixes.

<p align="center">
  <img width="1919" height="1094" alt="SynToolkit Preview" src="https://github.com/user-attachments/assets/0f403bcb-0be6-4805-9f7d-3959a9586abe" />
</p>

<p align="center">
  <a href="https://github.com/Synergy-Tweaks/SynToolkit/blob/main/LICENSE"><img alt="License" src="https://img.shields.io/github/license/Synergy-Tweaks/SynToolkit?style=for-the-badge&color=1A91FF" /></a>
  <a href="https://github.com/Synergy-Tweaks/SynToolkit/releases" target="_blank"><img alt="Release" src="https://img.shields.io/github/v/release/Synergy-Tweaks/SynToolkit?style=for-the-badge&color=1A91FF" /></a>
  <a href="https://github.com/Synergy-Tweaks/SynToolkit/releases" target="_blank"><img alt="Downloads" src="https://img.shields.io/github/downloads/Synergy-Tweaks/SynToolkit/total?style=for-the-badge&color=1A91FF&label=Downloads" /></a>
  <a href="https://github.com/Synergy-Tweaks/SynToolkit/stargazers" target="_blank"><img alt="Stars" src="https://img.shields.io/github/stars/Synergy-Tweaks/SynToolkit?style=for-the-badge&color=1A91FF" /></a>
  <a href="https://dsc.gg/kwanteks" target="_blank"><img alt="Discord" src="https://img.shields.io/discord/1395215998646489211?style=for-the-badge&color=1A91FF&logo=discord&logoColor=white&label=Discord" /></a>
</p>

## Features

- Home page with header art, community tiles, SynergyOS release notes, and favorites
- Specs page with a hardware and Windows snapshot, plus live CPU utilization and frequency
- Collapsible sidebar navigation and a loading splash on startup
- Dark theme with Mica backdrop and grey home-header gradients
- Display layout editor for arranging monitors
- Per-app audio mixer with device selection and a global hotkey
- Needs Attention checks for drivers, disk space, restore points, clock sync, and updates
- Startup scanner for launch-at-boot entries
- Installer (App Fetch) catalog with WinGet installs and a queue
- Uninstaller for installed desktop apps
- Power plan import, activate, edit, and restore
- Games library for Steam, Epic, and GOG titles, plus manual adds
- Adjustments for local account identity and wallpapers
- GPU page for driver installs, NVIDIA profile apply, and AMD driver DWORD packs
- Disk Cleanup for temp files, caches, and other junk
- Configuration sections for Tweaks, General, Interface, Windows, Advanced, Security, and Troubleshooting (performance, privacy, boot, Defender, mitigations, network fixes, System Restore, and more)

## Download and install

**Requirements**

- [SynergyOS](https://github.com/Synergy-Tweaks/SynergyOS) (officially supported environment)
- Windows 10 (1809 / build 17763) or Windows 11
- x64 recommended (x86 and ARM64 are also targeted)

Download the latest installer from the [Releases](https://github.com/Synergy-Tweaks/SynToolkit/releases) page. Prefer builds from this repository or the official Discord.

> [!IMPORTANT]
> Only download SynToolkit from this repo or our official Discord. We are not responsible for modified or repackaged copies distributed elsewhere.

## Build from source

The app targets **.NET 8** (`net8.0-windows10.0.26100.0`) and **Windows App SDK 1.7**.

1. Install [Visual Studio 2022](https://visualstudio.microsoft.com/vs/) with the **.NET desktop development** workload and the **Windows App SDK** components.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Clone the repository and open `SynToolkit.sln`.
4. Set the startup project to `SynToolkit`, platform **x64**, then build and run.

To publish a Release build and create an installer (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
.\build-installer.ps1
```

Use `-SkipInstaller` to publish only, or `-SkipBuild` if you already have a publish output.

## Contributing

Want to help fix a bug or build a new feature? Open the [Issues](https://github.com/Synergy-Tweaks/SynToolkit/issues) tab to see individual tickets, or check the [project board](https://github.com/orgs/Synergy-Tweaks/projects) for a status view of what is in progress.

> [!TIP]
> Comment on the issue you want to work on before starting, so we can assign it to you and avoid duplicating work.

## Thank You Contributors!
<a href="https://github.com/Synergy-Tweaks/SynToolkit/graphs/contributors" target="_blank"><img src="https://contrib.rocks/image?repo=Synergy-Tweaks/SynToolkit&columns=18" alt="Avatars of all contributors"></a>

## Third-party credits

Code, UI patterns, and libraries this project builds on:

- [WinUI 3 Gallery](https://apps.microsoft.com/detail/9P3JFPWWDZRC) — home page tile gallery and related UI patterns
- [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows) — settings cards, expanders, animations, and MVVM helpers
- [Atlas Toolbox](https://github.com/Atlas-OS/atlas-toolbox) — early base structure
- [Ameliorated Privacy+ Settings](https://github.com/Ameliorated-LLC/ame-settings-cli) — customization settings ideas
- [Ameliorated App Fetch](https://github.com/Ameliorated-LLC/appfetch) — installer / App Fetch catalog approach
- [AutoOS](https://github.com/tinodin/AutoOS) — disk cleanup approach
- [SteamTrayWebHelper](https://github.com/NobbyBollocks/SteamTrayWebHelper) — Steam WebHelper toggle (GPL-3.0)

Portable tools shipped under `SynToolkit/Assets` (CrystalDiskMark, 7-Zip, and related licenses in those folders) keep their own copyright and license notices.

MIT-licensed Ameliorated material retained in this repository is documented in [LICENSE-MIT.md](LICENSE-MIT.md).

## License

SynToolkit is licensed under the [GNU General Public License v3.0](LICENSE).

## Links

- **Discord:** [dsc.gg/kwanteks](https://dsc.gg/kwanteks)
- **YouTube:** [youtube.com/@kwanteks](https://youtube.com/@kwanteks)
- **Repository:** [github.com/Synergy-Tweaks/SynToolkit](https://github.com/Synergy-Tweaks/SynToolkit)

## Star History

<a href="https://www.star-history.com/?repos=synergy-tweaks%2Fsyntoolkit&type=date&legend=top-left">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=synergy-tweaks/syntoolkit&type=date&theme=dark&legend=top-left" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=synergy-tweaks/syntoolkit&type=date&legend=top-left" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=synergy-tweaks/syntoolkit&type=date&legend=top-left" />
 </picture>
</a>
