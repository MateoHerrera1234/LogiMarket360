using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Puertos;

// Representa un usuario autenticado localmente con su rol operativo
public sealed record UsuarioSesion(Guid Id, string NombreUsuario, string NombreCompleto, RolUsuario Rol);

// Servicio que mantiene la sesion activa y verifica permisos en Application
public interface IContextoSesion
{
    UsuarioSesion? UsuarioActual { get; }
    void IniciarSesion(UsuarioSesion usuario);
    void CerrarSesion();
    bool TienePermiso(params RolUsuario[] rolesPermitidos);
}

// Repositorio de catalogo para productos simples y kits compuestos
public interface IRepositorioCatalogo
{
    Task<IElementoCatalogo?> ObtenerPorSkuAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<IElementoCatalogo>> ListarTodosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductoSimple>> ListarProductosSimplesAsync(CancellationToken ct = default);
    Task GuardarProductoAsync(ProductoSimple producto, CancellationToken ct = default);
    Task GuardarKitAsync(KitCompuesto kit, CancellationToken ct = default);
    Task ActualizarStockAsync(string sku, int nuevoStock, int deltaMovimiento, string motivo, string referencia, CancellationToken ct = default);
}

// Repositorio para consulta y persistencia de clientes
public interface IRepositorioClientes
{
    Task<Cliente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Cliente?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<Cliente>> BuscarAsync(string? filtro, CancellationToken ct = default);
    Task GuardarAsync(Cliente cliente, CancellationToken ct = default);
}

// Repositorio para agregados de pedido y control de idempotencia
public interface IRepositorioPedidos
{
    Task<Pedido?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
    Task<Pedido?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<Pedido>> ListarTodosAsync(CancellationToken ct = default);
    Task GuardarNuevoAsync(Pedido pedido, CancellationToken ct = default);
    Task ActualizarAsync(Pedido pedido, CancellationToken ct = default);
    Task<bool> ExisteClaveIdempotenciaAsync(string claveIdempotencia, CancellationToken ct = default);
    Task<PagoRegistrado?> ObtenerPagoPorClaveAsync(string claveIdempotencia, CancellationToken ct = default);
    Task<PagoRegistrado?> ObtenerPagoPorPedidoAsync(Guid pedidoId, CancellationToken ct = default);
    Task RegistrarPagoAprobadoAsync(Guid pedidoId, string claveIdempotencia, string referenciaPasarela, string proveedor, decimal monto, CancellationToken ct = default);
    Task RegistrarDespachoAsync(Guid pedidoId, string numeroGuia, string tipoEntrega, int plazoDias, CancellationToken ct = default);
    Task RegistrarDevolucionAsync(Guid pedidoId, MotivoDevolucion motivo, decimal montoReembolso, bool stockReintegrado, string observacion, CancellationToken ct = default);
}

public sealed record PagoRegistrado(Guid PedidoId, string ReferenciaPasarela, string Proveedor, decimal Monto);

// Puerto para persistencia transaccional de Outbox y auditoria
public interface IRepositorioAuditoriaOutbox
{
    Task RegistrarAuditoriaAsync(string categoria, string accion, string detalle, string usuario, string? correlacionId = null, CancellationToken ct = default);
    Task AgregarEventoOutboxAsync(string tipoEvento, string payloadJson, string claveUnica, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid Id, string TipoEvento, string PayloadJson, int Intentos)>> ObtenerEventosOutboxPendientesAsync(CancellationToken ct = default);
    Task MarcarOutboxProcesadoAsync(Guid eventoId, CancellationToken ct = default);
    Task RegistrarFalloOutboxAsync(Guid eventoId, string error, CancellationToken ct = default);
    Task<IReadOnlyList<(DateTime FechaUtc, string Categoria, string Accion, string Detalle, string Usuario)>> ListarAuditoriaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<(DateTime FechaUtc, string Sku, int Delta, int StockResultante, string Motivo, string Referencia)>> ListarMovimientosInventarioAsync(CancellationToken ct = default);
}

// Unidad de trabajo real respaldada por transaccion de base de datos
public interface IUnidadTrabajo
{
    Task EjecutarEnTransaccionAsync(Func<CancellationToken, Task> operacion, CancellationToken ct = default);
}
