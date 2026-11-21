using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Estructurales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Devoluciones;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Comportamentales;

// Resultado uniforme de ejecutar un comando en Application
public sealed record ResultadoComando(bool Exitoso, string Mensaje, string? ReferenciaExtra = null);

// Contrato del patron Command
public interface IComandoLogiMarket
{
    string NombreComando { get; }
    Task<ResultadoComando> EjecutarAsync(CancellationToken ct = default);
}

// Invocador que ejecuta comandos y mantiene historial de ejecuciones
public sealed class InvocadorComandos
{
    private readonly List<(DateTime EjecutadoUtc, string Comando, bool Exitoso, string Mensaje)> _historial = new();

    public IReadOnlyList<(DateTime EjecutadoUtc, string Comando, bool Exitoso, string Mensaje)> Historial =>
        _historial.AsReadOnly();

    public async Task<ResultadoComando> EjecutarAsync(IComandoLogiMarket comando, CancellationToken ct = default)
    {
        if (comando is null)
            throw new ArgumentNullException(nameof(comando));

        var resultado = await comando.EjecutarAsync(ct);
        _historial.Add((DateTime.UtcNow, comando.NombreComando, resultado.Exitoso, resultado.Mensaje));
        return resultado;
    }
}

// Comando 1: confirmar pedido usando la cadena de validacion, fachada y observadores
public sealed class ConfirmarPedidoCommand : IComandoLogiMarket
{
    private readonly Pedido _pedido;
    private readonly string _claveIdempotencia;
    private readonly IManejadorValidacionPedido _cadenaValidacion;
    private readonly ConfirmacionPedidoFacade _fachada;
    private readonly PublicadorEventosSistema _publicador;

    public string NombreComando => "CONFIRMAR_PEDIDO";

    public ConfirmarPedidoCommand(
        Pedido pedido,
        string claveIdempotencia,
        IManejadorValidacionPedido cadenaValidacion,
        ConfirmacionPedidoFacade fachada,
        PublicadorEventosSistema publicador)
    {
        _pedido = pedido ?? throw new ArgumentNullException(nameof(pedido));
        _claveIdempotencia = claveIdempotencia;
        _cadenaValidacion = cadenaValidacion ?? throw new ArgumentNullException(nameof(cadenaValidacion));
        _fachada = fachada ?? throw new ArgumentNullException(nameof(fachada));
        _publicador = publicador ?? throw new ArgumentNullException(nameof(publicador));
    }

    public async Task<ResultadoComando> EjecutarAsync(CancellationToken ct = default)
    {
        var validacion = await _cadenaValidacion.ValidarAsync(new ContextoValidacionPedido(_pedido, _claveIdempotencia), ct);
        if (!validacion.EsValido)
            return new ResultadoComando(false, validacion.MotivoRechazo ?? "Validacion rechazada");

        var confirmacion = await _fachada.ConfirmarAsync(_pedido, _claveIdempotencia, ct);
        if (!confirmacion.Exitoso)
            return new ResultadoComando(false, confirmacion.Mensaje);

        await _publicador.PublicarEventoAsync(new EventoSistemaLogiMarket(
            "PEDIDO_CONFIRMADO",
            _pedido.Codigo,
            _pedido.NombreCliente,
            _pedido.Total,
            $"Pedido {_pedido.Codigo} confirmado. Guia: {confirmacion.NumeroGuia}",
            DateTime.UtcNow), ct);

        return new ResultadoComando(true, confirmacion.Mensaje, confirmacion.NumeroGuia);
    }
}

// Comando 2: cancelar pedido solo si esta en BORRADOR o VALIDADO
public sealed class CancelarPedidoCommand : IComandoLogiMarket
{
    private readonly Guid _pedidoId;
    private readonly IRepositorioPedidos _repositorioPedidos;
    private readonly IRepositorioAuditoriaOutbox _auditoria;
    private readonly IUnidadTrabajo _unidadTrabajo;
    private readonly IContextoSesion _sesion;

    public string NombreComando => "CANCELAR_PEDIDO";

    public CancelarPedidoCommand(
        Guid pedidoId,
        IRepositorioPedidos repositorioPedidos,
        IRepositorioAuditoriaOutbox auditoria,
        IUnidadTrabajo unidadTrabajo,
        IContextoSesion sesion)
    {
        _pedidoId = pedidoId;
        _repositorioPedidos = repositorioPedidos ?? throw new ArgumentNullException(nameof(repositorioPedidos));
        _auditoria = auditoria ?? throw new ArgumentNullException(nameof(auditoria));
        _unidadTrabajo = unidadTrabajo ?? throw new ArgumentNullException(nameof(unidadTrabajo));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    public async Task<ResultadoComando> EjecutarAsync(CancellationToken ct = default)
    {
        if (!_sesion.TienePermiso(RolUsuario.VENTAS, RolUsuario.ADMIN))
            return new ResultadoComando(false, "Sin permiso para cancelar pedidos");

        var pedido = await _repositorioPedidos.ObtenerPorIdAsync(_pedidoId, ct);
        if (pedido is null)
            return new ResultadoComando(false, "Pedido no encontrado");

        try
        {
            pedido.Cancelar();
        }
        catch (Exception ex)
        {
            return new ResultadoComando(false, ex.Message);
        }

        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";
        await _unidadTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            await _repositorioPedidos.ActualizarAsync(pedido, token);
            await _auditoria.RegistrarAuditoriaAsync(
                "PEDIDO",
                "CANCELAR_PEDIDO",
                $"Pedido {pedido.Codigo} cancelado",
                usuario,
                null,
                token);
        }, ct);

