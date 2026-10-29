# Cloud-Deployment: lokale Nachweise

Stand: 29.10.2026. Beide Repositories arbeiten auf dem bestehenden Branch `feature/cloud-deployment`. Kein Merge, Rebase, Push oder Cloud-Release wurde ausgefuehrt.

## Ausgangspunkt

| Repository | Pfad | Audit-HEAD |
|---|---|---|
| Backend | `D:/Programmier Umgebung/RecipeAtlas` | `15dd02b7d46ae9c457caca688fda678738ec97da` |
| Frontend | `D:/Programmier Umgebung/RecipeAtlasFrontend` | `e141187a6be8e1494391982c89f366ebe9b61eae` |

Remote-Branchinformationen wurden geladen; beide Zielbranches waren vorhanden. Frontend war sauber. Die vorhandenen Backend-Neon-Dateien `.agents/`, `.codex/`, `skills-lock.json` und die `.gitignore`-Erweiterung bleiben ausserhalb dieser Commits. `.env.local` und die urspruengliche SQLite-Datenbank wurden nicht fuer Tests verwendet oder veraendert.

## Ausgefuehrte Checks

Die Befehle stehen im [Operatorguide](../cloud-deployment.md). Unter Windows wurde der installierte Python-3.12-Pfad statt des nicht funktionierenden python-Alias verwendet, npm ueber npm.cmd. Ein laufender Benutzer-Debugprozess sperrte dessen DLL; deshalb pruefen die HTTP-Runner die frisch gebaute Release-DLL ueber RECIPOKE_DLL, ohne diesen Prozess zu stoppen.

| Befehl / Nachweis | Ergebnis |
|---|---|
| `dotnet restore --locked-mode`, `dotnet build -c Release --no-restore` | Erfolgreich, Build ohne Warnungen/Fehler |
| `dotnet run -c Release --no-build --project tests/Recipoke.Tests` | 703 Checks; Domain, Operationen, Imports, Prozesse, SQLite, Architektur |
| `python tests/integration.py` | 63 HTTP-Checks einschliesslich Neustart |
| `python tests/accounts_integration.py` | 159 HTTP-Checks |
| `python tests/concurrency_integration.py` | 111 HTTP-Checks |
| `python tests/shopping_integration.py` | 73 HTTP-Checks |
| `python scripts/export_openapi.py --check` | Vertrag unveraendert, OpenAPI nur in Development |
| EF `has-pending-model-changes` fuer RecipeDb und PostgresRecipeDb, Release | Beide ohne Modellabweichung |
| `python scripts/test_cloud_env.py` | 4 Tests: URL-Escaping/TLS, abgewiesene Eingaben, keine Shellauswertung/fremden Konfigurationseintraege, geschuetzter Runtime-Export ohne Ueberschreiben |
| `python scripts/test_build_cloud.py` | Staging-Test schliesst Secrets, Datenbanken, Sidecars und Build-Caches aus |
| Frontend `npm test` | 257 Tests erfolgreich, einschliesslich unveraenderter PWA-/Unsaved-Work-Logik |
| Frontend `npm run check:contracts` | 266 Vertragschecks erfolgreich |
| Frontend `npm run build` | Produktionsbuild samt Angular-Worker erfolgreich |
| `python scripts/build_cloud.py --frontend ../RecipeAtlasFrontend --tag recipoke:cloud-test` | Gemeinsames linux/amd64-Image erfolgreich gebaut |
| `python tests/cloud_integration.py --image recipoke:cloud-test --frontend ../RecipeAtlasFrontend --chromium "C:/Program Files/Google/Chrome/Application/chrome.exe"` | 45 HTTP-Checks plus Konfigurations-, SQL-, Event-, Image- und Browserassertions erfolgreich |
| Workflow-YAML mit installiertem Prettier-YAML-Parser | Beide geaenderten Workflows syntaktisch gueltig |
| Git diff --check | Keine Whitespacefehler |

