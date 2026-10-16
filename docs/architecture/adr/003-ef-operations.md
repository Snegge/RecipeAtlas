# ADR 003: Direktes EF Core in konkreten Operationen

Status: umgesetzt.

## Kontext

RecipeEndpoints vereinte Bindung, Validierung, Mapping und Commits. EF ist bereits die vorhandene Unit of Work.

## Alternativen

Generische Repositories; weitere Unit-of-Work-Wrapper; Mediator-Dispatch; alle EF-Abfragen im HTTP-Endpunkt belassen.

## Entscheidung

CreateRecipe, UpdateRecipe, DeleteRecipe sowie Lese-/Bildoperationen verwenden RecipeDb direkt und besitzen ihre Commits.

## Begründung

Die konkrete Operation macht Transaktionsgrenzen sichtbar, ohne EF-Methoden hinter identischen Abstraktionen zu verbergen. Ein universelles Result-Framework wird nicht benötigt.

## Konsequenzen

Application-Code ist bewusst an EF gekoppelt. Endpoint- und Domain-Code bleiben davon getrennt. Mehrere unabhängige SaveChanges-Aufrufe für eine atomare Operation sind untersagt.

## Verifikation

SQLite-Operationstests prüfen Reihenfolge, Bildverhalten, Cascades und Rollback nach injiziertem Fehler hinter einer bereits ausgeführten Schreibanweisung; HTTP-Tests prüfen die Abbildung.

## Erneut entscheiden, wenn

Komplexe mehrstufige Transaktionen oder konkrete alternative Speicheranforderungen rechtfertigen eine andere Grenze.
