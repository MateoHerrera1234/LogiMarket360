using System;
using System.Collections.Generic;

namespace LogiMarket.Domain.Catalogo;

// Visitante concreto que elabora el desglose fisico consolidado por SKU final
public sealed class VisitanteDesgloseFisico : IVisitorCatalogo
{
    private readonly Dictionary<string, int> _cantidadesPorSku = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> ObtenerDesglose() => _cantidadesPorSku;

    public void VisitarProducto(ProductoSimple producto, int cantidad)
    {
        if (_cantidadesPorSku.TryGetValue(producto.Sku, out int actual))
            _cantidadesPorSku[producto.Sku] = actual + cantidad;
        else
            _cantidadesPorSku[producto.Sku] = cantidad;
    }

    public void VisitarKit(KitCompuesto kit, int cantidad)
    {
        kit.ValidarSinCiclos();
        foreach (var (elemento, cantidadComponente) in kit.Componentes)
        {
            int totalSub = cantidad * cantidadComponente;
            if (elemento is ProductoSimple prod)
                VisitarProducto(prod, totalSub);
            else if (elemento is KitCompuesto subKit)
                VisitarKit(subKit, totalSub);
        }
    }
}

// Visitante concreto que estima el peso logistico total de productos y kits anidados
public sealed class VisitantePesoLogistico : IVisitorCatalogo
{
    public decimal PesoTotalKg { get; private set; }

    public void VisitarProducto(ProductoSimple producto, int cantidad)
    {
        PesoTotalKg += producto.PesoKg * cantidad;
    }

    public void VisitarKit(KitCompuesto kit, int cantidad)
    {
        kit.ValidarSinCiclos();
        foreach (var (elemento, cantidadComponente) in kit.Componentes)
        {
            int totalSub = cantidad * cantidadComponente;
            if (elemento is ProductoSimple prod)
                VisitarProducto(prod, totalSub);
            else if (elemento is KitCompuesto subKit)
                VisitarKit(subKit, totalSub);
        }
    }
}
