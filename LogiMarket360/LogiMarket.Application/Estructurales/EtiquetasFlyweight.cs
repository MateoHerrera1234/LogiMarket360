using System;
using System.Collections.Generic;

namespace LogiMarket.Application.Estructurales;

// Estado intrinseco inmutable compartido entre elementos visuales de catalogo o pedidos
public sealed record MetadatoVisualFlyweight(string Clave, string EtiquetaCorta, string ColorHex, string Descripcion);

// Fabrica Flyweight que reutiliza instancias inmutables por clave
public sealed class FabricaMetadatosVisuales
{
    private readonly Dictionary<string, MetadatoVisualFlyweight> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int CantidadInstanciasCreadas => _cache.Count;

    public MetadatoVisualFlyweight Obtener(string clave)
    {
        string normalizada = string.IsNullOrWhiteSpace(clave) ? "GENERAL" : clave.Trim().ToUpperInvariant();
        if (_cache.TryGetValue(normalizada, out var existente))
            return existente;

        var nuevo = normalizada switch
        {
            "KIT" => new MetadatoVisualFlyweight("KIT", "KIT", "#1D4ED8", "Paquete compuesto por multiples componentes"),
            "SIMPLE" => new MetadatoVisualFlyweight("SIMPLE", "ITEM", "#0F766E", "Producto fisico individual"),
            "VIP" => new MetadatoVisualFlyweight("VIP", "VIP", "#B45309", "Cliente preferencial con descuento 10%"),
            "MAYORISTA" => new MetadatoVisualFlyweight("MAYORISTA", "MAY", "#4338CA", "Descuento 15% desde 10 unidades fisicas"),
            "PAGADO" => new MetadatoVisualFlyweight("PAGADO", "PAGADO", "#15803D", "Cobro aprobado y listo para despacho"),
            "CANCELADO" => new MetadatoVisualFlyweight("CANCELADO", "ANULADO", "#B91C1C", "Pedido cancelado antes del pago"),
            _ => new MetadatoVisualFlyweight(normalizada, normalizada, "#334155", "Etiqueta operativa estandar")
        };

        _cache[normalizada] = nuevo;
        return nuevo;
    }
}