        return new ResultadoComando(true, $"Pedido {pedido.Codigo} cancelado");
    }
}

// Comando 3: despachar pedido PAGADO usando Abstract Factory para generar guia y plazo
public sealed class DespacharPedidoCommand : IComandoLogiMarket
{
    private readonly Guid _pedidoId;
    private readonly IRepositorioPedidos _repositorioPedidos;
    private readonly IRepositorioAuditoriaOutbox _auditoriaOutbox;
    private readonly IUnidadTrabajo _unidadTrabajo;
    private readonly IContextoSesion _sesion;

    public string NombreComando => "DESPACHAR_PEDIDO";

    public DespacharPedidoCommand(
        Guid pedidoId,
        IRepositorioPedidos repositorioPedidos,
        IRepositorioAuditoriaOutbox auditoriaOutbox,
        IUnidadTrabajo unidadTrabajo,
        IContextoSesion sesion)
    {
        _pedidoId = pedidoId;
        _repositorioPedidos = repositorioPedidos ?? throw new ArgumentNullException(nameof(repositorioPedidos));
        _auditoriaOutbox = auditoriaOutbox ?? throw new ArgumentNullException(nameof(auditoriaOutbox));
        _unidadTrabajo = unidadTrabajo ?? throw new ArgumentNullException(nameof(unidadTrabajo));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    public async Task<ResultadoComando> EjecutarAsync(CancellationToken ct = default)
    {
        if (!_sesion.TienePermiso(RolUsuario.BODEGA, RolUsuario.ADMIN))
            return new ResultadoComando(false, "Solo BODEGA o ADMIN pueden despachar pedidos");

        var pedido = await _repositorioPedidos.ObtenerPorIdAsync(_pedidoId, ct);
        if (pedido is null)
            return new ResultadoComando(false, "Pedido no encontrado");

        try
        {
            pedido.MarcarComoDespachado();
        }
        catch (Exception ex)
        {
            return new ResultadoComando(false, ex.Message);
        }

        var fabrica = SelectorFabricaEntrega.ObtenerFabrica(pedido.TipoEntrega);
        string guia = fabrica.CrearGeneradorGuia().GenerarEtiquetaGuia(pedido.Codigo, pedido.NombreCliente);
        int plazo = fabrica.CrearCalculadorPlazo().ObtenerPlazoDias();
        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";

        await _unidadTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            await _repositorioPedidos.ActualizarAsync(pedido, token);
            await _repositorioPedidos.RegistrarDespachoAsync(pedido.Id, guia, pedido.TipoEntrega.ToString(), plazo, token);
            await _auditoriaOutbox.RegistrarAuditoriaAsync(
                "DESPACHO",
                "DESPACHAR_PEDIDO",
                $"Pedido {pedido.Codigo} despachado con guia {guia}",
                usuario,
                null,
                token);
            await _auditoriaOutbox.AgregarEventoOutboxAsync(
                "PedidoDespachado",
                JsonSerializer.Serialize(new { pedido.Id, pedido.Codigo, Guia = guia, PlazoDias = plazo }),
                $"OUTBOX-DESP-{pedido.Id:N}",
                token);
        }, ct);

        return new ResultadoComando(true, $"Pedido {pedido.Codigo} despachado", guia);
    }
}

// Comando 4: procesar devolucion total usando Template Method, reintegrando stock una vez sin tarifa de envio
public sealed class ProcesarDevolucionCommand : IComandoLogiMarket
{
    private readonly Guid _pedidoId;
    private readonly MotivoDevolucion _motivo;
    private readonly bool _productoSinUso;
    private readonly string _observacion;
    private readonly DateTime _fechaSolicitudUtc;
    private readonly IRepositorioPedidos _repositorioPedidos;
    private readonly IRepositorioCatalogo _repositorioCatalogo;
    private readonly IRepositorioAuditoriaOutbox _auditoriaOutbox;
    private readonly IUnidadTrabajo _unidadTrabajo;
    private readonly IContextoSesion _sesion;
    private readonly PublicadorEventosSistema _publicador;

