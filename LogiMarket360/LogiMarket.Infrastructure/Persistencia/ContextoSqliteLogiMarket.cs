using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace LogiMarket.Infrastructure.Persistencia;

// Gestiona la conexion SQLite local, inicializacion de esquema y transacciones reales (Unit of Work)
public sealed class ContextoSqliteLogiMarket : IUnidadTrabajo, IContextoSesion, IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _conexionTransaccional;
    private SqliteTransaction? _transaccionActiva;

    public string RutaBaseDatos { get; }
    public UsuarioSesion? UsuarioActual { get; private set; }

    public ContextoSqliteLogiMarket(string? rutaBaseDatos = null)
    {
        if (string.IsNullOrWhiteSpace(rutaBaseDatos))
        {
            string carpetaLocal = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LogiMarket360");
            Directory.CreateDirectory(carpetaLocal);
            RutaBaseDatos = Path.Combine(carpetaLocal, "logimarket360.db");
        }
        else
        {
            RutaBaseDatos = rutaBaseDatos;
            string? dir = Path.GetDirectoryName(RutaBaseDatos);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = RutaBaseDatos,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        }.ToString();
    }

    public void IniciarSesion(UsuarioSesion usuario) =>
        UsuarioActual = usuario ?? throw new ArgumentNullException(nameof(usuario));

    public void CerrarSesion() => UsuarioActual = null;

    public bool TienePermiso(params RolUsuario[] rolesPermitidos)
    {
        if (UsuarioActual is null || rolesPermitidos is null || rolesPermitidos.Length == 0)
            return false;

        foreach (var rol in rolesPermitidos)
        {
            if (UsuarioActual.Rol == rol)
                return true;
        }

        return false;
    }

    public async Task<SqliteConnection> ObtenerConexionAbiertaAsync(CancellationToken ct = default)
    {
        if (_conexionTransaccional is not null && _conexionTransaccional.State == System.Data.ConnectionState.Open)
            return _conexionTransaccional;

        var conexion = new SqliteConnection(_connectionString);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    public SqliteTransaction? TransaccionActual => _transaccionActiva;

    public bool EsConexionCompartida(SqliteConnection conexion) =>
        _conexionTransaccional is not null && ReferenceEquals(conexion, _conexionTransaccional);

    public async Task EjecutarEnTransaccionAsync(Func<CancellationToken, Task> operacion, CancellationToken ct = default)
    {
        if (operacion is null)
            throw new ArgumentNullException(nameof(operacion));

        if (_transaccionActiva is not null)
        {
            await operacion(ct);
            return;
        }

        await using var conexion = new SqliteConnection(_connectionString);
        await conexion.OpenAsync(ct);
        await using var tx = (SqliteTransaction)await conexion.BeginTransactionAsync(ct);

        _conexionTransaccional = conexion;
        _transaccionActiva = tx;

        try
        {
            await operacion(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(ct);
            }
            catch
            {
                // Evita ocultar la excepcion original si el rollback falla
            }
            throw;
        }
        finally
        {
            _transaccionActiva = null;
            _conexionTransaccional = null;
        }
    }

    public async Task InicializarEsquemaAsync(CancellationToken ct = default)
    {
        await using var conexion = new SqliteConnection(_connectionString);
        await conexion.OpenAsync(ct);

        string sql = @"
CREATE TABLE IF NOT EXISTS Usuarios (
    Id TEXT PRIMARY KEY,
    NombreUsuario TEXT NOT NULL UNIQUE,
    NombreCompleto TEXT NOT NULL,
    Rol TEXT NOT NULL,
    Activo INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS CatalogoElementos (
    Sku TEXT PRIMARY KEY,
    Nombre TEXT NOT NULL,
    TipoElemento TEXT NOT NULL,
    PrecioUnitario REAL NOT NULL,
    PesoKg REAL NOT NULL,
    StockDisponible INTEGER NOT NULL,
    StockMinimo INTEGER NOT NULL,
    Activo INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS KitComponentes (
    KitSku TEXT NOT NULL,
    ComponenteSku TEXT NOT NULL,
    Cantidad INTEGER NOT NULL,
    PRIMARY KEY (KitSku, ComponenteSku),
    FOREIGN KEY (KitSku) REFERENCES CatalogoElementos(Sku),
    FOREIGN KEY (ComponenteSku) REFERENCES CatalogoElementos(Sku)
);

CREATE TABLE IF NOT EXISTS MovimientosInventario (
    Id TEXT PRIMARY KEY,
    FechaUtc TEXT NOT NULL,
    Sku TEXT NOT NULL,
    DeltaCantidad INTEGER NOT NULL,
    StockResultante INTEGER NOT NULL,
    Motivo TEXT NOT NULL,
    Referencia TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Clientes (
    Id TEXT PRIMARY KEY,
    Codigo TEXT NOT NULL UNIQUE,
    Nombre TEXT NOT NULL,
    Contacto TEXT NOT NULL,
    Categoria TEXT NOT NULL,
    Activo INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS Pedidos (
    Id TEXT PRIMARY KEY,
    Codigo TEXT NOT NULL UNIQUE,
    ClienteId TEXT NOT NULL,
    NombreCliente TEXT NOT NULL,
    CategoriaCliente TEXT NOT NULL,
    TipoEntrega TEXT NOT NULL,
    TarifaEnvio REAL NOT NULL,
    PlazoEntregaDias INTEGER NOT NULL,
    Subtotal REAL NOT NULL,
    Descuento REAL NOT NULL,
    PoliticaDescuento TEXT NOT NULL,
    Total REAL NOT NULL,
    Estado TEXT NOT NULL,
    FechaCreacionUtc TEXT NOT NULL,
    FechaEntregaUtc TEXT NULL,
    TieneDevolucion INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (ClienteId) REFERENCES Clientes(Id)
);

CREATE TABLE IF NOT EXISTS PedidoLineas (
    Id TEXT PRIMARY KEY,
    PedidoId TEXT NOT NULL,
    Sku TEXT NOT NULL,
    NombreProducto TEXT NOT NULL,
    EsKit INTEGER NOT NULL,
    Cantidad INTEGER NOT NULL,
    PrecioUnitario REAL NOT NULL,
    ComponentesJson TEXT NOT NULL,
    FOREIGN KEY (PedidoId) REFERENCES Pedidos(Id)
);

CREATE TABLE IF NOT EXISTS PagosPedido (
    Id TEXT PRIMARY KEY,
    PedidoId TEXT NOT NULL,
    ClaveIdempotencia TEXT NOT NULL UNIQUE,
    ReferenciaPasarela TEXT NOT NULL,
    Proveedor TEXT NOT NULL,
    Monto REAL NOT NULL,
    FechaUtc TEXT NOT NULL,
    FOREIGN KEY (PedidoId) REFERENCES Pedidos(Id)
);

CREATE TABLE IF NOT EXISTS DespachosPedido (
    Id TEXT PRIMARY KEY,
    PedidoId TEXT NOT NULL UNIQUE,
    NumeroGuia TEXT NOT NULL,
    TipoEntrega TEXT NOT NULL,
    PlazoDias INTEGER NOT NULL,
    FechaDespachoUtc TEXT NOT NULL,
    FOREIGN KEY (PedidoId) REFERENCES Pedidos(Id)
);

CREATE TABLE IF NOT EXISTS DevolucionesPedido (
    Id TEXT PRIMARY KEY,
    PedidoId TEXT NOT NULL UNIQUE,
    Motivo TEXT NOT NULL,
    MontoReembolso REAL NOT NULL,
    StockReintegrado INTEGER NOT NULL,
    Observacion TEXT NOT NULL,
    FechaUtc TEXT NOT NULL,
    FOREIGN KEY (PedidoId) REFERENCES Pedidos(Id)
);

CREATE TABLE IF NOT EXISTS AuditoriaLog (
    Id TEXT PRIMARY KEY,
    FechaUtc TEXT NOT NULL,
    Categoria TEXT NOT NULL,
    Accion TEXT NOT NULL,
    Detalle TEXT NOT NULL,
    Usuario TEXT NOT NULL,
    CorrelacionId TEXT NULL
);

CREATE TABLE IF NOT EXISTS EventosOutbox (
    Id TEXT PRIMARY KEY,
    TipoEvento TEXT NOT NULL,
    PayloadJson TEXT NOT NULL,
    ClaveUnica TEXT NOT NULL UNIQUE,
    Estado TEXT NOT NULL,
    Intentos INTEGER NOT NULL DEFAULT 0,
    UltimoError TEXT NULL,
    FechaCreacionUtc TEXT NOT NULL,
    FechaProcesadoUtc TEXT NULL
);";

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);

        await SembrarDatosInicialesAsync(conexion, ct);
    }

    private static async Task SembrarDatosInicialesAsync(SqliteConnection conexion, CancellationToken ct)
    {
        string seedSql = @"
INSERT OR IGNORE INTO Usuarios (Id, NombreUsuario, NombreCompleto, Rol, Activo) VALUES
    ('11111111-1111-1111-1111-111111111101', 'admin', 'Administrador General', 'ADMIN', 1),
    ('11111111-1111-1111-1111-111111111102', 'vendedor1', 'Ejecutivo de Ventas', 'VENTAS', 1),
    ('11111111-1111-1111-1111-111111111103', 'bodega1', 'Encargado de Bodega', 'BODEGA', 1),
    ('11111111-1111-1111-1111-111111111104', 'consulta1', 'Analista de Consulta', 'CONSULTA', 1);

INSERT OR IGNORE INTO Clientes (Id, Codigo, Nombre, Contacto, Categoria, Activo) VALUES
    ('22222222-2222-2222-2222-222222222201', 'CLI-001', 'Comercial Andina S.A.', 'compras@comercialandina.ec', 'MAYORISTA', 1),
    ('22222222-2222-2222-2222-222222222202', 'CLI-002', 'Estudio Creativo Sur', 'hola@estudiosur.ec', 'VIP', 1),
    ('22222222-2222-2222-2222-222222222203', 'CLI-003', 'Javier Vargas', 'jvargas@ejemplo.ec', 'NORMAL', 1);

INSERT OR IGNORE INTO CatalogoElementos (Sku, Nombre, TipoElemento, PrecioUnitario, PesoKg, StockDisponible, StockMinimo, Activo) VALUES
    ('SKU-MOU', 'Mouse Inalambrico Ergonomico', 'SIMPLE', 18.50, 0.15, 60, 10, 1),
    ('SKU-TEC', 'Teclado Mecanico Compacto', 'SIMPLE', 42.00, 0.65, 45, 8, 1),
    ('SKU-HUB', 'Hub USB-C Multipuerto', 'SIMPLE', 24.00, 0.20, 50, 10, 1),
    ('KIT_TELETRABAJO', 'Kit Teletrabajo Esencial', 'KIT', 79.00, 1.00, 0, 0, 1);

INSERT OR IGNORE INTO KitComponentes (KitSku, ComponenteSku, Cantidad) VALUES
    ('KIT_TELETRABAJO', 'SKU-MOU', 1),
    ('KIT_TELETRABAJO', 'SKU-TEC', 1),
    ('KIT_TELETRABAJO', 'SKU-HUB', 1);";

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = seedSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public void Dispose()
    {
        _transaccionActiva?.Dispose();
        _conexionTransaccional?.Dispose();
    }
}
