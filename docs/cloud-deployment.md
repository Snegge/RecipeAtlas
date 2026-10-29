# Render Free + Neon: ausführbarer Operatorablauf

Dieser Ablauf startet mit einem leeren Anwendungsschema in dem bereits verknüpften Neon-Projekt `recipoke-prototype` (`late-dew-86285796`), Organisation Snegge (`org-proud-boat-81326343`), Frankfurt, Datenbankbranch `production`. Es wird kein weiteres Neon-Projekt erstellt. Die folgenden Neon-Schreibbefehle wurden bei der Implementierung **nicht** ausgeführt.

## 1. Werkzeuge und lokale Geheimnisse

Benötigt: .NET SDK 10.0.401, Node 24.19.0, Python 3, Git und Docker mit Linux-Containern. PowerShell unter Windows kann `npm.cmd` verwenden. `python` ggf. durch den vollständigen installierten Python-Pfad ersetzen. Alle Backendbefehle im tatsächlichen RecipeAtlas-Checkout ausführen; Frontendpfad ausdrücklich angeben.

```
dotnet restore --locked-mode
dotnet tool restore
dotnet build -c Release --no-restore
python scripts/test_cloud_env.py
python scripts/test_build_cloud.py
```

`.env.local` aus dem abgeschlossenen Neon-Setup bleibt lokal und ignoriert. ASP.NET Core lädt diese Datei nicht automatisch. Ausschließlich `scripts/cloud_env.py --env-file .env.local -- ...` parst die beiden bekannten DATABASE_URL-Einträge, ohne Shell-Auswertung, und übergibt validierte Npgsql-Konfiguration als Prozessumgebung. SSL Mode=VerifyFull bleibt auch dann erhalten, wenn die generierte URL nur sslmode=require enthält. Datenbankfehlerdetails sind deaktiviert. Keine URLs/Passwörter in Argumente, CI-Logs, Angular oder Docker-Build-Argumente schreiben.

## 2. Schema auf Neon initialisieren/aktualisieren

Vor Ausführung Zielprojekt und Branch in Neon prüfen. Diese Befehle schreiben beim ersten Mal das Anwendungsschema in die bereits verknüpfte Datenbank:

```
python scripts/cloud_env.py --env-file .env.local -- dotnet backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll --migration-status
python scripts/cloud_env.py --env-file .env.local -- dotnet backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll --migrate
python scripts/cloud_env.py --env-file .env.local -- dotnet backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll --migration-status
```

Status 2 bedeutet ausstehende Migrationen; bei einer noch nicht vorhandenen Datenbank kann der erste Statusbefehl fehlschlagen. `--migrate` verwendet Database__MigrationConnection aus DATABASE_URL_UNPOOLED. Npgsql/EF nimmt die Migrationssperre; keinen Pooler für Administration verwenden. Nach einem Fehler nicht deployen, Ursache beheben und denselben Befehl erneut ausführen. Kein EnsureCreated/EnsureDeleted, kein Löschen oder Neuaufsetzen der Datenbank. Neue App-Versionen starten bei ausstehendem Schema nicht. Der bisherige SQLite-Startup-Pfad bleibt für lokale Entwicklung erhalten.

## 3. Ersten Administrator anlegen

```
python scripts/cloud_env.py --env-file .env.local -- dotnet backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll --bootstrap-admin
```

Username, Anzeigename und Passwort werden interaktiv abgefragt; Passwort nicht in Argumente schreiben. Erneutes Bootstrap ändert ein abgeschlossenes Setup nicht. Gezielte Recovery verwendet `--recover-admin`; sie widerruft frühere Sitzungen dieses Administrators. Es gibt keinen öffentlichen Bootstrap-Endpunkt.

## 4. Schutzmaterial und lokales Image

Einen sicheren, nicht versionierten Ablageort verwenden. Das vorhandene `.tools` ist ignoriert. Verzeichnis ggf. mit `mkdir .tools` anlegen. Auf gemeinsam genutzten Rechnern Zugriff auf dieses Verzeichnis auf den eigenen Benutzer beschränken.

