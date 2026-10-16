# ADR 012: Geschlossenes Upgrade und serverseitiger Administrator-Bootstrap

Status: umgesetzt; ergänzt/ersetzt ADR 008 nur für die hier ausdrücklich notwendigen API-, Authentifizierungs- und Schemaänderungen.

## Kontext

Bestehende IDs, Rezepte, Bilder und Mengen müssen erhalten bleiben. Das bisherige Passwort-Hashformat ist nicht als Identity-Hash zu interpretieren. Zwischen Schema-Upgrade und Passwortwahl darf kein unzugeordnetes Altmaterial öffentlich werden.

## Alternativen

Datenbank neu erstellen; altes Hashformat stillschweigend übernehmen; erstes öffentliches Admin-Registrierungsformular; Legacy-Rezepte zeitweise nullable/unautorisiert; geheimes Standardpasswort; destruktiver Down-Pfad.

## Entscheidung und Begründung

Historische Migrationen bleiben unverändert. Die additive Migration `20261016082127_AccountsAndCookbooks` erzeugt Identity-/Einladungs-/Cookbook-Tabellen, eine reservierte deaktivierte Identität ohne Passwort und ein ihr zugeordnetes Cookbook. Bestehende Rezepte werden mit unveränderten IDs dorthin zugewiesen. Die finale Cookbook-FK ist NOT NULL/Restrict ohne Default für zukünftige Rezepte; Zutaten-/Schritt-/Bild-Cascades bleiben gleich.

Ohne `AccountSetups`-Singleton antwortet das gesamte `/api` mit 503. Liveness und Development-OpenAPI sind davon getrennt. `--bootstrap-admin` migriert wie der normale Startup und aktiviert/benennt die reservierte Identität samt Passwort, Cookbook und Setup-Marker in einer Transaktion. Unterbrechung rollt zurück; ein abgeschlossener Retry ändert nichts. Das CLI nutzt verdeckte interaktive Eingabe oder geschützten stdin, keine Passwortargumente. `--recover-admin` ist eine getrennte bewusste Serveroperation für einen vorhandenen Administrator und kann keine gewöhnlichen Benutzer befördern.

## Konsequenzen

Upgrade verlangt Wartungsfenster, vollständiges Backup (DB inklusive Journals/WAL, Schlüssel, Konfiguration) und koordinierte Frontend-/Backend-Bereitstellung. Alte Owner-Cookies sind absichtlich ungültig. Unscoped-Recipe-Routen entfallen; JSON-Recipe-/Import-/Mengenfelder bleiben stabil. Der Content Root `RecipeAtlas.Api`, DataDirectory, Dateiname, Key-Ring und Volume-Identitäten werden nicht umbenannt. Ein neuer Cookie-Name schützt statt Legacy-Sitzungen zu übernehmen.

Startup-Migrationen bleiben erhalten, einschließlich Verfügbarkeitsrisiko bei Fehlern. `Down` verweigert ein verlustbehaftetes Zurückbauen; Rollback stellt das vollständige Pre-Upgrade-Backup mit alten Binärversionen wieder her. Nach dem Upgrade neu entstandene Daten sind darin nicht enthalten. [Runbook](../../accounts-and-cookbooks.md) beschreibt die konkreten Schritte.

## Verifikation

Console-Migrationstest beginnt beim alten Schema und prüft Daten/Zuordnung; Bootstrap-Fehlerinjektion nach echtem Write belegt vollständigen Rollback und sicheres Retry. Optionaler Cross-Binary-Test legt Daten und gültiges Owner-Cookie mit dem alten Publish an; vergleicht jede bisherige SQLite-Spalte einschließlich Bildbytes, IDs, Mengen und Reihenfolge; prüft geschlossene Zwischenphase, neue Ownership, verlängerte Historie, unveränderte Schlüssel, Cookie-Ablehnung und neue Anmeldung/Recovery. `has-pending-model-changes` und Migrationsdiscovery prüfen Snapshot/Tooling. Keine Benutzer-Datenbank wird für Tests migriert.

## Erneut entscheiden, wenn

Deployment-gesteuerte Migrationen, mehrere Instanzen oder eine weitere inkompatible Identitätsumstellung benötigt werden. Das Setup-Kommando ist kein allgemeiner Account-Provisionierungsmechanismus.
