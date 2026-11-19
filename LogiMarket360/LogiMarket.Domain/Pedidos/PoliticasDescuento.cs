using System;
using System.Collections.Generic;
using System.Linq;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Domain.Pedidos;

// Contrato del patron Strategy para politicas excluyentes de descuento
public interface IPoliticaDescuento
{
    string NombrePolitica { get; }
    int Prioridad { get; }
    bool EsAplicable(CategoriaCliente categoriaCliente, int totalUnidadesFisicas);
    decimal CalcularDescuento(decimal subtotal);
}

// Estrategia mayorista con 15 por ciento cuando hay al menos 10 unidades fisicas finales
public sealed class PoliticaMayorista15 : IPoliticaDescuento
{
    public string NombrePolitica => "MAYORISTA_15";
    public int Prioridad => 1;

    public bool EsAplicable(CategoriaCliente categoriaCliente, int totalUnidadesFisicas) =>
        totalUnidadesFisicas >= 10;

    public decimal CalcularDescuento(decimal subtotal) =>
        decimal.Round(subtotal * 0.15m, 2, MidpointRounding.AwayFromZero);
}

// Estrategia VIP con 10 por ciento si el cliente pertenece a esa categoria
public sealed class PoliticaVip10 : IPoliticaDescuento
{
    public string NombrePolitica => "VIP_10";
    public int Prioridad => 2;

    public bool EsAplicable(CategoriaCliente categoriaCliente, int totalUnidadesFisicas) =>
        categoriaCliente == CategoriaCliente.VIP;

    public decimal CalcularDescuento(decimal subtotal) =>
        decimal.Round(subtotal * 0.10m, 2, MidpointRounding.AwayFromZero);
}

// Estrategia por defecto sin descuento aplicable
public sealed class PoliticaSinDescuento : IPoliticaDescuento
{
    public string NombrePolitica => "NORMAL_0";
    public int Prioridad => 99;

    public bool EsAplicable(CategoriaCliente categoriaCliente, int totalUnidadesFisicas) => true;

    public decimal CalcularDescuento(decimal subtotal) => 0.00m;
}

// Calculadora que selecciona una unica politica segun prioridad sin acumular descuentos
public sealed class CalculadoraDescuentos
{
    private readonly IReadOnlyList<IPoliticaDescuento> _politicas;

    public CalculadoraDescuentos(IEnumerable<IPoliticaDescuento> politicas)
    {
        if (politicas is null)
            throw new ArgumentNullException(nameof(politicas));

        _politicas = politicas.OrderBy(p => p.Prioridad).ToList();
        if (_politicas.Count == 0)
            throw new ArgumentException("Debe registrarse al menos una politica de descuento", nameof(politicas));
    }

    public (IPoliticaDescuento PoliticaAplicada, decimal Descuento) Evaluar(
        decimal subtotal,
        CategoriaCliente categoriaCliente,
        int totalUnidadesFisicas)
    {
        foreach (var politica in _politicas)
        {
            if (politica.EsAplicable(categoriaCliente, totalUnidadesFisicas))
            {
                return (politica, politica.CalcularDescuento(subtotal));
            }
        }

        var sinDescuento = new PoliticaSinDescuento();
        return (sinDescuento, 0.00m);
    }
}