```
dotnet backend/RecipeAtlas.Api/bin/Release/net10.0/Recipoke.Api.dll --generate-protection-certificate .tools/protection.json
python scripts/build_cloud.py --frontend ../RecipeAtlasFrontend --tag recipoke:local
python tests/cloud_integration.py --image recipoke:local --frontend ../RecipeAtlasFrontend --chromium "C:/Program Files/Google/Chrome/Application/chrome.exe"
```

Die Zertifikatsdatei wird nur neu angelegt, nie überschrieben. Sie enthält ein zufälliges Passwort und ein base64-kodiertes PKCS#12 mit privatem Schlüssel; sicher außerhalb des Rechners sichern. Tests lesen niemals .env.local: sie erzeugen einen lokalen PostgreSQL-18-Container, zufällige Zugangsdaten, eigenes Zertifikat und frische App-Container. Sie entfernen nur ihre eindeutig benannten Testressourcen. Den Chromium-Pfad an das lokale System anpassen (Linux z.B. /usr/bin/google-chrome). Ohne --chromium meldet der Test den Browsercheck als ausgelassen. Der Release-Workflow verlangt ihn. Der Test erwartet die Release-DLL für Zertifikatsgenerierung und installierte Frontend-Abhängigkeiten (`npm ci`).

Build-Layout: Staging-Wurzel mit `backend/backend/RecipeAtlas.Api`, `backend/backend/Recipoke.Domain`, `backend/tools`, `frontend/src`, `frontend/public` und den ausdrücklich kopierten Build-/Lockdateien. Das Skript erzeugt dieses Layout in einem temporären Verzeichnis. `deploy/Render.Dockerfile` wird dort als Dockerfile verwendet. Es gibt keine COPY-Zugriffe außerhalb des Kontexts. Ein lokaler Build verwendet den aktuellen Arbeitsbaum; erst ein sauberer, committeter Checkout stimmt exakt mit den SHA-Labels ueberein. Der Release-Workflow verwendet saubere Checkouts. Ziel linux/amd64; Runtime-Benutzer app, beschreibbar benötigt wird nur das temporäre Verzeichnis `/tmp` für bestehende Importprozesse. Keine Build-Werkzeuge im Runtime-Image; Python/yt-dlp/Node sind bestehende Import-Laufzeitabhängigkeiten.

## 5. GitHub-Voraussetzungen

Workflow `.github/workflows/release-cloud.yml` zuerst durch normalen Review auf den Default-Branch bringen. workflow_dispatch wird normalerweise erst dort entdeckt. Diese Aufgabe merged oder pusht nichts.

Environments konfigurieren:

- `release-source`: nur Default-Branch-Workflow zulassen, Required Reviewers. Secret FRONTEND_READ_TOKEN mit ausschließlich Contents:read für Snegge/RecipeAtlasFrontend (Fine-grained PAT oder GitHub-App-Token). Variable TRUSTED_FRONTEND_REF standardmäßig main, nur administrativ auf einen geschützten Release-Ref ändern. Beide Eingaben müssen vollständige 40-stellige Commit-SHAs sein und Vorfahren der jeweils vertrauten Referenz sein.
- `release-publish`: Required Reviewers und Default-Branch-Beschränkung. GITHUB_TOKEN erhält nur in diesem Job packages:write; keine persönliche Publish-PAT nötig. GHCR muss Actions-Packagezugriff erlauben.
- `render-prototype`: Required Reviewers, Default-Branch-Beschränkung; nur hier RENDER_DEPLOY_HOOK_URL und optional Variable RENDER_HEALTH_URL hinterlegen.

Workflow-Verifikation führt gewählten Code ohne Publish-/Deploy-Secrets aus. Checkout-Credentials werden nicht im Git-Config behalten. Veröffentlichung lädt ausschließlich das geprüfte Image-Artefakt. Keine PR-Trigger für Releases. Prüfer müssen beide konkreten SHAs freigeben; niemals untrusted Revisionen freigeben oder Environment-Schutz lockern.