    public string NombreComando => "PROCESAR_DEVOLUCION";

    public ProcesarDevolucionCommand(
        Guid pedidoId,
        MotivoDevolucion motivo,
        bool productoSinUso,
        string observacion,
        DateTime fechaSolicitudUtc,
        IRepositorioPedidos repositorioPedidos,
        IRepositorioCatalogo repositorioCatalogo,
        IRepositorioAuditoriaOutbox auditoriaOutbox,
        IUnidadTrabajo unidadTrabajo,
        IContextoSesion sesion,
        PublicadorEventosSistema publicador)
    {
        _pedidoId = pedidoId;
        _motivo = motivo;
        _productoSinUso = productoSinUso;
        _observacion = observacion ?? string.Empty;
        _fechaSolicitudUtc = fechaSolicitudUtc;
        _repositorioPedidos = repositorioPedidos ?? throw new ArgumentNullException(nameof(repositorioPedidos));
        _repositorioCatalogo = repositorioCatalogo ?? throw new ArgumentNullException(nameof(repositorioCatalogo));
        _auditoriaOutbox = auditoriaOutbox ?? throw new ArgumentNullException(nameof(auditoriaOutbox));
        _unidadTrabajo = unidadTrabajo ?? throw new ArgumentNullException(nameof(unidadTrabajo));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _publicador = publicador ?? throw new ArgumentNullException(nameof(publicador));
    }

    public async Task<ResultadoComando> EjecutarAsync(CancellationToken ct = default)
    {
        if (!_sesion.TienePermiso(RolUsuario.VENTAS, RolUsuario.ADMIN))
            return new ResultadoComando(false, "Sin permiso para procesar devoluciones");

        var pedido = await _repositorioPedidos.ObtenerPorIdAsync(_pedidoId, ct);
        if (pedido is null)
            return new ResultadoComando(false, "Pedido no encontrado");

        ProcesadorDevolucionBase procesador = _motivo switch
        {
            MotivoDevolucion.DEFECTO => new ProcesadorDevolucionDefecto(),
            MotivoDevolucion.ARREPENTIMIENTO => new ProcesadorDevolucionArrepentimiento(),
            _ => throw new ArgumentOutOfRangeException(nameof(_motivo), "Motivo de devolucion no soportado")
        };

        var resultado = procesador.Ejecutar(new SolicitudDevolucion(
            pedido,
            _motivo,
            _fechaSolicitudUtc,
            _productoSinUso,
            _observacion));

        if (!resultado.Aceptada)
            return new ResultadoComando(false, resultado.Mensaje);

        pedido.MarcarDevolucionRegistrada();
        string usuario = _sesion.UsuarioActual?.NombreUsuario ?? "SISTEMA";
        var requerimientos = pedido.ObtenerRequerimientosStockPorSku();

        await _unidadTrabajo.EjecutarEnTransaccionAsync(async token =>
        {
            if (resultado.ReintegrarStock)
            {
                foreach (var (sku, cantidad) in requerimientos)
                {
                    var prod = await _repositorioCatalogo.ObtenerPorSkuAsync(sku, token);
                    if (prod is not null)
                    {
                        int stockActualizado = prod.StockDisponible + cantidad;
                        await _repositorioCatalogo.ActualizarStockAsync(
                            sku,
                            stockActualizado,
                            cantidad,
                            $"DEVOLUCION_{pedido.Codigo}",
                            pedido.Codigo,
                            token);
                    }
                }
            }

            await _repositorioPedidos.RegistrarDevolucionAsync(
                pedido.Id,
                _motivo,
                resultado.MontoReembolso,
                resultado.ReintegrarStock,
                _observacion,
                token);

            await _auditoriaOutbox.RegistrarAuditoriaAsync(
                "DEVOLUCION",
                "PROCESAR_DEVOLUCION",
                $"Pedido {pedido.Codigo} devuelto ({_motivo}). Reembolso: ${resultado.MontoReembolso:0.00}",
                usuario,
                null,
                token);

            await _auditoriaOutbox.AgregarEventoOutboxAsync(
                "DevolucionProcesada",
                JsonSerializer.Serialize(new { pedido.Id, pedido.Codigo, Motivo = _motivo.ToString(), resultado.MontoReembolso }),
                $"OUTBOX-DEV-{pedido.Id:N}",
                token);
        }, ct);

        await _publicador.PublicarEventoAsync(new EventoSistemaLogiMarket(
            "DEVOLUCION_PROCESADA",
            pedido.Codigo,
            pedido.NombreCliente,
            resultado.MontoReembolso,
            $"Reembolso aprobado por ${resultado.MontoReembolso:0.00} (sin tarifa de envio)",
            DateTime.UtcNow), ct);

        return new ResultadoComando(true, $"{resultado.Mensaje}. Reembolso: ${resultado.MontoReembolso:0.00}");
    }
}
