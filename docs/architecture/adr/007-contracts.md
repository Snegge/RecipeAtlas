# ADR 007: Verträge und Mengenfixtures über zwei Repositories

Status: umgesetzt.

## Kontext

Backend und Frontend teilen Regeln und JSON-Verträge, sind aber getrennte möglicherweise private Repositories.

## Alternativen

Zusätzliches Shared Repository/Package; TypeScript-Generator mit weiterer Toolchain; Frontend-CI mit implizitem Zugriff auf das private Backend.

## Entscheidung

Backend ist maßgeblich für OpenAPI, Domain-Mengendefinitionen und Tests/fixtures. Ein explizites Sync-Skript kopiert Snapshots; ein Compiler-basierter Frontend-Check prüft die vorhandenen Transporttypen.

## Begründung

Die kleine Vertragsfläche erhält Nullability-/Typprüfungen ohne neue Modellhierarchie oder Generatorabhängigkeit. Unabhängige CI bleibt ohne zusätzliche Credentials ausführbar.

## Konsequenzen

Ein Snapshot kann hinter einem noch nicht koordinierten Backend-Release zurückliegen. Deshalb bleibt der lokale Cross-Repository-Check verbindlich für gemeinsame Änderungen. Formularmodelle bleiben unabhängig.

## Verifikation

OpenAPI --check startet isoliert; Frontend prüft Typen, Routen, Header und Fixture-Fingerprints. Eine negative Typ-Probe prüft den Checker selbst.

## Erneut entscheiden, wenn

Wesentlich größere API-Fläche oder weitere Clients rechtfertigen erneut die Bewertung eines gepinnten TypeScript-Generators.