## 6. Erstes Image veröffentlichen

Nach Review/Default-Branch-Aufnahme manuell `Release combined application` starten. backend_revision/frontend_revision: die geprüften vollständigen Commit-SHAs; deploy=false. Der Workflow baut und prüft beide Checkouts, führt SQLite und PostgreSQL/Containerprüfungen aus und veröffentlicht erst danach nach GHCR.

Die Summary enthält beide SHAs und `ghcr.io/snegge/recipeatlas@sha256:...`. Das Image erhält einen eindeutigen Tag aus beiden SHAs, Run-ID und Attempt. Kein latest-Release. Digest aus der Summary übernehmen; nicht einen mutable Tag als Rollback-Identität verwenden. Veröffentlichte Release-Images für Rollback behalten, keine automatische Package-Löschung konfigurieren.

## 7. Render Existing Image erstellen

In Render: New > Web Service > Existing Image. Exakten GHCR-Digest aus Schritt 6 eintragen, Name recipoke-prototype, Region Frankfurt, Instance Type Free, Health Check Path `/health`. Keine persistenten Disks oder Pre-Deploy-Commands. Render setzt PORT; das Image verwendet 10000 als Standard und bindet an 0.0.0.0. Startkommando des Images beibehalten. Noch vor dem ersten sinnvollen Start Runtime-Secrets konfigurieren; fehlende DB/Key-Konfiguration führt bewusst zum Abbruch.

## 8–10. Runtime-Konfiguration, privates GHCR, öffentliche URL

Bei privatem GHCR-Package in Render Registry-Credentials mit GitHub-Benutzer und Token für read:packages hinterlegen; bei privatem Repository Zugriff/SSO des Tokens berücksichtigen. Ein öffentliches Package kann anonym gezogen werden, würde jedoch das Image öffentlich zugänglich machen. Sichtbarkeit bewusst selbst wählen; diese Aufgabe ändert sie nicht.

