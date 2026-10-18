# ADR 010: Einladungen als einmalige Bearer-Fähigkeit

Status: umgesetzt.

## Kontext

Nur Administratoren sollen gewöhnliche Konten erzeugen lassen. Es gibt weder E-Mail-Pflicht noch E-Mail-Infrastruktur. Zwei gleichzeitige Einlösungen oder ein Widerruf dürfen keine doppelten/halb angelegten Konten erzeugen.

## Alternativen

Öffentliche Registrierung; manuell gesetzte gemeinsame Passwörter; E-Mail-Provider; Klartexttokens in der Datenbank; vorab angelegte Konten mit getrennten, unabhängig committeten Schritten.

## Entscheidung und Begründung

`Invitations` erzeugt 32 kryptographisch zufällige Bytes als Base64url-Token und speichert nur SHA-256-Hexwerte mit Unique-Index. Der rohe Link wird ausschließlich als Ergebnis der Erstellung zurückgegeben. Die konfigurierte `Accounts:PublicAppUrl` ist die Vertrauensquelle; Host-Header werden nicht verwendet. Standardablauf: 72 Stunden, explizit konfigurierbar von 1 bis 720 Stunden. `TimeProvider` liefert die Zeit, `InvitationRules` zentralisiert Pending/Expired/Revoked/Consumed.

Der Link verwendet `/invite#token=...`. Fragmente werden nicht an Webserver/Referrer übertragen. Angular entfernt sie aus der Adresszeile und validiert per POST; Vorschau verbraucht nichts. Keine Drittinhalte/Analytics auf der Seite, Referrer-Policy no-referrer, API no-store. UI und Betriebshinweise erklären das Bearer-Risiko. Manuelles privates Teilen ersetzt E-Mail-Versand.

`BEGIN IMMEDIATE` liegt vor Zustandsprüfung und Identitätsanlage. `UserManager.CreateAsync`, persönliches Cookbook und Consumption werden gemeinsam committed. Identity darf innerhalb dieser äußeren Transaktion speichern; es gibt keinen unabhängigen zweiten Commit. Fehler einschließlich doppeltem normalisiertem Benutzernamen rollen alles zurück. Datenbank-Uniqueness bleibt letzte Integritätsgrenze. Widerruf benutzt dieselbe Transaktionsordnung: gewinnt er zuerst, schlägt Redeem mit 400 fehl; gewinnt Redeem, erhält Revoke 409. Terminalzustände werden nicht umgedeutet.

## Konsequenzen

Wer den Link kennt, kann genau ein gewöhnliches Konto anlegen. Es gibt keine automatische Cookbook-Mitgliedschaft. Verlorene Links werden widerrufen/neu erstellt, nie rekonstruiert. Normale Benutzer sehen kein Account-Verzeichnis. Shared Rate Limit für Validate/Redeem: 30/min; keine dauerhafte Jobqueue. Request-/Response-Bodies dürfen im Betrieb nicht protokolliert werden. Hashing von Passwörtern innerhalb des SQLite-Writetransaktionsfensters begrenzt parallele Writes; für diese kleine Installation ist atomare Provisionierung wichtiger.

## Verifikation

HTTP-Suite prüft Preview ohne Consume, ungültig/abgelaufen/widerrufen/verbraucht, Nicht-Admin-Erstellung/Widerruf, doppelte Namen mit Rollback, simultane Einlösungen, Revoke-Race, zwei Invitations mit gleichem normalisiertem Namen, Hashspeicherung, vertrauenswürdige URL trotz fremdem Host, Limiter und fehlende Secrets im Standardlog. Angular-Tests prüfen Terminalzustände, Submit-Fehler, Secret-Cleanup und verspätete Admin-Antwort nach Logout.

## Erneut entscheiden, wenn

Nachweisbare Anforderungen an E-Mail-Zustellung, Audit-Aufbewahrung, größere Invite-Mengen oder Account-Verifikation bestehen. Eine Einladung ist keine dauerhafte Freigabe eines Cookbooks.
