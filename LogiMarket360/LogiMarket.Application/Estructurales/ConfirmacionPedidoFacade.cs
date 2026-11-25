using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Estructurales;

// Resultado de la confirmacion coordinada de un pedido
public sealed record ResultadoConfirmacionPedido(
    bool Exitoso,
    string Mensaje,
    Guid PedidoId,
    string CodigoPedido,
    decimal TotalCobrado,
    string ReferenciaPago,
    string NumeroGuia);

// Fachada que coordina autorizacion, validacion, cobro idempotente, persistencia, stock y Outbox
public sealed class ConfirmacionPedidoFacade
{
    private readonly IContextoSesion _sesion;
    private readonly IRepositorioPedidos _repositorioPedidos;
    private readonly IRepositorioCatalogo _repositorioCatalogo;
    private readonly IRepositorioAuditoriaOutbox _auditoriaOutbox;
    private readonly IUnidadTrabajo _unidadTrabajo;
    private readonly IPasarelaPago _pasarelaPago;
    private readonly IMetricasSesion _metricas;

    public ConfirmacionPedidoFacade(
        IContextoSesion sesion,
        IRepositorioPedidos repositorioPedidos,
        IRepositorioCatalogo repositorioCatalogo,
        IRepositorioAuditoriaOutbox auditoriaOutbox,
        IUnidadTrabajo unidadTrabajo,
        IPasarelaPago pasarelaPago,
        IMetricasSesion metricas)
    {
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _repositorioPedidos = repositorioPedidos ?? throw new ArgumentNullException(nameof(repositorioPedidos));
        _repositorioCatalogo = repositorioCatalogo ?? throw new ArgumentNullException(nameof(repositorioCatalogo));
        _auditoriaOutbox = auditoriaOutbox ?? throw new ArgumentNullException(nameof(auditoriaOutbox));
        _unidadTrabajo = unidadTrabajo ?? throw new ArgumentNullException(nameof(unidadTrabajo));
        _pasarelaPago = pasarelaPago ?? throw new ArgumentNullException(nameof(pasarelaPago));
        _metricas = metricas ?? throw new ArgumentNullException(nameof(metricas));
    }

