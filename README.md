# MapNodes

**MapNodes** is the editor for [FactoryNodes](https://www.factorynodes.com) device files: the register map of a drive, a weight indicator,
a starter… — its parameters with their Modbus (and, optionally, EtherNet/IP) address, type, scale, unit, description and group, plus the
commands it offers as buttons (zero, tare…). FactoryNodes and the Factory Box read these files to back up, restore and show the
parameters of each device.

*MapNodes es el editor de los ficheros de dispositivo de FactoryNodes: el mapa de registros de un variador, un visor de peso, un arrancador…
Escribes la cabecera, el protocolo y los parámetros a mano o los pegas de una hoja de cálculo, y lo guardas como `<marca>/<modelo>.json`.*

## What it does

- **Header**: brand, model, device type, description, where the addresses come from, and whether it was tested on a real device.
- **Protocol**: register numbering (from 0 or from 1), word order of 32-bit values, registers per read, the EEPROM save command and what to
  know before restoring.
- **Parameters**: one row each — code, Modbus address, EtherNet/IP address (class/instance/attribute), type (UINT, INT, UDINT, DINT, REAL),
  scale, unit, description, group, basic, class (setting or live), writable, calibration, option texts, bit texts and a note.
  **Paste from a spreadsheet**: copy the table from the manual into Excel and paste it (code, address, type, scale, unit, description, group,
  class, writable).
- **Groups** and **commands**.
- **Problems**: the same checks FactoryNodes makes when it reads the file. Whatever MapNodes does not show (the device drawing, decimals that
  come from another parameter) is kept as it was when saving.
- Six languages: Spanish, Catalan, English, French, German and Portuguese.

The file format is described in [`src/libs/FactoryNodes.Devices/FORMAT.md`](src/libs/FactoryNodes.Devices/FORMAT.md); examples are in
[`library/devices`](library/devices).

## Build

Windows, .NET 8 SDK:

```
dotnet build MapNodes.sln
dotnet test MapNodes.sln
dotnet run --project src/apps/MapNodes
```

`MapNodes.exe some/file.json` opens a file; you can also drag one onto the window.

## Layout

- `src/libs/FactoryNodes.Devices` — the file format (read, check and write). No dependencies.
- `src/libs/FactoryNodes.MapNodes.Presentation` — the view models (no WPF: a web front end can use the same ones).
- `src/libs/FactoryNodes.Localization` — the six languages.
- `src/apps/MapNodes` — the Windows (WPF) window.
- `tests/FactoryNodes.MapNodes.Tests` — every file in `library/devices` opens and saves without losing anything.

## License

MIT — see [LICENSE](LICENSE).
