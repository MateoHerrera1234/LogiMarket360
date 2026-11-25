using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Estructurales;
using LogiMarket.Application.Puertos;
using LogiMarket.Infrastructure.Notificaciones;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Devoluciones;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;
using Xunit;

namespace LogiMarket.Tests;

public sealed class PruebasRequisitosAPE
{
    private static CalculadoraDescuentos CrearCalculadora() => new(new IPoliticaDescuento[]
    {
        new PoliticaMayorista15(), new PoliticaVip10(), new PoliticaSinDescuento()
    });

    private static Pedido CrearPedido(CategoriaCliente categoria, params (ProductoSimple Producto, int Cantidad)[] lineas)
    {
        var cliente = new Cliente(Guid.NewGuid(), $"CLI-{Guid.NewGuid():N}"[..12], "Cliente Prueba", "test@example.test", categoria);
        var builder = new PedidoBuilder(CrearCalculadora()).ParaCliente(cliente);
        foreach (var (producto, cantidad) in lineas)
            builder.AgregarElemento(producto, cantidad);
        return builder.Construir();
    }

    [Fact]
    public void CasoOficial_AnaVipKitTeletrabajoTotal59YReembolso54()
    {
        var p1 = new ProductoSimple("P01", "Mouse USB", 20m, .2m, 8);
        var p2 = new ProductoSimple("P02", "Teclado", 30m, .7m, 5);
        var p3 = new ProductoSimple("P03", "Memoria USB", 10m, .1m, 20);
        var kitOficina = new KitCompuesto("KIT_OFICINA", "Kit Oficina", 50m);
        kitOficina.AgregarComponente(p1, 1);
        kitOficina.AgregarComponente(p2, 1);
        var teletrabajo = new KitCompuesto("KIT_TELETRABAJO", "Kit Teletrabajo", 60m);
        teletrabajo.AgregarComponente(kitOficina, 1);
        teletrabajo.AgregarComponente(p3, 1);
        var pedido = new PedidoBuilder(CrearCalculadora())
            .ParaCliente(new Cliente(Guid.NewGuid(), "ANA", "Ana", "ana@example.test", CategoriaCliente.VIP))
            .AgregarElemento(teletrabajo, 1).Construir();

        Assert.Equal(60m, pedido.Subtotal);
        Assert.Equal(6m, pedido.Descuento);
        Assert.Equal(59m, pedido.Total);
        Assert.Equal(54m, pedido.CalcularImporteReembolsableProductos());
        Assert.Equal(7, 8 - pedido.ObtenerRequerimientosStockPorSku()["P01"]);
        Assert.Equal(4, 5 - pedido.ObtenerRequerimientosStockPorSku()["P02"]);
        Assert.Equal(19, 20 - pedido.ObtenerRequerimientosStockPorSku()["P03"]);
    }

    [Fact]
    public void CasoOficial_LuisNormalCompraDosWebcamYDosAlfombrillasTotal102()
    {
        var p4 = new ProductoSimple("P04", "Webcam", 40m, .35m, 4);
        var p5 = new ProductoSimple("P05", "Alfombrilla", 5m, .1m, 30);
        var pedido = new PedidoBuilder(CrearCalculadora())
            .ParaCliente(new Cliente(Guid.NewGuid(), "CLI-LUIS", "Luis", "luis@example.test", CategoriaCliente.NORMAL))
            .ConTipoEntrega(TipoEntrega.EXPRES)
            .AgregarElemento(p4, 2)
            .AgregarElemento(p5, 2)
            .Construir();
        Assert.Equal(90m, pedido.Subtotal);
        Assert.Equal(0m, pedido.Descuento);
        Assert.Equal(102m, pedido.Total);
        Assert.Equal(2, 4 - pedido.ObtenerRequerimientosStockPorSku()["P04"]);
        Assert.Equal(28, 30 - pedido.ObtenerRequerimientosStockPorSku()["P05"]);
    }

    [Fact]
    public void CasoOficial_CarmenMayoristaCompraDiezMemoriasTotal90()
    {
        var usb = new ProductoSimple("P03", "Memoria USB", 10m, .1m, 20);
        var pedido = CrearPedido(CategoriaCliente.MAYORISTA, (usb, 10));
        Assert.Equal(15m, pedido.Descuento);
        Assert.Equal(90m, pedido.Total);
    }

