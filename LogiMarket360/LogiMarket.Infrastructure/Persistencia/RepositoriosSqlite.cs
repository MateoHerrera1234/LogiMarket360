using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;
using Microsoft.Data.Sqlite;

namespace LogiMarket.Infrastructure.Persistencia;

// Repositorio SQLite para productos simples y kits compuestos con validacion sin ciclos
public sealed class RepositorioCatalogoSqlite : IRepositorioCatalogo
{
    private readonly ContextoSqliteLogiMarket _contexto;

    public RepositorioCatalogoSqlite(ContextoSqliteLogiMarket contexto)
    {
        _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
    }

    public async Task<IElementoCatalogo?> ObtenerPorSkuAsync(string sku, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return null;

        var todos = await ListarTodosAsync(ct);
        return todos.FirstOrDefault(e => string.Equals(e.Sku, sku.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ProductoSimple>> ListarProductosSimplesAsync(CancellationToken ct = default)
    {
        var todos = await ListarTodosAsync(ct);
        return todos.OfType<ProductoSimple>().ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<IElementoCatalogo>> ListarTodosAsync(CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var simples = new Dictionary<string, ProductoSimple>(StringComparer.OrdinalIgnoreCase);
            var kits = new Dictionary<string, (string Sku, string Nombre, decimal Precio)>(StringComparer.OrdinalIgnoreCase);

            await using (var cmd = conexion.CreateCommand())
            {
                cmd.Transaction = _contexto.TransaccionActual;
                cmd.CommandText = "SELECT Sku, Nombre, TipoElemento, PrecioUnitario, PesoKg, StockDisponible FROM CatalogoElementos ORDER BY Sku";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    string sku = reader.GetString(0);
                    string nombre = reader.GetString(1);
                    string tipo = reader.GetString(2);
                    decimal precio = Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture);
                    decimal peso = Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture);
                    int stock = reader.GetInt32(5);

                    if (string.Equals(tipo, "KIT", StringComparison.OrdinalIgnoreCase))
                        kits[sku] = (sku, nombre, precio);
                    else
                        simples[sku] = new ProductoSimple(sku, nombre, precio, peso, stock);
                }
            }

            var relaciones = new List<(string KitSku, string CompSku, int Cantidad)>();
            await using (var cmdRel = conexion.CreateCommand())
            {
                cmdRel.Transaction = _contexto.TransaccionActual;
                cmdRel.CommandText = "SELECT KitSku, ComponenteSku, Cantidad FROM KitComponentes";
                await using var reader = await cmdRel.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    relaciones.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
                }
            }

            var instanciasKit = new Dictionary<string, KitCompuesto>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in kits)
            {
                var info = kv.Value;
                instanciasKit[kv.Key] = new KitCompuesto(info.Sku, info.Nombre, info.Precio);
            }

            foreach (var (kitSku, compSku, cant) in relaciones)
            {
                if (!instanciasKit.TryGetValue(kitSku, out var kitPadre))
                    continue;

                if (simples.TryGetValue(compSku, out var prodComp))
                    kitPadre.AgregarComponente(prodComp, cant);
                else if (instanciasKit.TryGetValue(compSku, out var subKit))
                    kitPadre.AgregarComponente(subKit, cant);
            }

