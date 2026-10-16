# ADR 011: Eindeutige Ownership und zentrale Cookbook-Autorisierung

Status: umgesetzt.

## Kontext

Ein Rezept gehört genau einem Cookbook. Globale Account-Verwaltung darf keinen automatischen Zugriff auf private Inhalte gewähren. Mitgliedschaften können sich ändern, während ein Editor geöffnet ist.

## Alternativen

Doppelte Owner-Mitgliedschaft plus Owner-Spalte; globale Administrator-Vollmacht; Rollen im Cookie; allgemeine Permission-Engine; separate Mandantendatenbanken; lose Autorisierungsprüfung außerhalb einer späteren Schreibtransaktion.

## Entscheidung und Begründung

`Cookbook.OwnerUserId` ist die einzige Ownership-Quelle. `CookbookMembership` speichert ausschließlich Editor/Viewer (Composite-Key, Role-Check-Constraint), Owner wird abgeleitet. Domain-`CookbookPermissions` definiert Read/Write/Manage; unbekannte Enumwerte gewähren nichts. Owner bleibt unveränderlich und kann nicht austreten. Accounts und Cookbook-Rollen sind getrennt; alle aktiven Konten dürfen eigene Books erstellen.

`CookbookAccess` liest aktive Identität und aktuelle Mitgliedschaft aus SQLite. Recipe- und Image-Operationen verwenden diese gemeinsame Regel und filtern zusätzlich nach Cookbook-ID. URLs sind `/api/cookbooks/{cookbookId}/recipes/{id}`; Body-IDs können keinen Umzug bewirken. Kein EF-/Identity-Modell wird serialisiert. Offenlegung: 401 ohne Sitzung, 404 außerhalb zugänglicher Cookbooks, 403 bei fehlender Fähigkeit innerhalb eines zugänglichen Cookbooks.

Jeder sicherheitsrelevante Write startet Serializable/SQLite `BEGIN IMMEDIATE` vor der Prüfung und committet Berechtigung und Änderung in einer Reihenfolge mit Membership-/Account-Änderungen. Für mehrere dadurch zusammengehörige Statements genügt die bisherige implizite einzelne SaveChanges-Transaktion nicht mehr. Bereits autorisierte Writes dürfen vor einem wartenden Widerruf abschließen; spätere Writes sehen neue Rechte. Dies implementiert keine allgemeine Konflikterkennung für gleichzeitige Rezeptbearbeitung.

## Konsequenzen

SQLite serialisiert Writes. Owner-Deaktivierung löscht nichts; aktive Mitglieder behalten ihre Rollen und Inhalte. Reaktivierung benötigt neue Anmeldung. Images bleiben private DB-Blobs; alle API-Antworten no-store, keine ungeschützten Conditional-304-Pfade. Importe bleiben aktive-user-geschützte Draft-Generierung ohne Persistenz; Speichern prüft die Zielbookrechte neu.

Angular besitzt `SessionState` als einzige Benutzerzustandsquelle. HTTP-Requests werden bei Sessionwechsel abgebrochen; Epoch-Prüfungen verhindern nachträgliche Promise-Wiederbefüllung. `CookbookContext` ist Navigation/Anzeige, keine Autorität. Viewer-UI ist read-only; Serverprüfungen bleiben maßgeblich. Bei 403/404 im Editor bleiben Eingaben/Fotos erhalten und die Navigation fragt weiter nach ungesicherten Änderungen. Expliziter Logout/Accountwechsel löscht private Daten. Ein abgelaufenes Login allein verwirft keine ungesicherten Texte.

Diese Grenze kann spätere Shopping-List-/Cooking-Session-Operationen schützen; solche Features sind nicht implementiert. Keine Organisationen, Ownership-Transfers oder veränderbaren Rollenmodelle.

## Verifikation

HTTP-Tests mit zwei Ownern, Editor, Viewer, Außenstehendem und Administrator prüfen CRUD, Listen/Suche, Bilder, falsche Nested-ID, Legacy-Routen, Rollenänderungen, Austritt und Account-Deaktivierung. Ein realer SQLite-Test pausiert nach dem Permissions-Read und zeigt, dass ein paralleler Downgrade den früheren Write nicht überholt. Der nächste Write erhält 403. Angular prüft Rollen-/Admin-Sichtbarkeit, Guards und Session-/Editor-Lebenszyklus.

## Erneut entscheiden, wenn

Ownership-Transfer, mehrere Cookbooks pro Rezept, konfigurierbare Rechte oder größere Schreibkonkurrenz konkret verlangt werden. Die aktuelle Grenze rechtfertigt weder Microservices noch eine generische Policy-Engine.
