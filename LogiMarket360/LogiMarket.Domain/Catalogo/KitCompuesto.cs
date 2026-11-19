using System;
using System.Collections.Generic;
using System.Linq;

namespace LogiMarket.Domain.Catalogo;

// Nodo compuesto del patron Composite que agrupa productos y otros kits sin ciclos
public sealed class KitCompuesto : IElementoCatalogo
{
    private readonly List<(IElementoCatalogo Elemento, int Cantidad)> _componentes = new();

    public KitCompuesto(string sku, string nombre, decimal precioUnitario)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("El SKU del kit es obligatorio", nameof(sku));

        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del kit es obligatorio", nameof(nombre));

        if (precioUnitario <= 0m)
            throw new ArgumentOutOfRangeException(nameof(precioUnitario), "El precio del kit debe ser positivo");

        Sku = sku.Trim().ToUpperInvariant();
        Nombre = nombre.Trim();
        PrecioUnitario = decimal.Round(precioUnitario, 2, MidpointRounding.AwayFromZero);
    }

    public string Sku { get; }
    public string Nombre { get; }
    public decimal PrecioUnitario { get; }
    public decimal PesoKg => _componentes.Sum(c => c.Elemento.CalcularPesoTotal(c.Cantidad));
    public int StockDisponible => CalcularStockMaximoDisponible();
    public IReadOnlyCollection<(IElementoCatalogo Elemento, int Cantidad)> Componentes => _componentes.AsReadOnly();

    public void AgregarComponente(IElementoCatalogo elemento, int cantidad)
    {
        if (elemento is null)
            throw new ArgumentNullException(nameof(elemento));

        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad del componente debe ser positiva");

        if (string.Equals(elemento.Sku, Sku, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El kit {Sku} no puede contenerse a si mismo");

        if (elemento.ContieneSku(Sku))
            throw new InvalidOperationException($"Se detecto una referencia circular al agregar {elemento.Sku} en {Sku}");

        _componentes.Add((elemento, cantidad));
        ValidarSinCiclos();
    }

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

        ValidarSinCiclos();

        var acumulado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (elemento, cantidadComponente) in _componentes)
        {
            int cantidadTotalComponente = cantidadSolicitada * cantidadComponente;
            var subExpansion = elemento.ExpandirCantidadesPorSku(cantidadTotalComponente);

            foreach (var par in subExpansion)
            {
                if (acumulado.TryGetValue(par.Key, out int existente))
                    acumulado[par.Key] = existente + par.Value;
                else
                    acumulado[par.Key] = par.Value;
            }
        }

        return acumulado;
    }

    public bool ContieneSku(string skuObjetivo, HashSet<string>? visitados = null)
    {
        if (string.IsNullOrWhiteSpace(skuObjetivo))
            return false;

        string normalizado = skuObjetivo.Trim().ToUpperInvariant();
        if (string.Equals(Sku, normalizado, StringComparison.OrdinalIgnoreCase))
            return true;

        visitados ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visitados.Add(Sku))
            return false;

        foreach (var (elemento, _) in _componentes)
        {
            if (elemento.ContieneSku(normalizado, visitados))
                return true;
        }

        return false;
    }

    public void ValidarSinCiclos(HashSet<string>? ancestros = null)
    {
        ancestros ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!ancestros.Add(Sku))
            throw new InvalidOperationException($"Ciclo detectado en la composicion del kit {Sku}");

        foreach (var (elemento, _) in _componentes)
        {
            elemento.ValidarSinCiclos(ancestros);
        }

        ancestros.Remove(Sku);
    }

    private int CalcularStockMaximoDisponible()
    {
        var requerimientos = ExpandirCantidadesPorSku(1);
        if (requerimientos.Count == 0)
            return 0;

        int minimoKits = int.MaxValue;
        foreach (var req in requerimientos)
        {
            var hoja = BuscarProductoSimple(req.Key);
            if (hoja is null || req.Value <= 0)
                return 0;

            int posibles = hoja.StockDisponible / req.Value;
            if (posibles < minimoKits)
                minimoKits = posibles;
        }

        return minimoKits == int.MaxValue ? 0 : minimoKits;
    }

    private ProductoSimple? BuscarProductoSimple(string sku)
    {
        foreach (var (elemento, _) in _componentes)
        {
            if (elemento is ProductoSimple simple && string.Equals(simple.Sku, sku, StringComparison.OrdinalIgnoreCase))
                return simple;

            if (elemento is KitCompuesto subKit)
            {
                var encontrado = subKit.BuscarProductoSimple(sku);
                if (encontrado is not null)
                    return encontrado;
            }
        }

        return null;
    }
}
