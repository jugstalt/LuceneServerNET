# REST Schnittstelle

Hier werden die Endpunkte für die API beschrieben. {index} entspricht immer dem Names des Index.
Der Rückgabewert ist immer vom Typ `IApiResult`:

```javascript
IApiResult {
    success	        boolean,
    milliSeconds	number($double)
}
```

Je nach Kontext gibt es noch zusätzliche Attribute, zB ein `hits`[]` bei Abfragen.

## Index Verwaltung

**[GET] /Lucene/createindex/{index}**

Erstellt einen Index mit dem Namen {index}

**[GET] /Lucene/removeindex/{index}**

Löscht den Index unwiderruflich

**[GET] /Lucene/indexexists/{index}**

Überprüft, ob ein Index existiert.

**[GET] /Lucene/refresh/{index}**

Führt zu einem Refresh des angeführten Index. Damit wird nach einem Hinzufügen von Dokumenten gewährleistet, dass nach den neu indizierten Dokumenten gesucht werden kann.
Verwendet man den `LuceneServerClient` erfolgt dieser Aufruf automatisch beim Entladen (`Dispose()`) des Clients.

**[GET] /Lucene/releaseall**

Gibt alle geteilten Resourcen innerhalb des Servers frei. Dies sind beispielsweise der `IndexReader` oder `IndexWriter`, die im Server über mehrere Abfragen geteilt werden, um eine optimale Performance zu bieten.
Nach dem Freigeben werden die Resourcen beim nächsten Zugriff auf einen Index wieder initialisiert.
Dieser Aufruf ist hauptsächlich für die Entwicklung notwendig und sollte in einem Produktivbetrieb nicht notwendig sein.

## Mapping

Damit wird die Struktur der Dokumente für diesen Index bestimmt. Das *Mapping* gibt an, welche Felder die einzelne Dokumente für diesen Index aufweisen können und ob diese Indiziert werden oder nur Zusatzinformation (ohne Suchmöglichkeiten) sind.

**[POST] /Lucene/map/{index}**

Request Body

```javascript
{
  "fields": [
    {
      "fieldType": "string",
      "name": "string",
      "store": true,
      "index": true
    }
  ],
  "primaryFields": [
    "string"
  ]
}
```

Es wird ein Json File übergeben, in dem die einzelnen Felder definiert werden. Als Werte für `fieldType` stehen

* `guid`: erzeugt ein Feld (_guid) für das automatisch eine Id erzeugt wird. Diese Id kann verwendet werden, um ein Dokument explizit zu suchen oder zu löschen

* `text`: Das Feld enthält Text der indiziert wird. In Feldern dieses Typs kann nach Schlagwörtern oder Wortfragmenten gesucht werden.

* `string`: Eine Zeichenkette, nach der gesucht werden kann.

* `int32`, `single`, `double`: Integer oder Fließkommazahlen

* `datetime`: Ein Datum mit Uhrzeit

Für jedes Feld kann angegeben werden, ob es indiziert und/oder gespeichert werden soll.
Nach indizierten Feldern kann gesucht werden. Bei *gespeicherten* Feldern wird auch der Inhalt 1:n bei einem Treffer zurückgegeben.

Unter `primaryFields` muss ein Array mit Spaltennamen angegeben werden, in denen bei einer Suche standardmäßig gesucht werden sollte. In allen anderen Feldern wird nur gesucht, wenn dies in der Abfrage explizit angeführt wird.

**[GET] /Lucene/mapping/{index}**

Hier kann das *Mapping* für diesen Index abgefragt werden. Das Ergebnis ist wieder ein `IApiResult` Objekt mit einer zusätzlichen Eigenschaft `mapping`:

```javascript
{
  "mapping": {
    "fields": [
      {
        "fieldType": "guid",
        "name": "_guid",
        "store": true,
        "index": true
      },
      ...
    ],
    "primaryFields": [
      "title",
      "content"
    ]
  },
  "success": true,
  "milliSeconds": 12.2342
}
```

## Dokumente

Ein Eintrag im Index wird als Dokument bezeichnet. Dokumente können mit den folgenden Schnittstellen indiziert, abgefragt und gelöscht werden.
Werden neue Dokumente zu einem Index hinzufügt oder gelöscht, wird das erst nach einem Refresh sichtbar (siehe oben).

**[POST] /Lucene/index/{index}**

Damit werden einzelnen Dokumente in den Index übernommen. Die Felder der übergebenen Dokumente müssen dem *Mapping* entsprechen. Nicht *gemappte* Felder werden ignoriert.

Request Body

```javascript
[
  {   
      // any kind of properties
      "title": "my book title",
      "summary": "lorem ispum..."
  }
]
```

**[GET] /Lucene/remove/{index}?term={query-term}&termField={optinal:field-used-for-term}**

Als `term` kann eine Abfrage übergeben werden. Nach den Kriterien aus der Abfrage wird in `termField` gesucht und die entsprechenden Objekte gelöscht.

Der *Default* Wert für `termField` ist `_guid`. Idealweise stellt man im *Mapping* für Dokument ein Field mit den Type `guid` ein. Nur so können einzelne Dokument wieder gezielt gelöscht werden.

**[GET] /Lucene/search/{index}?q={query-term}&outFields={optional:fields-for-the-result-comma-separated}**

Mit dieser Methode können Dokumente gesucht werden. Das Ergebnis ist vom Typ `IApiResult` mit einem zusätzlichen ``hits`` Attribute:

Beispiel: 
/Lucene/search/{index}?q=rules
/Lucene/search/{index}?q=rules&outFields=_guid,title,publish_date

```javascript
{
  "hits": [
    {
      "_score": 1.4272848,
      "_guid": "256c3f501ef64327b81f4b8141a6f5ef",
      "title": "LuceneServerNET rules!",
      "publish_date": "2021-02-12T17:09:17"
    }
  ],
  "success": true,
  "milliSeconds": 1.8725
}
```

Der *Query-Term* entspricht der *Lucene* Syntax, siehe auch [hier](https://lucene.apache.org/core/2_9_4/queryparsersyntax.html) 

**[GET] /Lucene/group/{index}?groupField={field}&q={optional:query-term}**

Mit dieser Methode können mögliche Werte eines Feldes ermittelt werden, die optional einem *Query-Term* entsprechen. Die Methode ist vergleichbar mit einem `DISTINCT` in einer Datenbank.
Die `hits` sind ein *String-Array*.

Beispiel: /Lucene/search/{index}?groupField=feed

```javascript
{
  "hits": [
    "derstandard",
    "diepresse",
    "kurier",
    "krone",
    "futurezone"
  ],
  "success": true,
  "milliSeconds": 81.4196
}
```