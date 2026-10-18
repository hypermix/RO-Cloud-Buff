# Dependencies

This build includes the following components. Their upstream license terms apply.

- .NET runtime and Windows Desktop/WPF runtime — Microsoft/.NET Foundation, MIT and accompanying notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT · https://github.com/dotnet/wpf/blob/main/LICENSE.TXT
- SharpPcap 6.3.1 — see upstream license files: https://github.com/dotpcap/sharppcap/tree/master/LICENSES
- Microsoft.Data.Sqlite 10.0.13 — MIT: https://github.com/dotnet/efcore/blob/main/LICENSE.txt
- SQLitePCLRaw and bundled SQLite — upstream licensing: https://github.com/ericsink/SQLitePCL.raw · https://www.sqlite.org/copyright.html

Npcap is a separately installed dependency and is NOT bundled or redistributed with this build. Its use and redistribution conditions are described at https://npcap.com/guide/.

Package dependency information can be regenerated from the included project files using `dotnet list package --include-transitive`. The developer source projects and existing capture data are not included in the portable binary ZIP.

Additional UI dependencies:
- Microsoft.Web.WebView2 1.0.4258.31: package license and Microsoft notices apply; Runtime installed separately.
- Lucide: ISC license, included at src/Web/lucide-LICENSE.txt (Web/lucide-LICENSE.txt in binary distribution).

- PacketDotNet 1.4.8 — MPL-2.0, included unmodified. Corresponding source: https://github.com/dotpcap/packetnet/tree/690707ce56d6e9c266daf6236c4f76ac5035334c

Binary distributions include additional package license files and metadata in licenses/.
