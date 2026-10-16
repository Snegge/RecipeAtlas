# ADR 008: Kompatibilität vor kosmetischer Vereinheitlichung

Status: als Refactoring-Entscheidung historisch umgesetzt. Die notwendigen Account-/Cookbook-Vertragsänderungen sind ausdrücklich in [ADR 012](012-account-upgrade.md) dokumentiert.

## Kontext

Der Recipoke-Rename hat absichtlich alte Sicherheits-, Konfigurations- und Speicheridentitäten bewahrt.

## Alternativen

API-Verzeichnis und alle Legacy-Namen vereinheitlichen; Daten neu erzeugen; neue Migration nur wegen Namespace-/Dateiverschiebung.

## Entscheidung

Content Root backend/RecipeAtlas.Api, Datenpfade, Cookie, Header, User-Secrets-ID, Data-Protection-Name, Deploymentvariablen, Volumes und Migrations-IDs bleiben stabil.

## Begründung

Relative DataDirectory-Werte, Cookies und vorhandene Installationen dürfen durch interne Strukturarbeit nicht ungültig werden.

## Konsequenzen

Das API-Verzeichnis weicht bewusst vom illustrativen Zielpfad Recipoke.Api ab. Historische CLR-Typnamen im EF-Snapshot werden angepasst; historische SQL-Operationen werden nicht umgeschrieben.

## Verifikation

EF-Discovery und Modellvergleich, identische Migrations-SQL und Upgrade-Smoke mit synthetischen Daten/Keys prüfen die tatsächliche Kompatibilität.

## Erneut entscheiden, wenn

Eine separate, ausdrücklich geplante und rückrollbare Konfigurations-/Datenmigration mit dokumentierter Übergangsstrategie.