Nach Festlegung der Render-URL (hier als konkret gewünschter Servicename https://recipoke-prototype.onrender.com; die tatsächlich von Render zugeteilte URL verwenden, falls abweichend):

```
python scripts/cloud_env.py --env-file .env.local --protection .tools/protection.json --public-url https://recipoke-prototype.onrender.com --write-render-env .tools/render.env
```

Datei privat in Renders Environment-Einstellungen importieren. Sie enthält nur die benötigten Runtime-Einträge, nicht die direkte Migrationsverbindung. Danach schützen bzw. sicher entfernen; nicht hochladen oder committen. Render terminiert HTTPS; die App ignoriert Forwarded-Headers, erzwingt Secure-Cookies und baut absolute Einladungslinks ausschließlich aus Accounts__PublicAppUrl. Kein HTTPS-Redirect im Container, keine Redirect-Schleife. Keine CORS-/Cookie-Umstellung nötig.

| Exakter Schlüssel | Zweck | Wo erforderlich | Geheim | Herkunft |
|---|---|---|---|---|
| Database__Provider | PostgreSql ausdrücklich auswählen | Cloud und Operator | nein | Literal PostgreSql |
| Database__Connection | Npgsql Runtime-Verbindung | App | ja | Gepoolte DATABASE_URL, durch cloud_env.py konvertiert |
| Database__MigrationConnection | Direkte Verbindung für migrate/status/bootstrap | Operator, nicht Render | ja | DATABASE_URL_UNPOOLED, durch cloud_env.py konvertiert |
| DataProtection__Certificate | Base64-PKCS#12 mit privatem Schlüssel | App | ja | protection.json |
| DataProtection__Password | Passwort für PKCS#12 | App | ja | protection.json |
| DataProtection__PreviousCertificates__0__Certificate | Altes PKCS#12 für Entschlüsselung | App während/nach Rotation | ja | vorheriges protection.json; weitere Indizes möglich |
| DataProtection__PreviousCertificates__0__Password | Passwort des alten Zertifikats | App bei Rotation | ja | vorheriges protection.json |
| Accounts__PublicAppUrl | Vertrauenswürdiger HTTPS-Origin | Render | nein | Zugewiesene Render-URL ohne Pfad/Query/Fragment |
| PORT | Öffentlicher interner HTTP-Port | Render | nein | Render; Standard 10000 |
| ASPNETCORE_ENVIRONMENT | Production | Render | nein | Literal Production |
| RecipeText__ApiKey | Optionaler bestehender AI-Provider | Nur bei bewusst aktiviertem Textimport | ja | Providerkonto; für Prototyptests leer lassen |
| FRONTEND_READ_TOKEN | Privaten zweiten Checkout lesen | release-source | ja | GitHub Fine-grained PAT/App mit Contents:read |
| TRUSTED_FRONTEND_REF | Geschützter vertrauenswürdiger Frontend-Ref | release-source, optional | nein | main ist Standard |
| RENDER_DEPLOY_HOOK_URL | Release auf Render anstoßen | render-prototype, nur deploy=true | ja | Render Service Settings > Deploy Hook |
| RENDER_HEALTH_URL | Versionierten Rollout prüfen | render-prototype, optional | nein | Öffentliche Service-URL plus /health |

Keine Neon API-Credentials in Render oder Angular benötigt. PostgreSQL-Passwörter sind Runtime-Secrets, keine Build-Argumente.

## 11–12. Deploy-Hook und erster Smoke

Render Deploy Hook als RENDER_DEPLOY_HOOK_URL nur im Environment render-prototype speichern. Der optionale Job ergänzt imgURL mit dem **geprüften Digest**; Render verlangt dieselbe Registry/Repository-Basis wie die Servicekonfiguration. Der Hook wird niemals geloggt. HTTP-Fehler brechen ab. 200/202 bedeutet nur angenommen. Bei RENDER_HEALTH_URL prüft der Job begrenzt auf HTTP 200 und beide Release-SHA-Header; ohne URL bleibt Render-seitige Gesundheit ausdrücklich ungeprüft. Die Workflow-Concurrency verhindert überlappende Releases dieses Workflows; Render-Deploys außerhalb des Workflows ebenfalls koordinieren.

Nach dem ersten Start prüfen: `/health` 200, `/login` und direkte Frontend-Routen laden, Manifest/Icons korrekt, `/api/auth/me` ohne Cookie 401, unbekannte `/api`-Route 404 statt HTML. Anmelden, Cookbook/Rezept/Bild anlegen, zweiten Benutzer einladen, Einkaufslisten teilen. App neu deployen: Daten und Login müssen erhalten bleiben. PWA installieren und Offline-Shell prüfen; private Daten bleiben onlinepflichtig. Render Free schläft bei Inaktivität; erster Aufruf kann langsam sein. Keine künstlichen Keep-alives. Vorhandene Long-Poll-Reconnects und Refreshes stellen aktuelle Shopping-Daten wieder her.

## 13. Weitere Releases

Tests mit exakten SHAs ausführen, DB/Keys sichern, notwendige Migrationen kontrolliert mit der passenden neuen Backend-DLL vor Veröffentlichung/Deployment ausführen. Für inkompatible Schemaänderungen Wartungsfenster planen, Schreibzugriffe stoppen und alte App-Version nicht parallel betreiben. Danach Workflow mit deploy=true starten oder publizierten Digest manuell in Render deployen. Bei Migrationfehlern kein Deployment. Nie SQLite-Migrationen auf PostgreSQL anwenden. `dotnet ef` benötigt jetzt `--context RecipeDb` oder `--context PostgresRecipeDb`.

Das Image enthält alle aktuellen Angular-Chunks, die der Worker vorlädt. Alte Tabs können ihre gecachten Versionen weiterverwenden; bei Cache-Verlust nach Image-Wechsel fehlen alte Chunks und ein expliziter Reload kann nötig sein. Kein dauerhaftes Asset-Archiv in Render Free. Unsaved-Work-Schutz bleibt bestehen; vor Releases lange offene Tabs prüfen. Alte Images bleiben für Rollback verfügbar.

## 14. Backups, Schlüsselrotation und Rollback

Neon-Datenbank mit pg_dump über direkten Endpoint sichern (Passwort via geschütztem PGPASSFILE, nicht CLI-Argument); zusätzlich Neon-Restore-Fenster des tatsächlichen Plans prüfen. DataProtectionKeys mit den übrigen Tabellen sichern. protection.json und alle alten Zertifikate separat verschlüsselt sichern. Datenbankbackup allein genügt nicht zum Entschlüsseln der Keyring-Einträge.

Rotation: neues protection.json an anderem Pfad erzeugen; aktuelle Certificate/Password durch neue Werte ersetzen, bisherige Werte unter PreviousCertificates__0 behalten (weitere Vorgänger ebenfalls behalten). Alte Entschlüsselungszertifikate erst entfernen, wenn kein aufbewahrter Keyring/Backup mehr davon abhängt; nicht einfach nach Cookieablauf löschen. Bestehende Keyring-Zeilen werden nicht automatisch mit dem neuen Zertifikat umverschlüsselt. Zertifikatsgültigkeit/Rotation vor den fünf Jahren planen.

Image-Rollback verwendet einen behaltenen Digest. Das rollt keine Datenbank zurück. Nur schema-kompatible ältere Images nutzen; andernfalls konsistentes DB-Backup und passende Key-Konfiguration im Wartungsfenster wiederherstellen. Keine automatische Down-Migration. Verlorenes Schutzmaterial erzwingt neue Anmeldung und kann andere geschützte Tokens unbrauchbar machen.

## Separater Opt-in-Transfer bestehender SQLite-Daten

Hier wird kein Transfer implementiert oder ausgeführt. Vor einem späteren Transfer Schreibzugriffe stoppen und SQLite-Datei plus Keyring sichern. Gemeinsam übertragen: Identity-Konten samt PasswordHash/SecurityStamp, Setup-Marker, Invitations, Cookbooks/Memberships/Einladungen, Rezepte/Zutaten/Schritte mit Reihenfolge und Mengenstrings, alle Bildbytes und MIME-Typen, Shopping-Listen/Mitglieder/Items/Reservations/Tokens/Receipts/Tombstones. IDs, Versionen und Operation-IDs erhalten; referenzielle Reihenfolge beachten. Bereits abgelaufene Reservierungen nicht versehentlich verlängern. Identity-Passworthashes sind grundsätzlich mit demselben Identity-Hasher kompatibel; repräsentative Logins prüfen. Neue PostgreSQL-Setup-Platzhalter müssen mit dem Bestand kontrolliert abgestimmt werden.

Kein pauschales sqlite dump in psql importieren: Datentypen/UTC-Conversions und Provider-Schema unterscheiden sich. Vor Freigabe Tabellenanzahlen, Mitgliedschaften, IDs, Mengen/Reihenfolge und SHA-256 aller Fotos vergleichen; Sequenzen für generierte integer-IDs auf mindestens MAX(Id) setzen (UUIDs besitzen keine Sequenz). Der unterstützte Erstprototyp erwartet neue Anmeldung; alte Cookie-Kompatibilität erfordert zusätzlich einen gesondert geprüften Transfer und Re-Schutz des bisherigen Keyrings mit gleichem ApplicationName. Keine echten Daten in Git oder CI-Artefakte exportieren.

Quellen: [Render Free](https://render.com/docs/free), [Deploy Hooks](https://render.com/docs/deploy-hooks), [ASP.NET Core Key Storage](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers), [Neon Pooling](https://neon.com/docs/connect/connection-pooling).
