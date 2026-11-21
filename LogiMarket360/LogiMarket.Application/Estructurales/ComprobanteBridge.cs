using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Estructurales;

// Abstraccion del patron Bridge desacoplada del medio de salida ISalidaDocumento
public abstract class ComprobanteBase
{
    protected readonly ISalidaDocumento Salida;

    protected ComprobanteBase(ISalidaDocumento salida)
    {
        Salida = salida ?? throw new ArgumentNullException(nameof(salida));
    }

    public abstract Task<string> EmitirAsync(CancellationToken ct = default);
}

// Comprobante formal de venta con desglose de totales y referencia de cobro
public sealed class ComprobanteVenta : ComprobanteBase
{
    private readonly Pedido _pedido;
    private readonly string _referenciaCobro;

    public ComprobanteVenta(ISalidaDocumento salida, Pedido pedido, string referenciaCobro)
        : base(salida)
    {
        _pedido = pedido ?? throw new ArgumentNullException(nameof(pedido));
        _referenciaCobro = referenciaCobro ?? string.Empty;
    }

    public override Task<string> EmitirAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PEDIDO: {_pedido.Codigo}");
        sb.AppendLine($"CLIENTE: {_pedido.NombreCliente} ({_pedido.CategoriaCliente})");
        sb.AppendLine($"ENTREGA: {_pedido.TipoEntrega} ({_pedido.PlazoEntregaDias} dias)");
        sb.AppendLine($"SUBTOTAL: ${_pedido.Subtotal:0.00}");
        sb.AppendLine($"DESCUENTO ({_pedido.PoliticaDescuentoAplicada}): -${_pedido.Descuento:0.00}");
        sb.AppendLine($"ENVIO: ${_pedido.TarifaEnvio:0.00}");
        sb.AppendLine($"TOTAL PAGADO: ${_pedido.Total:0.00}");
        sb.AppendLine($"REF. COBRO: {_referenciaCobro}");

        return Salida.EmitirAsync($"COMPROBANTE VENTA {_pedido.Codigo}", sb.ToString(), ct);
    }
}

// Comprobante de devolucion total que excluye tarifa de envio y muestra reembolso neto
public sealed class ComprobanteDevolucion : ComprobanteBase
{
    private readonly Pedido _pedido;
    private readonly MotivoDevolucion _motivo;
    private readonly decimal _montoReembolso;

    public ComprobanteDevolucion(ISalidaDocumento salida, Pedido pedido, MotivoDevolucion motivo, decimal montoReembolso)
        : base(salida)
    {
        _pedido = pedido ?? throw new ArgumentNullException(nameof(pedido));
        _motivo = motivo;
        _montoReembolso = montoReembolso;
    }

    public override Task<string> EmitirAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DEVOLUCION DE PEDIDO: {_pedido.Codigo}");
        sb.AppendLine($"CLIENTE: {_pedido.NombreCliente}");
        sb.AppendLine($"MOTIVO: {_motivo}");
        sb.AppendLine($"REEMBOLSO PRODUCTOS (SIN ENVIO): ${_montoReembolso:0.00}");

        return Salida.EmitirAsync($"COMPROBANTE DEVOLUCION {_pedido.Codigo}", sb.ToString(), ct);
    }
}
