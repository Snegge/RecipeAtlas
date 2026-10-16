# ADR 005: SQLite und Bilder in derselben Datenbank

Status: umgesetzt.

## Kontext

Eine kleine private Sammlung speichert Rezeptdaten und ein begrenztes Bild pro Rezept auf persistentem lokalen Datenträger.

## Alternativen

Externer Datenbankserver; Dateiablage für Bilder; Object Storage; Änderung der Startup-Migrationspolitik.

## Entscheidung

SQLite, Bildblobs, vorhandene Fluent-Mappings, Migrationen und Startup-Migration bleiben bestehen.

## Begründung

Backup, Konsistenz und Betrieb bleiben für eine Instanz einfach. Ein strukturelles Refactoring ist kein Anlass für eine Speicher- oder Datenmigration.

## Konsequenzen

Eine beschreibbare Instanz, wachsende Backups und notwendige Sicherungen vor Upgrades bleiben Grenzen. Die neue Domain-Namespace-Identität ändert keine Tabellen oder Migration-IDs.

## Verifikation

Keine ausstehenden Modelländerungen, identische erzeugte Migrations-SQL, synthetischer Altbestand und echte SQLite-Cascade-/Transaktionstests.

## Erneut entscheiden, wenn

Messbare Bild-/Backupgröße, parallele Schreiblast oder konkrete Hochverfügbarkeitsanforderungen; deploymentgesteuerte Migrationen können separat entschieden werden.