    [Fact]
    public void KitTeletrabajoPorSeisRequiereSeisTecladosYExcedeStockInicial()
    {
        var p1 = new ProductoSimple("P01", "Mouse", 20m, .2m, 8);
        var p2 = new ProductoSimple("P02", "Teclado", 30m, .7m, 5);
        var p3 = new ProductoSimple("P03", "USB", 10m, .1m, 20);
        var baseKit = new KitCompuesto("KIT_OFICINA", "Oficina", 50m);
        baseKit.AgregarComponente(p1, 1);
        baseKit.AgregarComponente(p2, 1);
        var kit = new KitCompuesto("KIT_TELETRABAJO", "Teletrabajo", 60m);
        kit.AgregarComponente(baseKit, 1);
        kit.AgregarComponente(p3, 1);
        Assert.Equal(6, kit.ExpandirCantidadesPorSku(6)["P02"]);
        Assert.True(kit.ExpandirCantidadesPorSku(6)["P02"] > p2.StockDisponible);
    }

    [Fact]
    public void KitCompuesto_RechazaCiclosIndirectos()
    {
        var a = new KitCompuesto("A", "A", 1m);
        var b = new KitCompuesto("B", "B", 1m);
        a.AgregarComponente(b, 1);
        Assert.Throws<InvalidOperationException>(() => b.AgregarComponente(a, 1));
    }

    [Fact]
    public void ProductoSimple_RechazaPrecioCeroPesoNegativoYStockNegativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductoSimple("A", "A", 0m, 1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductoSimple("A", "A", 1m, -1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductoSimple("A", "A", 1m, 1m, -1));
    }

    [Fact]
    public void AbstractFactory_EntregaEstandarGeneraTarifaYPlazoCorrectos()
    {
        var f = SelectorFabricaEntrega.ObtenerFabrica(TipoEntrega.ESTANDAR);
        Assert.Equal(5m, f.CrearCalculadorTarifa().ObtenerTarifaUsd());
        Assert.Equal(3, f.CrearCalculadorPlazo().ObtenerPlazoDias());
        Assert.Equal(TipoEntrega.ESTANDAR, f.CrearGeneradorGuia().Familia);
    }

    [Fact]
    public void AbstractFactory_EntregaExpresGeneraTarifaYPlazoCorrectos()
    {
        var f = SelectorFabricaEntrega.ObtenerFabrica(TipoEntrega.EXPRES);
        Assert.Equal(12m, f.CrearCalculadorTarifa().ObtenerTarifaUsd());
        Assert.Equal(1, f.CrearCalculadorPlazo().ObtenerPlazoDias());
        Assert.Equal(TipoEntrega.EXPRES, f.CrearGeneradorGuia().Familia);
    }

    [Fact]
    public void PedidoBuilder_RechazaPedidoSinCliente()
    {
        var builder = new PedidoBuilder(CrearCalculadora()).AgregarElemento(new ProductoSimple("P", "Producto", 1m, 0m, 1), 1);
        Assert.Throws<InvalidOperationException>(() => builder.Construir());
    }

    [Fact]
    public void PedidoBuilder_RechazaPedidoSinLineas()
    {
        var cliente = new Cliente(Guid.NewGuid(), "CLI-X", "Cliente", "", CategoriaCliente.NORMAL);
        var builder = new PedidoBuilder(CrearCalculadora()).ParaCliente(cliente);
        Assert.Throws<InvalidOperationException>(() => builder.Construir());
    }

    [Fact]
    public void PoliticaVip_AplicaDescuentoDiezPorCiento()
    {
        var pedido = CrearPedido(CategoriaCliente.VIP, (new ProductoSimple("P", "Producto", 100m, 1m, 1), 1));
        Assert.Equal(10m, pedido.Descuento);
    }

    [Fact]
    public void PoliticaMayorista_TienePrioridadSobreVip()
    {
        var pedido = CrearPedido(CategoriaCliente.VIP, (new ProductoSimple("P", "Producto", 10m, 1m, 10), 10));
        Assert.Equal("MAYORISTA_15", pedido.PoliticaDescuentoAplicada);
        Assert.Equal(15m, pedido.Descuento);
    }

