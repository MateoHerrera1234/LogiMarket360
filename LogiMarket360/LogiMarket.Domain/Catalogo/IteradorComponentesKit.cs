using System;
using System.Collections.Generic;

namespace LogiMarket.Domain.Catalogo;

// Representa un elemento fisico expandido durante el recorrido del iterador
public sealed record ComponenteFisicoItem(string Sku, string Nombre, int Cantidad, decimal PesoUnitarioKg);

// Contrato del patron Iterator para recorrer componentes fisicos sin exponer listas internas
public interface IIteradorComponentes
{
    bool TieneSiguiente();
    ComponenteFisicoItem Siguiente();
}

// Iterador determinista que expande productos y kits anidados propagando cantidades
public sealed class IteradorComponentesKit : IIteradorComponentes
{
    private readonly List<ComponenteFisicoItem> _itemsAplanados = new();
    private int _indiceActual;

    public IteradorComponentesKit(IElementoCatalogo raiz, int cantidadRaiz = 1)
    {
        if (raiz is null)
            throw new ArgumentNullException(nameof(raiz));

        if (cantidadRaiz <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidadRaiz), "La cantidad debe ser positiva");

        raiz.ValidarSinCiclos();
        AplanarElemento(raiz, cantidadRaiz);
    }

    public bool TieneSiguiente() => _indiceActual < _itemsAplanados.Count;

    public ComponenteFisicoItem Siguiente()
    {
        if (!TieneSiguiente())
            throw new InvalidOperationException("No existen mas componentes por recorrer en el iterador");

        var actual = _itemsAplanados[_indiceActual];
        _indiceActual++;
        return actual;
    }

    private void AplanarElemento(IElementoCatalogo elemento, int multiplicador)
    {
        if (elemento is ProductoSimple simple)
        {
            _itemsAplanados.Add(new ComponenteFisicoItem(
                simple.Sku,
                simple.Nombre,
                multiplicador,
                simple.PesoKg));
            return;
        }

        if (elemento is KitCompuesto kit)
        {
            foreach (var (subElemento, cantidadComponente) in kit.Componentes)
            {
                AplanarElemento(subElemento, multiplicador * cantidadComponente);
            }
        }
    }
}
