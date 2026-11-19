using System;
using System.Collections.Generic;

namespace LogiMarket.Domain.Catalogo;

// Hoja del patron Composite que representa un producto individual con invariantes
public sealed class ProductoSimple : IElementoCatalogo
{
    private int _stockDisponible;

    public ProductoSimple(string sku, string nombre, decimal precioUnitario, decimal pesoKg, int stockInicial)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("El SKU del producto es obligatorio", nameof(sku));

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del producto es obligatorio", nameof(nombre));

        if (precioUnitario <= 0m)
            throw new ArgumentOutOfRangeException(nameof(precioUnitario), "El precio debe ser positivo");

        if (pesoKg < 0m)
            throw new ArgumentOutOfRangeException(nameof(pesoKg), "El peso no puede ser negativo");

        if (stockInicial < 0)
            throw new ArgumentOutOfRangeException(nameof(stockInicial), "El stock inicial no puede ser negativo");

        Sku = sku.Trim().ToUpperInvariant();
        Nombre = nombre.Trim();
        PrecioUnitario = decimal.Round(precioUnitario, 2, MidpointRounding.AwayFromZero);
        PesoKg = pesoKg;
        _stockDisponible = stockInicial;
    }

    public string Sku { get; }
    public string Nombre { get; }
    public decimal PrecioUnitario { get; }
    public decimal PesoKg { get; }
    public int StockDisponible => _stockDisponible;

    public decimal CalcularPrecioTotal(int cantidad)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor a cero");

        return decimal.Round(PrecioUnitario * cantidad, 2, MidpointRounding.AwayFromZero);
    }

    public decimal CalcularPesoTotal(int cantidad)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor a cero");

        return PesoKg * cantidad;
    }

    public IReadOnlyDictionary<string, int> ExpandirCantidadesPorSku(int cantidadSolicitada)
    {
        if (cantidadSolicitada <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidadSolicitada), "La cantidad debe ser mayor a cero");

        return new Dictionary<string, int>
        {
            [Sku] = cantidadSolicitada
        };
    }

    public bool ContieneSku(string skuObjetivo, HashSet<string>? visitados = null)
    {
        if (string.IsNullOrWhiteSpace(skuObjetivo))
            return false;

        return string.Equals(Sku, skuObjetivo.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    public void ValidarSinCiclos(HashSet<string>? ancestros = null)
    {
        // Un producto simple es una hoja y no contiene subelementos
    }

    public void DescontarStock(int cantidad)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a descontar debe ser positiva");

        if (cantidad > _stockDisponible)
            throw new InvalidOperationException($"Stock insuficiente para el SKU {Sku}");

        _stockDisponible -= cantidad;
    }

    public void ReintegrarStock(int cantidad)
    {
        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad a reintegrar debe ser positiva");

        _stockDisponible += cantidad;
    }
}
