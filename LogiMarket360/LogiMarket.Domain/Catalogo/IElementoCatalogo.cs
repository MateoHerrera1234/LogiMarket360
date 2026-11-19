using System.Collections.Generic;

namespace LogiMarket.Domain.Catalogo;

// Contrato comun del patron Composite para productos simples y kits anidados
public interface IElementoCatalogo
{
    string Sku { get; }
    string Nombre { get; }
    decimal PrecioUnitario { get; }
    decimal PesoKg { get; }
    int StockDisponible { get; }

    decimal CalcularPrecioTotal(int cantidad);
    decimal CalcularPesoTotal(int cantidad);
    IReadOnlyDictionary<string, int> ExpandirCantidadesPorSku(int cantidadSolicitada);
    bool ContieneSku(string skuObjetivo, HashSet<string>? visitados = null);
    void ValidarSinCiclos(HashSet<string>? ancestros = null);
}