    public async Task<ResultadoConfirmacionPedido> ConfirmarAsync(
        Pedido pedido,
        string claveIdempotencia,
        CancellationToken ct = default)
    {
        if (pedido is null)
            throw new ArgumentNullException(nameof(pedido));

        if (string.IsNullOrWhiteSpace(claveIdempotencia))
            throw new ArgumentException("La clave de idempotencia es obligatoria", nameof(claveIdempotencia));

        if (!_sesion.TienePermiso(RolUsuario.VENTAS, RolUsuario.ADMIN))
        {
            string rolActual = _sesion.UsuarioActual?.Rol.ToString() ?? "SIN_SESION";
            return new ResultadoConfirmacionPedido(
                false,
                $"Permiso denegado para confirmar pedidos con rol {rolActual}",
                pedido.Id,
                pedido.Codigo,
                pedido.Total,
                string.Empty,
                string.Empty);
        }

        var requerimientos = pedido.ObtenerRequerimientosStockPorSku();
        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";
        string guiaGenerada = SelectorFabricaEntrega.ObtenerFabrica(pedido.TipoEntrega)
            .CrearGeneradorGuia().GenerarEtiquetaGuia(pedido.Codigo, pedido.NombreCliente);
        ResultadoConfirmacionPedido resultado = new(false, "La confirmación no se completó", pedido.Id, pedido.Codigo, pedido.Total, string.Empty, string.Empty);
        bool cobroRechazado = false;

        // La clave y el stock se vuelven a comprobar dentro de la misma transacción que escribe pago,
        // pedido, movimientos y Outbox. Esto evita que dos confirmaciones concurrentes lean el mismo stock.
        await _unidadTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            var pagoExistente = await _repositorioPedidos.ObtenerPagoPorClaveAsync(claveIdempotencia, token);
            if (pagoExistente is not null)
            {
                resultado = pagoExistente.PedidoId == pedido.Id
                    ? new ResultadoConfirmacionPedido(true, "Se devuelve el resultado del cobro ya registrado", pedido.Id, pedido.Codigo, pagoExistente.Monto, pagoExistente.ReferenciaPasarela, string.Empty)
                    : new ResultadoConfirmacionPedido(false, "La clave de idempotencia ya pertenece a otro pedido", pedido.Id, pedido.Codigo, pedido.Total, string.Empty, string.Empty);
                return;
            }

            if (pedido.Estado is not (EstadoPedido.BORRADOR or EstadoPedido.VALIDADO))
            {
                resultado = new ResultadoConfirmacionPedido(false, $"No se puede confirmar un pedido en estado {pedido.Estado}", pedido.Id, pedido.Codigo, pedido.Total, string.Empty, string.Empty);
                return;
            }
            if (pedido.Estado == EstadoPedido.BORRADOR)
                pedido.Validar();

            foreach (var (sku, cantidadRequerida) in requerimientos)
            {
                var elemento = await _repositorioCatalogo.ObtenerPorSkuAsync(sku, token);
                if (elemento is null)
                {
                    resultado = new ResultadoConfirmacionPedido(false, $"No existe el componente fisico con SKU {sku}", pedido.Id, pedido.Codigo, pedido.Total, string.Empty, string.Empty);
                    return;
                }
                if (elemento.StockDisponible < cantidadRequerida)
                {
                    resultado = new ResultadoConfirmacionPedido(false, $"Stock insuficiente para SKU {sku}: disponible {elemento.StockDisponible}, requerido {cantidadRequerida}", pedido.Id, pedido.Codigo, pedido.Total, string.Empty, string.Empty);
                    return;
                }
            }

            var cobro = await _pasarelaPago.CobrarAsync(pedido.Total, claveIdempotencia, token);
            if (!cobro.Exitoso)
            {
                cobroRechazado = true;
                resultado = new ResultadoConfirmacionPedido(false, $"Cobro rechazado: {cobro.Mensaje}", pedido.Id, pedido.Codigo, pedido.Total, cobro.ReferenciaTransaccion, string.Empty);
                return;
            }

            pedido.MarcarComoPagado();
            var existente = await _repositorioPedidos.ObtenerPorIdAsync(pedido.Id, token);
            if (existente is null)
                await _repositorioPedidos.GuardarNuevoAsync(pedido, token);
            else
                await _repositorioPedidos.ActualizarAsync(pedido, token);

            foreach (var (sku, cantidadRequerida) in requerimientos)
            {
                var prod = await _repositorioCatalogo.ObtenerPorSkuAsync(sku, token);
                int nuevoStock = prod!.StockDisponible - cantidadRequerida;
                await _repositorioCatalogo.ActualizarStockAsync(sku, nuevoStock, -cantidadRequerida, $"VENTA_{pedido.Codigo}", pedido.Codigo, token);
            }

            await _repositorioPedidos.RegistrarPagoAprobadoAsync(pedido.Id, claveIdempotencia, cobro.ReferenciaTransaccion, cobro.Proveedor, pedido.Total, token);
            await _auditoriaOutbox.RegistrarAuditoriaAsync("PEDIDO", "CONFIRMAR_PEDIDO", $"Pedido {pedido.Codigo} pagado por ${pedido.Total:0.00} (Ref: {cobro.ReferenciaTransaccion})", usuario, claveIdempotencia, token);

            string payload = JsonSerializer.Serialize(new
            {
                pedido.Id, pedido.Codigo, pedido.ClienteId, pedido.NombreCliente, pedido.Total,
                ReferenciaPasarela = cobro.ReferenciaTransaccion, Guia = guiaGenerada
            });
            await _auditoriaOutbox.AgregarEventoOutboxAsync("PedidoConfirmado", payload, $"OUTBOX-CONF-{claveIdempotencia}", token);
            resultado = new ResultadoConfirmacionPedido(true, $"Pedido {pedido.Codigo} confirmado y pagado correctamente", pedido.Id, pedido.Codigo, pedido.Total, cobro.ReferenciaTransaccion, guiaGenerada);
        }, ct);

        if (cobroRechazado)
            _metricas.RegistrarCobroRechazado();
        if (resultado.Exitoso && resultado.Mensaje.Contains("confirmado y pagado", StringComparison.Ordinal))
            _metricas.RegistrarPedidoConfirmado(resultado.TotalCobrado);
        return resultado;
    }
}