Der PostgreSQL-Test erzeugt einen lokalen PostgreSQL-18-Container und neue synthetische Konten. Geprueft wurden parallele initiale Migrationen, erneutes No-op-Upgrade, Status, Startup-Abbruch ohne Schema, fehlertolerante Konfigurationsgrenzen ohne Secret-Ausgabe, Administrator/Login, Einladungen und Cookbook-Rechte, Rezeptversionen/409, Foto-Bytes, Rechteaenderung gegen gleichzeitigen Save, atomare identische Shopping-Retries und abgewiesene Operation-ID-Wiederverwendung. Ein echter SignalR-Long-Poll-Client erhielt genau eine Invalidation pro Commit, keine fuer Replay/abgewiesene Requests und nach Reconnect wieder Events.

Frische App-Container teilen ausschliesslich PostgreSQL und uebergebene Schutzkonfiguration. Daten und vorhandener Cookie blieben nach Ersatz erhalten. Keyring-XML enthielt verschluesseltes Schluesselmaterial. Ein weiteres frisches Image konnte nach Zertifikatsrotation mit altem Entschluesselungszertifikat denselben Cookie verwenden. Passwortwechsel widerrief die alte Sitzung auch nach einem weiteren Neustart. Das Image laeuft nicht als root; im Anwendungsverzeichnis lagen keine lokalen Datenbanken, PFX, .env.local oder Git-Dateien.

Die Chromium-Pruefung verwendet separate Profile/Contexts bei 1280 und 390 Pixeln. Sie besucht das echte kombinierte Produktionsimage, wartet auf Worker-Kontrolle und inspiziert tatsaechliche Cache-Eintraege. Offline-Deep-Link-Navigation rendert die Verbindung-erforderlich-UI; API-/Foto-/Hub-Anfragen erhalten kein Shell-HTML, Offline-POSTs keinen Erfolg. Die HTTP-Pruefungen bestaetigen MIME-Typen, immutable Hash-Bundles, revalidierende Worker-/Indexdateien, no-store-APIs und 404 statt HTML fuer unbekannte Serverrouten. Ein anfaenglich zu strenger Browserassert erwartete auf Mobile ausgeblendeten Brandingtext; er wurde auf gerenderte UI und Connection-Hinweis korrigiert.

## Bewusst nicht ausgefuehrt / Grenzen

- Keine Neon-Verbindung, Migration, Datenuebertragung, Administratoranlage oder sonstige Produktionsaenderung; die vorhandene Neon-Konfiguration ist nicht als getestete Verbindung zu verstehen.
- Kein GitHub-gehosteter Workflow, GHCR-Push, Render-Service, Deploy-Hook oder oeffentliches TLS. Die Workflow-YAML-/lokalen Tests ersetzen keinen ersten kontrollierten Cloud-Release.
- Kein reales Android-/iOS-Geraet, installierter App-Modus oder neuer Build-A/Build-B-Updateversuch. Die vorhandene Update-/Unsaved-Work-Implementierung blieb unveraendert und ihre Unit-Tests bestanden.
- Kein Lasttest, kein realer Neon-Pooler/Scale-to-zero-Test und keine bezahlten Importprovider. Transaktionsgebundene Advisory Locks sind fuer den Pooler ausgelegt; der Prototyp serialisiert alle fachlichen Writes.
- Kein erneuter historischer Alt-Binaer-Upgrade-Lauf; SQLite-Migrationen und Snapshot sind unveraendert, aktuelle SQLite-Regressionssuiten bestanden.
- Render Free behaelt keine alten Chunk-Dateien nach Imagewechsel. Vorab gecachte Shellversionen funktionieren weiter; nach Cacheverlust kann ein bewusst bestaetigter Reload noetig sein.

Die finale lokale Image-ID und die finalen Commit-SHAs stehen im Abschlussbericht. Das Image wurde nach den lokalen Commits erneut mit diesen SHA-Labels gebaut; veroeffentlichte Registry-Digests existieren in dieser Aufgabe nicht.
