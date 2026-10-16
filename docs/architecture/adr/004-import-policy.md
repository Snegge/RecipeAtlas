# ADR 004: Providerverträge und Ausführungsrichtlinie

Status: umgesetzt.

## Kontext

Die Importstrategie existierte bereits, aber Shared Orchestration prüfte konkret auf SocialRecipeImporter, um Deadlines und Fehler zu wählen.

## Alternativen

Typchecks erweitern; alle Provider auf ein identisches Timeout zwingen; eine allgemeine Policy Engine oder Job Queue einführen.

## Entscheidung

RecipeImporterRegistration verbindet IRecipeImporter mit ImportExecutionPolicy. Kleine Application-eigene Ports trennen Modell, Metadaten und Prozesse.

## Begründung

Priorität und bestehende 20/80-Sekunden-Deadlines bleiben erhalten, während neue Provider ihre Richtlinie explizit registrieren. Fehler und Cancellation bleiben beobachtbar.

## Konsequenzen

Importe liefern Entwürfe und besitzen keine Persistenz. Textdeadline und interne Prozess-/Modelllimits bleiben separate vorhandene Grenzen. yt-dlp benötigt weiterhin deploymentseitige Egress-Isolation.

## Verifikation

Substitute prüfen Auswahl, Aufruferabbruch, Policy-Timeout und Retrieval-Fehler; vorhandene Tests sichern Redirects, Outputs, Modellvalidierung und Prozesslimits.

## Erneut entscheiden, wenn

Nachgewiesener Bedarf an dauerhaften Jobs, weiteren Ausführungsformen oder unabhängig skalierter Isolation.