    [Fact]
    public void EstadoPedido_PedidoPagadoNoPuedeCancelarse()
    {
        var pedido = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("P", "Producto", 10m, 1m, 1), 1));
        pedido.Validar();
        pedido.MarcarComoPagado();
        Assert.Throws<InvalidOperationException>(() => pedido.Cancelar());
    }

    [Fact]
    public void DevolucionDefecto_DentroDeTreintaDiasAceptaYDevuelveProductosSinFlete()
    {
        var pedido = CrearPedido(CategoriaCliente.VIP, (new ProductoSimple("P", "Producto", 100m, 1m, 1), 1));
        Entregar(pedido, DateTime.UtcNow.AddDays(-20));
        var resultado = new ProcesadorDevolucionDefecto().Ejecutar(
            new SolicitudDevolucion(pedido, MotivoDevolucion.DEFECTO, DateTime.UtcNow, true, "garantía"));
        Assert.True(resultado.Aceptada);
        Assert.Equal(90m, resultado.MontoReembolso);
        Assert.True(resultado.ReintegrarStock);
    }

    [Fact]
    public void DevolucionDefecto_FueraDeTreintaDiasSeRechaza()
    {
        var pedido = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("P", "Producto", 10m, 1m, 1), 1));
        var entrega = DateTime.UtcNow.AddDays(-31);
        Entregar(pedido, entrega);
        var resultado = new ProcesadorDevolucionDefecto().Ejecutar(
            new SolicitudDevolucion(pedido, MotivoDevolucion.DEFECTO, DateTime.UtcNow, true, ""));
        Assert.False(resultado.Aceptada);
    }

    [Fact]
    public void DevolucionArrepentimiento_DiaDiezSeRechaza()
    {
        var pedido = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("P", "Producto", 10m, 1m, 1), 1));
        Entregar(pedido, DateTime.UtcNow.AddDays(-10));
        var resultado = new ProcesadorDevolucionArrepentimiento().Ejecutar(
            new SolicitudDevolucion(pedido, MotivoDevolucion.ARREPENTIMIENTO, DateTime.UtcNow, true, ""));
        Assert.False(resultado.Aceptada);
    }

    [Fact]
    public void DevolucionArrepentimiento_ProductoUsadoSeRechaza()
    {
        var pedido = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("P", "Producto", 10m, 1m, 1), 1));
        Entregar(pedido, DateTime.UtcNow.AddDays(-2));
        var resultado = new ProcesadorDevolucionArrepentimiento().Ejecutar(
            new SolicitudDevolucion(pedido, MotivoDevolucion.ARREPENTIMIENTO, DateTime.UtcNow, false, ""));
        Assert.False(resultado.Aceptada);
    }

    [Fact]
    public void Visitor_KitsCalculanDesgloseYPesoTotal()
    {
        var p1 = new ProductoSimple("P01", "Mouse", 20m, .2m, 8);
        var p2 = new ProductoSimple("P02", "Teclado", 30m, .7m, 5);
        var p3 = new ProductoSimple("P03", "USB", 10m, .1m, 20);
        var oficina = new KitCompuesto("KO", "Oficina", 50m);
        oficina.AgregarComponente(p1, 1); oficina.AgregarComponente(p2, 1);
        var remoto = new KitCompuesto("KR", "Teletrabajo", 60m);
        remoto.AgregarComponente(oficina, 1); remoto.AgregarComponente(p3, 1);
        var desglose = new VisitanteDesgloseFisico();
        var peso = new VisitantePesoLogistico();
        desglose.VisitarKit(remoto, 1); peso.VisitarKit(remoto, 1);
        Assert.Equal(3, desglose.ObtenerDesglose().Count);
        Assert.Equal(1.0m, peso.PesoTotalKg);
    }

    [Fact]
    public void Iterator_RecorreComponentesFisicosEnOrdenDeterminista()
    {
        var p1 = new ProductoSimple("P01", "Mouse", 20m, .2m, 8);
        var p2 = new ProductoSimple("P02", "Teclado", 30m, .7m, 5);
        var kit = new KitCompuesto("K", "Kit", 50m);
        kit.AgregarComponente(p1, 1); kit.AgregarComponente(p2, 1);
        var iterador = new IteradorComponentesKit(kit);
        Assert.Equal(new[] { "P01", "P02" }, new[] { iterador.Siguiente().Sku, iterador.Siguiente().Sku });
        Assert.False(iterador.TieneSiguiente());
    }

    [Fact]
    public void Flyweight_ComparteInstanciaPorClave()
    {
        var fabrica = new FabricaMetadatosVisuales();
        Assert.Same(fabrica.Obtener("KIT"), fabrica.Obtener("kit"));
        Assert.Equal(1, fabrica.CantidadInstanciasCreadas);
    }

    [Fact]
    public void Singleton_MetricasSesionExponeInstanciaCompartida()
    {
        Assert.Same(MetricasSesion.Instancia, MetricasSesion.Instancia);
    }

    [Fact]
    public void FactoryMethod_CreaNotificadoresSimuladosEmailYSmsConContratoComun()
    {
        INotificadorAviso email = new CreadorNotificadorEmail("email-test.log").CrearNotificador();
        INotificadorAviso sms = new CreadorNotificadorSms("sms-test.log").CrearNotificador();
        Assert.Equal("EMAIL", email.Canal);
        Assert.Equal("SMS", sms.Canal);
        Assert.NotSame(email, sms);
    }

    [Fact]
    public void Interpreter_RespetaPrecedenciaAndSobreOr()
    {
        var pedidoA = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("A", "Ana", 60m, 1m, 1), 1));
        pedidoA.Validar(); pedidoA.MarcarComoPagado();
        var pedidoB = CrearPedido(CategoriaCliente.NORMAL, (new ProductoSimple("B", "Cliente", 10m, 1m, 1), 1));
        var expresion = ParserFiltroPedidos.Parsear("ESTADO = PAGADO AND TOTAL >= 50 OR CLIENTE = Cliente");
        Assert.True(expresion.Evaluar(pedidoA));
        Assert.True(expresion.Evaluar(pedidoB));
    }

    [Fact]
    public void Interpreter_RechazaExpresionIncompleta()
    {
        Assert.ThrowsAny<Exception>(() => ParserFiltroPedidos.Parsear("ESTADO ="));
    }

    [Fact]
    public void Memento_LimitaHistorialACincoEstados()
    {
        var originator = new BorradorPedidoOriginator();
        var historial = new HistorialBorradorCaretaker();
        for (int i = 1; i <= 8; i++)
        {
            historial.GuardarEstado(originator);
            originator.AgregarOActualizarItem($"P{i}", $"Producto {i}", 1, i);
        }
        Assert.Equal(HistorialBorradorCaretaker.CapacidadMaxima, historial.CantidadPasados);
    }

    [Fact]
    public async Task Proxy_DeniegaRolConsultaAntesDeLlamarProveedor()
    {
        var proveedor = new PasarelaFalsa();
        var proxy = new PagoAutorizadoProxy(proveedor, new ContextoSesionFalso(RolUsuario.CONSULTA));
        var resultado = await proxy.CobrarAsync(10m, "K");
        Assert.False(resultado.Exitoso);
        Assert.Equal(0, proveedor.Llamadas);
    }

    [Fact]
    public async Task Proxy_AutorizaRolVentasYDelega()
    {
        var proveedor = new PasarelaFalsa();
        var proxy = new PagoAutorizadoProxy(proveedor, new ContextoSesionFalso(RolUsuario.VENTAS));
        var resultado = await proxy.CobrarAsync(10m, "K");
        Assert.True(resultado.Exitoso);
        Assert.Equal(1, proveedor.Llamadas);
    }

    private static void Entregar(Pedido pedido, DateTime fecha)
    {
        pedido.Validar(); pedido.MarcarComoPagado(); pedido.MarcarComoDespachado(); pedido.MarcarComoEntregado(fecha);
    }

    private sealed class ContextoSesionFalso(RolUsuario rol) : IContextoSesion
    {
        public UsuarioSesion? UsuarioActual { get; private set; } = new(Guid.NewGuid(), "prueba", "Usuario de prueba", rol);
        public void IniciarSesion(UsuarioSesion usuario) => UsuarioActual = usuario;
        public void CerrarSesion() => UsuarioActual = null;
        public bool TienePermiso(params RolUsuario[] rolesPermitidos) => UsuarioActual is not null && rolesPermitidos.Contains(UsuarioActual.Rol);
    }

    private sealed class PasarelaFalsa : IPasarelaPago
    {
        public string NombreProveedor => "FAKE";
        public int Llamadas { get; private set; }
        public Task<ResultadoCobro> CobrarAsync(decimal montoUsd, string claveIdempotencia, CancellationToken ct = default)
        {
            Llamadas++;
            return Task.FromResult(new ResultadoCobro(true, "REF", NombreProveedor, "OK"));
        }
    }
}
