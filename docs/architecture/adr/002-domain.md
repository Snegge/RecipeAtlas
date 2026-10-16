# ADR 002: Unabhängige Domain-Assembly

Status: umgesetzt.

## Kontext

Mengenregeln und Rezeptentitäten waren mit HTTP- und EF-Code in einer Assembly vermischt.

## Alternativen

Alles im API-Projekt belassen; sämtliche DTOs und Importentwürfe in Domain verschieben; neue Value Objects für jedes Feld.

## Entscheidung

Nur vorhandene Entitäten sowie reine Ausdrucks-/Einheitenregeln in Recipoke.Domain verschieben. Extraktion, DTOs, Review-Entwürfe und EF-Mappings bleiben außerhalb.

## Begründung

Die Compilergrenze verhindert Frameworkabhängigkeiten. Rational und ParsedQuantity repräsentieren bereits echte fachliche Konzepte; mehr Typen hätten derzeit keinen belegten Nutzen.

## Konsequenzen

Die Entitäten bleiben veränderbar; die Domain ist keine vollständige DDD-Aggregatimplementierung. RecipeImage enthält Bytes, aber keine Speicher-/HTTP-Implementierung.

## Verifikation

Domain kompiliert ohne Paket-/Frameworkreferenzen; Fixtures und IL-/Assembly-Referenzprüfungen sichern die Grenze. EF meldet keinen Modellunterschied.

## Erneut entscheiden, wenn

Neue fachliche Invarianten oder ein anderer Persistenzbedarf machen andere Entitätsgrenzen notwendig.
