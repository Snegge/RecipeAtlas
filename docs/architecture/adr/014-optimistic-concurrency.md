# ADR 014: Explizite optimistische Konkurrenzkontrolle

## Status und Kontext

Angenommen. Ersetzt ausschließlich die bisherige Last-write-wins-Grenze; ADR 003 (direktes EF), 005 (SQLite/Bilder), 009 (Sitzungen), 011 (Berechtigungsreihenfolge) und 013 (Consent) gelten weiter. Zwei berechtigte Personen können dasselbe Rezept laden und anschließend verschiedene Fassungen speichern. Der bisherige letzte Schreibrequest konnte unbemerkt Zutaten, Schritte oder Bilder überschreiben. Gleiches gilt für Cookbook-Metadaten und Mitglieder.

## Alternativen

Zeitstempel sind wegen Auflösung und Uhrzeitabhängigkeit ungeeignet. SQL-Server-rowversion steht in SQLite nicht zur Verfügung. Editiersperren, SignalR, CRDTs, automatische Feldmerges und ein universelles Conflict-Framework wären zusätzliche Produkt- und Betriebsentscheidungen. Ein Vergleich im Anwendungsspeicher vor einem unbedingten UPDATE schützt nicht atomar. Generische Repositories oder ein neuer Mediator verbessern diesen Vergleich nicht.

## Entscheidung

`Recipe.Version` und `Cookbook.Version` sind unabhängig erzeugte, nicht leere GUIDs. Sie werden als opake Strings transportiert; Angular berechnet oder interpretiert sie nicht. EF konfiguriert beide als Concurrency-Token. `ConcurrencyWrites.Advance` setzt den erwarteten Originalwert und einen neuen aktuellen Wert; EF verwendet den Originalwert im UPDATE/DELETE-Prädikat. Domain enthält nur die jeweiligen Eigenschaften, keine EF-/HTTP-Abhängigkeit und keine künstliche gemeinsame Aggregate-Basisklasse.

Ein Rezept umfasst seine Metadaten, geordneten Zutaten/Schritte und DB-Bildbytes. Das Cookbook umfasst Metadaten, Cover, akzeptierte Mitgliedschaften und offene Einladungen einschließlich Annahme/Ablehnung. Auch eine Mitgliedsänderung kann deshalb mit einer Umbenennung konkurrieren. Rezeptänderungen einschließlich Create/Delete ändern **nicht** die Cookbook-Version; verschiedene Rezepte bleiben unabhängig.

Jede Operation besitzt weiterhin ihre SQLite-Schreibtransaktion. `BEGIN IMMEDIATE` vor der zentralen Berechtigungsprüfung ordnet konkurrierende Rechteänderungen und Writes: der zuerst reservierte Writer darf nach seiner Prüfung abschließen, der nachfolgende prüft danach gültige Rechte. Ein SaveChanges genügt für normale Aggregate; bestehendes Cookbook-Delete umfasst zusätzlich ExecuteDelete und den abschließenden versionierten DELETE. Bei fehlgeschlagenem CAS wird alles zurückgerollt, Tracking verworfen und mit einer neuen Transaktion erneut auf aktuelle Aktivität, Zugriff und Existenz geprüft. Nur `DbUpdateConcurrencyException` wird übersetzt; sonstige SQLite-/Integritäts-/Providerfehler werden nicht zu Konflikten umetikettiert. Es gibt keinen inhaltlichen Retry; die bestehende SQLite-Contention-Politik bleibt unverändert.

Zusammenhängende GET-Repräsentationen verwenden einen verzögerten SQLite-Lesesnapshot. Ein split-query-Rezept oder das Member-Ergebnis kann damit keine älteren Inhalte mit einer neueren Version verbinden. Das gemeinsame `ReadSnapshot` besitzt nur technische Transaktionsressourcen, keine fachlichen Commits.

## Vertrag und Begründung

JSON-Mutationen vorhandener Aggregate verlangen `expectedVersion`; DELETE verwendet genau ein starkes `If-Match: "<version>"`. **Rohe Bild-PUTs verwenden ebenfalls If-Match**, weil ihr bestehender Binärbody keinen JSON-Versionsparameter aufnehmen kann. Kein neues Upload-/Staging-Protokoll ist nötig. Creation benötigt keine Version. Fehlend: 428/`concurrency_precondition_required`; missgebildet: 400/`invalid_concurrency_precondition`; veraltet im JSON: 409, im Header: 412, jeweils `concurrency_conflict`. Wildcards, schwache Tags und Tag-Listen sind unzulässig. Dies ist kein allgemeines HTTP-Cache-Protokoll. Fehler enthalten keine aktuelle Ressource oder Version. Authentifizierung/Scope/Rolle behalten Vorrang, Versionen sind keine Zugriffsberechtigung.

