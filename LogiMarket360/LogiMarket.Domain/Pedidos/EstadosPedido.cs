using System;
using LogiMarket.Domain.Enums;

namespace LogiMarket.Domain.Pedidos;

// Contrato del patron State que modela transiciones y permisos por estado del pedido
public interface IEstadoPedido
{
    EstadoPedido Estado { get; }
    bool PermiteEdicion { get; }
    IEstadoPedido Validar();
    IEstadoPedido Pagar();
    IEstadoPedido Despachar();
    IEstadoPedido Entregar();
    IEstadoPedido Cancelar();
}

public sealed class EstadoPedidoBorrador : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.BORRADOR;
    public bool PermiteEdicion => true;

    public IEstadoPedido Validar() => new EstadoPedidoValidado();
    public IEstadoPedido Pagar() => throw new InvalidOperationException("No se puede pagar un pedido en BORRADOR sin validarlo");
    public IEstadoPedido Despachar() => throw new InvalidOperationException("No se puede despachar un pedido sin pagar");
    public IEstadoPedido Entregar() => throw new InvalidOperationException("No se puede entregar un pedido sin despachar");
    public IEstadoPedido Cancelar() => new EstadoPedidoCancelado();
}

public sealed class EstadoPedidoValidado : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.VALIDADO;
    public bool PermiteEdicion => true;

    public IEstadoPedido Validar() => throw new InvalidOperationException("El pedido ya se encuentra VALIDADO");
    public IEstadoPedido Pagar() => new EstadoPedidoPagado();
    public IEstadoPedido Despachar() => throw new InvalidOperationException("No se puede despachar antes de pagar el pedido");
    public IEstadoPedido Entregar() => throw new InvalidOperationException("No se puede entregar un pedido sin despachar");
    public IEstadoPedido Cancelar() => new EstadoPedidoCancelado();
}

public sealed class EstadoPedidoPagado : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.PAGADO;
    public bool PermiteEdicion => false;

    public IEstadoPedido Validar() => throw new InvalidOperationException("Un pedido PAGADO no puede volver a validarse");
    public IEstadoPedido Pagar() => throw new InvalidOperationException("No se permite cobrar mas de una vez un pedido PAGADO");
    public IEstadoPedido Despachar() => new EstadoPedidoDespachado();
    public IEstadoPedido Entregar() => throw new InvalidOperationException("Debe despacharse el pedido antes de registrarlo como entregado");
    public IEstadoPedido Cancelar() => throw new InvalidOperationException("No se permite cancelar un pedido PAGADO");
}

public sealed class EstadoPedidoDespachado : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.DESPACHADO;
    public bool PermiteEdicion => false;

    public IEstadoPedido Validar() => throw new InvalidOperationException("El pedido ya fue despachado");
    public IEstadoPedido Pagar() => throw new InvalidOperationException("El pedido ya fue pagado");
    public IEstadoPedido Despachar() => throw new InvalidOperationException("El pedido ya se encuentra DESPACHADO");
    public IEstadoPedido Entregar() => new EstadoPedidoEntregado();
    public IEstadoPedido Cancelar() => throw new InvalidOperationException("No se puede cancelar un pedido DESPACHADO");
}

public sealed class EstadoPedidoEntregado : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.ENTREGADO;
    public bool PermiteEdicion => false;

    public IEstadoPedido Validar() => throw new InvalidOperationException("El pedido ya fue entregado");
    public IEstadoPedido Pagar() => throw new InvalidOperationException("El pedido ya fue pagado");
    public IEstadoPedido Despachar() => throw new InvalidOperationException("El pedido ya fue entregado");
    public IEstadoPedido Entregar() => throw new InvalidOperationException("El pedido ya se encuentra ENTREGADO");
    public IEstadoPedido Cancelar() => throw new InvalidOperationException("No se puede cancelar un pedido ENTREGADO");
}

public sealed class EstadoPedidoCancelado : IEstadoPedido
{
    public EstadoPedido Estado => EstadoPedido.CANCELADO;
    public bool PermiteEdicion => false;

    public IEstadoPedido Validar() => throw new InvalidOperationException("Un pedido CANCELADO no puede reactivarse");
    public IEstadoPedido Pagar() => throw new InvalidOperationException("Un pedido CANCELADO no puede pagarse");
    public IEstadoPedido Despachar() => throw new InvalidOperationException("Un pedido CANCELADO no puede despacharse");
    public IEstadoPedido Entregar() => throw new InvalidOperationException("Un pedido CANCELADO no puede entregarse");
    public IEstadoPedido Cancelar() => throw new InvalidOperationException("El pedido ya se encuentra CANCELADO");
}

// Fabrica auxiliar para reconstruir el objeto State desde persistencia
public static class FabricaEstadoPedido
{
    public static IEstadoPedido CrearDesde(EstadoPedido estado) => estado switch
    {
        EstadoPedido.BORRADOR => new EstadoPedidoBorrador(),
        EstadoPedido.VALIDADO => new EstadoPedidoValidado(),
        EstadoPedido.PAGADO => new EstadoPedidoPagado(),
        EstadoPedido.DESPACHADO => new EstadoPedidoDespachado(),
        EstadoPedido.ENTREGADO => new EstadoPedidoEntregado(),
        EstadoPedido.CANCELADO => new EstadoPedidoCancelado(),
        _ => throw new ArgumentOutOfRangeException(nameof(estado), "Estado de pedido no soportado")
    };
}
