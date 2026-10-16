# ADR 001: Featureorientierter Monolith

Status: umgesetzt.

## Kontext

Eine kleine Single-owner-Anwendung hat bereits Rezept- und Importfeatures und genau einen Prozess.

## Alternativen

Horizontale technische Schichten; strikte Clean Architecture mit separaten Application/Infrastructure-Assemblies; Microservices.

## Entscheidung

Ein deploybares API-Projekt organisiert Anwendungsfälle nach Feature; nur Domain erhält eine eigene Assembly.

## Begründung

Direkte Navigation vom HTTP-Endpunkt zur Operation bleibt einfach. Deployment und Datenkonsistenz benötigen keine verteilte Infrastruktur.

## Konsequenzen

Logische Grenzen innerhalb der API sind Konventionen plus Tests, keine vollständig vom Compiler isolierten Module. Die Struktur ist bewusst hybrid.

## Verifikation

Build, bestehende HTTP-Suite und ArchitectureTests prüfen Grenzen und Verhalten.

## Erneut entscheiden, wenn

Mehrere unabhängig verantwortliche Teams oder nachgewiesene Anforderungen an getrennte Skalierung/Isolation.
