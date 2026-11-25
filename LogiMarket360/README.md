# LogiMarket 360

Aplicación de escritorio Windows Forms para demostrar ventas, inventario, despacho y devoluciones con datos locales de prueba.

## Requisitos

- Windows
- .NET SDK 8
- Visual Studio 2022 o una versión compatible con .NET 8

## Restaurar, compilar y ejecutar

Desde esta carpeta:

```powershell
dotnet restore LogiMarket360.slnx
dotnet build LogiMarket360.slnx
dotnet run --project LogiMarket.WinForms\LogiMarket.WinForms.csproj
dotnet test LogiMarket.Tests\LogiMarket.Tests.csproj
```

La aplicación crea su base SQLite local en `%LOCALAPPDATA%\LogiMarket360\logimarket360.db` y carga datos iniciales idempotentes al abrirse. Para iniciar con una base vacía, cierra la aplicación y elimina ese archivo de prueba.

## Cuentas locales de demostración

| Usuario | Rol | Contraseña de demostración |
|---|---|---|
| admin | ADMIN | `Admin-LM360-2026!` |
| vendedor1 | VENTAS | `Ventas-LM360-2026!` |
| bodega1 | BODEGA | `Bodega-LM360-2026!` |
| consulta1 | CONSULTA | `Consulta-LM360-2026!` |

Estas credenciales solo son para la base local de demostración. Las contraseñas se guardan como hashes PBKDF2-SHA256 con sal aleatoria.

## Datos de prueba

El seed incluye P01–P05 y los kits KIT_OFICINA y KIT_TELETRABAJO de la guía APE 04.

## Verificaciones automatizadas

La suite incluye 38 pruebas unitarias y de integración. Se verifican el seed oficial, inicio de sesión local, idempotencia de pagos, stock bajo dos actualizaciones concurrentes y reintento de eventos Outbox después de un fallo transitorio. La matriz de aceptación P01–P38 con evidencia visual independiente.

## Estado técnico

La persistencia actual inicializa el esquema SQLite mediante SQL en `ContextoSqliteLogiMarket`.
