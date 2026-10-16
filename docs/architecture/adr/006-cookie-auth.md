# ADR 006: Same-origin-Cookie für den Eigentümer

Status: umgesetzt.

## Kontext

Das Frontend und die API teilen eine Origin; die Anwendung hat genau einen vorhandenen Owner-Hash.

## Alternativen

Browser-Bearer-Tokens; neues Identitätssystem; Umbenennen von Cookie, User-Secrets-ID und Data-Protection-Anwendungsname.

## Entscheidung

Cookie, Auth-Scheme, Hashprüfung, Data-Protection-Identität und X-RecipeAtlas bleiben unverändert. Hashing ist Infrastruktur, Cookie-Endpunkte sind Transport.

## Begründung

Vorhandene Sitzungen und Konfiguration bleiben verwendbar. Es entsteht keine neue Tokenablage im Browser und kein nicht angeforderter Account-Lebenszyklus.

## Konsequenzen

Single-owner und globales Loginbudget bleiben Grenzen. Datenbank und Schlüssel müssen gemeinsam erhalten werden. Kein neuer öffentlicher Dokumentationsendpunkt in Production.

## Verifikation

HTTP-Tests prüfen Cookie, Header, Auth-Status und Neustart; der isolierte OpenAPI-Export prüft Development/Production-Exposition.

## Erneut entscheiden, wenn

Eine ausdrücklich beauftragte Mehrbenutzer- oder externe Client-Authentifizierung erfordert eine eigene Sicherheitsentscheidung.
