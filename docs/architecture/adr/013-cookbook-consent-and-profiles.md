# ADR 013: Cookbook-Einladungen, Profilbilder und mobile Bearbeitung

Status: umgesetzt. Ergänzt ADR 009–012; die frühere Einschränkung „kein Cookbook-Delete“ und das sofortige Hinzufügen von Mitgliedern werden durch den ausdrücklich erweiterten Produktauftrag ersetzt.

## Kontext

Die mobile Oberfläche braucht eine App-Navigation, eine bebilderte Cookbook-Sammlung und eigene Erstellungsseiten. Neue Mitglieder sollen selbst zustimmen. Profile benötigen Anzeigename, Kontakt-E-Mail und Bild. Cookbook-Löschung soll nach ausdrücklicher Bestätigung den gesamten Inhalt entfernen. Bestehende Sitzungen, Mitgliedschaften und Rezeptdaten müssen weiter funktionieren.

## Alternativen

Reine Frontend-Platzhalter ohne Persistenz; sofortige Mitgliedschaft wie bisher; gemeinsames Tokenmodell für Account-Erstellung und Cookbook-Zustimmung; öffentliche Avatar-/Coverpfade; Object Storage; erzwungene Neuprovisionierung aller Benutzer; externe E-Mail-/Bildverarbeitungsdienste; nur leere Cookbooks löschen.

## Entscheidung und Begründung

- **Einladungen:** `CookbookInvitation` enthält nur offene Einladungen an existierende aktive Accounts. Ein Unique-Index auf Cookbook/User verhindert doppelte offene Einladungen. Die vorhandene Account-Erstellungseinladung bleibt ein separater Lebenszyklus. Accept legt Membership an und entfernt die Einladung in einer SQLite-Serializable-Transaktion; Decline entfernt nur die Einladung. Revoke/Accept werden durch dieselbe Transaktionsordnung serialisiert; ein bereits abgearbeiteter Eintrag liefert 404. Pending allein gewährt keinen Rezeptzugriff. Die Inbox sortiert deterministisch und liefert höchstens fünf Einträge; die UI lädt nach jeder Entscheidung nach und zeigt kein leeres Panel.
- **Erstellung:** Metadaten und initiale Einladungen werden zusammen committed. `StageAsync` speichert nicht unabhängig und verlangt die äußere Transaktion. Das Cover ist wie ein Rezeptfoto ein nachfolgender Upload. Das Frontend behält die erzeugte ID bei Uploadfehlern und erstellt beim Retry kein zweites Cookbook.
- **Bilder:** Cover und Avatare bleiben begrenzte JPEG/PNG/WebP-Blobs in SQLite. Eine gemeinsame Signaturpolitik wird auch von bestehenden Rezeptbildern verwendet, ohne deren Vertrag zu ändern. Es ist keine vollständige Bilddekodierung. Versionierte private URLs verhindern eine unveränderte DOM-Bildquelle nach Bildtausch; Responses bleiben no-store. Cover-Preview ist für den jeweiligen offenen Invite-Empfänger erlaubt, nicht für beliebige Außenstehende. Avatare sind für Selbstzugriff, globale Account-Administration, gemeinsame Mitglieder und die konkrete Owner/Invite-Beziehung freigegeben.
- **Profile/E-Mail:** Anzeigename und syntaktisch gültige, normalisierte, eindeutige E-Mail werden serverseitig validiert. Neue Account-Einlösungen verlangen E-Mail. Bestehende Benutzer und das unveränderte Bootstrap-Protokoll dürfen zunächst null behalten und vervollständigen das Profil später. E-Mail ist Kontaktinformation, kein neuer Login-Identifier; sie ist nur selbst/admin sichtbar. Es gibt weder E-Mail-Versand noch eine behauptete Verifikation. Profiländerungen aktualisieren dieselbe `SessionState`-Quelle und setzen nicht unnötig die Sitzung zurück.
- **Löschung:** Nur Owner, mit aktueller Manage-Prüfung innerhalb `BEGIN IMMEDIATE`. Erst werden Rezepte mit bestehenden Kind-/Bild-Cascades, dann Memberships, zuletzt Cookbook mit Cover/Einladungs-Cascades gelöscht. Ein Fehler rollt alles zurück. Das UI öffnet davor einen eigenen Bestätigungsdialog mit Name, Rezeptanzahl und Verlustumfang. Globale Administration gewährt weiterhin keine fremde Ownership.
- **UI:** Mobile links Drawer-Toggle, rechts deterministische interne Zurücknavigation außerhalb der Übersicht. Branding befindet sich im Drawer; Desktop nutzt dieselben Routen. Die Settings-Seite ist ausdrücklich nur die angeforderte leere Seite. Zwei mobile Cookbook-Spalten, rollenabhängige Aktionsmenüs und randlose untere Create/Add-Aktionen. Import liegt in `recipes/new` und erhält Draft-Review/Cancellation. Beim Ersetzen eines bearbeiteten Entwurfs wird nachgefragt; Sign-out schützt ebenfalls ungesicherte Eingaben.

