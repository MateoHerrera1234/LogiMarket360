using System;
using System.Collections.Generic;
using System.Linq;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Application.Comportamentales;

// Item inmutable dentro de la instantanea del borrador
public sealed record ItemBorradorMemento(string Sku, string Nombre, int Cantidad, decimal PrecioUnitario);

// Memento inmutable que captura el estado del borrador sin exponer referencias mutables
public sealed record BorradorPedidoMemento(
    Guid? ClienteId,
    TipoEntrega TipoEntrega,
    IReadOnlyList<ItemBorradorMemento> Items);

// Originator que representa el borrador de pedido en edicion antes de confirmar
public sealed class BorradorPedidoOriginator
{
    private readonly List<ItemBorradorMemento> _items = new();

    public Guid? ClienteId { get; set; }
    public TipoEntrega TipoEntrega { get; set; } = TipoEntrega.ESTANDAR;
    public IReadOnlyList<ItemBorradorMemento> Items => _items.AsReadOnly();

    public void EstablecerItems(IEnumerable<ItemBorradorMemento> items)
    {
        _items.Clear();
        if (items is not null)
            _items.AddRange(items);
    }

    public void AgregarOActualizarItem(string sku, string nombre, int cantidad, decimal precioUnitario)
    {
        int idx = _items.FindIndex(i => string.Equals(i.Sku, sku, StringComparison.OrdinalIgnoreCase));
        if (cantidad <= 0)
        {
            if (idx >= 0) _items.RemoveAt(idx);
            return;
        }

        var nuevo = new ItemBorradorMemento(sku, nombre, cantidad, precioUnitario);
        if (idx >= 0) _items[idx] = nuevo;
        else _items.Add(nuevo);
    }

    public BorradorPedidoMemento CrearMemento() =>
        new BorradorPedidoMemento(ClienteId, TipoEntrega, _items.ToList().AsReadOnly());

    public void Restaurar(BorradorPedidoMemento memento)
    {
        if (memento is null)
            throw new ArgumentNullException(nameof(memento));

        ClienteId = memento.ClienteId;
        TipoEntrega = memento.TipoEntrega;
        _items.Clear();
        _items.AddRange(memento.Items);
    }
}

// Caretaker con historial acotado a 5 estados y soporte de deshacer, rehacer y limpieza al confirmar
public sealed class HistorialBorradorCaretaker
{
    public const int CapacidadMaxima = 5;

    private readonly LinkedList<BorradorPedidoMemento> _pasados = new();
    private readonly Stack<BorradorPedidoMemento> _futuros = new();

    public bool PuedeDeshacer => _pasados.Count > 0;
    public bool PuedeRehacer => _futuros.Count > 0;
    public int CantidadPasados => _pasados.Count;

    public void GuardarEstado(BorradorPedidoOriginator originator)
    {
        if (originator is null)
            throw new ArgumentNullException(nameof(originator));

        _pasados.AddLast(originator.CrearMemento());
        while (_pasados.Count > CapacidadMaxima)
        {
            _pasados.RemoveFirst();
        }

        _futuros.Clear();
    }

    public bool Deshacer(BorradorPedidoOriginator originator)
    {
        if (!PuedeDeshacer || originator is null)
            return false;

        _futuros.Push(originator.CrearMemento());
        var anterior = _pasados.Last!.Value;
        _pasados.RemoveLast();
        originator.Restaurar(anterior);
        return true;
    }

    public bool Rehacer(BorradorPedidoOriginator originator)
    {
        if (!PuedeRehacer || originator is null)
            return false;

        _pasados.AddLast(originator.CrearMemento());
        while (_pasados.Count > CapacidadMaxima)
        {
            _pasados.RemoveFirst();
        }

        var siguiente = _futuros.Pop();
        originator.Restaurar(siguiente);
        return true;
    }

    public void LimpiarAlConfirmar()
    {
        _pasados.Clear();
        _futuros.Clear();
    }
}
