# Catálogo de dispositivos: un JSON por marca y modelo

`library/devices/<marca>/<modelo>.json` (UTF-8). Lo lee `DeviceModel.Parse`, que dice qué no vale.

```json
{
  "schemaVersion": 1,
  "brand": "Omron", "model": "MX2",
  "type": "drive",                  // de la lista cerrada de tipos (abajo); vacío o desconocido = «Otros». Por él se elige, antes que la marca
  "kind": "Variador",               // qué es, en palabras (texto libre, al lado del modelo)
  "version": 1,                     // la de este fichero: sube con cada corrección publicada
  "author": "",                     // quién lo ha hecho (los compartidos son CC-BY 4.0); vacío = FactoryNodes
  "language": "es",                 // el idioma de los textos de este fichero; las traducciones, en ficheros aparte (abajo)
  "description": "…", "source": "de dónde salen las direcciones", "verified": false,
  "svg": "<svg …>…</svg>",
  "protocol": {
    "registerBase": 1,              // 1: el manual numera desde 1 (trama = dirección − 1); 0: ya es la de la trama
    "wordOrder": "HighFirst",       // 32 bits: palabra alta primero (HighFirst) o baja (LowFirst)
    "maxRegistersPerRead": 16,
    "save": { "address": "0x0900", "value": 1, "description": "Guardar en EEPROM" },   // o null
    "restoreNotes": "lo que hay que saber antes de restaurar"
  },
  "groups": [ { "number": 1, "text": "Datos del motor" } ],
  "parameters": [ {
    "code": "F002",                 // código del fabricante: único; clave en las copias
    "address": "0x1103",            // como en el manual: «0x…» hexadecimal o decimal; vacía si el equipo solo habla EtherNet/IP
    "cip": "0x64/1/0x08",           // opcional (2026-10-27, PENDIENTES 45): la dirección EtherNet/IP, clase/instancia/atributo; valor
                                    // little-endian de 2 bytes (INT/UINT) o 4 (DINT/UDINT/REAL). Al menos una de las dos direcciones
    "type": "UDINT",                // UINT, INT (1 registro); UDINT, DINT, REAL (2)
    "scale": 0.01, "unit": "s",     // valor = registro × scale
    "description": "Tiempo de aceleración 1",
    "group": 2,                     // 0 = sin grupo (al final)
    "basic": true,                  // sale primero, en «Básicos»
    "kind": "Setting",              // Setting (se guarda y restaura) o Live (en vivo: solo se lee)
    "writable": true,               // false: se guarda pero no se escribe
    "options": { "0": "Adelante", "1": "Atrás" },   // texto de cada valor (claves numéricas)
    "bits": { "0": "Listo" },       // palabra de bits: se enseñan los que están a 1
    "note": "rango, de fábrica, unidad que depende de otro parámetro…",
    "decimalsFrom": { "from": "DspDiv", "shift": 0, "mask": 0, "map": { "5": 2 } },   // o null: ver abajo
    "calibration": true             // de la calibración de este equipo: al restaurar, sin marcar
  } ],
  "commands": [ {                   // órdenes como botones (cero, tara…); [] si no tiene
    "text": "Tara", "address": "0x0090", "value": 212,
    "clearFirst": true,             // escribir 0 antes (eNod4)
    "response": { "address": "0x0091", "done": 2, "error": 3 },   // o null: no se espera respuesta
    "confirm": false,               // true: pide confirmación (lo que mueve algo o cambia la calibración)
    "note": "lo que hace"
  } ]
}
```

## Decimales que da otro parámetro (`decimalsFrom`, básculas)
El peso de una báscula es un entero cuyos decimales dependen de otro parámetro: decimales = `(crudo de from >> shift) & mask`
(`mask` 0 = todos) y, con `map`, lo que diga la tabla (clave: ese valor en número). Se suman a los de `scale`. El
parámetro `from` no puede tomar los suyos de otro. Sin leer `from`, se enseña sin coma.
- DAT 400: `{ "from": "DspDiv", "map": { "0": 3, … "5": 2, … "14": 0 } }` (código del valor de división).
- eNod4: `{ "from": "StabDec", "shift": 8, "mask": 255 }` (byte alto de 0x0008 = número de decimales).

## Calibración (`calibration`)
Capacidad, sensibilidad, puntos y pendientes de calibración, cero de calibración, valor de división…: son de cada báscula.
Se guardan en las copias, pero al restaurar una copia o cargar un CSV llegan **sin marcar** (en otra báscula la
estropearían); se marcan a mano si es la misma báscula. Se pueden cambiar a mano en el módulo Modbus.

## Copia en CSV (`DeviceBackup.ToCsv`)
Punto y coma, UTF-8. Primera línea `# FactoryNodes;copia de parámetros;<dispositivo>;<marca modelo>;<fecha ISO>;<quién>;<nota>`;
segunda, `Código;Crudo;Valor;Descripción`; luego una fila por parámetro. Al importar manda la columna «Crudo».

