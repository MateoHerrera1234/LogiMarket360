using System;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.Creacionales;

// Producto abstracto A para calculo de tarifa de envio
public interface ICalculadorTarifaEntrega
{
    TipoEntrega Familia { get; }
    decimal ObtenerTarifaUsd();
}

// Producto abstracto B para calculo de plazo estimado
public interface ICalculadorPlazoEntrega
{
    TipoEntrega Familia { get; }
    int ObtenerPlazoDias();
}

// Producto abstracto C para generacion de guia interna
public interface IGeneradorGuiaEntrega
{
    TipoEntrega Familia { get; }
    string GenerarEtiquetaGuia(string codigoPedido, string nombreCliente);
}

// Contrato del patron Abstract Factory que agrupa una familia coherente de entrega
public interface IFabricaEntrega
{
    TipoEntrega Tipo { get; }
    ICalculadorTarifaEntrega CrearCalculadorTarifa();
    ICalculadorPlazoEntrega CrearCalculadorPlazo();
    IGeneradorGuiaEntrega CrearGeneradorGuia();
}

// Familia ESTANDAR con tarifa 5.00 USD y plazo 3 dias
public sealed class FabricaEntregaEstandar : IFabricaEntrega
{
    public TipoEntrega Tipo => TipoEntrega.ESTANDAR;
    public ICalculadorTarifaEntrega CrearCalculadorTarifa() => new TarifaEstandar();
    public ICalculadorPlazoEntrega CrearCalculadorPlazo() => new PlazoEstandar();
    public IGeneradorGuiaEntrega CrearGeneradorGuia() => new GuiaEstandar();

    private sealed class TarifaEstandar : ICalculadorTarifaEntrega
    {
        public TipoEntrega Familia => TipoEntrega.ESTANDAR;
        public decimal ObtenerTarifaUsd() => 5.00m;
    }

    private sealed class PlazoEstandar : ICalculadorPlazoEntrega
    {
        public TipoEntrega Familia => TipoEntrega.ESTANDAR;
        public int ObtenerPlazoDias() => 3;
    }

    private sealed class GuiaEstandar : IGeneradorGuiaEntrega
    {
        public TipoEntrega Familia => TipoEntrega.ESTANDAR;
        public string GenerarEtiquetaGuia(string codigoPedido, string nombreCliente) =>
            $"GUIA-EST-{codigoPedido}-{DateTime.UtcNow:yyyyMMddHHmmss} | CLIENTE: {nombreCliente} | PLAZO: 3 DIAS";
    }
}

// Familia EXPRES con tarifa 12.00 USD y plazo 1 dia
public sealed class FabricaEntregaExpres : IFabricaEntrega
{
    public TipoEntrega Tipo => TipoEntrega.EXPRES;
    public ICalculadorTarifaEntrega CrearCalculadorTarifa() => new TarifaExpres();
    public ICalculadorPlazoEntrega CrearCalculadorPlazo() => new PlazoExpres();
    public IGeneradorGuiaEntrega CrearGeneradorGuia() => new GuiaExpres();

    private sealed class TarifaExpres : ICalculadorTarifaEntrega
    {
        public TipoEntrega Familia => TipoEntrega.EXPRES;
        public decimal ObtenerTarifaUsd() => 12.00m;
    }

    private sealed class PlazoExpres : ICalculadorPlazoEntrega
    {
        public TipoEntrega Familia => TipoEntrega.EXPRES;
        public int ObtenerPlazoDias() => 1;
    }

    private sealed class GuiaExpres : IGeneradorGuiaEntrega
    {
        public TipoEntrega Familia => TipoEntrega.EXPRES;
        public string GenerarEtiquetaGuia(string codigoPedido, string nombreCliente) =>
            $"GUIA-EXP-{codigoPedido}-{DateTime.UtcNow:yyyyMMddHHmmss} | CLIENTE: {nombreCliente} | PRIORIDAD 1 DIA";
    }
}

// Selector de familia de entrega que impide mezclar variantes incompatibles
public static class SelectorFabricaEntrega
{
    public static IFabricaEntrega ObtenerFabrica(TipoEntrega tipo) => tipo switch
    {
        TipoEntrega.ESTANDAR => new FabricaEntregaEstandar(),
        TipoEntrega.EXPRES => new FabricaEntregaExpres(),
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), "Tipo de entrega no soportado")
    };
}
