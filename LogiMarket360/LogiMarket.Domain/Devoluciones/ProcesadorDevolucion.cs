using System;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Domain.Devoluciones;

// Solicitud de evaluacion para procesar una devolucion total
public sealed record SolicitudDevolucion(
    Pedido Pedido,
    MotivoDevolucion Motivo,
    DateTime FechaSolicitudUtc,
    bool ProductoSinUso,
    string Observacion);

// Resultado estructurado de la evaluacion y calculo de reembolso
public sealed record ResultadoDevolucion(
    bool Aceptada,
    string Mensaje,
    decimal MontoReembolso,
    bool ReintegrarStock);

// Clase base del patron Template Method que define la secuencia invariante de devolucion total
public abstract class ProcesadorDevolucionBase
{
    public ResultadoDevolucion Ejecutar(SolicitudDevolucion solicitud)
    {
        if (solicitud is null)
            throw new ArgumentNullException(nameof(solicitud));

        if (!VerificarElegibilidadPedido(solicitud, out string motivoRechazo))
            return new ResultadoDevolucion(false, motivoRechazo, 0.00m, false);

        if (!InspeccionarCondicionPorMotivo(solicitud, out string motivoCondicion))
            return new ResultadoDevolucion(false, motivoCondicion, 0.00m, false);

        decimal reembolso = CalcularMontoReembolso(solicitud.Pedido);
        bool reintegrar = DebeReintegrarStock(solicitud);

        return new ResultadoDevolucion(
            true,
            $"Devolucion aprobada por motivo {solicitud.Motivo}",
            reembolso,
            reintegrar);
    }

    protected virtual bool VerificarElegibilidadPedido(SolicitudDevolucion solicitud, out string error)
    {
        if (solicitud.Pedido.Estado != EstadoPedido.ENTREGADO)
        {
            error = "Solo se permiten devoluciones de pedidos en estado ENTREGADO";
            return false;
        }

        if (solicitud.Pedido.TieneDevolucionRegistrada)
        {
            error = "El pedido ya cuenta con una devolucion registrada";
            return false;
        }

        if (!solicitud.Pedido.FechaEntregaUtc.HasValue)
        {
            error = "El pedido no registra fecha de entrega valida";
            return false;
        }

        error = string.Empty;
        return true;
    }

    protected abstract bool InspeccionarCondicionPorMotivo(SolicitudDevolucion solicitud, out string error);

    protected virtual decimal CalcularMontoReembolso(Pedido pedido) =>
        pedido.CalcularImporteReembolsableProductos();

    protected virtual bool DebeReintegrarStock(SolicitudDevolucion solicitud) => true;
}

// Variante concreta para devolucion por DEFECTO aceptada hasta 30 dias tras la entrega
public sealed class ProcesadorDevolucionDefecto : ProcesadorDevolucionBase
{
    protected override bool InspeccionarCondicionPorMotivo(SolicitudDevolucion solicitud, out string error)
    {
        double diasTranscurridos = (solicitud.FechaSolicitudUtc - solicitud.Pedido.FechaEntregaUtc!.Value).TotalDays;
        if (diasTranscurridos < 0 || diasTranscurridos > 30)
        {
            error = "El plazo maximo para devolucion por DEFECTO es de 30 dias desde la entrega";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

// Variante concreta para devolucion por ARREPENTIMIENTO hasta 7 dias y producto sin uso
public sealed class ProcesadorDevolucionArrepentimiento : ProcesadorDevolucionBase
{
    protected override bool InspeccionarCondicionPorMotivo(SolicitudDevolucion solicitud, out string error)
    {
        double diasTranscurridos = (solicitud.FechaSolicitudUtc - solicitud.Pedido.FechaEntregaUtc!.Value).TotalDays;
        if (diasTranscurridos < 0 || diasTranscurridos > 7)
        {
            error = "El plazo maximo para devolucion por ARREPENTIMIENTO es de 7 dias desde la entrega";
            return false;
        }

        if (!solicitud.ProductoSinUso)
        {
            error = "La devolucion por ARREPENTIMIENTO exige que el producto este sin uso";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
