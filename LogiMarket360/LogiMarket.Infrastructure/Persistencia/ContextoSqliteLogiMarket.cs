using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace LogiMarket.Infrastructure.Persistencia;

// Gestiona la conexion SQLite local, inicializacion de esquema y transacciones reales (Unit of Work)
public sealed class ContextoSqliteLogiMarket : IUnidadTrabajo, IContextoSesion, IDisposable
{
    private const int IteracionesHash = 210_000;
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

    public async Task<bool> IniciarSesionAsync(string nombreUsuario, string contrasena, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrEmpty(contrasena))
            return false;

        await using var conexion = new SqliteConnection(_connectionString);
        await conexion.OpenAsync(ct);
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT Id, NombreUsuario, NombreCompleto, Rol, PasswordHash FROM Usuarios WHERE NombreUsuario = $usuario COLLATE NOCASE AND Activo = 1";
        cmd.Parameters.AddWithValue("$usuario", nombreUsuario.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return false;

        string hashGuardado = reader.GetString(4);
        if (!VerificarContrasena(contrasena, hashGuardado))
            return false;

        var usuario = new UsuarioSesion(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<RolUsuario>(reader.GetString(3), ignoreCase: true));
        IniciarSesion(usuario);
        return true;
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
    PasswordHash TEXT NOT NULL,
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

        // Add the credential column to databases created by earlier project versions.
        await using (var columnas = conexion.CreateCommand())
        {
            columnas.CommandText = "PRAGMA table_info(Usuarios)";
            await using var reader = await columnas.ExecuteReaderAsync(ct);
            bool tieneHash = false;
            while (await reader.ReadAsync(ct))
                tieneHash |= string.Equals(reader.GetString(1), "PasswordHash", StringComparison.OrdinalIgnoreCase);
            if (!tieneHash)
            {
                await reader.DisposeAsync();
                await using var alterar = conexion.CreateCommand();
                alterar.CommandText = "ALTER TABLE Usuarios ADD COLUMN PasswordHash TEXT NOT NULL DEFAULT ''";
                await alterar.ExecuteNonQueryAsync(ct);
            }
        }

        await SembrarDatosInicialesAsync(conexion, ct);
    }

    private static async Task SembrarDatosInicialesAsync(SqliteConnection conexion, CancellationToken ct)
    {
        string[][] usuarios =
        [
            ["11111111-1111-1111-1111-111111111101", "admin", "Administrador", "ADMIN", "Admin-LM360-2026!"],
            ["11111111-1111-1111-1111-111111111102", "vendedor1", "Ventas", "VENTAS", "Ventas-LM360-2026!"],
            ["11111111-1111-1111-1111-111111111103", "bodega1", "Bodega", "BODEGA", "Bodega-LM360-2026!"],
            ["11111111-1111-1111-1111-111111111104", "consulta1", "Consulta", "CONSULTA", "Consulta-LM360-2026!"]
        ];

        foreach (var u in usuarios)
        {
            string passwordHash = CrearHashContrasena(u[4]);
            await using var cmdUsuario = conexion.CreateCommand();
            cmdUsuario.CommandText = "INSERT OR IGNORE INTO Usuarios (Id, NombreUsuario, NombreCompleto, Rol, PasswordHash, Activo) VALUES ($id, $usuario, $nombre, $rol, $hash, 1)";
            cmdUsuario.Parameters.AddWithValue("$id", u[0]);
            cmdUsuario.Parameters.AddWithValue("$usuario", u[1]);
            cmdUsuario.Parameters.AddWithValue("$nombre", u[2]);
            cmdUsuario.Parameters.AddWithValue("$rol", u[3]);
            cmdUsuario.Parameters.AddWithValue("$hash", passwordHash);
            await cmdUsuario.ExecuteNonQueryAsync(ct);

            // Upgrade old demo rows that were originally seeded without credentials.
            await using var cmdActualizar = conexion.CreateCommand();
            cmdActualizar.CommandText = "UPDATE Usuarios SET PasswordHash = $hash WHERE NombreUsuario = $usuario AND PasswordHash = ''";
            cmdActualizar.Parameters.AddWithValue("$hash", passwordHash);
            cmdActualizar.Parameters.AddWithValue("$usuario", u[1]);
            await cmdActualizar.ExecuteNonQueryAsync(ct);
        }

        string seedSql = @"
INSERT OR IGNORE INTO Clientes (Id, Codigo, Nombre, Contacto, Categoria, Activo) VALUES
    ('22222222-2222-2222-2222-222222222201', 'CLI-ANA', 'Ana', 'ana@example.test', 'VIP', 1),
    ('22222222-2222-2222-2222-222222222202', 'CLI-LUIS', 'Luis', 'luis@example.test', 'NORMAL', 1),
    ('22222222-2222-2222-2222-222222222203', 'CLI-CARMEN', 'Carmen', 'carmen@example.test', 'MAYORISTA', 1);

INSERT OR IGNORE INTO CatalogoElementos (Sku, Nombre, TipoElemento, PrecioUnitario, PesoKg, StockDisponible, StockMinimo, Activo) VALUES
    ('P01', 'Mouse USB', 'SIMPLE', 20.00, 0.20, 8, 2, 1),
    ('P02', 'Teclado', 'SIMPLE', 30.00, 0.70, 5, 2, 1),
    ('P03', 'Memoria USB', 'SIMPLE', 10.00, 0.10, 20, 5, 1),
    ('P04', 'Webcam', 'SIMPLE', 40.00, 0.35, 4, 1, 1),
    ('P05', 'Alfombrilla', 'SIMPLE', 5.00, 0.10, 30, 5, 1),
    ('KIT_OFICINA', 'Kit Oficina', 'KIT', 50.00, 0, 0, 0, 1),
    ('KIT_TELETRABAJO', 'Kit Teletrabajo', 'KIT', 60.00, 0, 0, 0, 1);

INSERT OR IGNORE INTO KitComponentes (KitSku, ComponenteSku, Cantidad) VALUES
    ('KIT_OFICINA', 'P01', 1),
    ('KIT_OFICINA', 'P02', 1),
    ('KIT_TELETRABAJO', 'KIT_OFICINA', 1),
    ('KIT_TELETRABAJO', 'P03', 1);";

        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = seedSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string CrearHashContrasena(string contrasena)
    {
        byte[] sal = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, IteracionesHash, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256${IteracionesHash}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerificarContrasena(string contrasena, string formato)
    {
        string[] partes = formato.Split('$');
        if (partes.Length != 4 || partes[0] != "PBKDF2-SHA256" ||
            !int.TryParse(partes[1], out int iteraciones) || iteraciones < 100_000)
            return false;

        try
        {
            byte[] sal = Convert.FromBase64String(partes[2]);
            byte[] hashEsperado = Convert.FromBase64String(partes[3]);
            byte[] hashReal = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, hashEsperado.Length);
            return CryptographicOperations.FixedTimeEquals(hashEsperado, hashReal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _transaccionActiva?.Dispose();
        _conexionTransaccional?.Dispose();
    }
}
