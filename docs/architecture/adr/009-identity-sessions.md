# ADR 009: Identity und unmittelbar widerrufbare Cookie-Sitzungen

Status: umgesetzt; ersetzt ADR 006 hinsichtlich Owner-Hash, Cookie und Identitätsmodell. Same-origin-Deployment, Schreibheader und Data-Protection-Speicher bleiben bestehen.

## Kontext

Ein gemeinsames Owner-Passwort identifiziert keine einzelnen Mitglieder. Ein deaktiviertes Konto darf auf dem nächsten Request keine geschützte Operation ausführen; Reaktivierung darf alte Cookies nicht wieder gültig machen. Passwortwechsel müssen andere Sitzungen beenden.

## Alternativen

Owner-Hash um eine selbst entwickelte Konten-/Passwortverwaltung erweitern; JWTs im Browser; komplette Identity-UI mit öffentlicher Registrierung; lange periodische Security-Stamp-Prüfung.

## Entscheidung und Begründung

`AppUser` erbt außerhalb der Domain von `IdentityUser<Guid>`. `RecipeDb` nutzt `IdentityUserContext`, `UserManager` und `SignInManager` übernehmen Hashing, Validierung, Lockout und Security Stamps. `AccountAuthentication` koordiniert die Anwendungsfälle; `ISessionCookies` begrenzt die Framework-Sitzungsoperationen auf Sign-in/Sign-out. `ICurrentUser` liefert nur eine ID, niemals `HttpContext`.

`Recipoke.Session` verwendet den Identity-Application-Scheme. Der andere Name/Scheme macht alte Owner-Cookies bewusst unbrauchbar. HttpOnly, SameSite Strict, 14 Tage ohne Sliding und Secure in Production bleiben der Same-origin-Auswahl angemessen. `ActiveSessionValidation` prüft bei jedem Request Stamp und Aktivität. `AccountAccess` liest zusätzlich aktuelle Aktivität im Anwendungsfall; bei Writes liegt diese Prüfung innerhalb der Transaktion.

Deaktivierung ändert den Stamp; Reaktivierung ebenfalls. Passwortwechsel prüft das aktuelle Passwort, ändert den Stamp und erneuert nur das aufrufende Cookie. Server-Recovery verwendet Identity-Reset-Tokens ausschließlich intern und setzt Lockout zurück. Loginfehler bleiben generisch. Fünf Fehlversuche sperren 15 Minuten; der vorhandene globale Login-Limiter bleibt erhalten. Neue Passwörter haben 12–256 Zeichen ohne erfundene eigene Kryptographie oder zusätzliche Kompositionsregeln.

## Konsequenzen

Datenbankzugriffe pro Sitzung/Operation sind bewusst akzeptiert. Ein bereits laufender Request wird nicht rückwirkend abgebrochen; nach einer abgeschlossenen Deaktivierung gestartete Requests werden abgewiesen. Cookie-Rollenlisten würden Widerruf verzögern und werden nicht gespeichert. Administratorfähigkeit ist ein separates, nicht öffentlich änderbares Kontoattribut. Keine öffentliche Registrierung, kein JWT-Speicher, keine Identity-Entity im HTTP-Vertrag.

Die geteilten Loginbudgets können bei mehr Verkehr andere Nutzer blockieren; ohne verlässliche Proxy-Identität wird kein scheinbar sicheres IP-Modell eingeführt. Data-Protection-Name `RecipeAtlas` und Schlüsselverzeichnis bleiben erhalten, auch wenn Owner-Cookies absichtlich nicht wiederverwendet werden.

## Verifikation

`accounts_integration.py`: Login/Logout, generische Fehler, Lockout, CSRF, 401 statt HTML-Redirect, Passwortwechsel gegen zwei Cookies, Deaktivierung/Reaktivierung. `upgrade_compatibility.py`: echte alte Cookies werden nach Bootstrap abgewiesen, Recovery widerruft Identity-Sitzungen, Production setzt Secure/HttpOnly/Strict. Domain-Referenz- und IL-Architekturtests prüfen Frameworkfreiheit und verbieten Transportzugriffe aus Application-Code.

## Erneut entscheiden, wenn

Externe Clients, SSO, mehrere API-Instanzen oder größere Last andere Sitzungs-/Rate-Limit-Anforderungen konkret machen. E-Mail-Passwortreset und öffentliche Registrierung gehören nicht zu dieser Entscheidung.
