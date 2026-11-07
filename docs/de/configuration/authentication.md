# Authentifizierung

Standardmäßig ist der Server **offen**: Jeder Request ist erlaubt. Mit `Authorization:Type` = `Basic`
wird HTTP-Basic-Authentifizierung verlangt.

```json
"Authorization": {
  "Type": "Basic",
  "Clients": [
    {
      "Id": "reader",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "read" ]
    },
    {
      "Id": "importer",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "write" ]
    },
    {
      "Id": "admin",
      "SecretHash": "pbkdf2-sha256$...",
      "Roles": [ "admin" ]
    }
  ]
}
```

!!! warning
    Bei Basic-Authentifizierung werden die Zugangsdaten mit jedem Request gesendet. Immer **HTTPS** verwenden.

## Secrets

Secrets als Hash ablegen (`SecretHash`). Ein Hash wird so erzeugt:

```bash
dotnet LuceneServerNET.dll --hash-secret "my-secret"
```

Der Befehl gibt den Hash aus und beendet sich. Secrets im Klartext (`Secret`) werden aus
Kompatibilitätsgründen weiter unterstützt; der Server schreibt beim Start eine Warnung ins Log.

## Rollen

| Rolle | Erlaubt |
|---|---|
| `read` | `search`, `searchphonetic`, `group`, `indexexists`, `mapping`, `getmeta`, `getmetas`, `getmetanames`, `/info` |
| `write` | `read` + `createindex`, `map`, `index`, `remove`, `refresh`, `addmeta` |
| `admin` | `write` + `removeindex`, `releaseall` |

Höhere Rollen schließen niedrigere ein. Ein Client **ohne Rollen** erhält `admin` (abwärtskompatibel);
der Server schreibt eine Warnung ins Log. Ist `Basic` aktiv, aber kein Client konfiguriert, werden alle Requests abgelehnt.

## Aufruf

```bash
curl -u reader:my-secret "https://search.example.com/Lucene/search/products?q=bat*"
```

Der [.NET-Client](../clients/dotnet-client.md) nimmt die Zugangsdaten als Konstruktor-Parameter
oder in der URL (`https://id:secret@host`) entgegen.
