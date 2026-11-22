using System;
using System.Linq;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Creacionales;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;
using Xunit;

namespace LogiMarket.Tests;

public sealed class PruebasDominioYPatrones
{
    private static CalculadoraDescuentos CrearCalculadoraEstandar() =>
        new(new IPoliticaDescuento[]
        {
            new PoliticaMayorista15(),
            new PoliticaVip10(),
            new PoliticaSinDescuento()
        });

    [Fact]
    public void KitCompuesto_DebeExpandirCantidadesPorSkuHoja()
    {
        var p1 = new ProductoSimple("SKU-1", "Caja", 10.00m, 1.0m, 20);
        var p2 = new ProductoSimple("SKU-2", "Sensor", 15.00m, 0.5m, 30);

        var kit = new KitCompuesto("KIT-1", "Kit Base", 22.00m);
        kit.AgregarComponente(p1, 2);
        kit.AgregarComponente(p2, 1);

        var expansion = kit.ExpandirCantidadesPorSku(3);

        Assert.Equal(6, expansion["SKU-1"]);
        Assert.Equal(3, expansion["SKU-2"]);
    }

    [Fact]
    public void Pedido_DebeAplicarPoliticaMayoristaSinAcumularVip()
    {
        var prod = new ProductoSimple("SKU-10", "Insumo", 20.00m, 1.0m, 100);
        var clienteVip = new Cliente(Guid.NewGuid(), "CLI-VIP", "Cliente VIP", "vip@test.ec", CategoriaCliente.VIP, true);

        var builder = new PedidoBuilder(CrearCalculadoraEstandar())
            .ConCodigo("PED-TEST-01")
            .ParaCliente(clienteVip)
            .ConTipoEntrega(TipoEntrega.ESTANDAR)
            .AgregarElemento(prod, 10); // 10 unidades fisicas -> activa MAYORISTA_15 (prioridad 1)

        Pedido pedido = builder.Construir();

        Assert.Equal(200.00m, pedido.Subtotal);
        Assert.Equal("MAYORISTA_15", pedido.PoliticaDescuentoAplicada);
        Assert.Equal(30.00m, pedido.Descuento); // 15% de 200, no acumulado con VIP
        Assert.Equal(170.00m + pedido.TarifaEnvio, pedido.Total);
    }

    [Fact]
    public void Pedido_EstadoNoPermiteSaltarPagadoParaDespachar()
    {
        var prod = new ProductoSimple("SKU-11", "Item", 50.00m, 1.0m, 10);
        var cli = new Cliente(Guid.NewGuid(), "CLI-01", "Cliente", "cli@test.ec", CategoriaCliente.NORMAL, true);
        var pedido = new PedidoBuilder(CrearCalculadoraEstandar())
            .ConCodigo("PED-TEST-02")
            .ParaCliente(cli)
            .AgregarElemento(prod, 1)
            .Construir();

        Assert.Equal(EstadoPedido.BORRADOR, pedido.Estado);
        Assert.Throws<InvalidOperationException>(() => pedido.MarcarComoDespachado());

        pedido.Validar();
        pedido.MarcarComoPagado();
        pedido.MarcarComoDespachado();
        pedido.MarcarComoEntregado(DateTime.UtcNow);

        Assert.Equal(EstadoPedido.ENTREGADO, pedido.Estado);
        pedido.MarcarDevolucionRegistrada();
        Assert.True(pedido.TieneDevolucionRegistrada);
        Assert.Throws<InvalidOperationException>(() => pedido.MarcarDevolucionRegistrada());
    }

    [Fact]
    public void BorradorMemento_DebeSoportarDeshacerRehacerYLimpiarAlConfirmar()
    {
        var originator = new BorradorPedidoOriginator();
        var caretaker = new HistorialBorradorCaretaker();

        caretaker.GuardarEstado(originator);
        originator.AgregarOActualizarItem("SKU-1", "Prod 1", 2, 10.00m);

        caretaker.GuardarEstado(originator);
        originator.AgregarOActualizarItem("SKU-2", "Prod 2", 1, 25.00m);

        Assert.Equal(2, originator.Items.Count);
        Assert.True(caretaker.Deshacer(originator));
        Assert.Single(originator.Items);
        Assert.True(caretaker.Rehacer(originator));
        Assert.Equal(2, originator.Items.Count);

        caretaker.LimpiarAlConfirmar();
        Assert.False(caretaker.PuedeDeshacer);
        Assert.False(caretaker.PuedeRehacer);
    }

    [Fact]
    public void PlantillaCarritoBorrador_DebeClonarProfundamenteSinCompartirReferencias()
    {
        var original = new PlantillaCarritoBorrador(
            "PLANTILLA_A",
            Guid.NewGuid(),
            TipoEntrega.EXPRES,
            new[] { new ItemBorradorPrototipo("SKU-1", "Prod 1", 3, 12.50m) });

        var clon = original.Clonar();
        clon.Items[0].Cantidad = 99;

        Assert.Equal(3, original.Items[0].Cantidad);
        Assert.EndsWith("_COPIA", clon.NombrePlantilla);
    }
}