## Tipos (`type`), desde el 2026-11-12
Lista cerrada, `Types.json` (incrustado en `FactoryNodes.Devices`: un tipo nuevo es una línea, con su nombre en los seis idiomas): `drive`
(Variador), `starter` (Arrancador), `weighing` (Pesaje), `lighting` (Iluminación). El índice de la web lleva la lista, así que un programa de
antes conoce los tipos nuevos.

## El catálogo de la web (`DeviceIndex`, `DeviceDownloads`)
`https://www.factorynodes.com/devices/` (lo sube `build\publish-devices.ps1` desde `library/devices`): `index.json`, `index.json.sig` y cada
fichero en `<marca>/<modelo>.json` (`DeviceModel.CatalogPath`). Públicos; licencia CC-BY 4.0.
```json
{ "schemaVersion": 1, "date": "2026-11-12T12:54:03Z", "license": "CC-BY-4.0",
  "types": [ { "key": "drive", "es": "Variador", "ca": "…", "en": "…", "fr": "…", "de": "…", "pt": "…" } ],
  "models": [ { "type": "drive", "brand": "Omron", "model": "MX2", "kind": "Variador", "description": "…", "version": 1, "verified": false,
                "author": "", "parameters": 96, "path": "omron/MX2.json", "sha256": "…" } ] }
```
La firma (`index.json.sig`: ECDSA P-256 con SHA-256, DER en base64, la clave del catálogo de elementos) es del índice, sobre su texto con saltos
LF y sin BOM; cada fichero se comprueba con su `sha256` (mismo texto canónico). Un índice sin la firma de FactoryNodes o un fichero que no
cuadra no se usan: un modelo dice qué se escribe al restaurar y qué mandan las órdenes.

## Idiomas: el fichero base y sus traducciones (`DeviceTranslation`), desde el 2026-11-12
El fichero del modelo dice el idioma de sus textos (`language`). Cada traducción es **otro fichero**, junto a él, con el idioma antes de la
extensión (`omron/MX2.de.json`), y **solo lleva textos**, enlazados por el código de cada parámetro. Nunca direcciones, tipos ni escalas: lo
que dice cómo se habla con el equipo está en un solo sitio y una traducción no lo puede cambiar. Se reconoce por `translationOf`.
```json
{
  "schemaVersion": 1,
  "translationOf": { "brand": "Omron", "model": "MX2" },
  "language": "de", "version": 1, "author": "",
  "kind": "Frequenzumrichter", "description": "…", "restoreNotes": "…", "saveDescription": "…",
  "groups": { "1": "Motordaten" },                              // por el número del grupo
  "parameters": { "F002": { "description": "…", "note": "…", "unit": "…", "options": { "0": "…" }, "bits": { "0": "…" } } },
  "commands": { "Tara": { "text": "Tarieren", "note": "…" } }   // por el texto de la orden en el fichero base
}
```
Lo que una traducción no trae (o trae vacío) se enseña como en el fichero base. De las opciones y los bits solo se traducen los valores que el
fichero base tiene: no añade ni quita ninguno. `DeviceCatalog.Find` da el modelo en el idioma elegido para él (`Choose`), si no en el preferido
(`PreferredLanguage`, el del programa) y si no en el de su fichero; `Languages(key)` dice cuáles hay. A la Factory Box y a las copias va el
fichero base. En el índice de la web, cada modelo lleva `language` y `translations` (`language`, `version`, `author`, `path`, `sha256`).

## Normas de los idiomas (autor, 2026-11-12)
- **El fichero principal, en el idioma del manual del fabricante**, con los nombres tal como salen en él («si es inglés, inglés: así no hay
  problema de traducción en el idioma principal, salvo error en el pdf»). Los que ya había (MX2, ATV320, DAT 400, eNod4-F, el regulador LED) se
  quedan en castellano: «los dejamos como están; se traducen y ya está».
- **Las traducciones, a los demás idiomas de FactoryNodes**, y solo de **nombres de parámetro, opciones y bits** (con los grupos y las órdenes,
  que también son nombres): «notas largas… no». El formato admite más (nota, unidad, descripción), pero no se usa para las notas.

## El paquete de un modelo (`DevicePackage`), desde el 2026-11-12
En la web cada modelo es **un zip** (`omron/MX2.zip`) con su fichero principal y sus traducciones (autor: «que la descarga tenga el archivo de
parámetros y sus idiomas traducidos, todo comprimido en un zip»): viajan juntos y de la misma versión, y el índice lleva una sola huella por
modelo (`path` y `sha256` son los del zip; `translations` solo dice idioma, versión y autor). El zip es siempre igual para el mismo contenido
(entradas ordenadas, con fecha fija). En el PC y en el proyecto los ficheros van sueltos, como siempre. Al abrir un paquete se comprueba que
trae un solo fichero de dispositivo, que las traducciones son suyas, los nombres de dentro y el tamaño (40 ficheros, 4 MB cada uno, 16 MB).