Members-GET liefert `{ cookbook, members }` aus einem Snapshot. Cookbook-PUT/Coveroperationen liefern die aktuelle Repräsentation; Member-Writes liefern `{ version }`. Austritt/Selbstentfernung liefert 204 ohne nun geschützte Inhalte. Recipe-Bildänderungen liefern `{ imageUrl, version, updatedAtUtc }`; Bild-DELETE ist deshalb jetzt 200 statt 204. Einzelheiten und weitere Statusänderungen: [Runbook](../../shared-editing.md).

Angular besitzt Baseline und nachgeladenen Serverstand getrennt. Lokale Formularwerte und Bildblobs bleiben erhalten. Ein gemeinsamer rein darstellender Material-Dialog verlangt explizite Quellenwahl je Feld bzw. kompletter Zutaten-/Schrittsektion. Anwenden setzt die besprochene Serverfassung als Baseline, lässt den Entwurf dirty und speichert nicht. Laden der Serverfassung verlangt Discard-Bestätigung. Ein neuer konkurrierender Write erzeugt erneut einen Konflikt. Kein „Overwrite anyway“, Polling oder automatischer Merge.

Details und Foto bleiben zwei Requests. Der zweite nutzt die vom ersten zurückgegebene Version. Sein Fehler lässt die erfolgreiche Textänderung bestehen und das Foto lokal ausstehend. Ein expliziter Retry ohne neue Textänderung wiederholt nur das Foto. Cookbook-Einstellungen sperren Memberaktionen bei ungespeicherten Metadaten/Coveränderungen. Nach Memberkonflikt wird nur die Anzeige erneuert; die Aktion muss erneut gewählt werden.

## Konsequenzen

Backend und Frontend müssen gemeinsam veröffentlicht und alte Tabs neu geladen werden. Alte Clients erhalten keine unbedingte Schreibmöglichkeit. SQLite bleibt ein Writer-System; Tokens lösen keine Skalierungs-/Offline-Anforderung. Vergleiche sind konservativ, auch bei Änderungen verschiedener Felder oder semantisch identischen PUTs. Cookbook-Delete prüft Cookbook-Metadaten/Mitgliedschaft, **nicht** jede Rezeptversion: es entfernt im bestätigten Scope alle zu diesem Zeitpunkt enthaltenen Rezepte. Die angezeigte Rezeptanzahl ist informativ.

Die additive Migration initialisiert für jede vorhandene Zeile eine eigene zufällige Version, ohne alte Spalten, IDs, Beziehungen oder Bilddaten zu ändern. Down verweigert ein unsicheres Zurückfallen auf unbedingte Writes. Backups und koordinierter Rollback sind erforderlich. Historische Migrationen und kompatible Pfade/Schlüssel bleiben unverändert.

## Verifikation

`ConcurrencyTests` verwendet unabhängige SQLite-Verbindungen, echte CAS-Fehler, Child-Rollback und einen kontrollierten Berechtigungswechsel nach Rollback. Der bestehende Writer-/Rechterace verwendet nun Transaktionsinterzeption statt Timing-Schlaf. `concurrency_integration.py` prüft HTTP-Status/Codes, Child-/Bildatomarität, unabhängige Aggregate, Mitgliederraces und Widerruf. `concurrency_upgrade.py` startet die gepinnte echte Vorversion, migriert synthetische Daten und vergleicht sämtliche alten Spalten, Bildbytes, Keys, Cookies und Migrationshistorie. Frontend-Specs prüfen explizite Review-Schritte, partielle Saves und verspätete Sessionantworten; der optionale Chromium-Smoke verwendet zwei unabhängige angemeldete Browserkontexte.

## Erneut entscheiden, wenn

Messbare SQLite-Writerlast, mehrere API-Instanzen, externe Clients mit vollständiger ETag-Semantik oder echte Offline-/Live-Zusammenarbeit konkrete Anforderungen werden. Ein feinerer Membership-Token wäre erst bei belegten unnötigen Konflikten zu begründen. Diese Fähigkeiten sind hier nicht implementiert.