## Konsequenzen

Eine additive Migration erzeugt drei Tabellen und ergänzt Description sowie den eindeutigen E-Mail-Index. Bestehende akzeptierte Memberships bleiben akzeptiert; es gibt keine rückwirkende Zustimmungsanforderung. Cookie/Scheme, Schlüssel, Dateipfade, Rezept-/Mengenverträge und Datenbankprovider bleiben gleich. Neue Member-POSTs erzeugen jetzt Pending statt sofortigem Zugriff; beide Repositories müssen koordiniert ausgerollt werden.

Die UI nutzt vorhandene Angular-Material-Komponenten mit Tastatur-/Fokusverhalten. Sidenav wird nur im Root, Menu nur im Cookbook-Feature importiert, damit die bestehende initiale Bundle-Warnschwelle nicht erhöht werden muss. `PictureSelection` ist komponentenlokal und besitzt nur lokale Bildauswahl, keine HTTP- oder globale Editorzustände. Settings-/Create-Formulare erhalten Unsaved-Guards und sperren ihre Eingaben während des Speicherns. Andere Browserfenster aktualisieren Mitgliedschaften beim erneuten Laden; es wird keine Echtzeit-Synchronisation eingeführt.

Rollback erfolgt über vollständiges Backup und passende ältere Binärversionen. `Down` verweigert den Verlust von Covers, Profilbildern, Beschreibungen und offenen Einladungen. Keine Benutzerdatenbank wird in Tests verändert.

## Verifikation

Console-/Architekturtests behalten die Domain-/Providergrenzen. `ui_support_integration.py` prüft Consent, Fünferqueue, Duplicate/Reinvite/Cancel, atomare Erstellung, private Bilder, E-Mail-Eindeutigkeit und Delete-Rollback mit einem echten SQLite-Triggerfehler nach dem ersten Delete. `ui_upgrade.py` verwendet die frühere Accounts-Binärversion und vergleicht jede vorherige Tabellenspalte, bestehende Cookies/Memberships und Schlüssel. Frontend-TestBed prüft Rollenmenüs, Extra-Confirm, Bildretry ohne Duplikat, Profilvalidierung und verspätete Antworten nach Accountwechsel. Der Chromium-Smoke prüft echte Browserinteraktionen und Layouts bei 320/390/1280px; visuelle Screenshots ergänzen die automatisierten Overflow-Prüfungen.

## Erneut entscheiden, wenn

E-Mail-Verifikation/Zustellung, Echtzeit-Mitgliedschaftsänderungen, Invite-Audit/Expiry, größere Bildmengen, Ownership-Transfer oder Soft-Delete fachlich beauftragt werden. Diese Funktionen sind nicht als Platzhalter implementiert.
