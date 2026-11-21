using System;
using System.Collections.Generic;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Creacionales;

// Implementacion del patron Builder para ensamblar pedidos validos paso a paso
public sealed class PedidoBuilder
{
    private readonly CalculadoraDescuentos _calculadoraDescuentos;
    private readonly List<LineaPedido> _lineas = new();
    private Guid _id = Guid.NewGuid();
    private string _codigo = string.Empty;
    private Cliente? _cliente;
    private TipoEntrega _tipoEntrega = TipoEntrega.ESTANDAR;

    public PedidoBuilder(CalculadoraDescuentos calculadoraDescuentos)
    {
        _calculadoraDescuentos = calculadoraDescuentos ?? throw new ArgumentNullException(nameof(calculadoraDescuentos));
    }

    public PedidoBuilder ConId(Guid id)
    {
        if (id != Guid.Empty)
            _id = id;
        return this;
    }

    public PedidoBuilder ConCodigo(string codigo)
    {
        _codigo = codigo;
        return this;
    }

    public PedidoBuilder ParaCliente(Cliente cliente)
    {
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        return this;
    }

    public PedidoBuilder ConTipoEntrega(TipoEntrega tipoEntrega)
    {
        _tipoEntrega = tipoEntrega;
        return this;
    }

    public PedidoBuilder AgregarElemento(IElementoCatalogo elemento, int cantidad)
    {
        if (elemento is null)
            throw new ArgumentNullException(nameof(elemento));

        if (cantidad <= 0)
            throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor a cero");

        _lineas.Add(new LineaPedido(elemento, cantidad));
        return this;
    }

    public Pedido Construir()
    {
        if (_cliente is null)
            throw new InvalidOperationException("No se puede construir un pedido sin cliente asignado");

        if (_lineas.Count == 0)
            throw new InvalidOperationException("No se puede construir un pedido sin lineas de detalle");

        string codigoFinal = string.IsNullOrWhiteSpace(_codigo)
            ? $"PED-{DateTime.UtcNow:yyyyMMddHHmmssfff}"
            : _codigo;

        IFabricaEntrega fabrica = SelectorFabricaEntrega.ObtenerFabrica(_tipoEntrega);
        decimal tarifa = fabrica.CrearCalculadorTarifa().ObtenerTarifaUsd();
        int plazo = fabrica.CrearCalculadorPlazo().ObtenerPlazoDias();

        return new Pedido(
            _id,
            codigoFinal,
            _cliente.Id,
            _cliente.Nombre,
            _cliente.Categoria,
            _tipoEntrega,
            tarifa,
            plazo,
            _lineas,
            _calculadoraDescuentos);
    }
}