            var resultado = new List<IElementoCatalogo>();
            resultado.AddRange(simples.Values);
            resultado.AddRange(instanciasKit.Values);
            return resultado.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task GuardarProductoAsync(ProductoSimple producto, CancellationToken ct = default)
    {
        if (producto is null)
            throw new ArgumentNullException(nameof(producto));

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO CatalogoElementos (Sku, Nombre, TipoElemento, PrecioUnitario, PesoKg, StockDisponible, StockMinimo, Activo)
VALUES ($sku, $nombre, 'SIMPLE', $precio, $peso, $stock, 0, 1)
ON CONFLICT(Sku) DO UPDATE SET
    Nombre = excluded.Nombre,
    PrecioUnitario = excluded.PrecioUnitario,
    PesoKg = excluded.PesoKg,
    StockDisponible = excluded.StockDisponible,
    Activo = 1;";

            cmd.Parameters.AddWithValue("$sku", producto.Sku);
            cmd.Parameters.AddWithValue("$nombre", producto.Nombre);
            cmd.Parameters.AddWithValue("$precio", Convert.ToDouble(producto.PrecioUnitario));
            cmd.Parameters.AddWithValue("$peso", Convert.ToDouble(producto.PesoKg));
            cmd.Parameters.AddWithValue("$stock", producto.StockDisponible);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task GuardarKitAsync(KitCompuesto kit, CancellationToken ct = default)
    {
        if (kit is null)
            throw new ArgumentNullException(nameof(kit));

        kit.ValidarSinCiclos();

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmdKit = conexion.CreateCommand();
            cmdKit.Transaction = _contexto.TransaccionActual;
            cmdKit.CommandText = @"
INSERT INTO CatalogoElementos (Sku, Nombre, TipoElemento, PrecioUnitario, PesoKg, StockDisponible, StockMinimo, Activo)
VALUES ($sku, $nombre, 'KIT', $precio, $peso, 0, 0, 1)
ON CONFLICT(Sku) DO UPDATE SET
    Nombre = excluded.Nombre,
    PrecioUnitario = excluded.PrecioUnitario,
    PesoKg = excluded.PesoKg,
    Activo = 1;";

            cmdKit.Parameters.AddWithValue("$sku", kit.Sku);
            cmdKit.Parameters.AddWithValue("$nombre", kit.Nombre);
            cmdKit.Parameters.AddWithValue("$precio", Convert.ToDouble(kit.PrecioUnitario));
            cmdKit.Parameters.AddWithValue("$peso", Convert.ToDouble(kit.PesoKg));
            await cmdKit.ExecuteNonQueryAsync(ct);

            await using var cmdDel = conexion.CreateCommand();
            cmdDel.Transaction = _contexto.TransaccionActual;
            cmdDel.CommandText = "DELETE FROM KitComponentes WHERE KitSku = $kitSku";
            cmdDel.Parameters.AddWithValue("$kitSku", kit.Sku);
            await cmdDel.ExecuteNonQueryAsync(ct);

            foreach (var (comp, cantidad) in kit.Componentes)
            {
                await using var cmdIns = conexion.CreateCommand();
                cmdIns.Transaction = _contexto.TransaccionActual;
                cmdIns.CommandText = @"
INSERT INTO KitComponentes (KitSku, ComponenteSku, Cantidad)
VALUES ($kitSku, $compSku, $cant);";
                cmdIns.Parameters.AddWithValue("$kitSku", kit.Sku);
                cmdIns.Parameters.AddWithValue("$compSku", comp.Sku);
                cmdIns.Parameters.AddWithValue("$cant", cantidad);
                await cmdIns.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task ActualizarStockAsync(string sku, int nuevoStock, int deltaMovimiento, string motivo, string referencia, CancellationToken ct = default)
    {
        if (nuevoStock < 0)
            throw new InvalidOperationException($"El stock resultante para {sku} no puede ser negativo");

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            // Apply the delta against the value held by SQLite. This prevents a stale
            // read in a competing operation from overwriting a newer stock balance.
            await using var cmdUpd = conexion.CreateCommand();
            cmdUpd.Transaction = _contexto.TransaccionActual;
            cmdUpd.CommandText = @"
UPDATE CatalogoElementos
SET StockDisponible = StockDisponible + $delta
WHERE Sku = $sku AND StockDisponible + $delta >= 0;";
            cmdUpd.Parameters.AddWithValue("$delta", deltaMovimiento);
            cmdUpd.Parameters.AddWithValue("$sku", sku);
            int filasActualizadas = await cmdUpd.ExecuteNonQueryAsync(ct);
            if (filasActualizadas != 1)
                throw new InvalidOperationException($"No se pudo actualizar el stock de {sku}: el producto no existe o el stock resultante sería negativo.");

            await using var cmdLeerStock = conexion.CreateCommand();
            cmdLeerStock.Transaction = _contexto.TransaccionActual;
            cmdLeerStock.CommandText = "SELECT StockDisponible FROM CatalogoElementos WHERE Sku = $sku";
            cmdLeerStock.Parameters.AddWithValue("$sku", sku);
            object? stockLeido = await cmdLeerStock.ExecuteScalarAsync(ct);
            if (stockLeido is null || stockLeido is DBNull)
                throw new InvalidOperationException($"No se pudo leer el stock actualizado de {sku}.");
            int stockResultante = Convert.ToInt32(stockLeido, CultureInfo.InvariantCulture);

            await using var cmdMov = conexion.CreateCommand();
            cmdMov.Transaction = _contexto.TransaccionActual;
            cmdMov.CommandText = @"
INSERT INTO MovimientosInventario (Id, FechaUtc, Sku, DeltaCantidad, StockResultante, Motivo, Referencia)
VALUES ($id, $fecha, $sku, $delta, $stock, $motivo, $ref);";
            cmdMov.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmdMov.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            cmdMov.Parameters.AddWithValue("$sku", sku);
            cmdMov.Parameters.AddWithValue("$delta", deltaMovimiento);
            cmdMov.Parameters.AddWithValue("$stock", stockResultante);
            cmdMov.Parameters.AddWithValue("$motivo", motivo ?? string.Empty);
            cmdMov.Parameters.AddWithValue("$ref", referencia ?? string.Empty);
            await cmdMov.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }
}

// Repositorio SQLite para clientes
public sealed class RepositorioClientesSqlite : IRepositorioClientes
{
    private readonly ContextoSqliteLogiMarket _contexto;

    public RepositorioClientesSqlite(ContextoSqliteLogiMarket contexto)
    {
        _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
    }

    public async Task<Cliente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var todos = await BuscarAsync(null, ct);
        return todos.FirstOrDefault(c => c.Id == id);
    }

    public async Task<Cliente?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        var todos = await BuscarAsync(null, ct);
        return todos.FirstOrDefault(c => string.Equals(c.Codigo, codigo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<Cliente>> BuscarAsync(string? filtro, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var lista = new List<Cliente>();
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT Id, Codigo, Nombre, Contacto, Categoria, Activo FROM Clientes ORDER BY Codigo";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = Guid.Parse(reader.GetString(0));
                string codigo = reader.GetString(1);
                string nombre = reader.GetString(2);
                string contacto = reader.GetString(3);
                var categoria = Enum.Parse<CategoriaCliente>(reader.GetString(4), ignoreCase: true);
                bool activo = reader.GetInt32(5) == 1;

                var cliente = new Cliente(id, codigo, nombre, contacto, categoria, activo);
                if (string.IsNullOrWhiteSpace(filtro) ||
                    cliente.Codigo.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                    cliente.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase))
                {
                    lista.Add(cliente);
                }
            }

            return lista.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task GuardarAsync(Cliente cliente, CancellationToken ct = default)
    {
        if (cliente is null)
            throw new ArgumentNullException(nameof(cliente));

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO Clientes (Id, Codigo, Nombre, Contacto, Categoria, Activo)
VALUES ($id, $codigo, $nombre, $contacto, $cat, $activo)
ON CONFLICT(Id) DO UPDATE SET
    Codigo = excluded.Codigo,
    Nombre = excluded.Nombre,
    Contacto = excluded.Contacto,
    Categoria = excluded.Categoria,
    Activo = excluded.Activo;";

            cmd.Parameters.AddWithValue("$id", cliente.Id.ToString());
            cmd.Parameters.AddWithValue("$codigo", cliente.Codigo);
            cmd.Parameters.AddWithValue("$nombre", cliente.Nombre);
            cmd.Parameters.AddWithValue("$contacto", cliente.Contacto);
            cmd.Parameters.AddWithValue("$cat", cliente.Categoria.ToString());
            cmd.Parameters.AddWithValue("$activo", cliente.Activo ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }
}

// Repositorio SQLite para agregados de pedido, pagos idempotentes, despachos y devoluciones
public sealed class RepositorioPedidosSqlite : IRepositorioPedidos
{
    private readonly ContextoSqliteLogiMarket _contexto;

    public RepositorioPedidosSqlite(ContextoSqliteLogiMarket contexto)
    {
        _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
    }

    public async Task<Pedido?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default)
    {
        var todos = await ListarTodosAsync(ct);
        return todos.FirstOrDefault(p => p.Id == id);
    }

    public async Task<Pedido?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        var todos = await ListarTodosAsync(ct);
        return todos.FirstOrDefault(p => string.Equals(p.Codigo, codigo.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<Pedido>> ListarTodosAsync(CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var lineasPorPedido = new Dictionary<Guid, List<LineaPedido>>();
            await using (var cmdLineas = conexion.CreateCommand())
            {
                cmdLineas.Transaction = _contexto.TransaccionActual;
                cmdLineas.CommandText = "SELECT Id, PedidoId, Sku, NombreProducto, EsKit, Cantidad, PrecioUnitario, ComponentesJson FROM PedidoLineas";
                await using var rLineas = await cmdLineas.ExecuteReaderAsync(ct);
                while (await rLineas.ReadAsync(ct))
                {
                    var lineaId = Guid.Parse(rLineas.GetString(0));
                    var pedidoId = Guid.Parse(rLineas.GetString(1));
                    string sku = rLineas.GetString(2);
                    string nombre = rLineas.GetString(3);
                    bool esKit = rLineas.GetInt32(4) == 1;
                    int cant = rLineas.GetInt32(5);
                    decimal precio = Convert.ToDecimal(rLineas.GetDouble(6), CultureInfo.InvariantCulture);
                    string json = rLineas.GetString(7);

                    var comps = JsonSerializer.Deserialize<List<InstantaneaComponenteLinea>>(json) ?? new List<InstantaneaComponenteLinea>();
                    var linea = new LineaPedido(lineaId, sku, nombre, esKit, cant, precio, comps);

                    if (!lineasPorPedido.TryGetValue(pedidoId, out var lista))
                    {
                        lista = new List<LineaPedido>();
                        lineasPorPedido[pedidoId] = lista;
                    }
                    lista.Add(linea);
                }
            }

            var calculadora = new CalculadoraDescuentos(new IPoliticaDescuento[]
            {
                new PoliticaMayorista15(),
                new PoliticaVip10(),
                new PoliticaSinDescuento()
            });

            var pedidos = new List<Pedido>();
            await using var cmdPedidos = conexion.CreateCommand();
            cmdPedidos.Transaction = _contexto.TransaccionActual;
            cmdPedidos.CommandText = @"
SELECT Id, Codigo, ClienteId, NombreCliente, CategoriaCliente, TipoEntrega, TarifaEnvio,
       PlazoEntregaDias, Estado, FechaCreacionUtc, FechaEntregaUtc, TieneDevolucion
FROM Pedidos ORDER BY FechaCreacionUtc DESC";

            await using var reader = await cmdPedidos.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = Guid.Parse(reader.GetString(0));
                string codigo = reader.GetString(1);
                var clienteId = Guid.Parse(reader.GetString(2));
                string nombreCliente = reader.GetString(3);
                var catCliente = Enum.Parse<CategoriaCliente>(reader.GetString(4), ignoreCase: true);
                var tipoEntrega = Enum.Parse<TipoEntrega>(reader.GetString(5), ignoreCase: true);
                decimal tarifa = Convert.ToDecimal(reader.GetDouble(6), CultureInfo.InvariantCulture);
                int plazo = reader.GetInt32(7);
                var estadoPers = Enum.Parse<EstadoPedido>(reader.GetString(8), ignoreCase: true);
                DateTime? fechaEntrega = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                bool tieneDev = reader.GetInt32(11) == 1;

                lineasPorPedido.TryGetValue(id, out var lineas);
                var pedido = new Pedido(id, codigo, clienteId, nombreCliente, catCliente, tipoEntrega, tarifa, plazo, lineas ?? new List<LineaPedido>(), calculadora);

                ReconstruirEstadoPedido(pedido, estadoPers, fechaEntrega, tieneDev);
                pedidos.Add(pedido);
            }

            return pedidos.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    private static void ReconstruirEstadoPedido(Pedido pedido, EstadoPedido estadoObjetivo, DateTime? fechaEntrega, bool tieneDevolucion)
    {
        switch (estadoObjetivo)
        {
            case EstadoPedido.BORRADOR:
                break;
            case EstadoPedido.VALIDADO:
                pedido.Validar();
                break;
            case EstadoPedido.PAGADO:
                pedido.Validar();
                pedido.MarcarComoPagado();
                break;
            case EstadoPedido.DESPACHADO:
                pedido.Validar();
                pedido.MarcarComoPagado();
                pedido.MarcarComoDespachado();
                break;
            case EstadoPedido.ENTREGADO:
                pedido.Validar();
                pedido.MarcarComoPagado();
                pedido.MarcarComoDespachado();
                pedido.MarcarComoEntregado(fechaEntrega ?? DateTime.UtcNow);
                break;
            case EstadoPedido.CANCELADO:
                pedido.Cancelar();
                break;
        }

        if (tieneDevolucion && pedido.Estado == EstadoPedido.ENTREGADO)
            pedido.MarcarDevolucionRegistrada();
    }

    public async Task GuardarNuevoAsync(Pedido pedido, CancellationToken ct = default)
    {
        if (pedido is null)
            throw new ArgumentNullException(nameof(pedido));

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO Pedidos (
    Id, Codigo, ClienteId, NombreCliente, CategoriaCliente, TipoEntrega, TarifaEnvio,
    PlazoEntregaDias, Subtotal, Descuento, PoliticaDescuento, Total, Estado,
    FechaCreacionUtc, FechaEntregaUtc, TieneDevolucion
) VALUES (
    $id, $codigo, $clienteId, $nombreCliente, $cat, $entrega, $tarifa,
    $plazo, $subtotal, $descuento, $politica, $total, $estado,
    $creacion, $entregaUtc, $dev
);";

            cmd.Parameters.AddWithValue("$id", pedido.Id.ToString());
            cmd.Parameters.AddWithValue("$codigo", pedido.Codigo);
            cmd.Parameters.AddWithValue("$clienteId", pedido.ClienteId.ToString());
            cmd.Parameters.AddWithValue("$nombreCliente", pedido.NombreCliente);
            cmd.Parameters.AddWithValue("$cat", pedido.CategoriaCliente.ToString());
            cmd.Parameters.AddWithValue("$entrega", pedido.TipoEntrega.ToString());
            cmd.Parameters.AddWithValue("$tarifa", Convert.ToDouble(pedido.TarifaEnvio));
            cmd.Parameters.AddWithValue("$plazo", pedido.PlazoEntregaDias);
            cmd.Parameters.AddWithValue("$subtotal", Convert.ToDouble(pedido.Subtotal));
            cmd.Parameters.AddWithValue("$descuento", Convert.ToDouble(pedido.Descuento));
            cmd.Parameters.AddWithValue("$politica", pedido.PoliticaDescuentoAplicada);
            cmd.Parameters.AddWithValue("$total", Convert.ToDouble(pedido.Total));
            cmd.Parameters.AddWithValue("$estado", pedido.Estado.ToString());
            cmd.Parameters.AddWithValue("$creacion", pedido.FechaCreacionUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$entregaUtc", pedido.FechaEntregaUtc.HasValue ? pedido.FechaEntregaUtc.Value.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("$dev", pedido.TieneDevolucionRegistrada ? 1 : 0);

            await cmd.ExecuteNonQueryAsync(ct);

            foreach (var linea in pedido.Lineas)
            {
                await using var cmdLin = conexion.CreateCommand();
                cmdLin.Transaction = _contexto.TransaccionActual;
                cmdLin.CommandText = @"
INSERT INTO PedidoLineas (Id, PedidoId, Sku, NombreProducto, EsKit, Cantidad, PrecioUnitario, ComponentesJson)
VALUES ($id, $pedidoId, $sku, $nombre, $esKit, $cant, $precio, $json);";
                cmdLin.Parameters.AddWithValue("$id", linea.Id.ToString());
                cmdLin.Parameters.AddWithValue("$pedidoId", pedido.Id.ToString());
                cmdLin.Parameters.AddWithValue("$sku", linea.Sku);
                cmdLin.Parameters.AddWithValue("$nombre", linea.NombreProducto);
                cmdLin.Parameters.AddWithValue("$esKit", linea.EsKit ? 1 : 0);
                cmdLin.Parameters.AddWithValue("$cant", linea.Cantidad);
                cmdLin.Parameters.AddWithValue("$precio", Convert.ToDouble(linea.PrecioUnitarioInstantanea));
                cmdLin.Parameters.AddWithValue("$json", JsonSerializer.Serialize(linea.ComponentesExpandidos));
                await cmdLin.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task ActualizarAsync(Pedido pedido, CancellationToken ct = default)
    {
        if (pedido is null)
            throw new ArgumentNullException(nameof(pedido));

        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
UPDATE Pedidos SET
    Subtotal = $subtotal,
    Descuento = $descuento,
    PoliticaDescuento = $politica,
    Total = $total,
    Estado = $estado,
    FechaEntregaUtc = $entregaUtc,
    TieneDevolucion = $dev
WHERE Id = $id;";

            cmd.Parameters.AddWithValue("$subtotal", Convert.ToDouble(pedido.Subtotal));
            cmd.Parameters.AddWithValue("$descuento", Convert.ToDouble(pedido.Descuento));
            cmd.Parameters.AddWithValue("$politica", pedido.PoliticaDescuentoAplicada);
            cmd.Parameters.AddWithValue("$total", Convert.ToDouble(pedido.Total));
            cmd.Parameters.AddWithValue("$estado", pedido.Estado.ToString());
            cmd.Parameters.AddWithValue("$entregaUtc", pedido.FechaEntregaUtc.HasValue ? pedido.FechaEntregaUtc.Value.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("$dev", pedido.TieneDevolucionRegistrada ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", pedido.Id.ToString());

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<bool> ExisteClaveIdempotenciaAsync(string claveIdempotencia, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT COUNT(1) FROM PagosPedido WHERE ClaveIdempotencia = $clave";
            cmd.Parameters.AddWithValue("$clave", claveIdempotencia);
            long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            return count > 0;
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<PagoRegistrado?> ObtenerPagoPorClaveAsync(string claveIdempotencia, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT PedidoId, ReferenciaPasarela, Proveedor, Monto FROM PagosPedido WHERE ClaveIdempotencia = $clave";
            cmd.Parameters.AddWithValue("$clave", claveIdempotencia);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new PagoRegistrado(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture));
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<PagoRegistrado?> ObtenerPagoPorPedidoAsync(Guid pedidoId, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT ClaveIdempotencia, ReferenciaPasarela, Proveedor, Monto FROM PagosPedido WHERE PedidoId = $pedido ORDER BY FechaUtc DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$pedido", pedidoId.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new PagoRegistrado(pedidoId, reader.GetString(1), reader.GetString(2), Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture));
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task RegistrarPagoAprobadoAsync(Guid pedidoId, string claveIdempotencia, string referenciaPasarela, string proveedor, decimal monto, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO PagosPedido (Id, PedidoId, ClaveIdempotencia, ReferenciaPasarela, Proveedor, Monto, FechaUtc)
VALUES ($id, $pedidoId, $clave, $ref, $prov, $monto, $fecha);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$pedidoId", pedidoId.ToString());
            cmd.Parameters.AddWithValue("$clave", claveIdempotencia);
            cmd.Parameters.AddWithValue("$ref", referenciaPasarela);
            cmd.Parameters.AddWithValue("$prov", proveedor);
            cmd.Parameters.AddWithValue("$monto", Convert.ToDouble(monto));
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task RegistrarDespachoAsync(Guid pedidoId, string numeroGuia, string tipoEntrega, int plazoDias, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO DespachosPedido (Id, PedidoId, NumeroGuia, TipoEntrega, PlazoDias, FechaDespachoUtc)
VALUES ($id, $pedidoId, $guia, $entrega, $plazo, $fecha)
ON CONFLICT(PedidoId) DO UPDATE SET
    NumeroGuia = excluded.NumeroGuia,
    TipoEntrega = excluded.TipoEntrega,
    PlazoDias = excluded.PlazoDias,
    FechaDespachoUtc = excluded.FechaDespachoUtc;";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$pedidoId", pedidoId.ToString());
            cmd.Parameters.AddWithValue("$guia", numeroGuia);
            cmd.Parameters.AddWithValue("$entrega", tipoEntrega);
            cmd.Parameters.AddWithValue("$plazo", plazoDias);
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task RegistrarDevolucionAsync(Guid pedidoId, MotivoDevolucion motivo, decimal montoReembolso, bool stockReintegrado, string observacion, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO DevolucionesPedido (Id, PedidoId, Motivo, MontoReembolso, StockReintegrado, Observacion, FechaUtc)
VALUES ($id, $pedidoId, $motivo, $monto, $stockInt, $obs, $fecha);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$pedidoId", pedidoId.ToString());
            cmd.Parameters.AddWithValue("$motivo", motivo.ToString());
            cmd.Parameters.AddWithValue("$monto", Convert.ToDouble(montoReembolso));
            cmd.Parameters.AddWithValue("$stockInt", stockReintegrado ? 1 : 0);
            cmd.Parameters.AddWithValue("$obs", observacion ?? string.Empty);
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }
}

// Repositorio SQLite para auditoria, Outbox transaccional y consulta de movimientos de inventario
public sealed class RepositorioAuditoriaOutboxSqlite : IRepositorioAuditoriaOutbox
{
    private readonly ContextoSqliteLogiMarket _contexto;

    public RepositorioAuditoriaOutboxSqlite(ContextoSqliteLogiMarket contexto)
    {
        _contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
    }

    public async Task RegistrarAuditoriaAsync(string categoria, string accion, string detalle, string usuario, string? correlacionId = null, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT INTO AuditoriaLog (Id, FechaUtc, Categoria, Accion, Detalle, Usuario, CorrelacionId)
VALUES ($id, $fecha, $cat, $accion, $detalle, $usuario, $corr);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$cat", categoria);
            cmd.Parameters.AddWithValue("$accion", accion);
            cmd.Parameters.AddWithValue("$detalle", detalle);
            cmd.Parameters.AddWithValue("$usuario", usuario);
            cmd.Parameters.AddWithValue("$corr", (object?)correlacionId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task AgregarEventoOutboxAsync(string tipoEvento, string payloadJson, string claveUnica, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = @"
INSERT OR IGNORE INTO EventosOutbox (Id, TipoEvento, PayloadJson, ClaveUnica, Estado, Intentos, FechaCreacionUtc)
VALUES ($id, $tipo, $payload, $clave, 'PENDIENTE', 0, $fecha);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("$tipo", tipoEvento);
            cmd.Parameters.AddWithValue("$payload", payloadJson);
            cmd.Parameters.AddWithValue("$clave", claveUnica);
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<(Guid Id, string TipoEvento, string PayloadJson, int Intentos)>> ObtenerEventosOutboxPendientesAsync(CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var pendientes = new List<(Guid Id, string TipoEvento, string PayloadJson, int Intentos)>();
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT Id, TipoEvento, PayloadJson, Intentos FROM EventosOutbox WHERE Estado = 'PENDIENTE' ORDER BY FechaCreacionUtc";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                pendientes.Add((
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3)));
            }
            return pendientes.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task MarcarOutboxProcesadoAsync(Guid eventoId, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "UPDATE EventosOutbox SET Estado = 'PROCESADO', FechaProcesadoUtc = $fecha WHERE Id = $id";
            cmd.Parameters.AddWithValue("$fecha", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id", eventoId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task RegistrarFalloOutboxAsync(Guid eventoId, string error, CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "UPDATE EventosOutbox SET Intentos = Intentos + 1, UltimoError = $err WHERE Id = $id";
            cmd.Parameters.AddWithValue("$err", error);
            cmd.Parameters.AddWithValue("$id", eventoId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<(DateTime FechaUtc, string Categoria, string Accion, string Detalle, string Usuario)>> ListarAuditoriaAsync(CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var items = new List<(DateTime FechaUtc, string Categoria, string Accion, string Detalle, string Usuario)>();
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT FechaUtc, Categoria, Accion, Detalle, Usuario FROM AuditoriaLog ORDER BY FechaUtc DESC";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add((
                    DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)));
            }
            return items.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<(DateTime FechaUtc, string Sku, int Delta, int StockResultante, string Motivo, string Referencia)>> ListarMovimientosInventarioAsync(CancellationToken ct = default)
    {
        var conexion = await _contexto.ObtenerConexionAbiertaAsync(ct);
        try
        {
            var items = new List<(DateTime FechaUtc, string Sku, int Delta, int StockResultante, string Motivo, string Referencia)>();
            await using var cmd = conexion.CreateCommand();
            cmd.Transaction = _contexto.TransaccionActual;
            cmd.CommandText = "SELECT FechaUtc, Sku, DeltaCantidad, StockResultante, Motivo, Referencia FROM MovimientosInventario ORDER BY FechaUtc DESC";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add((
                    DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5)));
            }
            return items.AsReadOnly();
        }
        finally
        {
            if (!_contexto.EsConexionCompartida(conexion))
                await conexion.DisposeAsync();
        }
    }
}
